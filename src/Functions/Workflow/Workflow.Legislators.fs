module Ptp.Workflow.Legislators

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Queries
open Ptp.Http
open Ptp.Database

/// Fetch URLs for all legislators in the current session.
let fetchAllLegislatorsFromApi sessionYear = trial {
    let url = sprintf "/%s/legislators" sessionYear
    let! items = url |> fetchAllPages
    let link json = json?link.AsString()
    let! result = items |> deserializeAs link
    return result
    }

let fetchAllKnownLegislatorsQuery = 
    sprintf "SELECT Link from Legislator WHERE SessionId = %s" SessionIdSubQuery

let resolveUnknownLegislators allLegs = trial {
    let! knownLegs = dbQuery<string> fetchAllKnownLegislatorsQuery
    return allLegs |> except knownLegs
    }

let nextSteps result =
    let steps links = 
        links 
        |> List.map UpdateLegislator
    result |> workflowContinues steps

let workflow() =
    queryCurrentSessionYear()
    >>= fetchAllLegislatorsFromApi
    >>= resolveUnknownLegislators
    |>  nextSteps