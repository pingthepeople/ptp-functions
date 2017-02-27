// Configure Database 

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

#load "../shared/model.fs"
#load "../shared/db.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open IgaTracker.Model
open IgaTracker.Db
open Newtonsoft.Json

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
open Microsoft.Azure.WebJobs.Host

let Run(myTimer: TimerInfo, digests: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (DateTime.Now.ToString()))
    try
        log.Info(sprintf "[%s] Enqueue digest creation ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString")) 
        |> dapperQuery<User> "SELECT * FROM User WHERE DigestType <> 0"
        |> Seq.map JsonConvert.SerializeObject
        |> Seq.iter digests.Add
        log.Info(sprintf "[%s] Enqueue digest creation [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))
    with
    | ex -> log.Error(sprintf "Encountered error: %s" (ex.ToString())) 