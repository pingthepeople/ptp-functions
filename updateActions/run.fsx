
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
    log.Info(sprintf "F# function executed at: %s" (DateTime.Now.ToString()))
    try
        let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
        let date = DateTime.Now.ToString("yyyy-MM-dd")
        let sessionYear = cn |> currentSessionYear
        
        log.Info(sprintf "[%s] Fetching actions from API ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        let allActions = fetchAll (sprintf "/%s/bill-actions?minDate=%s" sessionYear date) 
        log.Info(sprintf "[%s] Fetching actions from API [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")) )

        log.Info(sprintf "[%s] Adding actions to database ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        let actionIdsRequiringAlert = allActions |> addToDatabase date cn
        log.Info(sprintf "[%s] Adding actions to database [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))

        log.Info(sprintf "[%s] Enqueue alerts for new actions ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        let enqueue json =
            log.Info(sprintf "[%s]  Enqueuing action %s" (DateTime.Now.ToString("HH:mm:ss.fff")) json)
            json |> actions.Add           
        actionIdsRequiringAlert |> Seq.map JsonConvert.SerializeObject |> Seq.iter enqueue
        log.Info(sprintf "[%s] Enqueue alerts for new actions [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))

        log.Info(sprintf "[%s] Updating bill/committee assignments ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        UpdateBillCommittees |> cn.Execute |> ignore
        log.Info(sprintf "[%s] Updating bill/committee assignments [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))
    with
    | ex -> log.Error(sprintf "Encountered error: %s" (ex.ToString())) 