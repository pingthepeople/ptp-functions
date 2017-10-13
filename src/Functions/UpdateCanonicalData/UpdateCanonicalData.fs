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
open Newtonsoft.Json

type WorkflowMessage = { Command:Workflow; State:string }

let chooseWorkflow msg =
    match msg.Command with
    | Workflow.UpdateLegislators -> Legislators.workflow
    | Workflow.UpdateCommittees  -> Committees.workflow
    | Workflow.UpdateSubjects    -> Subjects.workflow
    | Workflow.UpdateBills       -> Bills.workflow
    | _ -> raise (NotImplementedException())

let chooseNextOnFailure msg errs =
    match errs with
    | [ UnknownBill _ ] ->
        match msg.Command with
        | Workflow.UpdateActions      -> Some Workflow.UpdateBills
        | Workflow.UpdateChamberCal   -> Some Workflow.UpdateBills
        | Workflow.UpdateCommitteeCal -> Some Workflow.UpdateBills
        | Workflow.UpdateDeadBills    -> Some Workflow.UpdateBills
        | _ -> failwith "fffff"
    | _ -> failwith "fffff"

let chooseNextOnSuccess msg =
    match msg.Command with
    | Workflow.UpdateLegislators  -> None // Workflow.UpdateCommittees
    | Workflow.UpdateCommittees   -> None // Some Workflow.UpdateComMembers
    | Workflow.UpdateSubjects     -> None // Some Workflow.UpdateBills
    | Workflow.UpdateBills        -> None
    | Workflow.UpdateActions      -> Some Workflow.UpdateChamberCal
    | Workflow.UpdateChamberCal   -> Some Workflow.UpdateCommitteeCal
    | Workflow.UpdateCommitteeCal -> Some Workflow.UpdateDeadBills
    | Workflow.UpdateDeadBills    -> None
    | _ -> raise (NotImplementedException (msg.ToString()))

let chooseNext command result =
    match result with
    | Fail (errs) -> chooseNextOnFailure command errs
    | _ -> chooseNextOnSuccess command

let enqueueNext (log:TraceWriter) (queue:ICollector<string>) command=
    match command with
    | Some c -> 
        sprintf "Enqueueing next command: %A" command |> log.Info
        (c.ToString()) |> queue.Add
    | None -> 
        "Enqueueing no next command." |> log.Info
        ()


let deserialize command =
    command 
    |> JsonConvert.DeserializeObject<WorkflowMessage>


let Run(log: TraceWriter, command: string, nextCommand: ICollector<string>) =
    sprintf "Received command '%s'" command |> log.Info
    let msg = deserialize command
    msg
    |> chooseWorkflow
    |> executeWorkflow log msg
    |> chooseNext msg
    |> enqueueNext log nextCommand