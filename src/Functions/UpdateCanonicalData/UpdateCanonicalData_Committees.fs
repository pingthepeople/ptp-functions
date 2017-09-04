module Ptp.UpdateCanonicalData.Committees

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
open System

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
    tryF op "invalidate Committee cache"

/// Log the addition of any new committees
let logNewCommittees (log:TraceWriter) (committees: Committee list)= 
    committees
    |> List.map(fun s -> sprintf "%A: %s" s.Chamber s.Name)
    |> logNewAdditions log "committee"
    ok committees

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
