module Ptp.Workflow.Legislator

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

let igaWebUrl (link:string) replace = 
    link.Replace("legislators/", replace)
    |> trimPath
    |> sprintf "http://iga.in.gov/legislative/%s"

let infoUrl link = igaWebUrl link "legislators/legislator_"
let portaitUrl link = igaWebUrl link "portraits/legislator_"

let legislatorModel json = 
    let firstName = json?firstName.AsString();
    let lastName = json?lastName.AsString();
    let link = json?link.AsString();
    let partyName = json?party.AsString()
    let party = Enum.Parse(typedefof<Party>, partyName) :?> Party
    let chamberName = json?chamber?name.AsString()
    let chamber = Enum.Parse(typedefof<Chamber>, chamberName) :?> Chamber

    { Legislator.Id=0; 
      FirstName=firstName; 
      LastName=lastName; 
      Link=link;
      Party=party;
      Chamber=chamber;
      Image=(portaitUrl link); 
      SessionId=0; 
      District=0; }

let fetchLegislator link = trial {
    let! item = link |> fetch
    let! model = item |> deserializeOneAs legislatorModel
    return model
    }

let fetchLegislatorHtml (legislator:Legislator) = trial {
    let url = legislator.Link |> infoUrl
    let! html = fetchHtml url
    return (legislator, html)
    }

let resolveDistrict (legislator:Legislator, html:HtmlDocument) =
    let elements = html.CssSelect(".district-heading")
    match elements with
    | EmptySeq ->
        let msg = 
            sprintf "No district found for %s" legislator.Link
            |> DTOtoDomainConversionFailure
        warn msg legislator 
    | results ->
        let district = 
            results
            |> Seq.head
            |> (fun dh -> dh.InnerText()) // "District 42"
            |> split " "                  // "District", "42"
            |> Seq.item 1                 // "42"
            |> int                        // 42
        {legislator with District = district}
        |> ok

let insertLegislatorIfNotExists= sprintf """
IF NOT EXISTS (SELECT Id FROM Legislator WHERE Link = @Link and SessionId = %s)
BEGIN
INSERT INTO Legislator(FirstName,LastName,Link,Chamber,Party,District,Image,SessionId) 
VALUES (@FirstName,@LastName,@Link,@Chamber,@Party,@District,@Image,%s) END""" SessionIdSubQuery SessionIdSubQuery

/// Add new legislator records to the databsasase
let insertIfNotExists legislator = 
    dbCommand insertLegislatorIfNotExists legislator

/// Invalidate the Redis cache key for legislators
let invalidateLegislatorCache legislator = 
    [legislator] |> invalidateCache' LegislatorsKey

let workflow link =
    (fun () ->
        fetchLegislator link
        >>= fetchLegislatorHtml
        >>= resolveDistrict
        >>= insertIfNotExists
        >>= invalidateLegislatorCache
        |>  workflowTerminates)