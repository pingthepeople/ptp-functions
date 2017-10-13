module Ptp.Workflow.Function

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Workflow
open System
open Chessie.ErrorHandling
open Newtonsoft.Json

let deserialize command =
    command 
    |> JsonConvert.DeserializeObject<Workflow>

let chooseWorkflow msg =
    match msg with
    | UpdateLegislators -> Legislators.workflow
    | UpdateCommittees  -> Committees.workflow
    | UpdateSubjects    -> Subjects.workflow
    | UpdateBills       -> Bills.workflow
    | UpdateBill link   -> Bill.workflow link
    | _ -> raise (NotImplementedException())

let enqueueNext (log:TraceWriter) (queue:ICollector<string>) result =
    match result with
    | Ok (NextWorkflow next, _) ->
        match next with 
        | EmptySeq    -> 
            "This is a terminal step. Enqueueing no next step." 
            |> log.Info 
            |> ignore
        | steps ->
            let next = 
                steps 
                |> Seq.map JsonConvert.SerializeObject
            next 
            |> Seq.map (sprintf "Enqueueing next step: %s") 
            |> Seq.iter log.Info
            next 
            |> Seq.iter queue.Add
    | Bad _ ->
        "The workflow failed. Enqueueing no next step." 
        |> log.Info 
        |> ignore

let Run(log: TraceWriter, command: string, nextCommand: ICollector<string>) =
    sprintf "Received command '%s'" command |> log.Info
    let msg = deserialize command
    msg
    |> chooseWorkflow
    |> executeWorkflow log msg
    |> enqueueNext log nextCommand