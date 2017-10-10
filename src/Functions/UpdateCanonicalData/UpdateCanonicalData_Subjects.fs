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

/// Fetch all subject metadata from the IGA API for the specified session year
let filterOutKnownSubjects (allSubjects: Subject seq) = trial {
    let queryText = sprintf "SELECT Link from Subject WHERE SessionId = %s" SessionIdSubQuery
    let! knownSubjects = dbQuery<string> queryText
    let matchOnUrl (subj:Subject) link = subj.Link = link
    let unknownSubjects = allSubjects |> except' knownSubjects matchOnUrl
    return unknownSubjects
    }

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
    >> bind filterOutKnownSubjects
    >> bind persistNewSubjects
    >> bind invalidateSubjectsCache
    >> bind describeNewSubjects