module Ptp.Http

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Newtonsoft.Json
open Ptp.Core
open System
open System.Net
open System.Net.Http
open FSharp.Collections.ParallelSeq
open Microsoft.Azure.WebJobs.Host

let get (endpoint:string) = 
    let uri =
        match endpoint.StartsWith("http") with
        | true -> endpoint
        | false -> "https://api.iga.in.gov" + endpoint
    let standardHeaders = [ "Accept", "application/json"; "Authorization", "Token " + Environment.GetEnvironmentVariable("IgaApiKey") ]
    Http.RequestString(uri, httpMethod = "GET", headers = standardHeaders) 
    |> JsonValue.Parse

let tryGet endpoint =
    let failwith errors = 
        errors
        |> List.rev
        |> String.concat "\n"
        |> sprintf "Failed to fetch %s after 3 attempts: %s" endpoint
        |> failwith
    
    let rec tryGet' attempt endpoint errors =
        match attempt with
        | 3 -> failwith errors
        | x ->
            try
                System.Threading.Thread.Sleep(x * 1000)
                get endpoint
            with
            | ex -> tryGet' (x+1) endpoint (ex.ToString() :: errors)
    tryGet' 0 endpoint []    


let fetchAll endpoint =
    let rec fetchRec link =
        let json = tryGet link
        let items = json?items.AsArray() |> Array.toList
        match json.TryGetProperty("nextLink") with
        | Some link -> items @ (fetchRec (link.AsString()))
        | None -> items

    fetchRec endpoint

type Error = { Error:string; }

let httpResponse status content =
    content
    |> JsonConvert.SerializeObject 
    |> (fun j -> new StringContent(j, System.Text.Encoding.UTF8, "application/json"))
    |> (fun c -> new HttpResponseMessage(StatusCode = status, Content=c))

let constructHttpResponse twoTrackResult =
    match twoTrackResult with
    | Fail msgs -> 
        match List.head msgs with
        | RequestValidationError _ -> 
            let validationErrors = flatten msgs
            httpResponse HttpStatusCode.BadRequest validationErrors
        | _ -> 
            httpResponse HttpStatusCode.InternalServerError "An internal error occurred"
    | Pass (WorkflowSuccess s) -> 
        s |> httpResponse HttpStatusCode.OK
    | Warn (WorkflowSuccess s,msg) -> 
        s |> httpResponse HttpStatusCode.OK

let executeHttpWorkflow (log:TraceWriter) source workflow =
    executeWorkflow log source workflow
    |> constructHttpResponse

let validationError errorMessage = RequestValidationError(errorMessage) |> fail

let validateBody<'T> (errorMessage:string) (req:HttpRequestMessage) =
    if req.Content = null 
    then errorMessage |> validationError 
    else
        let content = req.Content.ReadAsStringAsync().Result
        if isEmpty content      
        then errorMessage |> validationError 
        else content |> JsonConvert.DeserializeObject<'T> |> ok

let inline validateStr x f (errorMessage:string) (param:String) =
    if f(param) 
    then x |> ok
    else errorMessage |> validationError

let inline validateInt x f (errorMessage:string) (param:int) =
    if f(param) 
    then x |> ok
    else errorMessage |> validationError

let fetch (url:string) =
    let op() = tryGet url
    tryF' op (fun e -> APIQueryError (QueryText(url),e))

let fetchAllPages (url:string) =
    let op() = fetchAll url
    tryF' op (fun e -> APIQueryError (QueryText(url),e))

/// Fetch all URLs in parallel and pair the responses with their URL
let fetchAllParallel (urls:string seq) =
    let fetchOne url =
        try
            ok (url, Some(tryGet url))
        with 
        | ex -> 
            let msg = APIQueryError (QueryText(url),(ex.ToString()))
            Result.Succeed((url,None),msg)
    urls
    |> PSeq.map fetchOne
    |> seq
    |> collect

let deserializeAs domainModel jsonValues =
    let op() = jsonValues |> Seq.map domainModel
    tryF' op DTOtoDomainConversionFailure

let serialize resp = 
    let op() = resp |> JsonConvert.SerializeObject
    tryF' op DomainToDTOConversionFailure