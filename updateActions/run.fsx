
#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/db.fsx"
#load "../shared/http.fsx"

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
open Newtonsoft.Json

let toActionModel (action,bill:Bill) = {
    Action.Id = 0;
    Date = action?date.AsDateTime();
    Link = action?link.AsString();
    ActionType = action?description.AsString() |> Action.ParseType;
    Description = action?description.AsString() |> Action.ParseDescription;
    Chamber = System.Enum.Parse(typeof<Chamber>, action?chamber?name.AsString()) :?> Chamber
    BillId = bill.Id;
}

let addToDatabase date (cn:SqlConnection) allActions =

    let bills = cn |> dapperQuery<Bill> SelectBillIdsAndNames
    let links = cn |> dapperParametrizedQuery<string> SelectActionLinksOccuringAfterDate {DateSelectArgs.Date=date}

    let addActionToDbAndGetId (action:Action) = cn |> dapperParametrizedQuery<int> InsertAction action |> Seq.head
    let fetchActionsRequiringAlert insertedIds = cn |> dapperMapParametrizedQuery<Action> SelectActionsRequiringNotification (Map ["Ids", insertedIds :> obj])

    let toKnownBills action = bills |> Seq.exists (fun bill -> bill.Name = action?billName?billName.AsString())
    let toUnrecordedActions action = links |> Seq.exists (fun link -> link = action?link.AsString()) |> not
    let toKnownActionTypes (action:Action) = action.ActionType <> ActionType.Unknown
    let actionAndBill action = (action, bills |> Seq.find (fun bill -> bill.Name = action?billName?billName.AsString()))

    allActions
        |> List.filter toKnownBills
        |> List.filter toUnrecordedActions
        |> List.map (actionAndBill >> toActionModel)
        |> List.filter toKnownActionTypes
        |> List.map addActionToDbAndGetId
        |> fetchActionsRequiringAlert 

// Azure Function entry point

#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Extensions/lib/net45/Microsoft.Azure.WebJobs.Extensions.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs.Extensions

let Run(myTimer: TimerInfo, actions: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (timestamp()))
    try
        let cn = new SqlConnection((sqlConStr()))
        
        log.Info(sprintf "[%s] Fetching actions from API ..." (timestamp()))
        let sessionYear = cn |> currentSessionYear
        let allActions = fetchAll (sprintf "/%s/bill-actions?minDate=%s" sessionYear (datestamp())) 
        log.Info(sprintf "[%s] Fetching actions from API [OK]" (timestamp()))

        log.Info(sprintf "[%s] Adding actions to database ..." (timestamp()))
        let actionIdsRequiringAlert = allActions |> addToDatabase (datestamp()) cn
        log.Info(sprintf "[%s] Adding actions to database [OK]" (timestamp()))

        log.Info(sprintf "[%s] Enqueue alerts for new actions ..." (timestamp()))
        let enqueue json =
            log.Info(sprintf "[%s]  Enqueuing action %s" (timestamp()) json)
            json |> actions.Add           
        actionIdsRequiringAlert |> Seq.map JsonConvert.SerializeObject |> Seq.iter enqueue
        log.Info(sprintf "[%s] Enqueue alerts for new actions [OK]" (timestamp()))

        log.Info(sprintf "[%s] Updating bill/committee assignments ..." (timestamp()))
        UpdateBillCommittees |> cn.Execute |> ignore
        log.Info(sprintf "[%s] Updating bill/committee assignments [OK]" (timestamp()))
    with
    | ex -> log.Error(sprintf "Encountered error: %s" (ex.ToString())) 