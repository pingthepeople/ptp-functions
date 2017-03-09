#load "module.fsx"

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open System
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open IgaTracker.Model
open IgaTracker.GenerateActionAlerts

let Run(action: string, notifications: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed for action %s at %s" action (timestamp()))
    try
        log.Info(sprintf "[%s] Generating action alerts ..." (timestamp()))
        let (emailMessages, smsMessages) = JsonConvert.DeserializeObject<Action>(action) |> generateAlerts
        log.Info(sprintf "[%s] Generating action alerts [OK]" (timestamp()))

        log.Info(sprintf "[%s] Enqueueing action alerts ..." (timestamp()))
        emailMessages |> Seq.iter (fun m -> 
            log.Info(sprintf "[%s]   Enqueuing email action alert to '%s' re: '%s'" (timestamp()) m.Recipient m.Subject )
            m |> JsonConvert.SerializeObject |> notifications.Add)
        smsMessages |> Seq.iter (fun m -> 
            log.Info(sprintf "[%s]   Enqueuing SMS action alert to '%s' re: '%s'" (timestamp()) m.Recipient m.Subject)
            m |> JsonConvert.SerializeObject |> notifications.Add)
        log.Info(sprintf "[%s] Enqueueing action alerts [OK]" (timestamp()))
    with
    | ex -> 
        log.Error(sprintf "Encountered error: %s" (ex.ToString())) 
        reraise()