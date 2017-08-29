module Ptp.Logging

open Microsoft.ApplicationInsights
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