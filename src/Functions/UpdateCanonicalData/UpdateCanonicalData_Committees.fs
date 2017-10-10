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
let committeeModel c =
  { Committee.Id=0; 
    SessionId=0; 
    Chamber=Enum.Parse(typedefof<Chamber>, c?chamber?name.AsString()) :?> Chamber; 
    Name=c?name.AsString(); 
    Link=c?link.AsString().Replace("standing-","") }

/// Fetch URLs for all committees in the current session year.
let fetchAllCommitteesFromAPI sessionYear = trial {
    let url = sprintf "/%s/committees" sessionYear
    let! pages = url |> fetchAllPages
    let link json = json?link.AsString()
    let! committeeUrls = pages |> deserializeAs link
    return committeeUrls
    }

/// Remove any URLs of committees that we already know about
let filterOutKnownCommittees (allCommitteeUrls: string seq) = trial {
    let queryText = sprintf "SELECT Link from Committee WHERE SessionId = %s" SessionIdSubQuery
    let! knownCommitteeUrls = dbQuery<string> queryText
    let unknownCommittees = allCommitteeUrls |> except knownCommitteeUrls
    return unknownCommittees
    }

/// Fetch full metadata for committess that we don't yet know about
let resolveNewCommittees urls = trial {
    let! pages = urls |> fetchAllParallel
    let! models = pages |> deserializeAs committeeModel
    return models
    }

/// Add new committee records to the database
let persistNewCommittees committees = 
    committees |> dbCommand InsertCommittee 

/// Invalidate the Redis cache key for committees
let invalidateCommitteeCache committees = 
    committees |> invalidateCache' CommitteesKey

/// Log the addition of new committees
let describeNewCommittees committees = 
    let describer (c:Committee) = sprintf "%A: %s" c.Chamber c.Name
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