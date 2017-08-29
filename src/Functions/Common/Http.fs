module Ptp.Http

open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Logging
open System

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