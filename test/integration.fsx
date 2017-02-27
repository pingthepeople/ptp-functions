#r "../packages/WindowsAzure.ServiceBus/lib/net45-full/Microsoft.ServiceBus.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Formatting.Common.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Markdown.dll"

#load "../shared/model.fs"

open System
open System.Runtime.Serialization
open Microsoft.ServiceBus
open Microsoft.ServiceBus.Messaging
open Newtonsoft.Json
open IgaTracker.Model
open FSharp.Data
open FSharp.Formatting.Common
open FSharp.Markdown


let enqueueNotification message = 
    let queueName = "notification"
    let connectionString = System.Environment.GetEnvironmentVariable("ServiceBus.ConnectionString")
    let queueClient = QueueClient.CreateFromConnectionString(connectionString, queueName)
    let json = JsonConvert.SerializeObject(message)
    let brokeredMessage = new BrokeredMessage(json)
    queueClient.Send(brokeredMessage)

let text = (sprintf "test message %s" (DateTime.Now.ToString()))

[<Literal>]
let body = """Hello! Please find attached today's legislative update.

#Today's House Activity

##Committee Hearings

* [HB1383](https://iga.in.gov/legislative/2017/bills/House/HB1383) ('Elementary school teachers.'): amend do pass, adopted
* [HB1449](https://iga.in.gov/legislative/2017/bills/House/HB1449) ('Teacher induction pilot program.'): amend do pass, adopted

##Second Readings

* [HB1130](https://iga.in.gov/legislative/2017/bills/House/HB1130) ('Protections for student journalists.'): ordered engrossed\n\n##Third Readings

#Today's Senate Activity\n\n##CommitteeHearings

* [SB0126](https://iga.in.gov/legislative/2017/bills/Senate/SB0126) ('Government ethics.'): amend do pass, adopted
* [SB0309](https://iga.in.gov/legislative/2017/bills/Senate/SB0309) ('Distributed generation.'): amend do pass, adopted"""



// Test sendNotification email handling by sending email message to 'notification' queue
// enqueueNotification {MessageType=MessageType.Email; Recipient="jhoerr@gmail.com"; Subject=text; Body=body}

// Test sendNotification SMS handling by sending SMS message to 'notification' queue
// enqueueNotification {MessageType=MessageType.SMS; Recipient="+15033606581"; Subject=text; Body="test link ->  http://google.com <- link"}

let html = body |> Markdown.Parse |> Markdown.WriteHtml
