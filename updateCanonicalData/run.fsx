#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/StackExchange.Redis/lib/net45/StackExchange.Redis.dll"

#load "../shared/queries.fs"
#load "../shared/http.fsx"
#load "../shared/db.fsx"
#load "../shared/cache.fsx"
#load "../shared/bill.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open FSharp.Data
open FSharp.Data.JsonExtensions
open IgaTracker.Model
open IgaTracker.Queries
open IgaTracker.Http
open IgaTracker.Db
open IgaTracker.Cache
open IgaTracker.Logging
open IgaTracker.Bill
open StackExchange.Redis


let updateSubjects cn =

    let sessionYear = cn |> currentSessionYear
    let sessionId = cn |> dapperQuery<int> "SELECT TOP 1 Id FROM Session ORDER BY Name Desc" |> Seq.head

    let toModel subject ={
        Subject.Id=0; 
        SessionId=sessionId; 
        Name=subject?entry.AsString(); 
        Link=subject?link.AsString() }

    // Find new subjects
    let knownSubjects = cn |> dapperQuery<string> "SELECT Link from Subject WHERE SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)"
    let newSubjects = 
        fetchAll (sprintf "/%s/subjects" sessionYear)
        |> List.filter (fun subject -> knownSubjects |> Seq.exists (fun knownSubject -> knownSubject = subject?link.AsString()) |> not)
        |> List.map toModel

    // Add them to the database
    newSubjects |> List.iter (fun subject -> cn |> dapperParametrizedQuery<int> InsertSubject subject |> ignore)
    newSubjects

let updateBills cn =

    // Find new bills
    let sessionYear = cn |> currentSessionYear
    let knownBills = cn |> dapperQuery<string> "SELECT Name from Bill WHERE SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)"
    let lastUpdate = cn |> dapperQueryOne<DateTime> "SELECT MAX(Created) from Bill WHERE SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)"
    let newBills = fetchAll (sprintf "/%s/bills?minFiledDate=%s" sessionYear (lastUpdate.ToString("yyyy-MM-dd")))
    let newBillMetadata =   
        newBills
        |> List.filter (fun bill -> knownBills |> Seq.exists (fun knownBill -> knownBill = bill?billName.AsString()) |> not)
        |> List.map (fun bill -> get (bill?link.AsString()))
    
    // Add the bills to the database
    let newBillRecords = newBillMetadata |> List.map (fun bill -> cn |> insertBill bill)

    // Determine bill subjects
    let subjects = cn |> dapperQuery<Subject> "SELECT Id,Name,Link From [Subject] WHERE SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)"
    
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

let updateCommittees cn =

    let sessionYear = cn |> currentSessionYear
    let sessionId = cn |> dapperQuery<int> "SELECT TOP 1 Id FROM Session ORDER BY Name Desc" |> Seq.head

    let toModel chamber c ={
        Committee.Id=0; 
        SessionId=sessionId; 
        Chamber=chamber; 
        Name=c?name.AsString(); 
        Link=c?link.AsString().Replace("standing-","") }

    let knownCommittees = 
        cn |> dapperQuery<string> "SELECT Link from Committee WHERE SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)"

    let fetchNewCommittees chamber =
        fetchAll (sprintf "/%s/chambers/%s/committees" sessionYear (chamber.ToString().ToLower()))
        |> List.map (fun committee -> committee |> toModel chamber)
        |> List.filter (fun committee -> knownCommittees |> Seq.exists (fun knownCommittee -> knownCommittee = committee.Link) |> not)
    
    // Find new committees
    let houseCommittees = fetchNewCommittees Chamber.House
    let senateCommittees = fetchNewCommittees Chamber.Senate

    // Add them to the database
    houseCommittees |> List.iter (fun committee -> cn |> dapperParametrizedQuery<int> InsertCommittee committee |> ignore)
    senateCommittees |> List.iter (fun committee -> cn |> dapperParametrizedQuery<int> InsertCommittee committee |> ignore)
    (houseCommittees, senateCommittees)

#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/Microsoft.Azure.WebJobs.Extensions/lib/net45/Microsoft.Azure.WebJobs.Extensions.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs.Extensions

let Run(myTimer: TimerInfo, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (timestamp()))
    try
        let cn = new SqlConnection(sqlConStr())
        let date = DateTime.Now.AddDays(-1.0).ToString("yyyy-MM-dd")

        let logTrace trace = 
            trace |> trackTrace "updateCanonicalData"
            trace |> log.Info

        log.Info(sprintf "[%s] Update subjects ..." (timestamp()))
        let newSubjects = cn |> updateSubjects
        newSubjects |> List.iter (fun subject -> logTrace (sprintf "  Added subject '%s'" (subject.Name)))
        log.Info(sprintf "[%s] Update subjects [OK]" (timestamp()) )

        log.Info(sprintf "[%s] Update bills ..." (timestamp()))
        let newBills = cn |> updateBills
        newBills |> List.iter (fun bill -> logTrace (sprintf "  Added bill '%s' ('%s')" bill.Name bill.Title))
        log.Info(sprintf "[%s] Update bills [OK]" (timestamp()) )
        
        log.Info(sprintf "[%s] Update committees ..." (timestamp()))
        let (houseCommittees, senateCommittees) = cn |> updateCommittees
        houseCommittees |> List.iter (fun committee -> logTrace (sprintf "   Added House committee '%s'" committee.Name))
        senateCommittees |> List.iter (fun committee -> logTrace (sprintf "   Added Senate committee '%s'" committee.Name))
        log.Info(sprintf "[%s] Update committees [OK]" (timestamp()))

        log.Info(sprintf "[%s] Invalidating caches ..." (timestamp()))
        newSubjects |> invalidateCache SubjectsKey
        newBills |> invalidateCache BillsKey
        (houseCommittees @ senateCommittees) |> invalidateCache CommitteesKey
        log.Info(sprintf "[%s] Invalidating caches [OK]" (timestamp()))

    with
    | ex -> 
        ex |> trackException "updateCanonicalData"
        log.Error(sprintf "Encountered error: %s" (ex.ToString())) 
        reraise()