module Ptp.UpdateCanonicalData.Committees

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Model
open Ptp.Queries
open Ptp.Http
open Ptp.Database
open Ptp.Cache
open Ptp.Logging
open System

// COMMITTEES
let committeeModel (url,c:JsonValue) =

    let name = c?name.AsString()

    let chamber = 
        match c.TryGetProperty("chamber") with
        | Some prop -> 
            let name = prop?name.AsString()
            Enum.Parse(typedefof<Chamber>, name) :?> Chamber
        |None -> 
            Chamber.None
    
    let committeeType = 
        match url with
        | Contains("standing-") s   -> CommitteeType.Standing
        | Contains("interim-") s    -> CommitteeType.Interim
        | Contains("conference-") s -> CommitteeType.Conference
        | _ -> sprintf "Uncrecognized committee type: %s" url |> failwith
    
    { Committee.Id=0; 
      SessionId=0; 
      Chamber=chamber; 
      CommitteeType=committeeType;
      Name=name; 
      Link=url }

/// Fetch URLs for all committees in the current session year.
let fetchAllCommitteesFromAPI sessionYear = trial {
    let url = sprintf "/%s/committees" sessionYear
    let! pages = url |> fetchAllPages
    let link json = json?link.AsString()
    let! committeeUrls = pages |> deserializeAs link
    return committeeUrls |> Seq.take 30
    }

let filterOutKnownCommitteesQuery = sprintf "SELECT Link from Committee WHERE SessionId = %s" SessionIdSubQuery
let insertCommitteeCommand = sprintf """INSERT INTO Committee(Name,Link,Chamber,CommitteeType,SessionId) VALUES (@Name,@Link,@Chamber,@CommitteeType,%s)""" SessionIdSubQuery

/// Remove any URLs of committees that we already know about
let filterOutKnownCommittees (allCommitteeUrls: string seq) = trial {
    let! knownCommitteeUrls = dbQuery<string> filterOutKnownCommitteesQuery
    let unknownCommittees = allCommitteeUrls |> except knownCommitteeUrls
    return unknownCommittees
    }

/// Fetch full metadata for committess that we don't yet know about
let resolveNewCommittees urls = trial {
    let! pages = urls |> fetchAllParallel
    let! models = pages |> chooseJson' |> deserializeAs committeeModel
    return models
    }

/// Add new committee records to the database
let persistNewCommittees committees = 
    committees |> dbCommand insertCommitteeCommand 

/// Invalidate the Redis cache key for committees
let invalidateCommitteeCache committees = 
    committees |> invalidateCache' CommitteesKey

/// Log the addition of new committees
let describeNewCommittees committees = 
    let describer (c:Committee) = sprintf "%A | %A: %s" c.Chamber c.CommitteeType c.Name
    committees |> describeNewItems describer

/// Find, add, and log new committees
let updateCommittees =
    getCurrentSessionYear
    >> bind fetchAllCommitteesFromAPI
    >> bind filterOutKnownCommittees
    >> bind resolveNewCommittees
    >> bind persistNewCommittees
    >> bind invalidateCommitteeCache
    >> bind describeNewCommittees