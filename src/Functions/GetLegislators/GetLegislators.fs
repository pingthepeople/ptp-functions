module GetLegislators

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.CssSelectorExtensions
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Logging
open System
open System.Net
open System.Net.Http

let detail side (node:HtmlNode) =
    node.CssSelect(sprintf ".legislator-lookup-details-%s" side)
    |> Seq.last
    |> (fun n -> n.CssSelect("p"))
    |> Seq.head
    |> (fun n -> n.InnerText().Trim())

let parse chamber (node:HtmlNode)  =
    let person = 
        node.Descendants ["h3"]
        |> Seq.head
        |> (fun h3 -> h3.Descendants ["a"])
        |> Seq.head
    let name = person.InnerText().Trim()
    let url = sprintf "https://iga.in.gov%s" (person.AttributeValue("href").Trim())
    let image =
        node.CssSelect (".legislator-lookup-portrait > img")
        |> Seq.head
        |> (fun n -> n.AttributeValue("src").Trim())
        |> sprintf "https://iga.in.gov%s"
    let party =
        node 
        |> detail "left"
        |> (fun p -> Enum.Parse(typedefof<Party>, p) :?> Party)
    let district = 
        node 
        |> detail "right" 
        |> Int32.Parse

    {Id=0; Name=name; Url=url; Chamber=chamber; Image=image; Party=party; District=district}

let validateLocation loc =
    let thisYear = System.DateTime.Now.Year
    let nextYear = thisYear + 1
    if   isEmpty loc.Address    then fail (HttpStatusCode.BadRequest, "Please provide an address")
    elif isEmpty loc.City       then fail (HttpStatusCode.BadRequest, "Please provide a city")
    elif isEmpty loc.Zip        then fail (HttpStatusCode.BadRequest, "Please provide a zip code")
    elif loc.Year > nextYear    then fail (HttpStatusCode.BadRequest, "The year cannot be past next year")
    elif loc.Year = 0           then ok { loc with Year=thisYear }
    else ok loc

let fetchLegislatorsHtml location =
    let year = location.Year
    let address = location.Address |> WebUtility.UrlEncode
    let city = location.City |> WebUtility.UrlEncode
    let zip = location.Zip |> WebUtility.UrlEncode  
    try 
        sprintf "http://iga.in.gov/legislative/%d/legislators/search/?txtAddress=%s&txtCity=%s&txtZip1=%s" year address city zip
        |> HtmlDocument.Load
        |> ok
    with
    | ex -> 
        fail (HttpStatusCode.InternalServerError, (sprintf "Failed to fetch legislators information from IGA: %s" ex.Message))

let parseLegislators (document:HtmlDocument) =
    let legislators = 
        document.CssSelect ("div.legislator-wrapper") 
        |> Seq.toArray
    if legislators |> Seq.isEmpty 
    then fail (HttpStatusCode.NotFound, "No legislators found for that address")
    else ok [ legislators.[0] |> parse Chamber.Senate;
              legislators.[1] |> parse Chamber.House ]

let deserializeLocation req = 
    req 
    |> validateBody<Location> "Please provide a location in the form '{ Address:STRING, City:STRING, Zip:STRING, Year:INT (optional)}'"

let processRequest req = 
    req
    |> deserializeLocation 
    >>= validateLocation 
    >>= fetchLegislatorsHtml
    >>= parseLegislators

let Run(req: HttpRequestMessage, log: TraceWriter) = 
    log.Info("GetLegislators function triggered.")
    req
    |> processRequest
    |> continueOnFail log "GetLegislators"
    |> constructHttpResponse