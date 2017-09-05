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
let resolveNewBills cn newBillMetadata = 
    let op() =
        let knownBills = cn |> dapperQuery<string> "SELECT Name from Bill WHERE SessionId = (SELECT TOP 1 Id From Session ORDER BY Name DESC)"
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
        newBillMetadata 
        |> List.map (fun metadata -> 
                let newBill = cn |> insertBill metadata
                (newBill, metadata))       
    tryF op "insert new bills"

/// Relate new bills to their subject(s)
let insertNewBillSubjects cn (metadataAndRecords:(Bill*JsonValue) list) =
    let op() =
        let subjects = cn |> dapperQuery<Subject> "SELECT Id,Link From [Subject] WHERE SessionId = (SELECT TOP 1 Id From Session ORDER BY Name DESC)"

        let newBillSubjectRecords (bill:Bill, billMetadata) = 
            let billSubjects = billMetadata?latestVersion?subjects.AsArray()
            let toBillSubjectRecord subject =
                let subjectId =  subjects |> Seq.find (fun s -> s.Link = subject?link.AsString()) |> (fun subject -> subject.Id)
                { BillSubject.Id = 0; BillId = bill.Id; SubjectId = subjectId; }
            billSubjects |> Array.map toBillSubjectRecord

        metadataAndRecords
        |> List.map (fun (bill,_) -> bill.Id)
        |> List.toArray
        |> fun ids -> 
            cn |> dapperParameterizedCommand "DELETE FROM BillSubject WHERE BillId in @Ids" {Ids=ids}

        metadataAndRecords
        |> Seq.collect newBillSubjectRecords
        |> Seq.toList
        |> fun subjects -> 
            cn |> dapperParameterizedCommand InsertBillSubject subjects 
        
        metadataAndRecords
    tryFIfAny metadataAndRecords op "insert new bill subjects"

let insertNewBillLegislators cn (metadataAndRecords:(Bill*JsonValue) list) =
    let op() =
        let legislators = cn |> dapperQuery<Legislator> "SELECT Id,Link From [Legislator] WHERE SessionId = (SELECT TOP 1 Id From Session ORDER BY Name DESC)"

        let parseRole role billId (jsonValue:JsonValue)=
            match (jsonValue.TryGetProperty("link")) with
            | None -> []
            | Some link -> 
                let legislator = legislators |> Seq.tryFind (fun l -> l.Link = link.AsString())
                match legislator with
                | None -> []
                | Some l -> [{BillId=billId; LegislatorId=l.Id; Position=role; Id=0}]

        let getRoles role billId property (jsonValue:JsonValue)=
            match jsonValue.TryGetProperty(property) with
            | None -> []
            | Some json ->
                json.AsArray()
                |> Array.toList
                |> List.collect (fun j -> parseRole role billId j)

        let newBillLegislatorRecords (bill:Bill,metadata) =
            let authors = getRoles BillPosition.Author bill.Id "authors" metadata 
            let coauthors = getRoles BillPosition.CoAuthor bill.Id "coauthors" metadata 
            let sponsors = getRoles BillPosition.Sponsor bill.Id "sponsors" metadata 
            let cosponsors = getRoles BillPosition.Sponsor bill.Id "cosponsors" metadata 
            authors @ coauthors @ sponsors @ cosponsors
        
        metadataAndRecords
        |> List.map (fun (bill,_) -> bill.Id)
        |> List.toArray
        |> fun ids -> 
            cn |> dapperParameterizedCommand "DELETE FROM LegislatorBill WHERE BillId in @Ids" {Ids=ids}
        
        metadataAndRecords 
        |> Seq.collect newBillLegislatorRecords
        |> Seq.toList
        |> fun records -> 
            cn |> dapperParameterizedCommand InsertLegislatorBill records
        
        metadataAndRecords

    tryFIfAny metadataAndRecords op "insert new bill legislators"


/// Invalidate the Redis cache key for bills
let invalidateBillCache bills = 
    let op() = 
        bills |> invalidateCache BillsKey
    tryF op "invalidate Bills cache"

/// Log the addition of any new bills
let logNewBills (log:TraceWriter) (metadataAndRecords:(Bill*JsonValue) list) = 
    metadataAndRecords
    |> List.map (fun (bill,_) -> bill.Name)
    |> logNewAdditions log "bill"
    ok metadataAndRecords

/// Find, add, and log new bills
let updateBills (log:TraceWriter) cn sessionId sessionYear =
    let AddNewBills = "UpdateCanonicalData / Add new bills"
    log.Info(sprintf "[START] %s" AddNewBills)
    getTimeOfLastBillUpdate cn sessionId
    >>= fetchRecentBills sessionYear
    >>= resolveNewBills cn
    >>= insertNewBills cn
    >>= insertNewBillSubjects cn
    >>= insertNewBillLegislators cn 
    >>= invalidateBillCache
    >>= logNewBills log
    |> haltOnFail log AddNewBills 