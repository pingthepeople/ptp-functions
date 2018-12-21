module Ptp.Messaging.Notification

open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Model
open Ptp.Database
open Ptp.Formatting
open Chessie.ErrorHandling
open Twilio
open Twilio.Rest.Api.V2010.Account
open Twilio.Types
open Newtonsoft.Json
open MailKit
open MimeKit
open MailKit.Net.Smtp
open Microsoft.Extensions.Logging

let ptpLogoMarkup = """
    <img alt="Ping the People logo" src="https://pingthepeopleprod.blob.core.windows.net/images/ptplogo.PNG" />
    <br/>
    <br/>
 """

let sendMail (msg:Message) = 
    let op() =
        let username = env "SendGrid.Username"
        let password = env "SendGrid.Password"
        let fromName = env "SendGrid.FromName" 
        let fromAddr = env "SendGrid.FromAddr"

        let generateBody m =
            let body = BodyBuilder()
            body.TextBody <- m.Body
            body.HtmlBody <- ptpLogoMarkup + (m.Body |> Markdig.Markdown.ToHtml)
            body.ToMessageBody()

        let mail = MimeMessage()
        mail.From.Add(MailboxAddress(fromName, fromAddr))
        mail.To.Add(MailboxAddress(msg.Recipient))
        mail.Subject <- msg.Subject
        mail.Body <- (msg |> generateBody)

        use client = new SmtpClient()
        // ignore cert validation...
        client.ServerCertificateValidationCallback <- (fun s c h e -> true)
        client.Connect ("smtp.sendgrid.com", 587, false);
        client.Authenticate (username, password);
        client.Send (mail);
        client.Disconnect (true);

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

let digest (msg:Message) =
    sprintf "%A;%s;%s;%s" msg.MessageType msg.Recipient msg.Subject msg.Body
    |> sha256Hash

let tryLogDeliveryQuery = """
MERGE INTO NotificationLog nl
USING (VALUES(
        @MessageType,
        @Recipient,
        @Subject,
        @Digest)) 
    X ([MessageType],[Recipient],[Subject],[Digest])
ON (nl.Digest=@Digest)
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([MessageType],[Recipient],[Subject],[Digest])
    VALUES (X.[MessageType],X.[Recipient],X.[Subject],X.[Digest])
OUTPUT $action;
"""

let generateLog msg = 
    let op()=
        let log = 
          { Digest= (digest msg)
            MessageType=msg.MessageType
            Recipient=msg.Recipient
            Subject=msg.Subject }
        (msg,log)
    tryFail op NotificationGenerationError

let logDelivery (msg,log) = trial {
    let! res = dbParameterizedQuery<string> tryLogDeliveryQuery log
    let inserted = res |> Seq.tryHead 
    return (msg,inserted)
}

let ensureNotYetDelivered (msg,inserted) =
    match inserted with
    | Some row -> ok msg
    | None -> fail NotificationAlreadyDelivered

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
    | Fail [NotificationAlreadyDelivered] -> ()
    | Fail errs -> throwErrors desc errs
    | _ -> ()

let workflow logDelivery sendSms sendMail msg = 
    fun () ->
        generateLog msg
        >>= logDelivery
        >>= ensureNotYetDelivered
        >>= deliver sendMail sendSms    
    
let Execute (log: ILogger) (notification: string) =
    match notification with
    | null -> 
        "Dequeued empty message" 
        |> log.LogWarning
    | n ->
        let msg = n |> JsonConvert.DeserializeObject<Message>
        let desc = sprintf "Notification (%A to: '%s' re: '%s')" msg.MessageType msg.Recipient msg.Subject
        workflow logDelivery sendSms sendMail msg
        |> executeWorkflow log desc
        |> evaluateResult desc
        |> ignore
