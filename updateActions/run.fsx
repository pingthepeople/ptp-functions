#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/StackExchange.Redis/lib/net45/StackExchange.Redis.dll"

#load "../shared/queries.fs"
#load "../shared/db.fsx"
#load "../shared/http.fsx"
#load "../shared/cache.fsx"
#load "../shared/bill.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open FSharp.Data
open FSharp.Data.JsonExtensions
open IgaTracker.Model
open IgaTracker.Http
open IgaTracker.Db
open IgaTracker.Queries
open IgaTracker.Cache
open IgaTracker.Logging
open IgaTracker.Bill

let toActionModel (action,bill:Bill) = {
    Action.Id = 0;
    Date = action?date.AsDateTime();
    Link = action?link.AsString();
    ActionType = action?description.AsString() |> Action.ParseType;
    Description = action?description.AsString() |> Action.ParseDescription;
    Chamber = System.Enum.Parse(typeof<Chamber>, action?chamber?name.AsString()) :?> Chamber
    BillId = bill.Id;
}

let createNewActionModels (cn:SqlConnection) allActions =  
    let bills = cn |> dapperQuery<Bill> SelectBillIdsAndNames
    let links = cn |> dapperParametrizedQuery<string> SelectActionLinksOccuringAfterDate {DateSelectArgs.Date=(datestamp())}

    let toKnownBills action = bills |> Seq.exists (fun bill -> bill.Name = action?billName?billName.AsString())
    let toUnrecordedActions action = links |> Seq.exists (fun link -> link = action?link.AsString()) |> not
    let actionAndBill action = (action, bills |> Seq.find (fun bill -> bill.Name = action?billName?billName.AsString()))

    allActions
        |> List.filter toKnownBills
        |> List.filter toUnrecordedActions
        |> List.map (actionAndBill >> toActionModel)

let ensureLatestBillMetadata (actions:Action list) cn =
    actions 
    |> Seq.map (fun a -> cn |> updateBillToLatest a.BillId) 
    |> ignore   

let addToDatabase (cn:SqlConnection) models =
    let toKnownActionTypes (action:Action) = action.ActionType <> ActionType.Unknown
    let addActionToDbAndGetId (action:Action) = cn |> dapperParameterizedQueryOne<int> InsertAction action
    let fetchActionsRequiringAlert insertedIds = cn |> dapperMapParametrizedQuery<Action> SelectActionsRequiringNotification (Map ["Ids", insertedIds :> obj])

    models
    |> List.filter toKnownActionTypes
    |> List.map addActionToDbAndGetId
    |> fetchActionsRequiringAlert 


// Azure Function entry point

#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Extensions/lib/net45/Microsoft.Azure.WebJobs.Extensions.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs.Extensions
open Newtonsoft.Json

let Run(myTimer: TimerInfo, actions: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (timestamp()))
    try
        let cn = new SqlConnection((sqlConStr()))
        
        log.Info(sprintf "[%s] Fetching actions from API ..." (timestamp()))
        let sessionYear = cn |> currentSessionYear
        let actionModels = 
            fetchAll (sprintf "/%s/bill-actions?minDate=%s" sessionYear (datestamp())) 
            |> createNewActionModels cn
        log.Info(sprintf "[%s] Fetching actions from API [OK]" (timestamp()))

        log.Info(sprintf "[%s] Adding actions to database ..." (timestamp()))
        let actionIdsRequiringAlert = actionModels |> addToDatabase cn
        log.Info(sprintf "[%s] Adding actions to database [OK]" (timestamp()))

        log.Info(sprintf "[%s] Ensuring latest bill metadata in database ..." (timestamp()))
        cn |> ensureLatestBillMetadata actionModels
        log.Info(sprintf "[%s] Ensuring latest bill metadata in database [OK]" (timestamp()))

        log.Info(sprintf "[%s] Enqueue alerts for new actions ..." (timestamp()))
        let enqueue json =
            let trace = sprintf "  Enqueuing action %s" json
            trace |> trackTrace "updateActions"
            trace |> log.Info
            json |> actions.Add           
        actionIdsRequiringAlert |> Seq.map JsonConvert.SerializeObject |> Seq.iter enqueue
        log.Info(sprintf "[%s] Enqueue alerts for new actions [OK]" (timestamp()))

        log.Info(sprintf "[%s] Updating bill/committee assignments ..." (timestamp()))
        UpdateBillCommittees |> cn.Execute |> ignore
        log.Info(sprintf "[%s] Updating bill/committee assignments [OK]" (timestamp()))

        log.Info(sprintf "[%s] Invalidating cache ..." (timestamp()))
        actionModels |> invalidateCache ActionsKey
        log.Info(sprintf "[%s] Invalidating cache [OK]" (timestamp()))

    with
    | ex -> 
        ex |> trackException "updateActions"
        log.Error(sprintf "Encountered error: %s" (ex.ToString())) 
        reraise()