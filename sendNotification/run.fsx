#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Primitives"
#r "System.Net.Http.Formatting"
#r "System.Web.Http"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Formatting.Common.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Markdown.dll"
#r "../packages/Twilio/lib/net451/Twilio.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

#load "../shared/csv.fsx"

open System
open System.IO
open System.Text
open System.Net
open System.Net.Http
open System.Net.Mail
open System.Net.Mime
open System.Threading
open System.Threading.Tasks
open FSharp.Data
open FSharp.Formatting.Common
open FSharp.Markdown
open Twilio
open Twilio.Rest.Api.V2010.Account
open Twilio.Types
open IgaTracker.Model
open IgaTracker.Csv
open IgaTracker.Logging
open Newtonsoft.Json

let getAttachment filename = 
    let path = System.IO.Path.GetTempFileName()
    filename |> downloadBlob (Environment.GetEnvironmentVariable("AzureStorage.ConnectionString")) path
    path

let sendEmailNotification message= 
    let mailMsg = new MailMessage();
    // To
    mailMsg.To.Add(new MailAddress(message.Recipient, message.Recipient));
    // From
    mailMsg.From <- new MailAddress("notifications@pingthepeople.org", "Ping the People");
    // Subject and multipart/alternative Body
    mailMsg.Subject <- message.Subject
    let text = message.Body
    let html = text |> Markdown.Parse |> Markdown.WriteHtml
    AlternateView.CreateAlternateViewFromString(text, null, MediaTypeNames.Text.Plain) |> mailMsg.AlternateViews.Add;
    AlternateView.CreateAlternateViewFromString(html, null, MediaTypeNames.Text.Html) |> mailMsg.AlternateViews.Add;

    let smtpClient = new SmtpClient("smtp.sendgrid.net", Convert.ToInt32(587));
    smtpClient.Credentials <- new System.Net.NetworkCredential("apikey", Environment.GetEnvironmentVariable("SendGridApiKey"))

    let sendMessage() = mailMsg |> smtpClient.Send
    
    // Attachment
    match message.Attachment with
    | "" -> 
        trackDependency "email" "message" sendMessage
    | filename ->
        let path = getAttachment filename
        let stream = File.OpenRead(path)
        new Attachment(stream, filename, "text/csv") |> mailMsg.Attachments.Add
        trackDependency "email" "messageWithAttachment" sendMessage
        stream.Close()
        path |> File.Delete


let sendSMSNotification message =
    let sid = Environment.GetEnvironmentVariable("Twilio.AccountSid")
    let token = Environment.GetEnvironmentVariable("Twilio.AuthToken")
    let phoneNumber = Environment.GetEnvironmentVariable("Twilio.PhoneNumber")
    let fromNumber = new PhoneNumber(phoneNumber)
    let toNumber = new PhoneNumber(message.Recipient)
    let sendMessage() =
        TwilioClient.Init(sid, token)
        MessageResource.Create(toNumber, from=fromNumber, body=message.Body) |> ignore
    trackDependency "sms" "message" sendMessage

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
open Microsoft.Azure.WebJobs.Host

let Run(message: string, log: TraceWriter) =
    log.Info(sprintf "F# function 'sendNotification' executed  at %s" (timestamp()))
    try
        let body = JsonConvert.DeserializeObject<Message>(message)
        let trace = sprintf "Delivering %A message to '%s' re: '%s'" body.MessageType body.Recipient body.Subject
        trace |> log.Info
        trace |> trackTrace "sendNotification"
        
        match body.MessageType with
        | MessageType.Email -> sendEmailNotification body
        | MessageType.SMS -> sendSMSNotification body
        | _ -> log.Error("unrecognized message type")
    with
    | ex -> 
        ex |> trackException "sendNotification"
        log.Error(sprintf "Encountered error: %s" (ex.ToString())) 
        reraise()