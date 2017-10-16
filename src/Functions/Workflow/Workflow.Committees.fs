module Ptp.Workflow.Committees

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Http
open Ptp.Database

// COMMITTEES

/// Fetch URLs for all committees in the current session year.
let fetchAllCommitteesFromAPI sessionYear = trial {
    let url = sprintf "/%s/committees" sessionYear
    let! pages = url |> fetchAllPages
    let link json = json?link.AsString()
    let! committeeUrls = pages |> deserializeAs link
    return committeeUrls
    }

let nextSteps result =
    match result with
    | Ok (links, msgs) ->
        let next = 
            links
            |> Seq.map UpdateCommittee
            |> NextWorkflow
        Next.Succeed(next,msgs)
    | Bad msgs ->       
        Next.FailWith(msgs)

/// Fetch and enqueue all commitees for processing
let workflow() =
    getCurrentSessionYear()
    >>= fetchAllCommitteesFromAPI
    |>  nextSteps