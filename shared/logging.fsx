#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

namespace IgaTracker

module Logging =

    open System
    open Microsoft.ApplicationInsights

    let getTelemetryClient () =
        let client = TelemetryClient()
        client.InstrumentationKey <- System.Environment.GetEnvironmentVariable("ApplicationInsights.InstrumentationKey")
        client

    let trackException (ex:Exception) =
        getTelemetryClient().TrackException(ex)

    let trackTrace (trace:string) = 
        getTelemetryClient().TrackTrace(trace)

    let trackDependency name command func = 
        let start = System.DateTimeOffset(System.DateTime.UtcNow)
        let timer = System.Diagnostics.Stopwatch.StartNew()
        let trackDependency' success = 
            let telemetryClient = getTelemetryClient()
            telemetryClient.TrackDependency(name, command, start, timer.Elapsed, success)

        try
            let result = func()
            trackDependency' true
            result
        with
        | ex -> 
            trackDependency' false
            reraise()
