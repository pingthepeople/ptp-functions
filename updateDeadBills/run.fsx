
#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/StackExchange.Redis/lib/net45/StackExchange.Redis.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/db.fsx"
#load "../shared/cache.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open IgaTracker.Model
open IgaTracker.Db
open IgaTracker.Queries
open IgaTracker.Cache

let getNewDeadBills cn = 
    let deadBillIds = cn |> dapperQuery<int> FetchNewDeadBills
    cn |> dapperMapParametrizedQuery<Bill> UpdateDeadBills (Map ["Ids", deadBillIds :> obj])

// Azure Function entry point

#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Extensions/lib/net45/Microsoft.Azure.WebJobs.Extensions.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs.Extensions
open Newtonsoft.Json

let Run(myTimer: TimerInfo, deadbills: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (timestamp()))
    try
        let cn = new SqlConnection((sqlConStr()))
        
        log.Info(sprintf "[%s] Fetching newly dead bills ..." (timestamp()))
        let deadBills = cn |> getNewDeadBills
        log.Info(sprintf "[%s] Fetching newly dead bills [OK]" (timestamp()))

        log.Info(sprintf "[%s] Enqueue alerts for newly dead bills ..." (timestamp()))
        deadBills 
            |> Seq.map JsonConvert.SerializeObject 
            |> Seq.iter (fun json ->
                log.Info(sprintf "[%s]  Enqueuing action %s" (timestamp()) json)
                json |> deadbills.Add)
        log.Info(sprintf "[%s] Enqueue alerts for newly dead bills [OK]" (timestamp()))

        log.Info(sprintf "[%s] Invalidating cache ..." (timestamp()))
        deadBills |> invalidateCache BillsKey
        log.Info(sprintf "[%s] Invalidating cache [OK]" (timestamp()))

    with
    | ex -> 
        log.Error(sprintf "Encountered error: %s" (ex.ToString())) 
        reraise()