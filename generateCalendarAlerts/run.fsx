#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"

#load "../shared/db.fsx"
#load "../shared/alert.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open IgaTracker.Model
open IgaTracker.Db
open IgaTracker.Alert
open IgaTracker.Logging

// Format a nice description of the action
let formatBody sessionYear (bill:Bill) (scheduledAction:ScheduledAction) includeLink =
    let billName =
        match includeLink with
        | true ->  bill.WebLink sessionYear
        | false -> Bill.PrettyPrintName bill.Name
    sprintf "%s ('%s') %s." billName (bill.Title.TrimEnd('.')) (scheduledAction.Describe includeLink)

// Create action alert messages for people that have opted-in to receiving them
let generateAlerts (scheduledAction:ScheduledAction) =
    let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
    let sessionYear = cn |> currentSessionYear
    let bill = cn |> dapperParametrizedQuery<Bill> "SELECT * FROM Bill WHERE Id = @Id" {Id=scheduledAction.BillId} |> Seq.head
    let emailBody = formatBody sessionYear bill scheduledAction true
    let smsBody = formatBody sessionYear bill scheduledAction false
    cn |> generateAlertsForBill bill (emailBody,smsBody)
    

// Azure function entry point

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json

let Run(scheduledAction: string, notifications: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed for scheduled action %s at %s" scheduledAction (timestamp()))
    try
        log.Info(sprintf "[%s] Generating scheduled action alerts ..." (timestamp()))
        let messages = JsonConvert.DeserializeObject<ScheduledAction>(scheduledAction) |> generateAlerts
        log.Info(sprintf "[%s] Generating scheduled action alerts [OK]" (timestamp()))

        log.Info(sprintf "[%s] Enqueueing scheduled action alerts ..." (timestamp()))
        let enqueue msg = 
            let json = msg |> JsonConvert.SerializeObject
            log.Info(sprintf "[%s]   Enqueuing scheduled action alert: %s" (timestamp()) json)
            json |> notifications.Add
        messages |> List.iter enqueue
        log.Info(sprintf "[%s] Enqueueing scheduled action alerts [OK]" (timestamp()))
    with
    | ex -> 
        trackException ex
        log.Error(sprintf "[%s] Encountered error: %s" (timestamp()) (ex.ToString())) 
        reraise()