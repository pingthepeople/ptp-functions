module Ptp.UpdateCanonicalData.Legislators

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
open FSharp.Collections.ParallelSeq

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
    tryF op "invalidate legislators cache"

/// Log the addition of any new committees
let logNewLegislators (log:TraceWriter) (legislators: Legislator list)= 
    legislators
    |> List.map(fun s -> sprintf "%A: %s %s" s.Chamber s.FirstName s.LastName)
    |> logNewAdditions log "legislator"
    ok legislators

let updateLegislators (log:TraceWriter) cn sessionId sessionYear =
    let AddNewLegislators = "UpdateCanonicalData / Add new legislators"
    log.Info(sprintf "[START] %s" AddNewLegislators)
    fetchAllLegislators sessionYear
    >>= resolveNewLegislators cn sessionId
    >>= insertNewLegislators cn
    >>= invalidateLegislatorCache
    >>= logNewLegislators log
    |> haltOnFail log AddNewLegislators
