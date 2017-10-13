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

let generateReport body = trial { 
    let! result = dbParameterizedQuery<BillStatus> FetchBillStatusForUser {Id=body.Id}
    return result |> JsonConvert.SerializeObject
    }

let deserializeId = 
    validateBody<Body> "A user id is expected in the form '{ Id: INT }'"

let Run(req: HttpRequestMessage, log: TraceWriter) =
    let deserializeId() = deserializeId req
    
    let workflow =
        deserializeId
        >> bind generateReport
        >> bind successWithData
    
    workflow
    |> executeWorkflow log Workflow.HttpGenerateBillReport
    |> constructHttpResponse
