module Ptp.Http

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Newtonsoft.Json
open Ptp.Core
open Ptp.Logging
open System
open System.Net
open System.Net.Http
open FSharp.Collections.ParallelSeq

let get endpoint = 
    let uri = "https://api.iga.in.gov" + endpoint
    let standardHeaders = [ "Accept", "application/json"; "Authorization", "Token " + Environment.GetEnvironmentVariable("IgaApiKey") ]
    let func() = Http.RequestString(uri, httpMethod = "GET", headers = standardHeaders) 
    let result = trackDependency "http" uri func
    result |> JsonValue.Parse

let tryGet endpoint =
    try
        get endpoint
    with 
    | ex -> 
        Console.WriteLine(sprintf "failed to get %s: %s" endpoint (ex.ToString()))
        JsonValue.Null

let fetchAll endpoint =
    let rec fetchRec link =
        let json = get link
        let items = json?items.AsArray() |> Array.toList
        try
            let nextLink = json?nextLink.ToString().Trim('"')
            items @ (fetchRec nextLink)
        with
        | ex -> items
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

let fetchAllPages (query:string) =
    let op() = 
        query
        |> fetchAll
    tryF' op (fun e -> APIQueryError (QueryText(query),e))

let fetchAllParallel (queries:string seq) =
    match queries with
    | EmptySeq -> 
        Seq.empty |> ok 
    | _ ->
        let op() =
            queries
            |> PSeq.map fetchAll 
            |> PSeq.concat
            |> Seq.filter (fun j -> j <> JsonValue.Null)
        let query = sprintf "Multiple queries starting with %s" (queries |> Seq.head)
        tryF' op (fun e -> APIQueryError (QueryText(query),e))

let deserializeAs domainModel jsonValues =
    let op() =
        jsonValues
        |> Seq.map domainModel
    tryF' op DTOtoDomainConversionFailure
