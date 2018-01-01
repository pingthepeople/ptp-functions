module Ptp.Messaging

open Ptp.Database
open Ptp.Model
open Ptp.Formatting
open Chessie.ErrorHandling
open Ptp.Core

/// A markdown bill link and title 
let markdownBillHrefAndTitle bill =
    sprintf "[%s](%s) ('%s')" (printBillName bill) (webLink bill) bill.Title

/// A simple email subject, 'Update on <bill name> (<bill title>)'
let formatSubject bill =
    sprintf "Update on %s" (printBillNameAndTitle bill)

/// An SMS message
let smsAlert subject body recipient = 
    {
        MessageType=MessageType.SMS; 
        Recipient=recipient; 
        Subject=subject;
        Body=body; 
        Attachment=""
    }

/// An email message 
let emailAlert subject body recipient = 
    {
        MessageType=MessageType.Email; 
        Recipient=recipient;
        Subject=subject;
        Body=body; 
        Attachment=""
    }

let fetchBillQuery = "SELECT * FROM Bill WHERE Id = @Id"

let fetchRecipientsQuery = """
SELECT u.Email, u.Mobile, ub.ReceiveAlertEmail, ub.ReceiveAlertSms 
FROM users u
JOIN UserBill ub 
    ON ub.UserId = u.Id AND BillId = @Id
WHERE u.Id IN ( SELECT UserId FROM UserBill WHERE BillId = @Id )
"""

let fetchBill id =
    dbParameterizedQueryOne<Bill> fetchBillQuery {Id=id}

let fetchRecipients id bill = trial {
    let! recipients = dbParameterizedQuery<Recipient> fetchRecipientsQuery {Id=id}
    return (bill,recipients)
    }

let generateSmsNotifications formatBody (bill,recipients) =
    let subject = formatSubject bill
    let title = printBillNameAndTitle bill
    let body = formatBody title
    let msg = smsAlert subject body
    recipients 
    |> Seq.filter (fun r -> r.ReceiveAlertSms && r.Mobile <> null)
    |> Seq.map (fun r -> r.Mobile)
    |> Seq.map msg

let generateEmailNotifications formatBody (bill,recipients) =
    let subject = formatSubject bill
    let title = markdownBillHrefAndTitle bill
    let body = formatBody title
    let msg = emailAlert subject body
    recipients 
    |> Seq.filter (fun r -> r.ReceiveAlertEmail && r.Email <> null)
    |> Seq.map (fun r -> r.Email)
    |> Seq.map msg

let generateNotificationsForRecipients formatBody (bill,recipients) =
    let op() =
        let sms = generateSmsNotifications formatBody (bill,recipients)
        let emails = generateEmailNotifications formatBody (bill,recipients)
        Seq.append sms emails
    tryFail op NotificationGenerationError

/// Generate Email and SMS notification messages with the provided body 
/// formatter for users requesting alerts about the specified bill.
let generateNotifications formatBody billId =
    fetchBill billId
    >>= fetchRecipients billId
    >>= generateNotificationsForRecipients formatBody