#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

#load "../shared/queries.fs"
#load "../shared/db.fsx"
#load "../shared/csv.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open IgaTracker.Model
open IgaTracker.Queries
open IgaTracker.Db
open IgaTracker.Csv
open IgaTracker.Logging
open Newtonsoft.Json

#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Extensions/lib/net45/Microsoft.Azure.WebJobs.Extensions.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs.Extensions

let Run(myTimer: TimerInfo, digests: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (timestamp()))
    try
        let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString")) 
        let connStr = Environment.GetEnvironmentVariable("AzureStorage.ConnectionString")
        let allBillsSpreadsheetFilename = generateAllBillsSpreadsheetFilename()

        log.Info(sprintf "[%s] Create today's spreadsheet for all bills ('%s') ..." (timestamp()) allBillsSpreadsheetFilename)
        cn
        // fetch the current status of all bills
        |> dapperQuery<BillStatus> FetchAllBillStatus
        // genereate a CSV file and upload it to a storage blob
        |> postSpreadsheet connStr allBillsSpreadsheetFilename
        log.Info(sprintf "[%s] Create today's spreadsheet for all bills ('%s') [OK]" (timestamp()) allBillsSpreadsheetFilename)

        log.Info(sprintf "[%s] Enqueue digest creation ..." (timestamp()))
        cn
        // fetch all users requiring a digest email
        |> dapperQuery<User> FetchDigestUsers
        // serialize the user objects
        |> Seq.map JsonConvert.SerializeObject
        // enqueue the users for digest generation
        |> Seq.iter digests.Add
        log.Info(sprintf "[%s] Enqueue digest creation [OK]" (timestamp()))
    with
    | ex -> 
        trackException ex
        log.Error(sprintf "Encountered error: %s" (ex.ToString())) 
        reraise()