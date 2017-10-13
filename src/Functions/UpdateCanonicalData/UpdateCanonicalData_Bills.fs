﻿module Ptp.UpdateCanonicalData.Bills

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Queries
open Ptp.Http
open Ptp.Database
open System

let resolveLastUpdateTimestamp results = 
    let head = results |> Seq.tryHead
    match head with
    | Some datetime -> ok datetime
    | None -> ok (DateTime(2000,1,1))

let queryText = sprintf """select IsNull((SELECT max(ApiUpdated) 
FROM Bill WHERE SessionId = %s),'2000-1-1')""" SessionIdSubQuery

let getLastUpdateTimestamp sessionYear = trial {
    let! lastUpdate = dbQueryOne<DateTime> queryText
    return (sessionYear, lastUpdate)
    }

let fetchRecentlyUpdatedBillsFromApi (sessionYear, lastUpdate) = trial {
    // get a listing of all bills
    let url = sprintf "/%s/bills?per_page=200" sessionYear
    let! pages = url |> fetchAllPages 
    // parse the url for each bill
    let billLink json = json?link.AsString()
    let! billUrls = pages |> deserializeAs billLink
    // grab the full bill metadata from each bill url
    let! metadata = billUrls |> Seq.take 20 |> fetchAllParallel
    // find the recently updated metadata based on the 'latestVersion.updated' timestamp
    let wasRecentlyUpdated json = 
        json?latestVersion?updated.AsDateTime() > lastUpdate
    let recentlyUpdated = metadata |> chooseSnd |> Seq.filter wasRecentlyUpdated
    return recentlyUpdated
    }

let nextSteps result =
    match result with
    | Ok (bills, msgs) ->
        let next = 
            bills 
            |> Seq.map (fun json -> json?Link.AsString())
            |> Seq.map UpdateBill
            |> NextWorkflow
        Next.Succeed(next,msgs)
    | Bad msgs ->       Next.FailWith(msgs)

let workflow() =
    getCurrentSessionYear()
    >>= getLastUpdateTimestamp
    >>= fetchRecentlyUpdatedBillsFromApi
    |>  nextSteps