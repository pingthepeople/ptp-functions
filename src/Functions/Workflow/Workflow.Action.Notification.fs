module Ptp.Workflow.ActionNotification

open Chessie.ErrorHandling
open Ptp.Common.Model
open Ptp.Common.Database
open Ptp.Common.Core
open Ptp.Workflow.Messaging

let formatBody (action:Action) bill title =
    let desc = action.Description.TrimEnd(';')
    match action.ActionType with
    | ActionType.AssignedToCommittee -> sprintf "%s was assigned to the %A Committee on %s." title action.Chamber desc
    | ActionType.CommitteeReading -> sprintf "%s had a committee hearing in the %A. The vote was: %s." title action.Chamber desc
    | ActionType.SecondReading -> sprintf "%s had a second reading in the %A. The vote was: %s." title action.Chamber desc
    | ActionType.ThirdReading -> sprintf "%s had a third reading in the %A. The vote was: %s." title action.Chamber desc
    | ActionType.SignedByPresidentOfSenate -> sprintf "%s has been signed by the President of the Senate. It will now be sent to the Governor to be signed into law or vetoed." title
    | ActionType.SignedByGovernor -> sprintf "%s has been signed into law by the Governor." title
    | ActionType.VetoedByGovernor -> sprintf "%s has been vetoed by the Governor. The Assembly now has the option to override that veto." title
    | ActionType.VetoOverridden -> sprintf "The veto on %s has been overridden in the %A. The vote was: %s." title action.Chamber desc
    | _ -> "(some other event type?)"

let fetchActionQuery = "SELECT * FROM Action WHERE Id = @Id"

let fetchAction id = 
    dbParameterizedQueryOne<Action> fetchActionQuery {Id=id}
   
let generateActionNotifications action =
    let formatBody = formatBody action
    generateNotifications formatBody action.BillId
    
let workflow enqueueNotifications id =
    fun () ->
        fetchAction id
        >>= generateActionNotifications
        >>= enqueueNotifications
        |> workflowTerminates