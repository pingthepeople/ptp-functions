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

let updateBills (cn,sessionId,sessionYear) =
    let toModel bill = {
        Bill.Id=0; 
        SessionId=1; 
        Name=bill?billName.AsString(); 
        Link=bill?link.AsString(); 
        Title=bill?latestVersion?shortDescription.AsString(); 
        Description= bill?latestVersion?digest.AsString();
        Topics=bill?latestVersion?subjects.AsArray() |> Array.toList |> List.map (fun a -> a?entry.AsString()) |> String.concat ", "; 
        Authors=bill?latestVersion?authors.AsArray() |> Array.toList |> List.map (fun a -> a?lastName.AsString()) |> List.sort |> String.concat ", "; }

    // Find new bills
    let knownBills = cn |> dapperMapParametrizedQuery<string> "SELECT Name from Bill WHERE SessionId = @SessionId" (Map["SessionId",sessionId:>obj])
    let lastUpdate = cn |> dapperMapParametrizedQuery<DateTime> "SELECT MAX(Created) from Bill WHERE SessionId = @SessionId" (Map["SessionId",sessionId:>obj]) |> Seq.head
    let newBills = 
        fetchAll (sprintf "/%s/bills?minFiledDate=%s" sessionYear (lastUpdate.ToString("yyyy-MM-dd")))
        |> List.filter (fun bill -> knownBills |> Seq.exists (fun knownBill -> knownBill = bill?billName.AsString()) |> not)
        |> List.map (fun bill -> get (bill?link.AsString()) |> toModel)

    // Add them to the database
    newBills |> List.iter (fun bill -> cn |> dapperParametrizedQuery<int> InsertBill bill |> ignore)
    
    newBills

let updateCommittees (cn,sessionId,sessionYear) =
    let toModel chamber c ={
        Committee.Id=0; 
        SessionId=1; 
        Chamber=chamber; 
        Name=c?name.AsString(); 
        Link=c?link.AsString().Replace("standing-","") }

    // Find new committees
    let knownCommittees = cn |> dapperMapParametrizedQuery<string> "SELECT Link from Committee WHERE SessionId = @SessionId" (Map["SessionId",sessionId:>obj])
    let houseCommittees = 
        fetchAll (sprintf "/%s/chambers/house/committees" sessionYear)
        |> List.map (fun committee -> committee |> toModel Chamber.House)
        |> List.filter (fun committee -> knownCommittees |> Seq.exists (fun knownCommittee -> knownCommittee = committee.Link) |> not)
    let senateCommittees = 
        fetchAll (sprintf "/%s/chambers/senate/committees" sessionYear)
        |> List.map (fun committee -> committee |> toModel Chamber.Senate)
        |> List.filter (fun committee -> knownCommittees |> Seq.exists (fun knownCommittee -> knownCommittee = committee.Link) |> not)

    // Add them to the database
    houseCommittees |> List.iter (fun committee -> cn |> dapperParametrizedQuery<int> InsertCommittee committee |> ignore)
    senateCommittees |> List.iter (fun committee -> cn |> dapperParametrizedQuery<int> InsertCommittee committee |> ignore)
    (houseCommittees, senateCommittees)

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
open Microsoft.Azure.WebJobs.Host

let Run(myTimer: TimerInfo, scheduledActions: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (DateTime.Now.ToString()))
    try
        let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
        let sessionYear = (System.Environment.GetEnvironmentVariable("SessionYear"))
        let sessionId = cn |> dapperMapParametrizedQuery<int> "SELECT Id From [Session] WHERE Name = @Name" (Map["Name",sessionYear:>obj]) |> Seq.head
        let date = DateTime.Now.AddDays(-1.0).ToString("yyyy-MM-dd")
        
        log.Info(sprintf "[%s] Update bills ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        (cn,sessionId,sessionYear) 
        |> updateBills 
        |> List.iter (fun bill -> log.Info(sprintf "[%s] Added bill '%s' ('%s')" (DateTime.Now.ToString("HH:mm:ss.fff")) bill.Name bill.Title))
        log.Info(sprintf "[%s] Update bills [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")) )
        
        log.Info(sprintf "[%s] Update bills ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        let (houseCommittees, senateCommittees) = (cn,sessionId,sessionYear) |> updateCommittees
        houseCommittees |> List.iter (fun committee -> log.Info(sprintf "[%s] Added House committee '%s'" (DateTime.Now.ToString("HH:mm:ss.fff")) committee.Name))
        senateCommittees |> List.iter (fun committee -> log.Info(sprintf "[%s] Added Senate committee '%s'" (DateTime.Now.ToString("HH:mm:ss.fff")) committee.Name))
        log.Info(sprintf "[%s] Update bills [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")) )
    with
    | ex -> log.Error(sprintf "Encountered error: %s" (ex.ToString())) 