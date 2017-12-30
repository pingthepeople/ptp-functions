
module Ptp.Workflow.Actions

open Chessie.ErrorHandling
open Ptp.Core
open Ptp.Http
open Ptp.Database

/// Get all actions that occurred today
let fetchRecentActions sessionYear = trial {
    let! links = 
        datestamp()
        |> sprintf "/%s/bill-actions?minDate=%s&per_page=200" sessionYear
        |> fetchAllLinks
    return links 
}    

/// Filter out the actions that we already know about (by their link)
let filterKnownActions =
    queryAndFilterKnownLinks "Action"

/// Enqueue new actions to be resolved
let nextSteps result =
    let steps links = 
        links |> Seq.map UpdateAction
    result |> workflowContinues steps

let workflow() =
    queryCurrentSessionYear()
    >>= fetchRecentActions
    >>= filterKnownActions
    |>  nextSteps