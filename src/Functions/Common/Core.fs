module Ptp.Core

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open System
open System.Net

type Command =
    | UpdateBills=1
    | UpdateSubjects=2
    | UpdateLegislators=3
    | UpdateCommittees=4
    | UpdateActions=5
    | UpdateChamberCalendar=6
    | UpdateCommitteeCalendar=7
    | UpdateDeadBills=8

type QueryText = QueryText of string
type CommandText = CommandText of string

type WorkFlowFailure =
    | DatabaseCommandError of CommandText * string
    | DatabaseQueryError of QueryText * string
    | APICommandError of CommandText * string
    | APIQueryError of QueryText * string
    | DTOtoDomainConversionFailure of string
    | CacheInvalidationError of string


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

let tee f x =
    f(x)
    x

let tryHttpF f (status:HttpStatusCode) msg =
    try 
        f() |> ok 
    with 
        ex -> fail ((status,sprintf "Failed to %s: %s" msg (ex.ToString())))


let tryF f msg =
    try 
        f() |> ok 
    with 
        ex -> fail (sprintf "Failed to %s: %s" msg (ex.ToString()))

let tryF' f failure =
    try 
        f() |> ok 
    with 
        ex ->
            ex.ToString()
            |> failure
            |> fail

let tryFIfAny x f msg =
    if x |> Seq.isEmpty 
    then ok x
    else 
        try 
            f() |> ok 
        with 
            ex -> fail (sprintf "Failed to %s: %s" msg (ex.ToString()))

let tryRun desc (log:TraceWriter) f =
    try
        log.Info(sprintf "[START] %s" desc)
        f log |> ignore
        log.Info(sprintf "[FINISH] %s" desc)
    with
    | ex -> 
        log.Error(sprintf "[ERROR] %s" desc)
        reraise()