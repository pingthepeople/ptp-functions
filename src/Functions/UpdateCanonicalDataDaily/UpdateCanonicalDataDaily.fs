module UpdateCanonicalDataDaily

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.UpdateCanonicalData.Committees
open Ptp.UpdateCanonicalData.Legislators
open Ptp.UpdateCanonicalData.Memberships

let updateCanonicalData (log:TraceWriter) =

    updateLegislators log |> ignore
    updateCommittees  log |> ignore
    updateCommitteeMemberships log |> ignore

let Run(myTimer: TimerInfo, log: TraceWriter) =
     updateCanonicalData 
     |> tryRun "UpdateCanonicalData" log 
