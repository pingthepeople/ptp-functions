#load "module.fsx"

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open System
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open IgaTracker.Model
open IgaTracker.GenerateCalendarAlerts

let Run(scheduledActionId: string, notifications: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed for action %s at %s" scheduledActionId (DateTime.Now.ToString()))
    try
        log.Info(sprintf "[%s] Generating scheduled action alerts ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        let (emailMessages, smsMessages) = (Int32.Parse(scheduledActionId)) |> generateAlerts
        log.Info(sprintf "[%s] Generating scheduled action alerts [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))

        log.Info(sprintf "[%s] Enqueueing scheduled action alerts ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        emailMessages |> Seq.iter (fun m -> 
            log.Info(sprintf "[%s]   Enqueuing email alert to '%s' re: '%s'" (DateTime.Now.ToString("HH:mm:ss.fff")) m.Recipient m.Subject )
            notifications.Add(JsonConvert.SerializeObject(m)))
        smsMessages |> Seq.iter (fun m -> 
            log.Info(sprintf "[%s]   Enqueuing SMS alert to '%s' re: '%s'" (DateTime.Now.ToString("HH:mm:ss.fff")) m.Recipient m.Subject)
            notifications.Add(JsonConvert.SerializeObject(m)))
        log.Info(sprintf "[%s] Enqueueing scheduled action alerts [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))
    with
    | ex -> 
        log.Error(sprintf "Encountered error: %s" (ex.ToString())) 
        reraise()

