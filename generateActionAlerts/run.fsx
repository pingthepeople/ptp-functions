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
let formatBody sessionYear (bill:Bill) (action:Action) includeLinks =
    let billName =
        match includeLinks with 
        | true -> bill.WebLink sessionYear
        | false -> Bill.PrettyPrintName bill.Name
    sprintf "%s ('%s') %s." billName (bill.Title.TrimEnd('.')) (action.Describe)

// Create action alert messages for people that have opted-in to receiving them
let generateAlerts (action:Action) =
    let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
    let sessionYear = cn |> currentSessionYear
    let bill = cn |> dapperParametrizedQuery<Bill> "SELECT * FROM Bill WHERE Id = @Id" {Id=action.BillId} |> Seq.head
    let emailBody = formatBody sessionYear bill action true
    let smsBody = formatBody sessionYear bill action false
    cn |> generateAlertsForBill bill (emailBody,smsBody)


// Azure function entry point

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json

let Run(action: string, notifications: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed for action %s at %s" action (timestamp()))
    try
        log.Info(sprintf "[%s] Generating action alerts ..." (timestamp()))
        let messages = JsonConvert.DeserializeObject<Action>(action) |> generateAlerts
        log.Info(sprintf "[%s] Generating action alerts [OK]" (timestamp()))

        log.Info(sprintf "[%s] Enqueueing action alerts ..." (timestamp()))
        let enqueue json = 
            let trace = sprintf "Enqueuing scheduled action alert: %s" (timestamp()) json
            trace |> trackTrace "generateActionAlerts"
            trace |> log.Info
            json |> notifications.Add
        messages 
        |> List.map JsonConvert.SerializeObject
        |> List.iter enqueue
        log.Info(sprintf "[%s] Enqueueing action alerts [OK]" (timestamp()))
    with
    | ex -> 
        ex |> trackException "generateActionAlerts"
        log.Error(sprintf "[%s] Encountered error: %s" (timestamp()) (ex.ToString())) 
        reraise()