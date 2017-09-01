module Ptp.Core

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open System
open System.Net


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
let sqlConStr() = System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString")

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

let tryRun desc (log:TraceWriter) f =
    try
        log.Info(sprintf "[START] %s" desc)
        f log |> ignore
        log.Info(sprintf "[FINISH] %s" desc)
    with
    | ex -> 
        log.Error(sprintf "[ERROR] %s" desc)
        reraise()
