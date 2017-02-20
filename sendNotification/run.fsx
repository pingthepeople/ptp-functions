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
#load "../shared/queries.fs"
#load "../shared/http.fsx"
#load "../shared/db.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open FSharp.Data
open FSharp.Formatting.Common
open FSharp.Markdown
open StrongGrid
open IgaTracker.Model
open IgaTracker.Queries
open IgaTracker.Http
open IgaTracker.Db

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
open Microsoft.Azure.WebJobs.Host

let Run(message: Message, log: TraceWriter) =
    log.Info(sprintf "F# function 'sendNotification' executed for %A msg to %s with subject %s at %s" message.MessageType message.Recipient message.Subject (DateTime.Now.ToString()))
    match message.MessageType with
    | MessageType.Email -> sendEmailNotification message
    | MessageType.SMS -> sendSMSNotification message
    | _ -> log.Error("unrecognized message type") 

