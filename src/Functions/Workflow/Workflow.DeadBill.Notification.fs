module Ptp.Workflow.DeadBillNotification

open Ptp.Common.Core
open Ptp.Common.Model
open Ptp.Workflow.Messaging
open Ptp.Common.Database
open Chessie.ErrorHandling

let diedInChamber (action:Action) =
    match (action.ActionType) with
    | ActionType.AssignedToCommittee -> action.Chamber
    | ActionType.CommitteeReading    -> action.Chamber
    | ActionType.SecondReading       -> action.Chamber
    | ActionType.ThirdReading        ->
        match action.Chamber with
        | Chamber.House  -> Chamber.Senate
        | Chamber.Senate -> Chamber.House
        | _ -> failwith ("Unrecognized chamber")
    | _ -> failwith ("Unrecognized action type")

let diedForReason (action:Action) = 
    match (action.ActionType) with
    | ActionType.AssignedToCommittee -> "committee hearing"
    | ActionType.CommitteeReading    -> "second reading"
    | ActionType.SecondReading       -> "third reading"
    | ActionType.ThirdReading        -> "committee assignment"
    | _ -> failwith ("Unrecognized action type")

// Format a nice description of the action
let formatBody (action:Action option) (bill:Bill) title =
    
    let diedInChamber = 
        match action with
        | None    -> bill.Chamber
        | Some(x) -> diedInChamber x

    let diedForReason = 
        match action with
        | None    -> "committee assignment"
        | Some(x) -> diedForReason x

    sprintf "%s has died in the %A upon missing the deadline for a %s." title diedInChamber diedForReason

let fetchActionQuery = """
SELECT TOP 1 * 
FROM Action 
WHERE 
    BillId = @Id 
    AND ActionType <> 0
ORDER BY Date DESC"""

let fetchAction id = 
    dbParameterizedQuery<Action> fetchActionQuery {Id=id}
   
let generateActionNotifications billId action =
    let formatBody = action |> Seq.tryHead |> formatBody      
    generateNotifications formatBody billId
    
let workflow enqueueNotifications id =
    fun () ->
        fetchAction id
        >>= generateActionNotifications id
        >>= enqueueNotifications
        |> workflowTerminates
