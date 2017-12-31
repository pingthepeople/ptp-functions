// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.

open Microsoft.ServiceBus.Messaging
open Newtonsoft.Json
open Ptp.Core

[<EntryPoint>]
let main argv = 

    let conStr = "Endpoint=sb://pingthepeople-dev.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=8h9V4Tf0L8J6drNHmJGHCFgu0PKnLtEkyVoolWdZY9E="
    let queueName = "command"
    let queueClient = QueueClient.CreateFromConnectionString(conStr, queueName, ReceiveMode.ReceiveAndDelete)

    (*
    UpdateLegislators 
    |> JsonConvert.SerializeObject 
    |> (fun b -> new BrokeredMessage(b))
    |> queueClient.Send
    |> ignore
    *)

    while (true) do 
        System.Console.Out.WriteLine("received message...")
        queueClient.Receive() |> ignore
    
    printfn "%A" argv
    0 // return an integer exit code
