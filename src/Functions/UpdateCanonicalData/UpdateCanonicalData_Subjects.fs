module Ptp.UpdateCanonicalData.Subjects

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

// SUBJECTS 

/// Fetch all bill subjects for the current session from the IGA API
let fetchAllSubjects sessionId sessionYear = 
    let toModel subject = 
      { Subject.Id=0; 
        SessionId=sessionId; 
        Name=subject?entry.AsString(); 
        Link=subject?link.AsString() }
    let op() = 
        fetchAll (sprintf "/%s/subjects" sessionYear)
        |> List.map toModel
    tryF op "fetch all subjects"

/// Filter out any bill subjects that we already have in the database
let resolveNewSubjects cn sessionId  (subjects: Subject list) = 
    let op() = 
        let knownSubjects = 
            cn 
            |> dapperQuery<string> (sprintf "SELECT Link from Subject WHERE SessionId = %d" sessionId)
        subjects
        |> List.filter (fun s -> knownSubjects |> Seq.exists (fun ks -> ks = s.Link) |> not)
    tryF op "resolve new subjects"

/// Add new bill subject records to the database
let insertNewSubjects cn (subjects: Subject list) = 
    let insert subject =
        cn 
        |> dapperParametrizedQuery<int> InsertSubject subject 
        |> ignore
    let op () = 
        subjects |> List.iter insert
        subjects
    tryF op "insert new subjects"

/// Invalidate the Redis cache key for bill subjects
let invalidateSubjectsCache  (subjects: Subject list) =
    let op() = 
        subjects |> invalidateCache SubjectsKey
    tryF op "invalidate Subjects cache"

/// Log the addition of any new bill subjects
let logNewSubjects (log:TraceWriter) (subjects: Subject list) = 
    subjects
    |> List.map(fun s -> s.Name)
    |> logNewAdditions log "subject"
    ok subjects

/// Find, add, and log new subjects
let updateSubjects (log:TraceWriter) cn sessionId sessionYear =
    let AddNewSubjects = "UpdateCanonicalData / Add new subjects"
    log.Info(sprintf "[START] %s" AddNewSubjects)
    fetchAllSubjects sessionId sessionYear
    >>= resolveNewSubjects cn sessionId 
    >>= insertNewSubjects cn
    >>= invalidateSubjectsCache
    >>= logNewSubjects log    
    |> haltOnFail log AddNewSubjects