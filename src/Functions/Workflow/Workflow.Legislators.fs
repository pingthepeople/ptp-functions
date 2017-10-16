module Ptp.Workflow.Legislators

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Model
open Ptp.Queries
open Ptp.Http
open Ptp.Database
open Ptp.Cache
open System

let legislatorModel json = 
    let firstName = json?firstName.AsString();
    let lastName = json?lastName.AsString();
    let link = json?link.AsString();
    let partyName = json?party.AsString()
    let party = Enum.Parse(typedefof<Party>, partyName) :?> Party
    let chamber =  
        let pos = json?position_title.AsString()
        match pos with
        | "Senator" -> Chamber.Senate
        | "Representative" -> Chamber.House
        | _ -> sprintf "Could not determine chamber for '%s'" pos |> failwith

    { Legislator.Id=0; 
      FirstName=firstName; 
      LastName=lastName; 
      Link=link;
      Party=party;
      Chamber=chamber;
      Image=""; 
      SessionId=0; 
      District=0; }

/// Fetch URLs for all legislators in the current session.
let fetchAllLegislatorsFromApi sessionYear = trial {
    let url = sprintf "/%s/legislators" sessionYear
    let! items = url |> fetchAllPages
    let! result = items |> deserializeAs legislatorModel
    return result
    }

let fetchAllKnownLegislatorsQuery = sprintf "SELECT Link from Legislator WHERE SessionId = %s" SessionIdSubQuery
let insertLegislator= sprintf """INSERT INTO Legislator(FirstName,LastName,Link,Chamber,Party,District,Image,SessionId) 
VALUES (@FirstName,@LastName,@Link,@Chamber,@Party,@District,@Image,%s)""" SessionIdSubQuery

let fetchKnownLegislatorsFromDb allLegs = trial {
    let! knownLegs = dbQuery<string> fetchAllKnownLegislatorsQuery
    return (allLegs,knownLegs)
    }

/// Add new legislator records to the databsasase
let persistNewLegislators (allLegs,knownLegs) = 
    let stringKey (a:string) = a
    let modelKey (a:Legislator) = a.Link
    allLegs 
    |> except'' knownLegs stringKey modelKey   
    |> dbCommand insertLegislator

/// Invalidate the Redis cache key for legislators
let invalidateLegislatorCache legislators = 
    invalidateCache' LegislatorsKey legislators

let nextSteps result =
    match result with
    | Ok (_, msgs) ->   
        let next = [ UpdateCommittees; UpdateSubjects ]
        Next.Succeed(NextWorkflow next, msgs)
    | Bad msgs ->       
        Next.FailWith(msgs)

let workflow() =
    getCurrentSessionYear()
    >>= fetchAllLegislatorsFromApi
    >>= fetchKnownLegislatorsFromDb
    >>= persistNewLegislators
    >>= invalidateLegislatorCache
    |>  nextSteps