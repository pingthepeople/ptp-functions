module GenerateBillReport

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open Ptp.Queries
open Ptp.Logging
open System
open System.Net
open System.Net.Http
open System.Data.SqlClient

[<CLIMutable>]
type Body  = { Id : int }

let generateReport body = 
    try
        new SqlConnection(sqlConStr()) 
        |> dapperMapParametrizedQuery<BillStatus> FetchBillStatusForUser (Map["Id", body.Id :> obj]) 
        |> JsonConvert.SerializeObject
        |> ok
    with
    | ex -> fail (HttpStatusCode.InternalServerError, ("Failed to generate report: " + ex.Message))

let deserializeId = 
    validateBody<Body> "A user id is expected in the form '{ Id: INT }'"

let processRequest = 
    deserializeId
    >> bind generateReport

let Run(req: HttpRequestMessage, log: TraceWriter) =
    req
    |> processRequest
    |> continueOnFail log "GenerateBillReport"
    |> constructHttpResponse
