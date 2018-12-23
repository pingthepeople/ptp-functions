module Ptp.Functions

open Ptp.Common.Core
open Ptp.Workflow

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.ServiceBus
open System
open System.Net.Http
open Newtonsoft.Json
open Microsoft.Extensions.Logging
open Microsoft.Azure.WebJobs.Extensions.Http
    
/// Workflow Functions

[<Literal>]
let sb="AzureWebJobsServiceBus"

let enqueue (log:ILogger) (collector:ICollector<string>) workflow = 
    workflow
    |> tee (printfn "Enqueuing %A")
    |> JsonConvert.SerializeObject
    |> collector.Add

[<FunctionName("UpdateLegislators")>]
let UpdateLegislators 
    ([<TimerTrigger("0 0 6 * * 1-5")>] timer: TimerInfo)
    (log: ILogger)
    ([<ServiceBus("command", Connection=sb)>] nextCommand: ICollector<string>) =
        Workflow.UpdateLegislators |> enqueue log nextCommand

[<FunctionName("UpdateCommittees")>]
let UpdateCommittees 
    ([<TimerTrigger("0 10 6 * * 1-5")>] timer: TimerInfo)
    (log: ILogger)
    ([<ServiceBus("command", Connection=sb)>] nextCommand: ICollector<string>) =
    Workflow.UpdateCommittees |> enqueue log nextCommand
    
[<FunctionName("UpdateSubjectsAndBills")>]
let UpdateSubjectsAndBills
    ([<TimerTrigger("0 20 6 * * 1-5")>] timer: TimerInfo)
    (log: ILogger)
    ([<ServiceBus("command", Connection=sb)>] nextCommand: ICollector<string>) =
    Workflow.UpdateSubjects |> enqueue log nextCommand

[<FunctionName("UpdateCalendarsAndActions")>]
let UpdateCalendarsAndActions
    ([<TimerTrigger("0 */10 8-20 * * 1-5")>] timer: TimerInfo)
    (log: ILogger)
    ([<ServiceBus("command", Connection=sb)>] nextCommand: ICollector<string>) =
    Workflow.UpdateCalendars |> enqueue log nextCommand
    Workflow.UpdateActions |> enqueue log nextCommand

[<FunctionName("UpdateDeadBills")>]
let UpdateDeadBills
    ([<TimerTrigger("0 0 9 * * 1-6")>] timer: TimerInfo)
    (log: ILogger)
    ([<ServiceBus("command", Connection=sb)>] nextCommand: ICollector<string>) =
    Workflow.UpdateDeadBills |> enqueue log nextCommand

[<FunctionName("DailyRoundup")>]
let DailyRoundup
    ([<TimerTrigger("0 30 19 * * 1-5")>] timer: TimerInfo)
    (log: ILogger)
    ([<ServiceBus("command", Connection=sb)>] nextCommand: ICollector<string>) =
    Workflow.DailyRoundup |> enqueue log nextCommand

[<FunctionName("Workflow")>]
let Workflow
    ([<ServiceBusTrigger("command", Connection=sb)>] command)
    (log: ILogger)
    ([<ServiceBus("command", Connection=sb)>] nextCommand: ICollector<string>)
    ([<ServiceBus("notification", Connection=sb)>] notifications: ICollector<string>) =
    Ptp.Workflow.Orchestrator.Execute log command nextCommand notifications


/// Messaging Functions

[<FunctionName("Notifications")>]
let Notification
    ([<ServiceBusTrigger("notification", Connection=sb)>] notification)
    (log: ILogger) =
    Ptp.Messaging.Notification.Execute log notification


/// API Functions

[<FunctionName("GetLegislators")>]
let GetLegislators
    ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route="getLegislators")>] req)
    (log: ILogger) =
    Ptp.API.GetLegislators.Execute log req

[<FunctionName("GenerateBillReport")>]
let GenerateBillReport
    ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route="generateBillReport")>] req)
    (log: ILogger) =
    Ptp.API.GetBillReport.Execute log req