module Ptp.Workflow.Actions

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Http
open Ptp.Database

/// Get all actions that occurred today
let fetchRecentActions sessionYear = trial {
    let minDate = datestamp()
    let url = sprintf "/%s/bill-actions?minDate=%s&per_page=200" sessionYear minDate
    let! actions = url |> fetchAllPages 
    let! actionLinks = actions |> deserializeAs (fun json -> json?link.AsString())
    return actionLinks
}    

/// Filter out the actions that we already know about (by their link)
let filterKnownActions actionLinks =
    let query = 
        actionLinks 
        |> toSqlValuesList
        |> sprintf """
SELECT a.Link FROM 
( VALUES %s ) AS a(Link)
EXCEPT SELECT Link FROM Action;
"""
    dbQuery<string> query

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