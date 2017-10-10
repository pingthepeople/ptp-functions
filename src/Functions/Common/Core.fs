﻿module Ptp.Core

open Chessie.ErrorHandling
open System

type Workflow =
    | UpdateBills=1
    | UpdateSubjects=2
    | UpdateLegislators=3
    | UpdateCommittees=4
    | UpdateActions=5
    | UpdateChamberCal=6
    | UpdateCommitteeCal=7
    | UpdateDeadBills=8
    | UpdateComMembers=9
    // HTTP
    | HttpGenerateBillReport=10
    | HttpGetLegislators=11

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