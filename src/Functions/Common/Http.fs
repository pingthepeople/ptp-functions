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

let get endpoint = 
    let uri = "https://api.iga.in.gov" + endpoint
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

let constructHttpResponse twoTrackInput =
    let success(resp,msgs) = 
        httpResponse HttpStatusCode.OK resp
    let failure (msgs) = 
        let (status,error) = msgs |> Seq.head
        httpResponse status {Error=error}
    either success failure twoTrackInput 

let validateBody<'T> (errorMessage:string) (req:HttpRequestMessage) =
    let emptyContent = (HttpStatusCode.BadRequest, errorMessage)
    if req.Content = null 
    then fail emptyContent 
    else
        let content = req.Content.ReadAsStringAsync().Result
        if isEmpty content      
        then fail emptyContent 
        else ok (content |> JsonConvert.DeserializeObject<'T>)

let fetch (url:string) =
    let op() = 
        tryGet url
    tryF' op (fun e -> APIQueryError (QueryText(url),e))

let fetchAllPages (url:string) =
    let op() = 
        fetchAll url
    tryF' op (fun e -> APIQueryError (QueryText(url),e))

let fetchAllParallel (urls:string seq) =
    match urls with
    | EmptySeq -> 
        Seq.empty |> ok 
    | _ ->
        let op() =
            urls
            |> PSeq.map fetchAll 
            |> PSeq.concat
            |> Seq.filter (fun j -> j <> JsonValue.Null)
        let query = sprintf "Multiple queries starting with %s" (urls |> Seq.head)
        tryF' op (fun e -> APIQueryError (QueryText(query),e))

let deserializeAs domainModel jsonValues =
    let op() =
        jsonValues
        |> Seq.map domainModel
    tryF' op DTOtoDomainConversionFailure
