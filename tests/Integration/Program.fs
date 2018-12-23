open System
open Ptp.Common.Core
open Microsoft.Azure.ServiceBus
open Newtonsoft.Json

[<EntryPoint>]
let main argv =

    let conStr = "Endpoint=sb://pingthepeople-dev.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=8h9V4Tf0L8J6drNHmJGHCFgu0PKnLtEkyVoolWdZY9E="
    let queueName = "command"
    let queueClient = new QueueClient(conStr, queueName, ReceiveMode.ReceiveAndDelete)
        
    GenerateActionNotification(9036)
    |> JsonConvert.SerializeObject 
    |> System.Text.Encoding.UTF8.GetBytes
    |> (fun b -> new Message(b))
    |> queueClient.SendAsync
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> ignore

    printf "Message enqueue to service bus."

    0

