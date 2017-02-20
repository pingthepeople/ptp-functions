#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Primitives"
#r "System.Net.Http.Formatting"
#r "System.Web.Http"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Formatting.Common.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Markdown.dll"
#r "../packages/StrongGrid/lib/net452/StrongGrid.dll"

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
open IgaTracker.Model

let sendEmailNotification message= 
    let client = new StrongGrid.Client(Environment.GetEnvironmentVariable("SendGridApiKey"))
    let toAddress = new Model.MailAddress(message.Recipient, message.Recipient)
    let fromAddress = new Model.MailAddress("jhoerr@gmail.edu", "John Hoerr")
    let htmlContent = message.Body |> Markdown.Parse |> Markdown.WriteHtml
    client.Mail.SendToSingleRecipientAsync(toAddress, fromAddress, message.Subject, htmlContent, message.Body, trackOpens=false, trackClicks=false).Wait()

let sendSMSNotification message =
    // stub
    message |> ignore

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/WindowsAzure.ServiceBus/lib/net45-full/Microsoft.ServiceBus.dll"

open Microsoft.Azure.WebJobs.Host
open Microsoft.ServiceBus.Messaging

let Run(message: BrokeredMessage, log: TraceWriter) =
    log.Info(sprintf "F# function 'sendNotification' executed  at %s" (DateTime.Now.ToString()))
    let body = message.GetBody<Message>()
    log.Info(sprintf "Delivering %A message to '%s' re: '%s'" body.MessageType body.Recipient body.Subject)
    match body.MessageType with
    | MessageType.Email -> sendEmailNotification body
    | MessageType.SMS -> sendSMSNotification body
    | _ -> log.Error("unrecognized message type")