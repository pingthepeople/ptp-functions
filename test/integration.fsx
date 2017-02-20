#r "../packages/WindowsAzure.ServiceBus/lib/net45-full/Microsoft.ServiceBus.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"
#load "../shared/model.fs"

open System
open System.Runtime.Serialization
open Microsoft.ServiceBus
open Microsoft.ServiceBus.Messaging
open Newtonsoft.Json
open IgaTracker.Model

let enqueueNotification message = 
    let queueName = "notification"
    let connectionString = System.Environment.GetEnvironmentVariable("ServiceBus.ConnectionString")
    let queueClient = QueueClient.CreateFromConnectionString(connectionString, queueName)
    let json = JsonConvert.SerializeObject(message)
    let brokeredMessage = new BrokeredMessage(json)
    queueClient.Send(brokeredMessage)

let text = (sprintf "test message %s" (DateTime.Now.ToString()))

// Test sendNotification email handling by sending email message to 'notification' queue
enqueueNotification {MessageType=MessageType.Email; Recipient="CHANGEME"; Subject=text; Body=text}

// Test sendNotification SMS handling by sending SMS message to 'notification' queue
enqueueNotification {MessageType=MessageType.SMS; Recipient="+1CHANGEME"; Subject=text; Body=text}