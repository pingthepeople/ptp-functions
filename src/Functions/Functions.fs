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
open Microsoft.Azure.WebJobs.Extensions.Storage
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.Azure.WebJobs.Extensions.Timers

    
[<Literal>]
let sbConnection = "ServiceBus.ConnectionString"

/// Workflow Functions

let enqueue (collector:ICollector<string>) workflow = 
    Workflow.UpdateLegislators
    |> JsonConvert.SerializeObject
    |> collector.Add

[<FunctionName("UpdateLegislators")>]
let UpdateLegislators 
    ([<TimerTrigger("0 0 6 * * 1-5")>] timer,
        [<ServiceBus("command", Connection=sbConnection)>] nextCommand: ICollector<string>) =
        Workflow.UpdateLegislators |> enqueue nextCommand

[<FunctionName("UpdateCommittees")>]
let UpdateCommittees 
    ([<TimerTrigger("0 10 6 * * 1-5")>] timer,
        [<ServiceBus("command", Connection=sbConnection)>] nextCommand: ICollector<string>) =
    Workflow.UpdateCommittees |> enqueue nextCommand

[<FunctionName("UpdateSubjectsAndBills")>]
let UpdateSubjectsAndBills
    ([<TimerTrigger("0 20 6 * * 1-5")>] timer,
        [<ServiceBus("command", Connection=sbConnection)>] nextCommand: ICollector<string>) =
    Workflow.UpdateSubjects |> enqueue nextCommand

[<FunctionName("UpdateCalendarsAndActions")>]
let UpdateCalendarsAndActions
    ([<TimerTrigger("0 */10 8-20 * * 1-5")>] timer,
        [<ServiceBus("command", Connection=sbConnection)>] nextCommand: ICollector<string>) =
    Workflow.UpdateCalendars |> enqueue nextCommand
    Workflow.UpdateActions |> enqueue nextCommand

[<FunctionName("UpdateDeadBills")>]
let UpdateDeadBills
    ([<TimerTrigger("0 0 9 * * 1-6")>] timer,
        [<ServiceBus("command", Connection=sbConnection)>] nextCommand: ICollector<string>) =
    Workflow.UpdateDeadBills |> enqueue nextCommand

[<DisableAttribute("DISABLE_DAILY_ROUNDUP")>]
[<FunctionName("DailyRoundup")>]
let DailyRoundup
    ([<TimerTrigger("0 30 19 * * 1-5")>] timer,
        [<ServiceBus("command", Connection=sbConnection)>] nextCommand: ICollector<string>) =
    Workflow.DailyRoundup |> enqueue nextCommand

[<FunctionName("Workflow")>]
let Workflow
    ([<ServiceBusTrigger("command", Connection=sbConnection)>] command,
        log: ILogger,
        [<ServiceBus("command", Connection=sbConnection)>] nextCommand: ICollector<string>,
        [<ServiceBus("notification", Connection=sbConnection)>] notifications: ICollector<string>) =
    Ptp.Workflow.Orchestrator.Execute log command nextCommand notifications


/// Messaging Functions

[<FunctionName("Notifications")>]
let Notification
    ([<ServiceBusTrigger("notification", Connection=sbConnection)>] notification, log: ILogger) =
    Ptp.Messaging.Notification.Execute log notification


/// API Functions

[<FunctionName("GetLegislators")>]
let GetLegislators
    ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route="getLegislators")>] req, log: ILogger) =
    Ptp.API.GetLegislators.Execute log req

[<FunctionName("GenerateBillReport")>]
let GenerateBillReport
    ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route="generateBillReport")>] req, log: ILogger) =
    Ptp.API.GetBillReport.Execute log req