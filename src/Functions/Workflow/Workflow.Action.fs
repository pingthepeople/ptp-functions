module Ptp.Workflow.Action

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open Ptp.Formatting
open Ptp.Cache
open Newtonsoft.Json

/// Generate an informative sms/email message body for this action.
let formatBody (action:Action) title =
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
let billQuery = "SELECT TOP 1 * FROM Bill WHERE Link = @Link"

let insertAction = """
IF NOT EXISTS (SELECT Id from Action WHERE Link = @Link)
	BEGIN
		INSERT INTO Action(Description,Link,Date,ActionType,Chamber,BillId) 
		VALUES (@Description,@Link,@Date,@ActionType,@Chamber,@BillId)
		SELECT CAST(SCOPE_IDENTITY() as int)
	END 
ELSE 
	BEGIN
		SELECT 0
	END"""

let selectRecipients = """
SELECT 
    u.Email
    , u.Mobile
    , ub.ReceiveAlertEmail
    , ub.ReceiveAlertSms
FROM UserBill ub 
JOIN Users u 
    ON ub.UserId = u.Id
WHERE ub.BillId = @Id"""

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
    let action' = {action with Id=id}
    return (action', bill)
    }

/// If the Action is already in te DB, we're done.
let haltIfActionAlreadyExists (action:Action,bill) =
    match action.Id with
    | 0 -> fail EntityAlreadyExists
    | _ -> ok (action,bill)

[<CLIMutable>]
type Recipient = {Email: string; Mobile:string; ReceiveAlertEmail: bool; ReceiveAlertSms: bool}

/// If this is a new action of a known type, generate a list of recipients for notifications.
let resolveRecipients (action:Action,bill:Bill) = trial {
    match int action.ActionType with
    | 0 -> 
        return (action, bill, Seq.empty)
    | _ ->
        let! recipients = dbParameterizedQuery<Recipient> selectRecipients {Id=bill.Id}
        return (action, bill, recipients)
    }

/// Generate email alerts for this action
let emailNotification action (bill:Bill) = 
    let subject = 
        sprintf "Update on %s" (printBillNameAndTitle bill)
    let body = 
        markdownBillHrefAndTitle bill
        |> formatBody action
    {MessageType=MessageType.Email; Subject=subject; Body=body; Recipient=""; Attachment=""}

/// Generate sms alerts for this action
let smsNotification action (bill:Bill) =
    let body = 
        printBillNameAndTitle bill
        |> formatBody action
    {MessageType=MessageType.SMS; Subject=""; Body=body; Recipient=""; Attachment=""}
    
let generateNotifications (action, bill, recipients) =
    let op() =
        let emails = 
            let message = emailNotification action bill
            recipients
            |> Seq.filter (fun r -> r.ReceiveAlertEmail)
            |> Seq.map (fun r -> {message with Recipient=r.Email})
        let texts = 
            let message = smsNotification action bill
            recipients
            |> Seq.filter (fun r -> r.ReceiveAlertSms)
            |> Seq.map (fun r -> {message with Recipient=r.Mobile})
        emails 
        |> Seq.append texts
        |> Seq.map JsonConvert.SerializeObject
    tryFail op NotificationGenerationError

let invalidateActionsCache = 
    tryInvalidateCache ActionsKey

let inline nextSteps result = 
    match result with
    | Ok (_, msgs) ->   
        Next.Succeed(terminalState, msgs)
    | Bad (EntityAlreadyExists::msgs) ->       
        Next.Succeed(terminalState, msgs)
    | Bad ((UnknownBill bill)::msgs) ->
        let updateBill = NextWorkflow [UpdateBill bill]
        Next.Succeed(updateBill, msgs)
    | Bad msgs ->
        Next.FailWith(msgs)

let workflow link enqueueNotifications = 
    fun () ->
        fetch link
        >>= resolveBill
        >>= validateBill
        >>= resolveAction
        >>= insertActionIfNotExists
        >>= haltIfActionAlreadyExists
        >>= invalidateActionsCache
        >>= resolveRecipients
        >>= generateNotifications
        >>= enqueueNotifications
        |> nextSteps
