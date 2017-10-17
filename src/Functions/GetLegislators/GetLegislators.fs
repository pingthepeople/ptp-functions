module Ptp.GetLegislators

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.CssSelectorExtensions
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Model
open Ptp.Http
open System
open System.Net
open System.Net.Http
open Ptp.Database
open Ptp.Queries

let district (node:HtmlNode)  =
    node.CssSelect(".legislator-lookup-details-right")
    |> Seq.last
    |> (fun n -> n.CssSelect("p"))
    |> Seq.head
    |> (fun n -> n.InnerText().Trim())
    |> Int32.Parse

let validateRequest loc = trial {
    let hasValue x = String.IsNullOrWhiteSpace(x) = false
    let validations =
        [
            loc.Address |> validateStr loc hasValue "Please provide an address" 
            loc.City    |> validateStr loc hasValue "Please provide a city" 
            loc.Zip     |> validateStr loc hasValue "Please provide a zip code" 
        ]
    let! result::_ = validations |> collect
    return result
    }

let fetchLegislatorsHtml (location,year) = trial {
    let address = location.Address |> WebUtility.UrlEncode
    let city = location.City |> WebUtility.UrlEncode
    let zip = location.Zip |> WebUtility.UrlEncode
    let url = sprintf "http://iga.in.gov/legislative/%s/legislators/search/?txtAddress=%s&txtCity=%s&txtZip1=%s" year address city zip
    let! html = fetchHtml url
    return html
    }

let parseLegislativeDistricts (document:HtmlDocument) =
    let legislators = 
        document.CssSelect ("div.legislator-wrapper") 
        |> Seq.toArray
    match legislators with
    | EmptySeq -> fail (UnknownEntity "No legislators found for that address")
    | _ ->
        let senDistrict = legislators.[0] |> district
        let repDistrict = legislators.[1] |> district
        (senDistrict, repDistrict) |> ok

type DistrictChamberQuery = {District:int; Chamber:Chamber}

let legislatorQuery = sprintf """SELECT TOP 1 * FROM Legislator 
WHERE 
    District = @District
    AND Chamber = @Chamber
    AND SessionId = %s""" SessionIdSubQuery

let lookupLegislator (district, chamber) = trial {
    let query = {District=district; Chamber=chamber}
    let! result = dbParameterizedQueryOne<Legislator> legislatorQuery query
    return result
    }

let associateWithKnownLegislators (senDistrict, repDistrict) = trial {
    let! sen = (senDistrict, Chamber.Senate) |> lookupLegislator
    let! rep = (repDistrict, Chamber.House) |> lookupLegislator
    return { Senator = sen; Representative = rep }
    }

let queryCurrentSessionYear req = trial {
    let! year = queryCurrentSessionYear()
    return (req,year)
    }

let deserializeLocationError = """Please provide a location of ContentType 'application/json' in the form '{ Address:string, City:string, Zip:string }'"""

let deserializeLocation = 
    validateBody<Location> deserializeLocationError

let workflow req =
    fun () ->
        deserializeLocation req
        >>= validateRequest
        >>= queryCurrentSessionYear
        >>= fetchLegislatorsHtml
        >>= parseLegislativeDistricts
        >>= associateWithKnownLegislators

let Run(req: HttpRequestMessage, log: TraceWriter) = 
    req
    |> workflow
    |> executeHttpWorkflow log HttpWorkflow.GetLegislators