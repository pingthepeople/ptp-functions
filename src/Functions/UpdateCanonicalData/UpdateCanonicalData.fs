module Ptp.UpdateCanonicalData.Function

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.UpdateCanonicalData.Subjects
open Ptp.UpdateCanonicalData.Committees
open Ptp.UpdateCanonicalData.Legislators
open Ptp.UpdateCanonicalData.Bills
open Ptp.Core
open System
open Chessie.ErrorHandling
open Ptp.Logging

let chooseWorkflow command =
    match command with
    | Workflow.UpdateLegislators -> updateLegislators
    | Workflow.UpdateCommittees  -> updateCommittees
    | Workflow.UpdateSubjects    -> updateSubjects
    | Workflow.UpdateBills       -> updateBills
    | _ -> raise (NotImplementedException())

let chooseNextOnFailure command errs =
    match errs with
    | [ UnknownBill _ ] ->
        match command with
        | Workflow.UpdateActions      -> Some Workflow.UpdateBills
        | Workflow.UpdateChamberCal   -> Some Workflow.UpdateBills
        | Workflow.UpdateCommitteeCal -> Some Workflow.UpdateBills
        | Workflow.UpdateDeadBills    -> Some Workflow.UpdateBills
        | _ -> failwith "fffff"
    | _ -> failwith "fffff"

let chooseNextOnSuccess command =
    match command with
    | Workflow.UpdateLegislators  -> None // Workflow.UpdateCommittees
    | Workflow.UpdateCommittees   -> None // Some Workflow.UpdateComMembers
    | Workflow.UpdateSubjects     -> None // Some Workflow.UpdateBills
    | Workflow.UpdateBills        -> None
    | Workflow.UpdateActions      -> Some Workflow.UpdateChamberCal
    | Workflow.UpdateChamberCal   -> Some Workflow.UpdateCommitteeCal
    | Workflow.UpdateCommitteeCal -> Some Workflow.UpdateDeadBills
    | Workflow.UpdateDeadBills    -> None
    | _ -> raise (NotImplementedException (command.ToString()))

let chooseNext command result =
    match result with
    | Fail (errs) -> chooseNextOnFailure command errs
    | _ -> chooseNextOnSuccess command

let enqueue (log:TraceWriter) (queue:ICollector<string>) command=
    match command with
    | Some c -> 
        sprintf "Enqueueing next command: %A" command |> log.Info
        (c.ToString()) |> queue.Add
    | None -> 
        "Enqueueing no next command." |> log.Info
        ()

let tryParse command =
    let parsed = Enum.Parse(typedefof<Workflow>, command) :?> Workflow
    match int parsed with
    | 0 -> failwith "Could not parse Workflow" 
    | _ -> parsed

let Run(log: TraceWriter, command: string, nextCommand: ICollector<string>) =
    sprintf "Received command '%s'" command |> log.Info
    let parsedCommand = tryParse command
    parsedCommand
    |> chooseWorkflow
    |> runWorkflow log parsedCommand
    |> chooseNext parsedCommand
    |> enqueue log nextCommand