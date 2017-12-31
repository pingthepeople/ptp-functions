module Ptp.DataUpdateSubjectsBills

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open Ptp.Core

let Run(myTimer: TimerInfo, log: TraceWriter, nextCommand: ICollector<string>) =
    Workflow.UpdateSubjects
    |> JsonConvert.SerializeObject
    |> nextCommand.Add