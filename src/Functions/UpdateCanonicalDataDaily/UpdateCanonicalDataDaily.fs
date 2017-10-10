module Ptp.UpdateCanonicalDataDaily

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.Core

let Run(myTimer: TimerInfo, log: TraceWriter, nextCommand: ICollector<Workflow>) =
    Workflow.UpdateLegislators
    |> nextCommand.Add