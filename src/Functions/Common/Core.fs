module Ptp.Core

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs.Host
open System

type Workflow =
    | UpdateBills=10
    | UpdateSubjects=20
    | UpdateLegislators=30
    | UpdateCommittees=40
    | UpdateActions=50
    | UpdateChamberCal=60
    | UpdateCommitteeCal=70
    | UpdateDeadBills=80
    
type HttpWorkflow =
    | GenerateBillReport=90
    | GetLegislators=100

type QueryText = QueryText of string
type CommandText = CommandText of string

type WorkflowSuccess = WorkflowSuccess of string

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

let (|StartsWith|_|) (p:string) (s:string) =
    if s.StartsWith(p) then
        Some(s.Substring(p.Length))
    else
        None

let (|Contains|_|) (p:string) (s:string) =
    if s.Contains(p) then Some(s) else None
    
let (|EmptySeq|_|) a = 
    if Seq.isEmpty a then Some () else None

let isEmpty str = str |> String.IsNullOrWhiteSpace
let timestamp() = System.DateTime.Now.ToString("HH:mm:ss.fff")
let datestamp() = System.DateTime.Now.ToString("yyyy-MM-dd")

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

let tryF' f failure =
    try 
        f() |> ok 
    with 
        ex ->
            ex.ToString()
            |> failure            |> fail


let success a =
    WorkflowSuccess "Success!" |> ok

let successWithData data = 
    WorkflowSuccess data |> ok

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

let flatten (msgs:WorkFlowFailure list) = 
    msgs 
    |> List.rev 
    |> List.map (fun wf -> wf.ToString())
    |> String.concat "\n" 

/// Log succesful or failed completion of the function, along with any warnings.
let executeWorkflow (log:TraceWriter) source workflow = 

    log.Info(sprintf "[START] %A" source)
    let result = workflow()
    match result with
    | Fail (errs) ->  
        errs |> flatten |> log.Error
    | Warn (WorkflowSuccess s, errs) ->  
        errs |> flatten |> log.Warning
        s |> log.Info
    | Pass (WorkflowSuccess s) -> 
        s |> log.Info
    log.Info(sprintf "[FINISH] %A" source)
    result

let throwOnFail result =
    match result with   
    | Fail (boo) ->
        boo |> flatten |> Exception |> raise
    | _ -> ignore