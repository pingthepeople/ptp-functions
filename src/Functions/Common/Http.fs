module Ptp.Http

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Newtonsoft.Json
open Ptp.Logging
open System
open System.Net
open System.Net.Http

let get endpoint = 
    let uri = "https://api.iga.in.gov" + endpoint
    let standardHeaders = [ "Accept", "application/json"; "Authorization", "Token " + Environment.GetEnvironmentVariable("IgaApiKey") ]
    let func() = Http.RequestString(uri, httpMethod = "GET", headers = standardHeaders) 
    let result = trackDependency "http" uri func
    result |> JsonValue.Parse

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