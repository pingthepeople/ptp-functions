module Ptp.UpdateCanonicalData.Bills

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Model
open Ptp.Queries
open Ptp.Http
open Ptp.Database
open Ptp.Cache
open Ptp.Logging
open Ptp.Bill
open System
open FSharp.Collections.ParallelSeq

// BILLS
/// Determine the time of entry into the database of the most recent bill.
/// The IGA API allows us to query new bills by filing date, which is a fast convenience.
let getTimeOfLastBillUpdate cn sessionId = 
    let op() =
        cn |> dapperQueryOne<DateTime> (sprintf "SELECT MAX(Created) from Bill WHERE SessionId = %d" sessionId)
    tryF op "fetch time of last bill update"

/// Fetch all bills from the IGA API filed after a certain date.
let fetchRecentBills sessionYear (lastUpdate:DateTime) = 
    let op() = 
        fetchAll (sprintf "/%s/bills?minFiledDate=%s" sessionYear (lastUpdate.ToString("yyyy-MM-dd")))
    tryF op "fetch recent bills"

/// Filter out any bills that we already have in the database
let resolveNewBills cn sessionId newBillMetadata = 
    let op() =
        let knownBills = cn |> dapperQuery<string> (sprintf "SELECT Name from Bill WHERE SessionId = %d" sessionId)
        newBillMetadata
        |> List.filter (fun bill -> knownBills |> Seq.exists (fun knownBill -> knownBill = bill?billName.AsString()) |> not)
        |> List.map (fun bill -> bill?link.AsString())
        |> PSeq.map tryGet
        |> PSeq.toList
        |> List.filter (fun j -> j <> JsonValue.Null)

    tryF op "resolve new bills"

/// Add new bill records to the database
let insertNewBills cn newBillMetadata =
    let op() =
        let newBillRecords = 
            newBillMetadata 
            |> List.map (fun bill -> cn |> insertBill bill)
        (newBillRecords, newBillMetadata)
    tryF op "insert new bills"

/// Relate new bills to their subject(s)
let insertNewBillSubjects cn sessionId (newBillRecords, newBillMetadata) =
    let op() =
        let subjects = cn |> dapperQuery<Subject> (sprintf "SELECT Id,Name,Link From [Subject] WHERE SessionId = %d" sessionId)

        let pairBillRecordWithMetadata (bill:Bill) =
            let billMetadata = newBillMetadata |> List.find(fun newBill -> bill.Name = (newBill?billName.AsString()))
            (bill,billMetadata)

        let newBillSubjectRecords (bill:Bill, billMetadata) = 
            let billSubjects = billMetadata?latestVersion?subjects.AsArray()
            let toBillSubjectRecord subject =
                let subjectId =  subjects |> Seq.find (fun s -> s.Link = subject?link.AsString()) |> (fun subject -> subject.Id)
                { BillSubject.Id = 0; BillId = bill.Id; SubjectId = subjectId; }
            billSubjects |> Array.map toBillSubjectRecord

        newBillRecords
            |> Seq.map pairBillRecordWithMetadata
            |> Seq.collect newBillSubjectRecords
            |> Seq.iter (fun subject -> cn |> dapperParametrizedQuery<int> InsertBillSubject subject |> ignore)
        
        newBillRecords

    tryF op "insert new bill subjects"

/// Invalidate the Redis cache key for bills
let invalidateBillCache bills = 
    let op() = 
        bills |> invalidateCache BillsKey
    tryF op "invalidate Bills cache"

/// Log the addition of any new bills
let logNewBills (log:TraceWriter) (bills: Bill list) = 
    bills
    |> List.map(fun s -> s.Name)
    |> logNewAdditions log "bill"
    ok bills

/// Find, add, and log new bills
let updateBills (log:TraceWriter) cn sessionId sessionYear =
    let AddNewBills = "UpdateCanonicalData / Add new bills"
    log.Info(sprintf "[START] %s" AddNewBills)
    getTimeOfLastBillUpdate cn sessionId
    >>= fetchRecentBills sessionYear
    >>= resolveNewBills cn sessionId
    >>= insertNewBills cn
    >>= insertNewBillSubjects cn sessionId
    >>= invalidateBillCache
    >>= logNewBills log
    |> haltOnFail log AddNewBills 
