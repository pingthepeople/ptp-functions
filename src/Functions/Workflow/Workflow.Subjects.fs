module Ptp.Workflow.Subjects

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Model
open Ptp.Queries
open Ptp.Http
open Ptp.Database
open Ptp.Cache

let subjectModel json = 
  { Subject.Id=0; 
    SessionId=0; 
    Name=json?entry.AsString(); 
    Link=json?link.AsString() }

/// Fetch all subject metadata from the IGA API for the specified session year
let fetchAllSubjectsFromApi sessionYear = trial {
    let url = sprintf "/%s/subjects" sessionYear 
    let! pages = url |> fetchAllPages
    let! models = pages |> deserializeAs subjectModel
    return models
    }

let fetchKnownSubjectsQuery = sprintf "SELECT Id, Link from Subject WHERE SessionId = %s" SessionIdSubQuery
let persisSubjectsQuery = sprintf """INSERT INTO Subject(Name,Link,SessionId) 
VALUES (@Name,@Link,%s)""" SessionIdSubQuery

let fetchKnownSubjectsFromDb allSubjects = trial {
    let! knownSubjects = dbQuery<LinkAndId> fetchKnownSubjectsQuery
    return (allSubjects, knownSubjects)
    }
/// Fetch all subject metadata from the IGA API for the specified session year
let filterOutKnownSubjects (allSubjects:Subject seq, knownSubjects: LinkAndId seq) =
    allSubjects 
    |> except'' knownSubjects (fun ks -> ks.Link) (fun s -> s.Link) 
    |> Seq.toList
    |> ok

let persistNewSubjects subjects = 
    dbCommand persisSubjectsQuery subjects

/// Invalidate the Redis cache key for bill subjects
let invalidateSubjectsCache (subjects: Subject seq) =
    invalidateCache' SubjectsKey subjects


let nextSteps result =
    match result with
    | Ok (_, msgs) ->   
        let next = [ UpdateBills ]
        Next.Succeed(NextWorkflow next,msgs)
    | Bad msgs ->       Next.FailWith(msgs)

/// Find, add, and log new subjects
let workflow() =
    getCurrentSessionYear()
    >>= fetchAllSubjectsFromApi
    >>= fetchKnownSubjectsFromDb
    >>= filterOutKnownSubjects
    >>= persistNewSubjects
    >>= invalidateSubjectsCache
    |>  nextSteps