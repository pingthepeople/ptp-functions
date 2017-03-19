// Configure Database 

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/http.fsx"
#load "../shared/db.fsx"

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

let updateSubjects (cn,sessionId,sessionYear) =
    let toModel subject ={
        Subject.Id=0; 
        SessionId=1; 
        Name=subject?entry.AsString(); 
        Link=subject?link.AsString() }

    // Find new subjects
    let knownSubjects = cn |> dapperMapParametrizedQuery<string> "SELECT Link from Subject WHERE SessionId = @SessionId" (Map["SessionId",sessionId:>obj])
    let newSubjects = 
        fetchAll (sprintf "/%s/subjects" sessionYear)
        |> List.filter (fun subject -> knownSubjects |> Seq.exists (fun knownSubject -> knownSubject = subject?link.AsString()) |> not)
        |> List.map toModel

    // Add them to the database
    newSubjects |> List.iter (fun subject -> cn |> dapperParametrizedQuery<int> InsertSubject subject |> ignore)
    newSubjects

let updateBills (cn,sessionId,sessionYear) =
    let parseChamber (name:string) = 
        match name.Substring(0,1) with
        | "H" -> Chamber.House
        | "S" -> Chamber.Senate
        | _ -> raise(System.ArgumentException("unrecognized chamber for bill " + name))

    let toModel bill = 
        let name = bill?billName.AsString()
        { Bill.Id=0; 
        SessionId=1; 
        Name=name; 
        Link=bill?link.AsString(); 
        Title=bill?latestVersion?shortDescription.AsString(); 
        Description= bill?latestVersion?digest.AsString();
        Chamber=parseChamber name;
        Authors=bill?latestVersion?authors.AsArray() |> Array.toList |> List.map (fun a -> a?lastName.AsString()) |> List.sort |> String.concat ", "; }

    // Find new bills
    let knownBills = cn |> dapperMapParametrizedQuery<string> "SELECT Name from Bill WHERE SessionId = @SessionId" (Map["SessionId",sessionId:>obj])
    let lastUpdate = cn |> dapperMapParametrizedQuery<DateTime> "SELECT MAX(Created) from Bill WHERE SessionId = @SessionId" (Map["SessionId",sessionId:>obj]) |> Seq.head
    let newBills = fetchAll (sprintf "/%s/bills?minFiledDate=%s" sessionYear (lastUpdate.ToString("yyyy-MM-dd")))
    let newBillMetadata =
        newBills
        |> List.filter (fun bill -> knownBills |> Seq.exists (fun knownBill -> knownBill = bill?billName.AsString()) |> not)
        |> List.map (fun bill -> get (bill?link.AsString()))
    
    // Add the bills to the database
    let newBillModels = newBillMetadata |> List.map toModel
    newBillModels |> List.iter (fun bill -> cn |> dapperParametrizedQuery<int> InsertBill bill |> ignore)

    // Determine bill subjects
    let newBillRecords = cn |> dapperMapParametrizedQuery<Bill> "Select Id,Name from Bill where Created > @Created" (Map ["Created", lastUpdate:>obj])
    let subjects = cn |> dapperMapParametrizedQuery<Subject> "SELECT Id,Name,Link From [Subject] WHERE SessionId = @SessionId" (Map["SessionId",sessionId:>obj])
    
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

    newBillModels

let updateCommittees (cn,sessionId,sessionYear) =
    let toModel chamber c ={
        Committee.Id=0; 
        SessionId=1; 
        Chamber=chamber; 
        Name=c?name.AsString(); 
        Link=c?link.AsString().Replace("standing-","") }

    let knownCommittees = 
        cn |> dapperMapParametrizedQuery<string> "SELECT Link from Committee WHERE SessionId = @SessionId" (Map["SessionId",sessionId:>obj])

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
        let sessionYear = cn |> currentSessionYear
        let sessionId = cn |> dapperMapParametrizedQuery<int> "SELECT Id From [Session] WHERE Name = @Name" (Map["Name",sessionYear:>obj]) |> Seq.head
        let date = DateTime.Now.AddDays(-1.0).ToString("yyyy-MM-dd")

        log.Info(sprintf "[%s] Update subjects ..." (timestamp()))
        (cn,sessionId,sessionYear) 
        |> updateSubjects
        |> List.iter (fun subject -> log.Info(sprintf "[%s]   Added subject '%s'" (timestamp()) subject.Name))
        log.Info(sprintf "[%s] Update subjects [OK]" (timestamp()) )

        log.Info(sprintf "[%s] Update bills ..." (timestamp()))
        (cn,sessionId,sessionYear) 
        |> updateBills 
        |> List.iter (fun bill -> log.Info(sprintf "[%s]   Added bill '%s' ('%s')" (timestamp()) bill.Name bill.Title))
        log.Info(sprintf "[%s] Update bills [OK]" (timestamp()) )
        
        log.Info(sprintf "[%s] Update committees ..." (timestamp()))
        let (houseCommittees, senateCommittees) = (cn,sessionId,sessionYear) |> updateCommittees
        houseCommittees |> List.iter (fun committee -> log.Info(sprintf "[%s]   Added House committee '%s'" (timestamp()) committee.Name))
        senateCommittees |> List.iter (fun committee -> log.Info(sprintf "[%s]   Added Senate committee '%s'" (timestamp()) committee.Name))
        log.Info(sprintf "[%s] Update committees [OK]" (timestamp()))
    with
    | ex -> log.Error(sprintf "Encountered error: %s" (ex.ToString())) 