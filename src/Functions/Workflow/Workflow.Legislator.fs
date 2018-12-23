module Ptp.Workflow.Legislator

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Common.Core
open Ptp.Common.Model
open Ptp.Common.Http
open Ptp.Common.Database
open Ptp.Common.Cache
open System

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
      Image=(legislatorPortraitUrl link); 
      WebUrl=(legislatorWebUrl link);
      SessionId=0; 
      District=0; }

let fetchLegislator link = trial {
    let! item = link |> fetch
    let! model = item |> deserializeOneAs legislatorModel
    return model
    }

let fetchLegislatorHtml (legislator:Legislator) = trial {
    let url = legislator.Link |> legislatorWebUrl
    let! html = fetchHtml url
    return (legislator, html)
    }

let resolveDistrict (legislator:Legislator, html:HtmlDocument) =
    let elements = html.CssSelect(".district-heading")
    match elements with
    | EmptySeq ->
        sprintf "No district found for %s" legislator.Link
        |> DTOtoDomainConversionFailure
        |> warn' legislator
    | results ->
        results
        |> Seq.head
        |> fun dh -> dh.InnerText() // "District 42"
        |> split " "                // "District", "42"
        |> List.item 1              // "42"
        |> int                      // 42
        |> fun d -> {legislator with District = d}
        |> ok

let insertLegislatorIfNotExists = (sprintf """
IF NOT EXISTS 
    ( SELECT Id FROM Legislator 
      WHERE Link = @Link )
BEGIN
    INSERT INTO Legislator 
    (FirstName,LastName,Link,Chamber,Party,District,Image,WebUrl,SessionId) 
    VALUES (@FirstName,@LastName,@Link,@Chamber,@Party,@District,@Image,@WebUrl,%s) 
END""" SessionIdSubQuery)

/// Add new legislator records to the databsasase
let insertIfNotExists legislator = 
    dbCommand insertLegislatorIfNotExists legislator

/// Invalidate the Redis cache key for legislators
let invalidateLegislatorCache = 
    tryInvalidateCache LegislatorsKey

let workflow link =
    fun () ->
        fetchLegislator link
        >>= fetchLegislatorHtml
        >>= resolveDistrict
        >>= insertIfNotExists
        >>= invalidateLegislatorCache
        |>  workflowTerminates