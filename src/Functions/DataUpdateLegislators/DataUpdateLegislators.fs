module Ptp.DataUpdateLegislators

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open Ptp.Core

let Run(myTimer: TimerInfo, log: TraceWriter, nextCommand: ICollector<string>) =
    Workflow.UpdateLegislators
    |> JsonConvert.SerializeObject
    |> nextCommand.Add