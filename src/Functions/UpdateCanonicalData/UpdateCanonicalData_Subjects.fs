module Ptp.UpdateCanonicalData.Subjects

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
let fetchKnownSubjectsFromDb allSubjects = trial {
        let queryText = sprintf "SELECT Id, Link from Subject WHERE SessionId = %s" SessionIdSubQuery
        let! knownSubjects = dbQuery<LinkAndId> queryText
        return (allSubjects, knownSubjects)
    }
/// Fetch all subject metadata from the IGA API for the specified session year
let filterOutKnownSubjects (allSubjects:Subject seq, knownSubjects: LinkAndId seq) =
    let matchOnUrl (a:Subject) b = a.Link = b.Link
    allSubjects 
    |> except' knownSubjects matchOnUrl 
    |> Seq.toList
    |> ok

let persistNewSubjects subjects = 
    let foo = sprintf """INSERT INTO Subject(Name,Link,SessionId) 
VALUES (@Name,@Link,%s)""" SessionIdSubQuery
    dbCommand foo subjects

/// Invalidate the Redis cache key for bill subjects
let invalidateSubjectsCache (subjects: Subject seq) =
    invalidateCache' SubjectsKey subjects

/// Log the addition of any new bill subjects
let describeNewSubjects (subjects: Subject seq) = 
    let describer (s:Subject) = sprintf "%s" s.Name
    subjects |> describeNewItems describer

/// Find, add, and log new subjects
let updateSubjects =
    getCurrentSessionYear
    >> bind fetchAllSubjectsFromApi
    >> bind fetchKnownSubjectsFromDb
    >> bind filterOutKnownSubjects
    >> bind persistNewSubjects
    >> bind invalidateSubjectsCache
    >> bind describeNewSubjects