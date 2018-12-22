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
open Serilog
open Serilog.Core
open Serilog.Context
    
let logger =
    let appInsightsKey = System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")
    Serilog.Debugging.SelfLog.Enable(Console.Out);
    Serilog.LoggerConfiguration()
        .Enrich.WithDemystifiedStackTraces()
        .WriteTo.Console()
        .WriteTo.ApplicationInsightsTraces(appInsightsKey)
        .CreateLogger()

/// Workflow Functions

let enqueue (collector:ICollector<string>) workflow = 
    sprintf "Timer Trigger: Enqueueing %A" workflow 
    |> logger.Information
    workflow
    |> JsonConvert.SerializeObject
    |> collector.Add

[<FunctionName("Heartbeat")>]
let Heartbeat 
    ([<TimerTrigger("0 */1 * * * *")>] timer, 
        log: Microsoft.Extensions.Logging.ILogger) =
        log.LogInformation "Heartbeat (ILogger)"

[<FunctionName("UpdateLegislators")>]
let UpdateLegislators 
    ([<TimerTrigger("0 0 6 * * 1-5")>] timer,
        [<ServiceBus("command", EntityType.Queue, Connection="ServiceBus.ConnectionString")>] nextCommand: ICollector<string>) =
        Workflow.UpdateLegislators |> enqueue nextCommand

[<FunctionName("UpdateCommittees")>]
let UpdateCommittees 
    ([<TimerTrigger("0 10 6 * * 1-5")>] timer,
        [<ServiceBus("command", EntityType.Queue, Connection="ServiceBus.ConnectionString")>] nextCommand: ICollector<string>) =
    Console.Out.WriteLine("Enqueuing UpdateCommittees...")
    Workflow.UpdateCommittees |> enqueue nextCommand
    Console.Out.WriteLine("Enqueued UpdateCommittees.")

[<FunctionName("UpdateSubjectsAndBills")>]
let UpdateSubjectsAndBills
    ([<TimerTrigger("0 20 6 * * 1-5")>] timer,
        [<ServiceBus("command", EntityType.Queue, Connection="ServiceBus.ConnectionString")>] nextCommand: ICollector<string>) =
    Workflow.UpdateSubjects |> enqueue nextCommand

[<FunctionName("UpdateCalendarsAndActions")>]
let UpdateCalendarsAndActions
    ([<TimerTrigger("0 */10 8-20 * * 1-5")>] timer,
        [<ServiceBus("command", EntityType.Queue, Connection="ServiceBus.ConnectionString")>] nextCommand: ICollector<string>) =
    Workflow.UpdateCalendars |> enqueue nextCommand
    Workflow.UpdateActions |> enqueue nextCommand

[<FunctionName("UpdateDeadBills")>]
let UpdateDeadBills
    ([<TimerTrigger("0 0 9 * * 1-6")>] timer,
        [<ServiceBus("command", EntityType.Queue, Connection="ServiceBus.ConnectionString")>] nextCommand: ICollector<string>) =
    Workflow.UpdateDeadBills |> enqueue nextCommand

[<DisableAttribute("DISABLE_DAILY_ROUNDUP")>]
[<FunctionName("DailyRoundup")>]
let DailyRoundup
    ([<TimerTrigger("0 30 19 * * 1-5")>] timer,
        [<ServiceBus("command", EntityType.Queue, Connection="ServiceBus.ConnectionString")>] nextCommand: ICollector<string>) =
    Workflow.DailyRoundup |> enqueue nextCommand

[<FunctionName("Workflow")>]
let Workflow
    ([<ServiceBusTrigger("command", Connection="ServiceBus.ConnectionString")>] command,
        log: Microsoft.Extensions.Logging.ILogger,
        [<ServiceBus("command", EntityType.Queue, Connection="ServiceBus.ConnectionString")>] nextCommand: ICollector<string>,
        [<ServiceBus("notification", EntityType.Queue, Connection="ServiceBus.ConnectionString")>] notifications: ICollector<string>) =
    Ptp.Workflow.Orchestrator.Execute log command nextCommand notifications


/// Messaging Functions

[<FunctionName("Notifications")>]
let Notification
    ([<ServiceBusTrigger("notification", Connection="ServiceBus.ConnectionString")>] notification, 
        log: Microsoft.Extensions.Logging.ILogger) =
    Ptp.Messaging.Notification.Execute log notification


/// API Functions

[<FunctionName("GetLegislators")>]
let GetLegislators
    ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route="getLegislators")>] req, 
        log: Microsoft.Extensions.Logging.ILogger) =
    Ptp.API.GetLegislators.Execute log req

[<FunctionName("GenerateBillReport")>]
let GenerateBillReport
    ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route="generateBillReport")>] req, 
        log: Microsoft.Extensions.Logging.ILogger) =
    Ptp.API.GetBillReport.Execute log req