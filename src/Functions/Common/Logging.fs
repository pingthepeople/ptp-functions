module Ptp.Logging

open Ptp.Core
open Chessie.ErrorHandling
open Microsoft.ApplicationInsights
open Microsoft.Azure.WebJobs.Host
open System

let telemetryClient = 
    lazy (
        let client = TelemetryClient()
        client.InstrumentationKey <- System.Environment.GetEnvironmentVariable("ApplicationInsights.InstrumentationKey")
        client
    )

let trackException source ex =
    let props = dict["source",source]
    telemetryClient.Force().TrackException(ex, props)

let trackTrace source trace = 
    let props = dict["source",source]
    telemetryClient.Force().TrackTrace(trace, props)

let trackRequest name start duration responseCode success = 
    telemetryClient.Force().TrackRequest(name, start, duration, responseCode, success)

let trackDependency name command func = 
    let start = System.DateTimeOffset(System.DateTime.UtcNow)
    let timer = System.Diagnostics.Stopwatch.StartNew()
    let trackDependency' success = 
        telemetryClient.Force().TrackDependency(name, command, start, timer.Elapsed, success)

    try
        let result = func()
        trackDependency' true
        result
    with
    | ex -> 
        trackDependency' false
        reraise()

let success msg = 
    sprintf "[SUCCESS] %s" msg

let toError (msgs:WorkFlowFailure list) = 
    msgs 
    |> List.rev 
    |> List.map (fun wf -> wf.ToString())
    |> String.concat "\n" 

/// Log succesful or failed completion of the function, along with any warnings.
let evaluate (log:TraceWriter) source workflow = 

    log.Info(sprintf "[START] %A" source)

    let result = workflow()
    match result with
    | Fail (boo) ->  
        boo |> toError |> log.Error
    | Warn (yay,boo) ->  
        boo |> toError |> log.Warning
        yay |> success |> log.Info
    | Pass (yay) -> 
        yay |> success |> log.Info
    
    log.Info(sprintf "[FINISH] %A" source)
    
    result
   
let throwOnFail result =
    match result with   
    | Fail (boo) ->
        boo |> toError |> Exception |> raise
    | _ -> ignore

let format msgs =
    msgs 
    |> Seq.map (fun m -> sprintf "* %s" (m.ToString()))
    |> String.concat "\n"

let logStart (log:TraceWriter) source =
    log.Info(sprintf "[START] %s" source)

let logFinish (log:TraceWriter) source str =
    log.Info(sprintf "[FINISH] %s\n%s" source str)

let logError (log:TraceWriter) source str =
    log.Error(sprintf "[ERROR] %s:\n%s" source str)
    str

let onSuccess (log:TraceWriter) source (resp,msgs) =
    format msgs
    |> logFinish log source 

let onFailure  (log:TraceWriter) source msgs =
    format msgs
    |> logError log source

let haltOnFail (log:TraceWriter) source twoTrackInput =
    let failure (msgs) =
        onFailure log source msgs
        |> failwith
    let success = 
        onSuccess log source
    eitherTee success failure twoTrackInput 

let continueOnFail (log:TraceWriter) source twoTrackInput =
    let failure (msgs) =
        onFailure log source msgs
        |> ignore
    let success = 
        onSuccess log source
    eitherTee success failure twoTrackInput 

let logNewAdditions (log:TraceWriter) category (items: string list) = 
    match items with
    | [] -> log.Info(sprintf "No new %ss found." category)
    | _  ->
        items 
        |> String.concat "\n"
        |> (fun s -> log.Info((sprintf "Found new %ss:\n%s" category s)))



let describeList (items: string seq) = 
    match items with
    | EmptySeq -> 
        "No new items."
    | _  ->
        items 
        |> String.concat "\n"
        |> sprintf "Found new items:\n%s"

let inline describeNewItems toString items =
    items 
    |> Seq.map toString
    |> describeList
    |> ok
