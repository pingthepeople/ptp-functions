module Ptp.Logging

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
    |> Seq.collect (fun m -> m.ToString())
    |> Seq.toArray
    |> (fun a -> String.Join ("\n", a))

let logResult (log:TraceWriter) source twoTrackInput =
    let success(resp,msgs) = 
        match msgs |> Seq.isEmpty with
        | true -> 
            log.Info (sprintf "%s succeeded" source)
        | false -> 
            log.Info (sprintf "%s succeeded with messages:\n%s" source (format msgs))
    let failure (msgs) = 
        log.Error (sprintf "%s failed:\n%s" source (format msgs))
    eitherTee success failure twoTrackInput 