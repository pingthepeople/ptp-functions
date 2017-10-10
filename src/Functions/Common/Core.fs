module Ptp.Core

open Chessie.ErrorHandling
open System
open System.Net

type Update =
    | Bills=1
    | Subjects=2
    | Legislators=3
    | Committees=4
    | Actions=5
    | ChamberCal=6
    | CommitteeCal=7
    | DeadBills=8
    | ComMembers=9

type QueryText = QueryText of string
type CommandText = CommandText of string

type WorkFlowFailure =
    | DatabaseCommandError of CommandText * string
    | DatabaseQueryError of QueryText * string
    | DatabaseQueryExpectationError of QueryText * string
    | APICommandError of CommandText * string
    | APIQueryError of QueryText * string
    | DTOtoDomainConversionFailure of string
    | CacheInvalidationError of string
    | UnknownBill of string

let (|StartsWith|_|) (p:string) (s:string) =
    if s.StartsWith(p) then
        Some(s.Substring(p.Length))
    else
        None
    
let (|EmptySeq|_|) a = 
    if Seq.isEmpty a then Some () else None

let isEmpty str = str |> String.IsNullOrWhiteSpace
let timestamp() = System.DateTime.Now.ToString("HH:mm:ss.fff")
let datestamp() = System.DateTime.Now.ToString("yyyy-MM-dd")

let inline except b a =
    let notInB a' = 
        b 
        |> Seq.exists (fun b' -> a' = b') 
        |> not
    a |> Seq.filter notInB

let inline except' b matchPredicate a =
    let notInB a' = 
        b 
        |> Seq.exists (fun b' -> matchPredicate a' b') 
        |> not
    a |> Seq.filter notInB

let inline intersect' b matchPredicate a =
    let inB a' = 
        b 
        |> Seq.exists (fun b' -> matchPredicate a' b') 
    a |> Seq.filter inB

let inline intersect b a =
    a |> intersect' b (fun a' b' -> a' = b') 

let tee f x =
    f(x)
    x

let tryF' f failure =
    try 
        f() |> ok 
    with 
        ex ->
            ex.ToString()
            |> failure
            |> fail