module Ptp.Workflow.Action

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open Ptp.Cache

/// Determine the action type based on the API description
let parseType description =
    match description with
    | StartsWith "First reading: referred to Committee on " rest -> ActionType.AssignedToCommittee
    | StartsWith "Committee report" rest -> ActionType.CommitteeReading
    | StartsWith "Second reading" rest -> ActionType.SecondReading
    | StartsWith "Third reading" rest -> ActionType.ThirdReading
    | StartsWith "Signed by the President of the Senate" rest -> ActionType.SignedByPresidentOfSenate
    | StartsWith "Signed by the Governor" rest -> ActionType.SignedByGovernor
    | StartsWith "Vetoed by the Governor" rest -> ActionType.VetoedByGovernor
    | StartsWith "Veto overridden" rest -> ActionType.VetoOverridden
    | _ -> ActionType.Unknown

/// Determine the interesting parts of the API description
let parseDescription description =
    match description with
    | StartsWith "First reading: referred to Committee on " rest -> rest
    | StartsWith "Committee report: " rest -> rest
    | StartsWith "Second reading: " rest -> rest
    | StartsWith "Third reading: " rest -> rest
    | StartsWith "Signed by the President of the Senate" rest -> rest
    | StartsWith "Signed by the Governor" rest -> rest
    | StartsWith "Vetoed by the Governor" rest -> rest
    | StartsWith "Veto overridden by the House; " rest -> rest
    | StartsWith "Veto overridden by the Senate; " rest -> rest
    | other -> other

// DB Query Text 
let billQuery = """
SELECT TOP 1 * 
FROM Bill 
WHERE Link = @Link"""

let insertAction = """
IF NOT EXISTS 
    ( SELECT Id FROM Action 
      WHERE Link = @Link )
	BEGIN
		INSERT INTO Action
        (Description,Link,Date,ActionType,Chamber,BillId) 
		VALUES (@Description,@Link,@Date,@ActionType,@Chamber,@BillId)
		SELECT CAST(SCOPE_IDENTITY() as int)
	END 
ELSE 
	BEGIN
		SELECT 0
	END"""

let billLink json = 
    json?billName?link.AsString()

/// Get the bill associated with this action (if known)
let resolveBill json = trial {
    let! result = 
        {Link=(billLink json)}
        |> dbParameterizedQuery<Bill> billQuery
    let bill = result |> Seq.tryHead
    return (json, bill)
    }

/// If the bill is not known, fail with an appropriate error
let validateBill (json, bill:Bill option) =
    match bill with 
    | None -> fail (UnknownBill (billLink json))
    | Some b -> ok (json, b)

/// Generate an Action model from the API metadata
let resolveAction (json, bill:Bill) =
    let op() = 
        let desc = json?description.AsString()
        let type' = desc |> parseType
        let description = desc |> parseDescription
        let chamberName = json?chamber?name.AsString()
        let chamber = System.Enum.Parse(typeof<Chamber>, chamberName) :?> Chamber
        let action = 
            {
                Action.Id = 0;
                Date = json?date.AsDateTime();
                Link = json?link.AsString();
                ActionType = type'
                Description = description
                Chamber = chamber
                BillId = bill.Id;
            }
        (action,bill)
    tryFail op DTOtoDomainConversionFailure

/// Add the Action to the database if it's not already present
let insertActionIfNotExists (action:Action, bill) = trial {
    let! id = dbParameterizedQueryOne<int> insertAction action
    return {action with Id=id}
    }

/// If the Action is already in te DB, we're done.
let haltIfActionAlreadyExists (action:Action) =
    match action.Id with
    | 0 -> fail EntityAlreadyExists
    | _ -> ok action

let assignCommitteeQuery = (sprintf """
INSERT INTO BillCommittee(BillId, Assigned, CommitteeId)
VALUES (
    @BillId, 
    @Date,
    (SELECT Id FROM Committee 
        WHERE Chamber=@Chamber 
        AND Name=@Description
        AND SessionId = %s))
""" SessionIdSubQuery)

let updateCommitteeAssignment (action:Action) = trial {
    if (action.ActionType = ActionType.AssignedToCommittee)
    then 
        let! res = dbCommand assignCommitteeQuery action
        return action
    else 
        return action
}

let tryInvalidateCache (action:Action) =
    tryInvalidateCache ActionsKey action

let inline nextSteps link (result:Result<Action,WorkFlowFailure>) = 
    match result with
    | Ok (a, _) ->
        if a.ActionType = ActionType.Unknown
        then Next.Succeed(terminalState)
        else Next.Succeed(NextWorkflow([GenerateActionNotification(a.Id)]))
    | Bad (EntityAlreadyExists::msgs) ->       
        Next.Succeed(terminalState, msgs)
    | Bad ((UnknownBill bill)::msgs) ->
        let updateBill = NextWorkflow [UpdateBill bill]
        Next.Succeed(updateBill, msgs)
    | Bad msgs ->
        msgs |> rollbackInsert "Action" link

let workflow link = 
    fun () ->
        fetch link
        >>= resolveBill
        >>= validateBill
        >>= resolveAction
        >>= insertActionIfNotExists
        >>= haltIfActionAlreadyExists
        >>= updateCommitteeAssignment
        >>= tryInvalidateCache
        |> nextSteps link