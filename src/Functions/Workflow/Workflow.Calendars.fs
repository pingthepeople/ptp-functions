module Ptp.Workflow.Calendars

open Chessie.ErrorHandling
open Ptp.Core
open Ptp.Http
open Ptp.Database
open Ptp.Model

/// Get all actions that occurred today
let fetchChamberCalendarLinks sessionYear chamber  = trial {
    let c = chamber.ToString().ToLower()
    let! links = 
        datestamp()
        |> sprintf "/%s/chambers/%s/calendars?minDate=%s&per_page=200" sessionYear c
        |> fetchAllLinks
    return links 
    } 

let fetchCommitteeCalendarLinks sessionYear = trial {
    let! links = 
        datestamp()
        |> sprintf "/%s/meetings?minDate=%s&per_page=200" sessionYear
        |> fetchAllLinks
    return links 
    }

let fetchCalendarLinks sessionYear = trial {
        let! h = Chamber.House |> fetchChamberCalendarLinks sessionYear
        let! s = Chamber.Senate |> fetchChamberCalendarLinks sessionYear
        let! c = fetchCommitteeCalendarLinks sessionYear
        return h |> Seq.append s |> Seq.append c
    }

/// Filter out the actions that we already know about (by their link)
let filterKnownCalendars =
    queryAndFilterKnownLinks "ScheduledAction"

/// Enqueue new actions to be resolved
let nextSteps result =
    let steps links = 
        links |> Seq.map UpdateCalendar
    result |> workflowContinues steps

let workflow() =
    queryCurrentSessionYear()
    >>= fetchCalendarLinks
    >>= filterKnownCalendars
    |>  nextSteps

