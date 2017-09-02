module UpdateCanonicalData

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Microsoft.Azure.WebJobs
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
open System.Data.SqlClient
open FSharp.Collections.ParallelSeq

let logNewAdditions (log:TraceWriter) category (items: string list) = 
    match items with
    | [] -> log.Info(sprintf "No new %ss found." category)
    | _  ->
        items 
        |> String.concat "\n"
        |> (fun s -> log.Info((sprintf "Found new %ss:\n%s" category s)))

// SUBJECTS 

/// Fetch all bill subjects for the current session from the IGA API
let fetchAllSubjects sessionId sessionYear = 
    let toModel subject = 
      { Subject.Id=0; 
        SessionId=sessionId; 
        Name=subject?entry.AsString(); 
        Link=subject?link.AsString() }
    let op() = 
        fetchAll (sprintf "/%s/subjects" sessionYear)
        |> List.map toModel
    tryF op "fetch all subjects"

/// Filter out any bill subjects that we already have in the database
let resolveNewSubjects cn sessionId  (subjects: Subject list) = 
    let op() = 
        let knownSubjects = 
            cn 
            |> dapperQuery<string> (sprintf "SELECT Link from Subject WHERE SessionId = %d" sessionId)
        subjects
        |> List.filter (fun s -> knownSubjects |> Seq.exists (fun ks -> ks = s.Link) |> not)
    tryF op "resolve new subjects"

/// Add new bill subject records to the database
let insertNewSubjects cn (subjects: Subject list) = 
    let insert subject =
        cn 
        |> dapperParametrizedQuery<int> InsertSubject subject 
        |> ignore
    let op () = 
        subjects |> List.iter insert
        subjects
    tryF op "insert new subjects"

/// Invalidate the Redis cache key for bill subjects
let invalidateSubjectsCache  (subjects: Subject list) =
    let op() = 
        subjects |> invalidateCache SubjectsKey
        subjects
    tryF op "invalidate Subjects cache"

/// Log the addition of any new bill subjects
let logNewSubjects (log:TraceWriter) (subjects: Subject list) = 
    subjects
    |> List.map(fun s -> s.Name)
    |> logNewAdditions log "subject"
    ok subjects

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
        |> List.map (fun bill -> get (bill?link.AsString()))
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
        bills 
    tryF op "invalidate Bills cache"

/// Log the addition of any new bills
let logNewBills (log:TraceWriter) (bills: Bill list) = 
    bills
    |> List.map(fun s -> s.Name)
    |> logNewAdditions log "bill"
    ok bills

// COMMITTEES
/// Fetch all committees for the current session year from the IGA API 
let fetchAllCommittees sessionId sessionYear = 
    let toModel chamber c ={
        Committee.Id=0; 
        SessionId=sessionId; 
        Chamber=chamber; 
        Name=c?name.AsString(); 
        Link=c?link.AsString().Replace("standing-","") }

    let op () =
        let house =
            fetchAll (sprintf "/%s/chambers/house/committees" sessionYear)
            |> List.map (fun c-> toModel Chamber.House c)
        let senate =
            fetchAll (sprintf "/%s/chambers/senate/committees" sessionYear)
            |> List.map (fun c -> toModel Chamber.Senate c)
        house 
        |> List.append senate

    tryF op "fetch all committees"

/// Filter out any committees that we already have in the database    
let resolveNewCommittees cn sessionId (committees : Committee list)= 
    let op () =
        let knownCommittees = 
            cn |> dapperQuery<string> (sprintf "SELECT Link from Committee WHERE SessionId = %d" sessionId)
        committees
        |> List.filter (fun c -> knownCommittees |> Seq.exists (fun kc -> kc = c.Link) |> not)

    tryF op "resolve new committees"

/// Add new committee records to the database
let insertNewCommittees cn committees = 
    let op () =
        committees 
        |> List.iter (fun c -> 
            cn 
            |> dapperParametrizedQuery<int> InsertCommittee c 
            |> ignore)
        committees
    tryF op "insert new committees"

/// Invalidate the Redis cache key for committees
let invalidateCommitteeCache committees = 
    let op () =
        committees |> invalidateCache CommitteesKey
        committees
    tryF op "invalidate Committee cache"

/// Log the addition of any new committees
let logNewCommittees (log:TraceWriter) (committees: Committee list)= 
    committees
    |> List.map(fun s -> sprintf "%A: %s" s.Chamber s.Name)
    |> logNewAdditions log "committee"
    ok committees

// LEGISLATORS

/// Fetch all bill subjects for the current session from the IGA API
let fetchAllLegislators sessionYear = 
    let op() = 
        fetchAll (sprintf "/%s/legislators" sessionYear)
        |> List.map (fun l -> l?link.AsString())
    tryF op "fetch all legislators"

/// Filter out any committees that we already have in the database    
let resolveNewLegislators cn sessionId (links : string list)= 
    let toModel l = 
      { Legislator.Id=0; 
        SessionId=sessionId; 
        FirstName=l?firstName.AsString(); 
        LastName=l?lastName.AsString(); 
        Link=l?link.AsString();
        Party=Enum.Parse(typedefof<Party>, l?party.AsString()) :?> Party;
        Chamber=Enum.Parse(typedefof<Chamber>, l?chamber?name.AsString()) :?> Chamber;
        Image=""; 
        District=0; }

    let op () =
        let knownCommittees = 
            cn |> dapperQuery<string> (sprintf "SELECT Link from Legislator WHERE SessionId = %d" sessionId)
        links
        |> List.filter (fun l -> knownCommittees |> Seq.exists (fun kc -> kc = l) |> not)
        |> PSeq.map tryGet 
        |> PSeq.toList
        |> List.filter (fun j -> j <> JsonValue.Null)
        |> List.map toModel

    tryF op "resolve new legislators"

/// Add new committee records to the database
let insertNewLegislators cn legislators = 
    let op () =
        legislators 
        |> List.iter (fun l -> 
            cn 
            |> dapperParametrizedQuery<int> InsertLegislator l 
            |> ignore)
        legislators
    tryF op "insert new legislators"

/// Invalidate the Redis cache key for committees
let invalidateLegislatorCache legislators = 
    let op () =
        legislators  |> invalidateCache LegislatorsKey
        legislators 
    tryF op "invalidate legislators cache"

/// Log the addition of any new committees
let logNewLegislators (log:TraceWriter) (legislators: Legislator list)= 
    legislators
    |> List.map(fun s -> sprintf "%A: %s %s" s.Chamber s.FirstName s.LastName)
    |> logNewAdditions log "legislator"
    ok legislators

/// Find, add, and log new subjects
let updateSubjects (log:TraceWriter) cn sessionId sessionYear =
    let AddNewSubjects = "UpdateCanonicalData / Add new subjects"
    log.Info(sprintf "[START] %s" AddNewSubjects)
    fetchAllSubjects sessionId sessionYear
    >>= resolveNewSubjects cn sessionId 
    >>= insertNewSubjects cn
    >>= invalidateSubjectsCache
    >>= logNewSubjects log    
    |> haltOnFail log AddNewSubjects

/// Find, add, and log new committees
let updateCommittees (log:TraceWriter) cn sessionId sessionYear =
    let AddNewCommittees = "UpdateCanonicalData / Add new committees"
    log.Info(sprintf "[START] %s" AddNewCommittees)
    fetchAllCommittees sessionId sessionYear
    >>= resolveNewCommittees cn sessionId
    >>= insertNewCommittees cn
    >>= invalidateCommitteeCache
    >>= logNewCommittees log
    |> haltOnFail log AddNewCommittees

let updateLegislators (log:TraceWriter) cn sessionId sessionYear =
    let AddNewLegislators = "UpdateCanonicalData / Add new legislators"
    log.Info(sprintf "[START] %s" AddNewLegislators)
    fetchAllLegislators sessionYear
    >>= resolveNewLegislators cn sessionId
    >>= insertNewLegislators cn
    >>= invalidateLegislatorCache
    >>= logNewLegislators log
    |> haltOnFail log AddNewLegislators

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

let updateCanonicalData (log:TraceWriter) =
    use cn = new SqlConnection(sqlConStr())
    let sessionYear = cn |> currentSessionYear
    let sessionId = cn |> currentSessionId

    updateSubjects    log cn sessionId sessionYear |> ignore
    updateCommittees  log cn sessionId sessionYear |> ignore
    updateLegislators log cn sessionId sessionYear |> ignore
    updateBills       log cn sessionId sessionYear |> ignore

let Run(myTimer: TimerInfo, log: TraceWriter) =
     updateCanonicalData 
     |> tryRun "UpdateCanonicalData" log 
