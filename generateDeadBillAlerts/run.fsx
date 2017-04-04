#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"

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

let deathChamber (action:Action) =
    match (action.ActionType) with
    | ActionType.AssignedToCommittee -> action.Chamber
    | ActionType.CommitteeReading    -> action.Chamber
    | ActionType.SecondReading       -> action.Chamber
    | ActionType.ThirdReading        ->
        match action.Chamber with
        | Chamber.House  -> Chamber.Senate
        | Chamber.Senate -> Chamber.House
        | _ -> failwith ("Unrecognized chamber")
    | _ -> failwith ("Unrecognized action type")

let deathReason (action:Action) = 
    match (action.ActionType) with
    | ActionType.AssignedToCommittee -> "committee hearing"
    | ActionType.CommitteeReading    -> "second reading"
    | ActionType.SecondReading       -> "third reading"
    | ActionType.ThirdReading        -> "committee assignment"
    | _ -> failwith ("Unrecognized action type")

// Format a nice description of the action
let formatBody sessionYear (bill:Bill) action messageType =
    let billName =
        match messageType with 
        | MessageType.Email -> bill.WebLink sessionYear
        | MessageType.SMS   -> Bill.PrettyPrintName bill.Name
        | _ -> failwith ("Unknown message type")

    let diedInChamber = 
        match action with
        | None    -> bill.Chamber
        | Some(x) -> x |> deathChamber

    let diedForReason = 
        match action with
        | None    -> "committee assignment"
        | Some(x) -> x |> deathReason

    sprintf "%s ('%s') has died in the %A upon missing the deadline for a %s." billName (bill.Title.TrimEnd('.')) diedInChamber diedForReason

// Create action alert messages for people that have opted-in to receiving them
let generateAlerts (bill:Bill) =
    let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
    let sessionYear = cn |> currentSessionYear
    let mostRecentAction = cn |> dapperParametrizedQuery<Action> "SELECT TOP 1 * FROM Action WHERE BillId = @Id ORDER BY Date DESC" {Id=bill.Id} |> Seq.tryHead
    let emailBody = formatBody sessionYear bill mostRecentAction MessageType.Email
    let smsBody = formatBody sessionYear bill mostRecentAction MessageType.SMS
    cn |> generateAlertsForBill bill (emailBody,smsBody)


// Azure function entry point

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json

let Run(bill: string, notifications: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed for action %s at %s" bill (timestamp()))
    try
        log.Info(sprintf "[%s] Generating dead bill alerts ..." (timestamp()))
        let messages = JsonConvert.DeserializeObject<Bill>(bill) |> generateAlerts
        log.Info(sprintf "[%s] Generating dead bill alerts [OK]" (timestamp()))

        log.Info(sprintf "[%s] Enqueueing dead bill alerts ..." (timestamp()))
        let enqueue json = 
            let trace = sprintf "  Enqueuing dead bill alert: %s" json 
            trace |> trackTrace "generateDeadBillAlerts"
            trace |> log.Info
            json |> notifications.Add
        messages 
        |> List.map JsonConvert.SerializeObject
        |> List.iter enqueue
        log.Info(sprintf "[%s] Enqueueing dead bill alerts [OK]" (timestamp()))
    with
    | ex -> 
        ex |> trackException "generateDeadBillAlerts"
        log.Error(sprintf "[%s] Encountered error: %s" (timestamp()) (ex.ToString())) 
        reraise()