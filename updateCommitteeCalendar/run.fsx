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

let toModel (bills:Bill seq) (committees:Committee seq) (billname,meeting) = 
    let billId = bills |> Seq.find (fun b -> b.Name = billname) |> (fun b -> b.Id)
    let committeeLink = meeting?committee?link.AsString().Replace("standing-","")
    let committee = committees |> Seq.find (fun committee -> committee.Link = committeeLink) 

    {ScheduledAction.Id = 0;
    ActionType = ActionType.CommitteeReading;
    Date = meeting?meetingDate.AsDateTime();
    Start = meeting?starttime.AsString();
    End = meeting?endtime.AsString();
    Location = meeting?location.AsString();
    Link = meeting?link.AsString();
    Chamber = committee.Chamber;
    BillId = billId;}

let generateScheduledActions cn = 

    let sessionYear = cn |> currentSessionYear
    let bills = cn |> dapperQuery<Bill> SelectBillIdsAndNames
    let committees = cn |> dapperQuery<Committee> SelectCommitteeChamberNamesAndLinks
    let links = cn |> dapperParametrizedQuery<string> "SELECT Link from ScheduledAction WHERE Date >= @Date" {DateSelectArgs.Date=datestamp()}

    let toUnknownMeeting meeting = links |> Seq.exists (fun link -> link = meeting?link.AsString()) |> not 
    let fetchMeetingMetdata meeting = meeting?link.AsString() |> get 
    let allBillNamesFromAllMeetings metadata = 
        metadata?agenda.AsArray() 
        |> Array.map (fun a -> (a?bill.AsArray().[0]?billName.AsString(), metadata))
        |> Array.toList 
    let toKnownBills (billname,meeting) = bills |> Seq.exists(fun b -> b.Name = billname)
    let toModel' (billname,meeting) = (billname,meeting) |> toModel bills committees

    fetchAll (sprintf "/%s/meetings?minDate=%s" sessionYear (datestamp()))
    |> List.filter toUnknownMeeting
    |> List.map fetchMeetingMetdata
    |> List.collect allBillNamesFromAllMeetings
    |> List.filter toKnownBills
    |> List.map toModel'

let addToDatabase cn scheduledActions =

    let insertAndFetchId scheduledAction = cn |> dapperParametrizedQuery<int> InsertScheduledAction scheduledAction |> Seq.head 
    let ids = scheduledActions |> List.map insertAndFetchId
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

        log.Info(sprintf "[%s] Fetch committee meetings from API ..." (timestamp()))
        let scheduledActionModels = cn |> generateScheduledActions 
        log.Info(sprintf "[%s] Fetch committee meetings from API [OK]" (timestamp()) )

        log.Info(sprintf "[%s] Add scheduled actions to database ..." (timestamp()))
        let scheduledActionsRequiringAlerts = scheduledActionModels |> addToDatabase cn
        log.Info(sprintf "[%s] Add scheduled actions to database [OK]" (timestamp()))

        log.Info(sprintf "[%s] Enqueue alerts for scheduled actions ..." (timestamp()))
        let enqueue scheduledAction = 
            let json = scheduledAction |> JsonConvert.SerializeObject
            log.Info(sprintf "[%s]  Enqueuing scheduled action %s" (timestamp()) json)
            json |> scheduledActions.Add
        scheduledActionsRequiringAlerts |> Seq.iter enqueue
        log.Info(sprintf "[%s] Enqueue alerts for scheduled actions [OK]" (timestamp()))   

        log.Info(sprintf "[%s] Invalidating cache ..." (timestamp()))
        scheduledActionModels |> invalidateCache ScheduledActionsKey
        log.Info(sprintf "[%s] Invalidating cache [OK]" (timestamp()))

    with
    | ex -> 
        trackException ex
        log.Error(sprintf "{%s] Encountered error: %s" (timestamp()) (ex.ToString()))
        reraise()    