
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

#load "module.fsx"
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
open IgaTracker.UpdateActions
open Newtonsoft.Json

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