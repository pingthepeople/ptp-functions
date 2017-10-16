module GetLegislators

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

    {Name=name; Link=url; Chamber=chamber; Image=image; Party=party; District=district}

let setReasonableDefaults loc = 
    match loc.Year with 
    | 0 -> ok {loc with Year = System.DateTime.Now.Year}
    | _ -> ok loc

let validateRequest loc = trial {
    let thisYear = System.DateTime.Now.Year
    let nextYear = thisYear + 1
    let withinNextYear x = x <= (thisYear+1)
    let hasValue x = String.IsNullOrWhiteSpace(x) = false
    let validations =
        [
            loc.Year    |> validateInt loc withinNextYear (sprintf "Year can't be past %d" nextYear)
            loc.Address |> validateStr loc hasValue "Please provide an address" 
            loc.City    |> validateStr loc hasValue "Please provide a city" 
            loc.Zip     |> validateStr loc hasValue "Please provide a zip code" 
        ]
    let! result::_ = validations |> collect
    return result
    }

let fetchLegislatorsHtml location =
    let year = location.Year
    let address = location.Address |> WebUtility.UrlEncode
    let city = location.City |> WebUtility.UrlEncode
    let zip = location.Zip |> WebUtility.UrlEncode
    let url = sprintf "http://iga.in.gov/legislative/%d/legislators/search/?txtAddress=%s&txtCity=%s&txtZip1=%s" year address city zip
    let op() = url |> HtmlDocument.Load
    tryFail op (fun err -> (APIQueryError(QueryText(url), err)))

let parseLegislators (document:HtmlDocument) =
    let legislators = 
        document.CssSelect ("div.legislator-wrapper") 
        |> Seq.toArray
    if legislators |> Seq.isEmpty 
    then fail (UnknownEntity "No legislators found for that address")
    else ok [ legislators.[0] |> parse Chamber.Senate;
              legislators.[1] |> parse Chamber.House ]

let deserializeBody = 
    validateBody<Location> "Please provide a location of ContentType 'application/json' in the form '{ Address:string, City:string, Zip:string, Year:int (optional)}'"

let workflow req =
    (fun _ -> deserializeBody req)
    >> bind setReasonableDefaults
    >> bind validateRequest
    >> bind fetchLegislatorsHtml
    >> bind parseLegislators
    >> bind serialize

let Run(req: HttpRequestMessage, log: TraceWriter) = 
    req
    |> workflow
    |> executeHttpWorkflow log HttpWorkflow.GetLegislators
