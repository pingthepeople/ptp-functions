module Ptp.Common.Core

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Extensions.Logging
open System
open Newtonsoft.Json

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
    | UpdateDeadBills
    | DailyRoundup
    | GenerateCalendarNotification of int
    | GenerateActionNotification of int
    | GenerateRoundupNotification of int
    | GenerateDeadBillNotification of int
    
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
    | APIBillNotAvailable of string
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
    | NotificationAlreadyDelivered
    | NotificationGenerationError of string
    | NotificationDeliveryError of string
    | BadRequestError of string

type NextWorkflow = NextWorkflow of Workflow seq
type Next = Result<NextWorkflow,WorkFlowFailure>
let terminalState = NextWorkflow List.empty<Workflow>
let mapNext m x = x |> Seq.map m |> NextWorkflow

let (|StartsWith|_|) (p:string) (s:string) =
    if s.StartsWith(p) 
    then Some(s.Substring(p.Length))
    else None

let (|Contains|_|) (p:string) (s:string) =
    if s.Contains(p) 
    then Some(s) 
    else None
    
let (|EmptySeq|_|) a = 
    if Seq.isEmpty a 
    then Some () 
    else None

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
/// Whether a string has value (is not null/empty)
let hasValue str = 
    System.String.IsNullOrWhiteSpace(str) = false
/// A milisecond timestamp, eg. '14:10:02.352'
let timestamp() = System.DateTime.Now.ToString("HH:mm:ss.fff")
/// A timestamped message, eg. '14:10:02.352 msg'
let timestamped s = sprintf "%s %s" (timestamp()) s
/// The current datestamp in sortable/comparable form, eg. '2018-01-03'
let datestamp() = System.DateTime.Now.ToString("yyyy-MM-dd")
let env = System.Environment.GetEnvironmentVariable
/// Split a string return the results as a list.
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

let inline flattenMsgs msgs = 
    msgs 
    |> Seq.map (fun wf -> wf.ToString())
    |> String.concat "\n" 

let logStart (log: ILogger) cmd =
    sprintf "[Start] [%A]" cmd 
    |> log.LogInformation

let logFinish cmd (stopwatch:Diagnostics.Stopwatch) logger label msg =
    sprintf "[%s] [%A] (%d ms) %s" label cmd stopwatch.ElapsedMilliseconds msg
    |> logger

/// Log succesful or failed completion of the function, along with any warnings.
let executeWorkflow (log:ILogger) source workflow =
    logStart log source
    let logFinish = logFinish source (Diagnostics.Stopwatch.StartNew())
    let result = workflow()
    match result with
    | Fail (errs) -> 
        errs |> flattenMsgs |> logFinish log.LogError "Error"
    | Warn (_, errs) ->  
        errs |> flattenMsgs |> logFinish log.LogWarning "Warning"       
    | Pass (_) -> logFinish log.LogInformation "Success" ""
    result

let inline throwErrors source errs =
    errs 
    |> flattenMsgs
    |> sprintf "[Error] [%A]:\n%s" source 
    |> Exception 
    |> raise

let inline throwOnFail source result =
    match result with
    | Fail (errs) -> throwErrors source errs
    | _ -> ignore

let inline enqueue (queue:ICollector<string>) items =
    items 
    |> Seq.map JsonConvert.SerializeObject
    |> Seq.iter queue.Add

let inline tryEnqueue (queue:ICollector<string>) items =
    let op() = 
        enqueue queue items
        items
    tryFail op EnqueueFailure

let enqueueNext (log:ILogger) source enqueue result =
    match result with
    | Ok (NextWorkflow next, _) ->
        match next with 
        | EmptySeq    -> 
            sprintf "[Next] [%A] This is a terminal step." source
            |> log.LogInformation
            |> ignore
        | steps ->
            steps 
            |> Seq.map (fun n -> n.ToString())
            |> String.concat "\n"
            |> sprintf "[Next] [%A] Next steps:\n%s" source
            |> log.LogInformation
            steps
            |> enqueue
        result
    | _ -> 
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

let inline deserializeQueueItem<'t> (log: ILogger) str =
    let error e =
        e
        |> timestamped 
        |> log.LogWarning
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

let processQueueItem<'t> (log: ILogger) str action  =
    match deserializeQueueItem<'t> log str with
    | Some item -> 
        item |> action
        ignore
    | None -> 
        ignore