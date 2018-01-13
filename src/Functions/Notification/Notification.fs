module Ptp.Notification.Function

open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Model
open Ptp.Database
open Chessie.ErrorHandling
open FSharp.Markdown
open SendGrid;
open SendGrid.Helpers.Mail
open Twilio;
open Twilio.Rest.Api.V2010.Account;
open Twilio.Types;
open System.Security.Cryptography
open System.Text
open Newtonsoft.Json
open Ptp.Core

let ptpLogoMarkup = """
    <img alt="Ping the People logo" src="https://pingthepeopleprod.blob.core.windows.net/images/ptplogo.PNG" />
    <br/>
    <br/>
 """

let sendMail (msg:Message) = 
    let op() =
        let apiKey = env "SendGrid.ApiKey"
        let fromAddr = env "SendGrid.FromAddr"
        let fromName = env "SendGrid.FromName" 
        let from = EmailAddress(fromAddr, fromName)
        let toAddr = msg.Recipient |> EmailAddress
        let subject = msg.Subject
        let textContent = msg.Body
        let htmlContent = ptpLogoMarkup + (msg.Body |> Markdown.Parse |> Markdown.WriteHtml)
        let mail = MailHelper.CreateSingleEmail(from, toAddr, subject, textContent, htmlContent)
        SendGridClient(apiKey).SendEmailAsync(mail).Wait() |> ignore
        msg
    tryFail op NotificationDeliveryError

let sendSms (msg:Message) = 
    let op() =
        let body = sprintf "[Ping the People] %s" msg.Body
        let accountSid = env "Twilio.AccountSid"
        let authToken = env "Twilio.AuthToken"
        let fromNumber = env "Twilio.From"
        TwilioClient.Init(accountSid, authToken)
        let toNumber = PhoneNumber(msg.Recipient)
        let fromNumber = PhoneNumber(fromNumber)
        MessageResource.Create(``to``=toNumber, from=fromNumber, body=body) |> ignore
        msg
    tryFail op NotificationDeliveryError

let digest (str:string) = 
    str
    |> Encoding.UTF8.GetBytes
    |> SHA256Managed.Create().ComputeHash
    |> Array.map (fun b -> b.ToString("x2").ToUpper())
    |> String.concat ""
    
let tryLogDeliveryQuery = """
    INSERT INTO NotificationLog (Recipient,MessageType,Subject,Digest,UserId)
    VALUES (@Recipient
           ,@MessageType
           ,@Subject
           ,@Digest
           ,( SELECT Id FROM users
              WHERE (@MessageType=1 AND Email=@Recipient)
                 OR (@MessageType=2 AND Mobile=@Recipient)))
"""

let generateLog msg = 
    let op()=
        let log = 
          { Digest=(digest msg.Body)
            MessageType=msg.MessageType
            Recipient=msg.Recipient
            Subject=msg.Subject }
        (msg,log)
    tryFail op NotificationGenerationError

let logDelivery (msg,log) = trial {
    let! id = dbParameterizedQueryOne<int> tryLogDeliveryQuery log
    return (msg,id)
}


let deliver sendMail sendSms (msg:Message) =
    match msg.MessageType with
    | MessageType.Email ->  msg |> sendMail
    | MessageType.SMS ->    msg |> sendSms
    | _ -> 
        sprintf "Unrecognized message type '%A'" msg.MessageType
        |> NotificationDeliveryError
        |> fail   
        
let evaluateResult desc result = 
    match result with
    | Fail errs -> throwErrors desc errs
    | _ -> ()

let workflow logDelivery sendSms sendMail msg = 
    fun () ->
        generateLog msg
        >>= logDelivery
        >>= deliver sendMail sendSms    
    
let Run(log: TraceWriter, notification: string) =
    match notification with
    | null -> 
        "Dequeued empty message" 
        |> log.Warning
    | n ->
        let msg = n |> JsonConvert.DeserializeObject<Message>
        let desc = sprintf "Notification (%A to: '%s' re: '%s')" msg.MessageType msg.Recipient msg.Subject
        workflow logDelivery sendSms sendMail msg
        |> executeWorkflow log desc
        |> evaluateResult desc
        |> ignore
