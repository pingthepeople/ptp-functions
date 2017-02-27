// Configure Database 

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

#load "../shared/model.fs"
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
open Newtonsoft.Json

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
open Microsoft.Azure.WebJobs.Host

let Run(myTimer: TimerInfo, digests: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (DateTime.Now.ToString()))
    try
        let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString")) 
        let connStr = Environment.GetEnvironmentVariable("AzureStorage.ConnectionString")
        let allBillsSpreadsheetFilename = generateAllBillsSpreadsheetFilename DateTime.Now

        log.Info(sprintf "[%s] Create today's spreadsheet for all bills ('%s') ..." (DateTime.Now.ToString("HH:mm:ss.fff")) allBillsSpreadsheetFilename)
        cn
        // fetch the current status of all bills
        |> dapperQuery<BillStatus> FetchAllBillStatus
        // genereate a CSV file and upload it to a storage blob
        |> postSpreadsheet connStr allBillsSpreadsheetFilename
        log.Info(sprintf "[%s] Create today's spreadsheet for all bills ('%s') [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")) allBillsSpreadsheetFilename)

        log.Info(sprintf "[%s] Enqueue digest creation ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        cn
        // fetch all users requiring a digest email
        |> dapperQuery<User> FetchDigestUsers
        // serialize the user objects
        |> Seq.map JsonConvert.SerializeObject
        // enqueue the users for digest generation
        |> Seq.iter digests.Add
        log.Info(sprintf "[%s] Enqueue digest creation [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))
    with
    | ex -> log.Error(sprintf "Encountered error: %s" (ex.ToString())) 