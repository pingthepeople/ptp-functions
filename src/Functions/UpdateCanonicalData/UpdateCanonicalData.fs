module Ptp.UpdateCanonicalData.Function

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.UpdateCanonicalData.Subjects
open Ptp.UpdateCanonicalData.Committees
open Ptp.UpdateCanonicalData.Legislators
open Ptp.UpdateCanonicalData.Bills
open Ptp.UpdateCanonicalData.Memberships
open Ptp.Core
open System
open Chessie.ErrorHandling
open Ptp.Logging

let chooseWorkflow command =
    match command with
    | Workflow.UpdateLegislators -> updateLegislators
    | Workflow.UpdateCommittees  -> updateCommittees
    | Workflow.UpdateComMembers  -> updateCommitteeMemberships
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
    | Workflow.UpdateLegislators  -> Some Workflow.UpdateCommittees
    | Workflow.UpdateCommittees   -> Some Workflow.UpdateComMembers
    | Workflow.UpdateComMembers   -> Some Workflow.UpdateSubjects
    | Workflow.UpdateSubjects     -> Some Workflow.UpdateBills
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

let enqueue (queue:ICollector<Workflow>) command =
    match command with
    | Some com -> com |> queue.Add
    | None -> ()

let Run(log: TraceWriter, command: Workflow, nextCommand: ICollector<Workflow>) =
    command
    |> chooseWorkflow
    |> runWorkflow log command
    |> chooseNext command
    |> enqueue nextCommand