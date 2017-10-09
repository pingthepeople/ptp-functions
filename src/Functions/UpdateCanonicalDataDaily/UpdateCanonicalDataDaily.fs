module UpdateCanonicalDataDaily

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Database
open Ptp.UpdateCanonicalData.Committees
open Ptp.UpdateCanonicalData.Legislators
open Ptp.UpdateCanonicalData.Memberships
open Ptp.UpdateCanonicalData.Bills
open System.Data.SqlClient

let updateCanonicalData (log:TraceWriter) =
    use cn = new SqlConnection(sqlConStr())
    let sessionYear = cn |> currentSessionYear
    let sessionId = cn |> currentSessionId

    updateLegislators log |> ignore
    updateCommittees  log |> ignore
    updateMemberships log cn sessionId             |> ignore

let Run(myTimer: TimerInfo, log: TraceWriter) =
     updateCanonicalData 
     |> tryRun "UpdateCanonicalData" log 
