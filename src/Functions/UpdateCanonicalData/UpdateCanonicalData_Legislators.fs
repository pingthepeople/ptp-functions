module Ptp.UpdateCanonicalData.Legislators

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

let legislatorModel (url,json) = 
    let firstName = json?firstName.AsString();
    let lastName = json?lastName.AsString();
    let partyName = json?party.AsString()
    let party = Enum.Parse(typedefof<Party>, partyName) :?> Party
    let chamberName =  json?chamber?name.AsString()
    let chamber = Enum.Parse(typedefof<Chamber>, chamberName) :?> Chamber

    { Legislator.Id=0; 
      FirstName=firstName; 
      LastName=lastName; 
      Link=url;
      Party=party;
      Chamber=chamber;
      Image=""; 
      SessionId=0; 
      District=0; }

/// Fetch URLs for all legislators in the current session.
let fetchAllLegislatorsLinksFromApi sessionYear = trial {
    let url = sprintf "/%s/legislators" sessionYear
    let legislatorUrl result = result?link.AsString()
    let! pages = url |> fetchAllPages
    let! result = pages |> deserializeAs legislatorUrl
    return result
    }

let fetchAllKnownLegislatorsQuery = sprintf "SELECT Link from Legislator WHERE SessionId = %s" SessionIdSubQuery
let insertLegislator= sprintf """INSERT INTO Legislator(FirstName,LastName,Link,Chamber,Party,District,Image,SessionId) 
VALUES (@FirstName,@LastName,@Link,@Chamber,@Party,@District,@Image,%s)""" SessionIdSubQuery

let fetchKnownLegislatorsFromDb allLegs = trial {
    let! knownLegs = dbQuery<string> fetchAllKnownLegislatorsQuery
    return (allLegs,knownLegs)
    }

/// Filter out URLs for any legislator that we already have in the database    
let filterOutKnownLegislators (allLegs,knownLegs) = 
    allLegs |> except knownLegs |> ok

/// Get full metadata for legislators that we don't yet know about
let resolveNewLegislators urls = trial {
    let! metadata = urls |> fetchAllParallel
    let! models = metadata |> chooseBoth |> deserializeAs legislatorModel
    return models
    }

/// Add new legislator records to the databsasase
let persistNewLegislators legislators = 
    dbCommand insertLegislator legislators

/// Invalidate the Redis cache key for legislators
let invalidateLegislatorCache legislators = 
    invalidateCache' LegislatorsKey legislators


/// Define afetchAllLegislatorsLinksFromApikflow
let workflow =
    getCurrentSessionYear
    >> bind fetchAllLegislatorsLinksFromApi
    >> bind fetchKnownLegislatorsFromDb
    >> bind filterOutKnownLegislators
    >> bind resolveNewLegislators
    >> bind persistNewLegislators
    >> bind invalidateLegislatorCache
    >> bind success