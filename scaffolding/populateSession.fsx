// Configure HTTP / JSON 

#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Primitives"
#r "System.Net.Http.Formatting"
#r "System.Web.Http"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"

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

let doWithPage endpoint func =
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

// Configure Database 

#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/DapperExtensions/lib/net45/DapperExtensions.dll"
#load "../modules/model.fs"
#load "../modules/queries.fs"

open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open DapperExtensions
open IgaTracker.Model
open IgaTracker.Queries

let connStr = System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString")
let cn = new SqlConnection(connStr)
let sessionYear = "/2017"

let populateCommittees session = 
    
    let toModel chamber c ={
        Committee.Id=0; 
        SessionId=1; 
        Chamber=chamber; 
        Name=c?name.AsString(); 
        Link=c?link.AsString().Replace("standing-","") }
    
    let houseCommittess = 
        fetchAll (session + "/chambers/house/committees/standing") 
        |> List.map (toModel Chamber.House) 
    let senateCommittess = 
        fetchAll (session + "/chambers/senate/committees/standing") 
        |> List.map (toModel Chamber.House) 
    
    cn.Open();
    houseCommittess |> List.iter (fun c -> cn.Insert(c) |> ignore)
    senateCommittess |> List.iter (fun c -> cn.Insert(c) |> ignore)
    cn.Close();

let populateBills session =
    
    let toModel bill = {
        Bill.Id=0; 
        SessionId=1; 
        Name=bill?billName.AsString(); 
        Link=bill?link.AsString(); 
        Title=bill?latestVersion?shortDescription.AsString(); 
        Description= bill?latestVersion?digest.AsString();
        Topics=bill?latestVersion?subjects.AsArray() |> Array.toList |> List.map (fun a -> a?entry.AsString()) |> String.concat ", "; 
        Authors=bill?latestVersion?authors.AsArray() |> Array.toList |> List.map (fun a -> a?lastName.AsString()) |> List.sort |> String.concat ", "; }
    
    let bills = 
        fetchAll (session + "/bills") 
        |> List.map (fun b -> get (b?link.AsString()) |> toModel) 
    
    cn.Open();
    bills |> List.iter (fun b -> cn.Insert(b) |> ignore) 
    cn.Close();

