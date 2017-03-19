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

// Azure function entry point

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json

let isAuthorized (req:HttpRequestMessage) =
    true

let Run(req: HttpRequestMessage, log: TraceWriter) =
    async {
        log.Info(sprintf "F# HTTP trigger function processed a request.")
        match (req |> isAuthorized) with
        | false -> 
            return req.CreateResponse(HttpStatusCode.Unauthorized)
        | true ->
            let! reqContent = req.Content.ReadAsStringAsync() |> Async.AwaitTask
            let user = JsonConvert.DeserializeObject<User>(reqContent)
            let resContent = 
                new SqlConnection(sqlConStr()) 
                |> dapperMapParametrizedQuery<BillStatus> FetchBillStatusForUser (Map["Id", user.Id :> obj]) 
                |> JsonConvert.SerializeObject
            let res = req.CreateResponse(HttpStatusCode.OK)
            res.Content <- new StringContent(resContent, Encoding.UTF8, "application/json")
            return res
    } |> Async.RunSynchronously