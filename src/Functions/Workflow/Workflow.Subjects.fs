﻿module Ptp.Workflow.Subjects

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Common.Core
open Ptp.Common.Model
open Ptp.Common.Http
open Ptp.Common.Database
open Ptp.Common.Cache

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

let fetchKnownSubjectsQuery = (sprintf """
SELECT Id, Link FROM Subject 
WHERE SessionId = %s""" SessionIdSubQuery)

let persistSubjectsQuery = (sprintf """
IF NOT EXISTS
    ( SELECT Id FROM Subject
      WHERE Link=@Link )
BEGIN
    INSERT INTO Subject
    (Name,Link,SessionId) 
    VALUES (@Name,@Link,%s)
END""" SessionIdSubQuery)

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

let persistSubject subject =
    dbCommand persistSubjectsQuery subject

let persistNewSubjects subjects =
    subjects |> Seq.map persistSubject |> collect

/// Invalidate the Redis cache key for bill subjects
let invalidateSubjectsCache =
    tryInvalidateCacheIfAny SubjectsKey
    
let nextSteps result =
    let steps _ = [UpdateBills]
    result |> workflowContinues steps

/// Find, add, and log new subjects
let workflow() =
    queryCurrentSessionYear()
    >>= fetchAllSubjectsFromApi
    >>= fetchKnownSubjectsFromDb
    >>= filterOutKnownSubjects
    >>= persistNewSubjects
    >>= invalidateSubjectsCache
    |>  nextSteps