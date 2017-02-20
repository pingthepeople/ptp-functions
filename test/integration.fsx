#r "../packages/WindowsAzure.ServiceBus/lib/net45-full/Microsoft.ServiceBus.dll"
#load "../shared/model.fs"

open System
open System.Runtime.Serialization
open Microsoft.ServiceBus
open Microsoft.ServiceBus.Messaging
open IgaTracker.Model
let enqueueEmailNotification text =
    let queueName = "notification"
    let connectionString = System.Environment.GetEnvironmentVariable("ServiceBus.ConnectionString")
    let queueClient = QueueClient.CreateFromConnectionString(connectionString, queueName)
    let message = {MessageType=MessageType.Email; Recipient="jhoerr@gmail.com"; Subject=text; Body=text}
    let brokeredMessage = new BrokeredMessage(message)
    queueClient.Send(brokeredMessage)

enqueueEmailNotification (sprintf "test message %s" (DateTime.Now.ToString()))