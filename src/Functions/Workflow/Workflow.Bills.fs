module Ptp.Workflow.Bills

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Queries
open Ptp.Http
open Ptp.Database
open System

(*
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
*)

let fetchRecentlyUpdatedBillsFromApi sessionYear = trial {
    // get a listing of all bills
    let url = sprintf "/%s/bills?per_page=200" sessionYear
    let! pages = url |> fetchAllPages 
    // parse the url for each bill
    let! billUrls = 
        pages |> deserializeAs (fun json -> json?Link.AsString())
    return billUrls
    }

let nextSteps result =
    let steps links = links |> Seq.map UpdateBill
    result |> workflowContinues steps

let workflow() =
    queryCurrentSessionYear()
    >>= fetchRecentlyUpdatedBillsFromApi
    |>  nextSteps