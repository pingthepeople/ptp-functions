module Ptp.Messaging

open Ptp.Database
open Ptp.Model
open Ptp.Formatting
open Chessie.ErrorHandling
open Ptp.Core
open System

let linebreak = "<br/>"
let hr = "---"
let timeZone = "All times are Eastern Standard Time (EST)."
let locations = "All room and chamber locations are in the [Indiana State Capitol building](https://www.google.com/maps/place/Indiana+State+Capitol,+Indianapolis,+IN+46204/@39.7687106,-86.1650449,17z). Refer to [the building floor maps](https://iga.in.gov/information/location_maps) to find your room."
let settings = "You received this email because you requested legislative alerts from [Ping the People](https://pingthepeople.org). You can [update your legislative watchlist](https://pingthepeople.org) to change the type of alerts you receive, or to stop receiving them altogether. If you have comments or need help, please contact [help@pingthepeople.org](mailto:help@pingthepeople.org?subject=Legislative%20alerts)."
let closing = [linebreak; hr; linebreak; settings; timeZone; locations]

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
    let body = [body] @ closing |> String.concat "\n\n"
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
    let body = formatBody bill title
    let msg = smsAlert subject body
    recipients 
    |> Seq.filter (fun r -> r.ReceiveAlertSms && (String.IsNullOrWhiteSpace(r.Mobile) = false))
    |> Seq.map (fun r -> r.Mobile)
    |> Seq.distinct
    |> Seq.map msg

let generateEmailNotifications formatBody (bill,recipients) =
    let subject = formatSubject bill
    let title = markdownBillHrefAndTitle bill
    let body = formatBody bill title
    let msg = emailAlert subject body
    recipients 
    |> Seq.filter (fun r -> r.ReceiveAlertEmail && (String.IsNullOrWhiteSpace(r.Email) = false))
    |> Seq.map (fun r -> r.Email)
    |> Seq.distinct
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