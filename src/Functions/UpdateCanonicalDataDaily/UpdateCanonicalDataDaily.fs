module Ptp.UpdateCanonicalDataDaily

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.Core

let Run(myTimer: TimerInfo, log: TraceWriter, nextCommand: ICollector<Update>) =
    Update.Legislators
    |> nextCommand.Add