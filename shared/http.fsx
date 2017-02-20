#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Primitives"
#r "System.Net.Http.Formatting"
#r "System.Web.Http"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"

namespace IgaTracker 

module  Http =

    open System
    open System.IO
    open System.Text
    open System.Net
    open System.Net.Http
    open System.Threading
    open System.Threading.Tasks
    open FSharp.Data
    open FSharp.Data.JsonExtensions

    let get endpoint = 
        let standardHeaders = [ "Accept", "application/json"; "Authorization", "Token " + Environment.GetEnvironmentVariable("IgaApiKey") ]
        Http.RequestString("https://api.iga.in.gov" + endpoint, httpMethod = "GET", headers = standardHeaders) |> JsonValue.Parse

    let fetchAll (endpoint:string) =
        let rec fetchRec (link:string) =
            let json = get link
            let items = json?items.AsArray() |> Array.toList
            try
                let nextLink = json?nextLink.ToString().Trim('"')
                items @ (fetchRec nextLink)
            with
            | ex -> items
        fetchRec endpoint

    let doByPage endpoint func =
        let rec fetchRec link func =
            let json = get link
            func (json?items.AsArray())
            try
                let nextLink = json?nextLink.ToString().Trim('"')
                fetchRec nextLink func
            with
            | ex -> 
                printfn "no more pages"
                ignore
        fetchRec endpoint func

    let (|StartsWith|_|) (p:string) (s:string) =
        if s.StartsWith(p) then
            Some(s.Substring(p.Length))
        else
            None
