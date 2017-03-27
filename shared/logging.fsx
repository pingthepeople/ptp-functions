#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

namespace IgaTracker

module Logging =

    open System
    open Microsoft.ApplicationInsights

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
