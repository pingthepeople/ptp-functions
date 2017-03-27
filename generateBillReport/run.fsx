#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Data"
#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Primitives"
#r "System.Net.Http.Formatting"
#r "../packages/Dapper/lib/net45/Dapper.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/db.fsx"

open System
open System.Collections
open System.Collections.Specialized
open System.Data.SqlClient
open System.Dynamic
open System.IO
open System.Text
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open Dapper
open IgaTracker.Model
open IgaTracker.Db
open IgaTracker.Queries
open IgaTracker.Logging

// Azure function entry point

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json

[<CLIMutable>]
type Body  =
    { Id : int
      Secret : string }

let isAuthorized body =
    body.Secret = System.Environment.GetEnvironmentVariable("HttpTrigger.Secret")

let generateReport body = 
    new SqlConnection(sqlConStr()) 
    |> dapperMapParametrizedQuery<BillStatus> FetchBillStatusForUser (Map["Id", body.Id :> obj]) 
    |> JsonConvert.SerializeObject

let Run(req: HttpRequestMessage, log: TraceWriter) =
    log.Info(sprintf "[%s] F# HTTP trigger function processed a request." (timestamp()))
    try
        async {

            let! content = req.Content.ReadAsStringAsync() |> Async.AwaitTask
            let body = JsonConvert.DeserializeObject<Body>(content)
            let response = 
                match (body |> isAuthorized) with
                | false -> 
                    let trace = sprintf "[%s] Request to generate bill report for for user with Id %d was not authorized" (timestamp()) body.Id
                    trace |> log.Info
                    trace |> trackTrace "generateBillReport"
                    req.CreateResponse(HttpStatusCode.Unauthorized)
                | true ->
                    let trace = sprintf "[%s] Generating bill report for user with Id %d ..." (timestamp()) body.Id
                    trace |> log.Info
                    trace |> trackTrace "generateBillReport"
                    let res = req.CreateResponse(HttpStatusCode.OK)
                    let report = body |> generateReport
                    res.Content <- new StringContent(report, Encoding.UTF8, "application/json")
                    log.Info(sprintf "[%s] Generating bill report for user with Id %d [OK]" (timestamp()) body.Id)
                    res
            return response
        } |> Async.RunSynchronously
    with
    | ex ->
        ex |> trackException "generateBillReport"
        log.Error(sprintf "[%s] Encountered error: %s" (timestamp()) (ex.ToString())) 
        reraise()