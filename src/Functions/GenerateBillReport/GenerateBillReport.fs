module GenerateBillReport

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open Ptp.Queries
open System.Net.Http

[<CLIMutable>]
type Body  = { Id : int }

let generateReport body = trial { 
    let! result = dbParameterizedQuery<BillStatus> FetchBillStatusForUser {Id=body.Id}
    return result |> JsonConvert.SerializeObject
    }

let deserializeId = 
    validateBody<Body> "A user id is expected in the form '{ Id: INT }'"

let workflow req =
    (fun _ -> deserializeId req)
    >> bind generateReport

let Run(req: HttpRequestMessage, log: TraceWriter) =
    req
    |> workflow
    |> executeHttpWorkflow log HttpWorkflow.GenerateBillReport