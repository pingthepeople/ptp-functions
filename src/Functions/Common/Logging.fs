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
   
let format msgs =
    msgs 
    |> Seq.map (fun m -> sprintf "* %s" (m.ToString()))
    |> String.concat "\n"

let onSuccess (log:TraceWriter) source (resp,msgs) =
    format msgs
    |> (fun f -> log.Info (sprintf "[FINISH] %s\n%s" source f))

let onFailure  (log:TraceWriter) source msgs =
    format msgs
    |> (fun error -> sprintf "[ERROR] %s:\n%s" source error)
    |> tee log.Error

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