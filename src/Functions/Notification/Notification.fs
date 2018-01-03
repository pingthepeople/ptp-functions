module Ptp.Notification.Function

open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Model
open FSharp.Markdown
open SendGrid;
open SendGrid.Helpers.Mail
open Twilio;
open Twilio.Rest.Api.V2010.Account;
open Twilio.Types;

let ptpLogoMarkup = """
    <img alt="Ping the People logo" src="https://pingthepeopleprod.blob.core.windows.net/images/ptplogo.PNG" />
    <br/>
    <br/>
 """

let sendMail msg = 
    let apiKey = env "SendGrid.ApiKey"
    let fromAddr = env "SendGrid.FromAddr"
    let fromName = env "SendGrid.FromName" 
    let from = EmailAddress(fromAddr, fromName)
    let toAddr = msg.Recipient |> EmailAddress
    let subject = msg.Subject
    let textContent = msg.Body
    let htmlContent = ptpLogoMarkup + (msg.Body |> Markdown.Parse |> Markdown.WriteHtml)
    let mail = MailHelper.CreateSingleEmail(from, toAddr, subject, textContent, htmlContent)
    SendGridClient(apiKey).SendEmailAsync(mail).Wait()

let sendSms msg = 
    let body = sprintf "[Ping the People] %s" msg.Body
    let accountSid = env "Twilio.AccountSid"
    let authToken = env "Twilio.AuthToken"
    let fromNumber = env "Twilio.From"
    TwilioClient.Init(accountSid, authToken)
    let toNumber = PhoneNumber(msg.Recipient)
    let fromNumber = PhoneNumber(fromNumber)
    MessageResource.Create(``to``=toNumber, from=fromNumber, body=body) |> ignore

let Run(log: TraceWriter, notification: string) =
    match deserializeQueueItem<Message> log notification with
    | Some msg -> 
        sprintf "Sending %A to %s re: %s" msg.MessageType msg.Recipient msg.Subject
        |> log.Info
        match msg.MessageType with
        | MessageType.Email ->  msg |> sendMail
        | MessageType.SMS ->    msg |> sendSms
        | _ ->
            sprintf "Unrecognized message type '%A'" msg.MessageType
            |> failwith
    | None -> ()
