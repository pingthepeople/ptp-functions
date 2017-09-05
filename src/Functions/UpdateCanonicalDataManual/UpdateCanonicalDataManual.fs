module UpdateCanonicalDataManual

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Database
open Ptp.UpdateCanonicalData.Bills
open Ptp.UpdateCanonicalData.Subjects
open System.Data.SqlClient

let updateCanonicalData (log:TraceWriter) =
    use cn = new SqlConnection(sqlConStr())
    let sessionYear = cn |> currentSessionYear
    let sessionId = cn |> currentSessionId

    updateSubjects    log cn sessionId sessionYear |> ignore
    updateBills       log cn sessionId sessionYear |> ignore

let Run(input: string, log: TraceWriter) =
     updateCanonicalData 
     |> tryRun "UpdateCanonicalData" log 
