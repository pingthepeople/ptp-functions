#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Primitives"
#r "System.Net.Http.Formatting"
#r "System.Web.Http"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Formatting.Common.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Markdown.dll"
#r "../packages/StrongGrid/lib/net452/StrongGrid.dll"
#r "../packages/Twilio/lib/net451/Twilio.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

#load "../shared/model.fs"

open System
open System.IO
open System.Text
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open FSharp.Data
open FSharp.Formatting.Common
open FSharp.Markdown
open StrongGrid
open Twilio
open Twilio.Rest.Api.V2010.Account
open Twilio.Types
open IgaTracker.Model
open Newtonsoft.Json

let sendEmailNotification message= 
    let client = new StrongGrid.Client(Environment.GetEnvironmentVariable("SendGridApiKey"))
    let toAddress = new Model.MailAddress(message.Recipient, message.Recipient)
    let fromAddress = new Model.MailAddress("jhoerr@gmail.edu", "John Hoerr")
    let htmlContent = message.Body |> Markdown.Parse |> Markdown.WriteHtml
    client.Mail.SendToSingleRecipientAsync(toAddress, fromAddress, message.Subject, htmlContent, message.Body, trackOpens=false, trackClicks=false).Wait()

let sendSMSNotification message =
    let sid = Environment.GetEnvironmentVariable("Twilio.AccountSid")
    let token = Environment.GetEnvironmentVariable("Twilio.AuthToken")
    let phoneNumber = Environment.GetEnvironmentVariable("Twilio.PhoneNumber")
    let fromNumber = new PhoneNumber(phoneNumber)
    let toNumber = new PhoneNumber(message.Recipient)
    TwilioClient.Init(sid, token)
    MessageResource.Create(toNumber, from=fromNumber, body=message.Body) |> ignore

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
open Microsoft.Azure.WebJobs.Host

let Run(message: string, log: TraceWriter) =
    log.Info(sprintf "F# function 'sendNotification' executed  at %s" (DateTime.Now.ToString()))
    try
        let body = JsonConvert.DeserializeObject<Message>(message)
        log.Info(sprintf "Delivering %A message to '%s' re: '%s'" body.MessageType body.Recipient body.Subject)
        match body.MessageType with
        | MessageType.Email -> sendEmailNotification body
        | MessageType.SMS -> sendSMSNotification body
        | _ -> log.Error("unrecognized message type")
    with
    | ex -> 
        log.Error(sprintf "Encountered error: %s" (ex.ToString())) 
        reraise()