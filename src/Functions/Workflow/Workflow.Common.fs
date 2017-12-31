module Ptp.Workflow.Common

open Ptp.Model
open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Model
open Ptp.Core
open Ptp.Http
open Ptp.Database
open Ptp.Formatting
open Ptp.Cache
open Newtonsoft.Json

// JSON parsing functions for resolving members of committees and bills
let inline createMembership (unit:LinkAndId) (members: LinkAndId seq) position link = 
    let m = members |> Seq.tryFind (fun l -> l.Link = link)
    match m with
    | Some value -> [(unit.Id, value.Id, position)]
    | None   -> []

let doWithProperty name f (json:JsonValue) =
    match json.TryGetProperty(name) with
    | Some value -> value |> f
    | None -> []

let inline multiple unit candidates (json:JsonValue) toDomainObject property position = 
    let parseMultiple (value:JsonValue) =
        value.AsArray()
        |> Array.toList 
        |> List.map (fun m -> m?link.AsString())
        |> List.collect (createMembership unit candidates position)
        |> List.map toDomainObject
    json |> doWithProperty property parseMultiple

let inline single unit candidates (json:JsonValue) toDomainObject property position = 
    let parseMemberFromLink (value:JsonValue) =
        value.AsString()
        |> createMembership unit candidates position
        |> List.map toDomainObject        
    let parseLinkForPosition (value:JsonValue) =
        value |> doWithProperty "link" parseMemberFromLink
    json |> doWithProperty property parseLinkForPosition

// Notfications

[<CLIMutable>]
type Recipient = {Email: string; Mobile:string; ReceiveAlertEmail: bool; ReceiveAlertSms: bool}

let billQuery = "SELECT TOP 1 * FROM Bill WHERE Link = @Link"

let selectRecipients = """
SELECT 
    u.Email
    , u.Mobile
    , ub.ReceiveAlertEmail
    , ub.ReceiveAlertSms
FROM UserBill ub 
JOIN Users u 
    ON ub.UserId = u.Id
WHERE ub.BillId = @Id"""

/// If this is a new action of a known type, generate a list of recipients for notifications.
let resolveRecipients (bill:Bill) = trial {
    let! recipients = dbParameterizedQuery<Recipient> selectRecipients {Id=bill.Id}
    return (bill, recipients)
}

/// Generate email alerts for this action
let emailNotification formatBody (bill:Bill) = 
    let subject = sprintf "Update on %s" (printBillNameAndTitle bill)
    let body = markdownBillHrefAndTitle bill |> formatBody
    {MessageType=MessageType.Email; Subject=subject; Body=body; Recipient=""; Attachment=""}

/// Generate sms alerts for this action
let smsNotification formatBody (bill:Bill) =
    let body = printBillNameAndTitle bill |> formatBody
    {MessageType=MessageType.SMS; Subject=""; Body=body; Recipient=""; Attachment=""}

let fetchBill id =
    dbParameterizedQueryOne<Bill> billQuery id 

let composeNotifications formatBody (bill, recipients) =
    let op() =
        let emails = 
            let message = emailNotification formatBody bill
            recipients
            |> Seq.filter (fun r -> r.ReceiveAlertEmail)
            |> Seq.map (fun r -> {message with Recipient=r.Email})
        let texts = 
            let message = smsNotification formatBody bill
            recipients
            |> Seq.filter (fun r -> r.ReceiveAlertSms)
            |> Seq.map (fun r -> {message with Recipient=r.Mobile})
        emails 
        |> Seq.append texts
        |> Seq.map JsonConvert.SerializeObject
    tryFail op NotificationGenerationError

let generateNotifications formatBody (obj, billId) =
    (fetchBill id)
    >>= resolveRecipients
    >>= composeNotifications formatBody
