module Ptp.Workflow.Common

open Ptp.Model
open FSharp.Data
open FSharp.Data.JsonExtensions

// JSON parsing functions for resolving members of committees and bills
let inline createMembership (unit:LinkAndId) (members: LinkAndId seq) position link = 
    let m = members |> Seq.tryFind (fun l -> l.Link = link)
    match m with
    | Some value -> [(unit.Id, value.Id, position)]
    | None   -> []

let doWithProperty name f (json:JsonValue) =
    match json.TryGetProperty(name) with
    | Some value -> value |> f
    | None -> []

let inline multiple unit candidates (json:JsonValue) toDomainObject property position = 
    let parseMultiple (value:JsonValue) =
        value.AsArray()
        |> Array.toList 
        |> List.map (fun m -> m?link.AsString())
        |> List.collect (createMembership unit candidates position)
        |> List.map toDomainObject
    json |> doWithProperty property parseMultiple

let inline single unit candidates (json:JsonValue) toDomainObject property position = 
    let parseMemberFromLink (value:JsonValue) =
        value.AsString()
        |> createMembership unit candidates position
        |> List.map toDomainObject        
    let parseLinkForPosition (value:JsonValue) =
        value |> doWithProperty "link" parseMemberFromLink
    json |> doWithProperty property parseLinkForPosition