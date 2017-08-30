module GetLegislators

open FSharp.Data
open FSharp.Data.CssSelectorExtensions
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open Ptp.Model
open Ptp.Http
open System
open System.Net
open System.Net.Http
open System.Net.Http.Formatting

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

let lookup location =
    let address = location.Address |> WebUtility.UrlEncode
    let city = location.City |> WebUtility.UrlEncode
    let zip = location.Zip |> WebUtility.UrlEncode  

    let legislators  = 
        sprintf "http://iga.in.gov/legislative/%d/legislators/search/?txtAddress=%s&txtCity=%s&txtZip1=%s" 2017 address city zip
        |> HtmlDocument.Load
        |> (fun d -> d.CssSelect ("div.legislator-wrapper")) 
        |> Seq.toArray

    match (legislators |> Seq.isEmpty) with
    | true -> []
    | false ->
        [ legislators.[0] |> parse Chamber.Senate;
          legislators.[1] |> parse Chamber.House ]
    
let Run(req: HttpRequestMessage, log: TraceWriter) = 
    async {
        let! content = Async.AwaitTask(req.Content.ReadAsStringAsync())
        let legislators = 
            content 
            |> JsonConvert.DeserializeObject<Location>
            |> lookup

        match legislators |> Seq.isEmpty with
        | true -> return httpError HttpStatusCode.BadRequest  "No legislators for that address"
        | false -> return httpOk legislators
    } |> Async.RunSynchronously