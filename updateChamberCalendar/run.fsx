// Configure Database 

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/http.fsx"
#load "../shared/db.fsx"

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

let toModel (bills:Bill seq) (billname,calendar,chamber,actionType) = 
    {ScheduledAction.Id = 0;
    ActionType = actionType;
    Date = calendar?date.AsDateTime();
    Start = "";
    End = "";
    Location = chamber;
    Link = calendar?link.AsString();
    BillId = bills |> Seq.find (fun b -> b.Name = billname) |> (fun b -> b.Id);}

let generateModel calendar chamber (bills:Bill seq) actionType (section:JsonValue) =
    match section.TryGetProperty("bills") with
    | None -> [] // There is no bill data for this section. Bail.
    | Some(sectionBills) ->
        // Parse the bills from this section
        sectionBills.AsArray()
        // Parse the names of the bills in this section
        |> Array.map (fun bill -> bill?billName.AsString().Substring(0,6))
        // Filter out bills we're not tracking
        |> Array.filter (fun billName ->  bills |> Seq.exists(fun b -> b.Name = billName))
        // Map the bills to a ScheduledAction 
        |> Array.map (fun billName -> toModel bills (billName,calendar,chamber,actionType))
        |> Array.toList

let generateScheduledActions (chamber, sessionYear, date, (bills:Bill seq), links) = 
    
    let calendarMetadata = 
        // fetch committees occurring after today
        fetchAll (sprintf "/%s/chambers/%s/calendars?minDate=%s" sessionYear chamber date)
        // filter out 
        |> List.tryFind (fun calendarMetadata -> calendarMetadata?edition.AsString() = "First")

    match calendarMetadata with
    // no calendar found
    | None -> []  
     // a calendar was found but we aleady know about it
    | Some(metadata) when links |> Seq.exists (fun link -> link = metadata?link.AsString()) -> []
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

let addToDatabase cn scheduledActions =
    scheduledActions 
    // Insert the scheduled actions into the database. Capture new new record ids
    |> List.map (fun scheduledAction -> cn |> dapperParametrizedQuery<int> InsertScheduledAction scheduledAction |> Seq.head)
    // Filter the ids based on the bills that users want alerts on
    |> (fun ids -> cn |> dapperMapParametrizedQuery<int> SelectScheduledActionsRequiringNotification (Map ["Ids", ids :> obj]))

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
open Microsoft.Azure.WebJobs.Host

let Run(myTimer: TimerInfo, scheduledActions: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (DateTime.Now.ToString()))

    let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
    let sessionYear = (System.Environment.GetEnvironmentVariable("SessionYear"))
    let date = DateTime.Now.AddDays(1.0).ToString("yyyy-MM-dd")
    
    let bills = cn |> dapperQuery<Bill> "SELECT Id,Name from Bill"
    let links = cn |> dapperParametrizedQuery<string> "SELECT Link from ScheduledAction WHERE Date = @Date" {DateSelectArgs.Date=date}

    log.Info(sprintf "[%s] Fetch chamber calendar from API ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
    let houseScheduledActionModels = ("House", sessionYear, date, bills, links) |> generateScheduledActions 
    let senateScheduledActionModels = ("Senate", sessionYear, date, bills, links) |> generateScheduledActions 
    log.Info(sprintf "[%s] Fetch chamber calendar from API [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")) )

    log.Info(sprintf "[%s] Add scheduled actions to database ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
    let houseScheduledActionIdsRequringAlert = houseScheduledActionModels |> addToDatabase cn
    let senateScheduledActionIdsRequringAlert = senateScheduledActionModels |> addToDatabase cn
    log.Info(sprintf "[%s] Add scheduled actions to database [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))

    log.Info(sprintf "[%s] Enqueue alerts for scheduled actions ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
    houseScheduledActionIdsRequringAlert |> Seq.iter (fun id -> 
        log.Info(sprintf "[%s]  Enqueuing scheduled action %d" (DateTime.Now.ToString("HH:mm:ss.fff")) id)
        scheduledActions.Add(id.ToString()))
    senateScheduledActionIdsRequringAlert |> Seq.iter (fun id -> 
        log.Info(sprintf "[%s]  Enqueuing scheduled action %d" (DateTime.Now.ToString("HH:mm:ss.fff")) id)
        scheduledActions.Add(id.ToString()))
    log.Info(sprintf "[%s] Enqueue alerts for scheduled actions [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))    