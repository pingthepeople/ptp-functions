module Workflow.Integration

open Microsoft.Azure.ServiceBus
open Ptp.Core
open Newtonsoft.Json
open Swensen.Unquote
open Xunit

[<Fact>]
let ``enqueue command``()=

    let conStr = "Endpoint=sb://pingthepeople-dev.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=8h9V4Tf0L8J6drNHmJGHCFgu0PKnLtEkyVoolWdZY9E="
    let queueName = "command"
    let queueClient = QueueClient(conStr, queueName)

    UpdateBill "/2017/bills/hb1007" 
    |> JsonConvert.SerializeObject 
    |> System.Text.Encoding.UTF8.GetBytes
    |> Message
    |> queueClient.SendAsync
    |> Async.AwaitTask
    |> ignore