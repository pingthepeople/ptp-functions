module Ptp.DataUpdateEvents

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open Ptp.Core

let Run(myTimer: TimerInfo, log: TraceWriter, nextCommand: ICollector<string>) =
    [Workflow.UpdateCalendars; Workflow.UpdateActions]
    |> List.map JsonConvert.SerializeObject
    |> List.iter nextCommand.Add