module Ptp.Core

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open System
open Newtonsoft.Json
open System.Diagnostics

type Link = Link of String

type Workflow =
    | UpdateBills
    | UpdateBill of string
    | UpdateSubjects
    | UpdateLegislators
    | UpdateLegislator of string
    | UpdateCommittees
    | UpdateCommittee of string
    | UpdateActions
    | UpdateAction of string
    | UpdateCalendars
    | UpdateCalendar of string
    | SendCalendarNotification of int
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
    | EnqueueFailure of string
    | CacheInvalidationError of string
    | UnknownBill of string
    | UnknownBills of string seq
    | UnknownEntity of string
    | RequestValidationError of string
    | NextStepResolution of string
    | EntityAlreadyExists
    | NotificationGenerationError of string

type NextWorkflow = NextWorkflow of Workflow seq
type Next = Result<NextWorkflow,WorkFlowFailure>
let terminalState = NextWorkflow List.empty<Workflow>
let mapNext m x = x |> Seq.map m |> NextWorkflow

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
let split (delimiter:string) (s:string) = 
    s.Split([|delimiter|], StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList
let trimPath (s:string) = s.Trim([|' '; '/'|])

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
    try 
        f() |> ok 
    with ex ->
        ex.ToString()
        |> failure
        |> fail
    
let tryTee f x failure = 
    try 
        f()
        x |> ok
    with ex ->
        ex.ToString()
        |> failure
        |> (fun msg -> warn msg x)

let warn' success msg =
    warn msg success

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


let enqueue (queue:ICollector<string>) items =
    items |> Seq.iter queue.Add

let tryEnqueue (queue:ICollector<string>) items =
    let op() = enqueue queue items
    tryFail op EnqueueFailure

let enqueueNext log enqueue result =
    match result with
    | Ok (NextWorkflow next, _) ->
        match next with 
        | EmptySeq    -> 
            "Success. This is a terminal step"
            |> log
            |> ignore
        | steps ->
            let next = 
                steps 
                |> Seq.map JsonConvert.SerializeObject
            next 
            |> Seq.map (fun n -> n.ToString())
            |> String.concat "\n"
            |> sprintf "Success. Next steps:\n%s"
            |> log

            next
            |> enqueue
    | Bad _ ->
        "Failed. Enqueueing no next step."
        |> log
        |> ignore
    result

let inline workflowTerminates result = 
    match result with
    | Ok (_, msgs) ->   
        Next.Succeed(terminalState, msgs)
    | Bad msgs ->       
        Next.FailWith(msgs)

let inline workflowContinues steps result =
    match result with
    | Ok (success, msgs) ->
        let next = steps success
        Next.Succeed(NextWorkflow next,msgs)
    | Bad msgs ->       
        Next.FailWith(msgs)

let deserializeQueueItem<'t> (log: TraceWriter) str =
    let error e =
        e
        |> timestamped 
        |> log.Warning
    try
        match str with 
        | null -> 
            error "Received null message"
            None
        | "" -> 
            error "Received empty message"
            None
        | _  -> 
            str 
            |> JsonConvert.DeserializeObject<'t>
            |> Some
    with ex -> 
        sprintf "Exception when deserializing '%s': '%s'" str (ex.ToString())
        |> error
        None

let processQueueItem<'t> (log: TraceWriter) str action  =
    match deserializeQueueItem<'t> log str with
    | Some item -> 
        item |> action
        ignore
    | None -> 
        ignore