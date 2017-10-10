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
    | Update.Legislators -> updateLegislators
    | Update.Committees  -> updateCommittees
    | Update.ComMembers  -> updateCommitteeMemberships
    | Update.Subjects    -> updateSubjects
    | Update.Bills       -> updateBills
    | _ -> raise (NotImplementedException())

let chooseNextOnFailure command errs =
    match errs with
    | [ UnknownBill _ ] ->
        match command with
        | Update.Actions      -> Some Update.Bills
        | Update.ChamberCal   -> Some Update.Bills
        | Update.CommitteeCal -> Some Update.Bills
        | Update.DeadBills    -> Some Update.Bills
        | _ -> failwith "fffff"
    | _ -> failwith "fffff"

let chooseNextOnSuccess command =
    match command with
    | Update.Legislators  -> Some Update.Committees
    | Update.Committees   -> Some Update.ComMembers
    | Update.ComMembers   -> Some Update.Subjects
    | Update.Subjects     -> Some Update.Bills
    | Update.Bills        -> None
    | Update.Actions      -> Some Update.ChamberCal
    | Update.ChamberCal   -> Some Update.CommitteeCal
    | Update.CommitteeCal -> Some Update.DeadBills
    | Update.DeadBills    -> None
    | _ -> raise (NotImplementedException (command.ToString()))

let chooseNext command result =
    match result with
    | Fail (errs) -> chooseNextOnFailure command errs
    | _ -> chooseNextOnSuccess command

let enqueue (queue:ICollector<Update>) command =
    match command with
    | Some com -> com |> queue.Add
    | None -> ()

let Run(log: TraceWriter, command: Update, nextCommand: ICollector<Update>) =
    command
    |> chooseWorkflow
    |> runWorkflow log command
    |> chooseNext command
    |> enqueue nextCommand