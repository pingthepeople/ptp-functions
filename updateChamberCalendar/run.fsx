#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/StackExchange.Redis/lib/net45/StackExchange.Redis.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/http.fsx"
#load "../shared/db.fsx"
#load "../shared/cache.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open FSharp.Data
open FSharp.Data.JsonExtensions
open IgaTracker.Model
open IgaTracker.Queries
open IgaTracker.Http
open IgaTracker.Db
open IgaTracker.Cache
open IgaTracker.Logging
open StackExchange.Redis

let toModel (bills:Bill seq) (billname,calendar,chamber,actionType) = 
    {ScheduledAction.Id = 0;
    ActionType = actionType;
    Date = calendar?date.AsDateTime();
    Start = "";
    End = "";
    Location = chamber.ToString();
    Chamber = chamber;
    Link = calendar?link.AsString();
    BillId = bills |> Seq.find (fun b -> b.Name = billname) |> (fun b -> b.Id);}

let generateModel calendar chamber (bills:Bill seq) actionType (section:JsonValue) =

    let parseBillName bill = bill?billName.AsString().Substring(0,6)
    let toKnownBills billName = bills |> Seq.exists(fun b -> b.Name = billName)
    let createScheduledActionModel billName = toModel bills (billName,calendar,chamber,actionType)

    match section.TryGetProperty("bills") with
    | None -> [] // There is no bill data for this section. Bail.
    | Some(sectionBills) ->
        sectionBills.AsArray()
        |> Array.map parseBillName
        |> Array.filter toKnownBills
        |> Array.map createScheduledActionModel
        |> Array.toList

let fetchCalendarMetadataFromApi (chamber, sessionYear, date) = 

    let firstEditionCalendar calendarMetadata = calendarMetadata?edition.AsString() = "First"

    fetchAll (sprintf "/%s/chambers/%A/calendars?minDate=%s" sessionYear chamber date)
    |> List.tryFind firstEditionCalendar

let generateScheduledActionsForChamber (chamber, sessionYear, date, (bills:Bill seq), links) = 
    
    let calendarMetadata = fetchCalendarMetadataFromApi (chamber, sessionYear, date)
    let calendarHasAlreadyBeenRecorded metadata = links |> Seq.exists (fun link -> link = metadata?link.AsString())

    match calendarMetadata with
    // no calendar found
    | None -> []  
     // a calendar was found but we aleady know about it
    | Some(metadata) when metadata |> calendarHasAlreadyBeenRecorded -> []
    // A new calendar was found
    | Some(metadata) ->
        // Fetch the full calendar data 
        let calendar = get (metadata?link.AsString())
        // Parse the scheduled actions from each section of the calendar 
        let hb2 = calendar?hb2head |> generateModel calendar chamber bills ActionType.SecondReading
        let hb3 = calendar?hb3head |> generateModel calendar chamber bills ActionType.ThirdReading
        let sb2 = calendar?sb2head |> generateModel calendar chamber bills ActionType.SecondReading
        let sb3 = calendar?sb3head |> generateModel calendar chamber bills ActionType.ThirdReading
        // Concat the results of each section
        hb2 @ hb3 @ sb2 @ sb3

let generateScheduledActionModels cn = 

    let date = DateTime.Now.AddDays(1.0).ToString("yyyy-MM-dd")
    let sessionYear = cn |> currentSessionYear
    let bills = cn |> dapperQuery<Bill> SelectBillIdsAndNames
    let links = cn |> dapperParametrizedQuery<string> "SELECT Link from ScheduledAction WHERE Date >= @Date" {DateSelectArgs.Date=date}
    
    let houseScheduledActionModels = (Chamber.House, sessionYear, date, bills, links) |> generateScheduledActionsForChamber 
    let senateScheduledActionModels = (Chamber.Senate, sessionYear, date, bills, links) |> generateScheduledActionsForChamber 
    houseScheduledActionModels @ senateScheduledActionModels

let addToDatabase cn scheduledActions =
    
    let addToDatabaseAndFetchId scheduledAction = cn |> dapperParametrizedQuery<int> InsertScheduledAction scheduledAction |> Seq.head
    let ids = scheduledActions |> List.map addToDatabaseAndFetchId
    cn |> dapperMapParametrizedQuery<ScheduledAction> SelectScheduledActionsRequiringNotification (Map ["Ids", ids :> obj])


// Azure function entry point

#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Extensions/lib/net45/Microsoft.Azure.WebJobs.Extensions.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs.Extensions
open Newtonsoft.Json

let Run(myTimer: TimerInfo, scheduledActions: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (timestamp()))
    try
        let cn = new SqlConnection(sqlConStr())

        log.Info(sprintf "[%s] Fetch chamber calendar from API ..." (timestamp()))
        let scheduledActionModels = cn |> generateScheduledActionModels 
        log.Info(sprintf "[%s] Fetch chamber calendar from API [OK]" (timestamp()) )

        log.Info(sprintf "[%s] Add scheduled actions to database ..." (timestamp()))
        let scheduledActionsRequiringAlerts = scheduledActionModels |> addToDatabase cn
        log.Info(sprintf "[%s] Add scheduled actions to database [OK]" (timestamp()))

        log.Info(sprintf "[%s] Enqueue alerts for scheduled actions ..." (timestamp()))
        let enqueue json = 
            let trace = sprintf "  Enqueuing scheduled action %s" json
            trace |> trackTrace "updateChamberCalendar"
            trace |> log.Info
            json |> scheduledActions.Add
        scheduledActionsRequiringAlerts 
        |> Seq.map JsonConvert.SerializeObject
        |> Seq.iter enqueue
        log.Info(sprintf "[%s] Enqueue alerts for scheduled actions [OK]" (timestamp()))   

        log.Info(sprintf "[%s] Invalidating cache ..." (timestamp()))
        scheduledActionModels |> invalidateCache ScheduledActionsKey
        log.Info(sprintf "[%s] Invalidating cache [OK]" (timestamp()))
        
    with
    | ex -> 
        ex |> trackException "updateChamberCalendar"
        log.Error(sprintf "[%s] Encountered error: %s" (timestamp()) (ex.ToString())) 
        reraise()