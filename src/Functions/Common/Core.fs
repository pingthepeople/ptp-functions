module Ptp.Core

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs.Host
open System

type Link = Link of String

type Workflow =
    | UpdateBills
    | UpdateBill of string
    | UpdateSubjects
    | UpdateLegislators
    | UpdateCommittees
    | UpdateCommittee of string
    | UpdateActions
    | UpdateChamberCal
    | UpdateCommitteeCal
    | UpdateDeadBills
    
type HttpWorkflow =
    | GenerateBillReport=90
    | GetLegislators=100

type QueryText = QueryText of string
type CommandText = CommandText of string

type WorkFlowFailure =
    | DatabaseCommandError of CommandText * string
    | DatabaseQueryError of QueryText * string
    | DatabaseQueryExpectationError of QueryText * string
    | APICommandError of CommandText * string
    | APIQueryError of QueryText * string
    | DTOtoDomainConversionFailure of string
    | DomainToDTOConversionFailure of string
    | CacheInvalidationError of string
    | UnknownBill of string
    | UnknownEntity of string
    | RequestValidationError of string
    | NextStepResolution of string

type NextWorkflow = NextWorkflow of Workflow seq

type Next = Result<NextWorkflow,WorkFlowFailure>

let terminalState = NextWorkflow List.empty<Workflow>

let (|StartsWith|_|) (p:string) (s:string) =
    if s.StartsWith(p) then
        Some(s.Substring(p.Length))
    else
        None

let (|Contains|_|) (p:string) (s:string) =
    if s.Contains(p) then Some(s) else None
    
let (|EmptySeq|_|) a = 
    if Seq.isEmpty a then Some () else None

let right (p:string) (s:string) =
    if String.IsNullOrWhiteSpace(p) || String.IsNullOrWhiteSpace(s)
    then None
    else 
        let lastIndex = s.LastIndexOf(p)
        if lastIndex = -1 
        then None
        else
            let start = lastIndex + p.Length
            let length = s.Length - start
            let result = s.Substring(start, length)
            Some result

let isEmpty str = str |> String.IsNullOrWhiteSpace
let timestamp() = System.DateTime.Now.ToString("HH:mm:ss.fff")
let timestamped s = sprintf "%s %s" (timestamp()) s
let datestamp() = System.DateTime.Now.ToString("yyyy-MM-dd")
let env = System.Environment.GetEnvironmentVariable

let inline except'' a aKey bKey b =
    let b' = b |> Seq.map bKey
    let a' = a |> Seq.map aKey
    let pairs = Seq.zip b' b |> Seq.toList
    let keyDiff = (set b')-(set a') |> Set.toList
    keyDiff
    |> List.map (fun k -> 
        pairs 
        |> List.find (fun (k',value) -> k = k')
        |> snd)

let inline except' a key b =
    b |> except'' a key key

let inline except a b =
    b |> except' a (fun x -> x)

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

let tryExec f failure handle = 
    try 
        f() |> ok 
    with ex ->
        ex.ToString()
        |> failure
        |> handle

let tryFail f failure =
    tryExec f failure fail
    
let tryWarn f x failure = 
    let warn msg = warn msg x
    tryExec f failure warn


/// Given a tuple of 'a and an option 'b, 
/// unwrap and return only the 'b where 'b is Some value
let chooseSnd (items:list<('a*'b option)>) =
    items |> List.map snd |> List.choose id

/// Given a tuple of 'a and an option 'b, 
/// unwrap and return only the pairs where 'b is Some value.
let chooseBoth (items:list<('a*'b option)>) =
    items 
    |> List.map (fun (a,b) -> 
        match b with
        | Some value -> Some(a,value)
        | None -> None)
    |> List.choose id

let inline flatten source msgs = 
    msgs 
    |> Seq.map (fun wf -> wf.ToString())
    |> String.concat "\n" 
    |> sprintf "%A\n%s" source

/// Log succesful or failed completion of the function, along with any warnings.
let executeWorkflow (log:TraceWriter) source (workflow: unit -> Next) = 
    let result = workflow()
    match result with
    | Fail (errs) -> 
        errs |> flatten source |> log.Warning
    | Warn (NextWorkflow steps, errs) ->  
        errs |> flatten source |> log.Warning        
    | Pass (NextWorkflow steps) -> ()            
    result

let throwOnFail source result =
    match result with
    | Fail (errs) ->
        errs |> flatten source |> Exception |> raise
    | _ -> ignore