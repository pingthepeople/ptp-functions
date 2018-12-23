module Ptp.Workflow.RoundupNotification

open Ptp.Common.Core
open Ptp.Common.Formatting
open Ptp.Common.Model
open System
open Ptp.Common.Database
open Chessie.ErrorHandling

[<CLIMutable>]
type DigestAction = {
    SessionName:string;
    BillName:string;
    Name:string;
    Title:string;
    BillChamber:Chamber;
    ActionChamber:Chamber;
    ActionType:ActionType;
    Description:string;
}

[<CLIMutable>]
type DigestScheduledAction = {
    SessionName:string;
    BillName:string;
    Title:string;
    BillChamber:Chamber;
    ActionChamber:Chamber;
    ActionType:ActionType;
    Date:DateTime;
    Start:string;
    End:string;
    CustomStart:string;
    Location:string;
    CommitteeName:string;
}

type DigestParams = {
    Today:string;
    UserId:int
}

[<Literal>]
let FetchAllActions = """
SELECT 
	s.Name as SessionName
	,b.Name as BillName
	,b.Chamber as BillChamber
	,b.Title
	,a.Chamber as ActionChamber
	,a.ActionType
	,a.Description
FROM Action a
JOIN Bill b 
    ON a.BillId = b.Id
    AND b.SessionId = (SELECT TOP 1 Id FROM [Session] WHERE Active = 1)
JOIN Session s 
    ON b.SessionId = s.Id
WHERE 
    a.Created > @Today
    AND a.ActionType NOT IN (0,4) -- ignore unknown actions and committee assignments
    AND (((SELECT DigestType from users where Id = @UserId) = 2) -- All bills
        OR
        (b.Id IN (SELECT BillId from UserBill where UserId = @UserId))) -- My bills
ORDER BY b.Name""" 

[<Literal>]
let FetchAllScheduledActions = """
SELECT
	s.Name as SessionName
	,b.Name as BillName
	,b.Chamber as BillChamber
	,b.Title
	,sa.Chamber as ActionChamber
	,sa.ActionType
	,sa.Date
	,sa.[Start]
	,sa.[End]
    ,sa.CustomStart
	,sa.Location
    ,c.Name as CommitteeName
FROM ScheduledAction sa
JOIN Bill b 
    ON sa.BillId = b.Id
    AND b.SessionId = (SELECT TOP 1 Id FROM [Session] WHERE Active = 1)
JOIN Session s
    ON s.Id = b.SessionId
LEFT JOIN Committee c 
    ON c.Link = sa.CommitteeLink
WHERE 
    sa.Date > @Today 
    AND (((SELECT DigestType from users where Id = @UserId) = 2) -- All bills
        OR
        (b.Id IN (SELECT BillId from UserBill where UserId = @UserId))) -- My bills
ORDER BY b.Name""" 

let linebreak = "<br/>"
let hr = "---"
let allBillsSalutation = "Hello! Here are the day's legislative activity and upcoming events for all bills in this session of the Indiana General Assembly."
let myBillsSalutation =  "Hello! Here are the day's legislative activity and upcoming events for the bills you are following in this session of the Indiana General Assembly."
let timeZone = "All times are Eastern Standard Time (EST)."
let locations = "All room and chamber locations are in the [Indiana State Capitol building](https://www.google.com/maps/place/Indiana+State+Capitol,+Indianapolis,+IN+46204/@39.7687106,-86.1650449,17z). Refer to [the building floor maps](https://iga.in.gov/information/location_maps) to find your room."
let settings = "You received this email because you requested a daily legislative update from [Ping the People](https://pingthepeople.org). You can [update your account settings](https://pingthepeople.org/account) to change the type of digest your recive, or to stop receiving it altogether. If you have comments or need help, please contact [help@pingthepeople.org](mailto:help@pingthepeople.org?subject=Daily%20digest)."
let closing = [linebreak; hr; linebreak; settings; timeZone; locations]

let printSectionTitle actionType = 
    match actionType with 
    | ActionType.CommitteeReading -> "Committee Hearings"
    | ActionType.SecondReading -> "Second Readings"
    | ActionType.ThirdReading -> "Third Readings"
    | ActionType.SignedByPresidentOfSenate -> "Bills Sent to Governor"
    | ActionType.SignedByGovernor -> "Bills Signed by the Governor"
    | ActionType.VetoedByGovernor -> "Bills Vetoed by the Governor"
    | ActionType.VetoOverridden -> "Vetoes Overridden"
    | _ -> ""

let mdBill name session chamber (title:string) =
    sprintf "[%s](https://iga.in.gov/legislative/%s/bills/%s/%s) ('%s')" (printBillName' name) session (chamber.ToString().ToLower()) (printBillNumber' name) (title.TrimEnd('.'))

// ACTIONS
let listAction (a:DigestAction) = 
    sprintf "* %s: %s" (mdBill a.BillName a.SessionName a.BillChamber a.Title) a.Description

let listActions (actions:DigestAction seq) =
    actions
    |> Seq.sortBy (fun a -> a.BillName)
    |> Seq.map listAction
    |> String.concat "\n"

let describeActions chamber actionType (actions:DigestAction seq) = 
    let sectionTitle = sprintf "_%s_  " (printSectionTitle actionType)
    let section = 
        actions 
        |> Seq.filter (fun a -> a.ActionChamber = chamber && a.ActionType = actionType) 
        |> listActions

    match section with
    | EmptySeq -> []
    | _ -> [sectionTitle; section]

let describeActionsForChamber chamber (actions:DigestAction seq) = 
    let header = sprintf "**Today's %A Activity**  " chamber
    match actions with
    | EmptySeq -> 
        [linebreak; header; "(No Activity)"]
    | _ ->
        [linebreak; header] 
        @ (actions |> describeActions chamber ActionType.CommitteeReading)
        @ (actions |> describeActions chamber ActionType.SecondReading)
        @ (actions |> describeActions chamber ActionType.ThirdReading)
        @ (actions |> describeActions chamber ActionType.SignedByPresidentOfSenate)
        @ (actions |> describeActions chamber ActionType.SignedByGovernor)
        @ (actions |> describeActions chamber ActionType.VetoedByGovernor)
        @ (actions |> describeActions chamber ActionType.VetoOverridden)

// SCHEDULED ACTIONS
let listScheduledAction sa =
    sprintf "* %s" (mdBill sa.BillName sa.SessionName sa.BillChamber sa.Title)
    
let listScheduledActions (scheduledActions:DigestScheduledAction seq) =
    scheduledActions 
    |> Seq.sortBy (fun action -> action.BillName)
    |> Seq.map listScheduledAction
    |> String.concat "\n"

let describeScheduledActions (grouping,actions) =
    let sa = actions |> Seq.head
    let location = formatEventLocation sa.Location
    let time = formatEventTime sa.Start sa.End sa.CustomStart
    let sectionTitle = sprintf "%s, _%s%s_" grouping location time
    let section = listScheduledActions actions
    [sectionTitle; section]

let describeScheduledActionsForDay (date:DateTime,scheduledActions) = 
    let header = sprintf "**Events for _%s_**  " (formatEventDate date)
    let list groupings = 
        groupings
        |> Seq.sortBy fst
        |> Seq.collect describeScheduledActions
        |> Seq.toList
    let committeReadings = 
        scheduledActions 
        |> Seq.filter (fun sa -> sa.ActionType = ActionType.CommitteeReading)
        |> Seq.groupBy (fun sa -> sprintf "%s Hearing" (formatCommitteeName sa.ActionChamber sa.CommitteeName)) 
        |> list
    let secondReadings = 
        scheduledActions 
        |> Seq.filter (fun sa -> sa.ActionType = ActionType.SecondReading)
        |> Seq.groupBy (fun sa -> sprintf "Second Reading, %A" sa.ActionChamber)
        |> list
    let thirdReadings = 
        scheduledActions 
        |> Seq.filter (fun sa -> sa.ActionType = ActionType.ThirdReading)
        |> Seq.groupBy (fun sa -> sprintf "Third Reading, %A" sa.ActionChamber)
        |> list
    [linebreak; header] @ committeReadings @ secondReadings @ thirdReadings

let generateNotification (user,actions,scheduledActions) =
    let op()=
        let subject = sprintf "Legislative Update for %s" (DateTime.Now.ToString("D")) 
        let salutation =
            match user.DigestType with
            | DigestType.AllBills -> allBillsSalutation
            | _ ->                   myBillsSalutation
        let houseActions = actions |> describeActionsForChamber Chamber.House
        let senateActions = actions |> describeActionsForChamber Chamber.Senate
        let upcomingActions = 
            scheduledActions 
            |> Seq.groupBy (fun scheduledAction -> scheduledAction.Date)
            |> Seq.sortBy (fun (date,scheduledActions) -> date)
            |> Seq.collect describeScheduledActionsForDay
            |> Seq.toList
        let body = [salutation] @ houseActions @ senateActions @ upcomingActions @ closing |> String.concat "\n\n"
        [{Message.Recipient=user.Email; Subject = subject; Body=body; MessageType=MessageType.Email; Attachment=""}]
    tryFail op NotificationGenerationError

let fetchUserQuery = "SELECT * FROM users WHERE Id = @Id"
let fetchUser id =
    dbParameterizedQueryOne<User> fetchUserQuery {Id=id}

let ensureUserWantsDigest (user:User) =
    if user.DigestType = DigestType.None
    then
        sprintf "User %d does not request a digest." user.Id
        |> BadRequestError
        |> fail        
    else ok user

let ensureUserHasEmailAddress (user:User) =
    if (System.String.IsNullOrWhiteSpace(user.Email))
    then
        sprintf "User %d does not have a valid email address." user.Id
        |> BadRequestError
        |> fail        
    else ok user

let validateRequest (user:User) =
    ensureUserWantsDigest user
    >>= ensureUserHasEmailAddress

let fetchActions (user:User) = trial{
    let param = {UserId=user.Id; Today=(datestamp())}
    let! actions = dbParameterizedQuery<DigestAction> FetchAllActions param
    let! scheduledActions = dbParameterizedQuery<DigestScheduledAction> FetchAllScheduledActions param
    return (user, actions, scheduledActions)
}

let nextSteps result =
    match result with
    | Ok (_, msgs) ->   
        Next.Succeed(terminalState, msgs)
    | Bad ((BadRequestError err)::msgs) ->
        Next.Succeed(terminalState, (BadRequestError err)::msgs)
    | Bad msgs ->       
        Next.FailWith(msgs)

let workflow enqueueNotification id =
    fun () ->
        fetchUser id
        >>= validateRequest
        >>= fetchActions
        >>= generateNotification
        >>= enqueueNotification
        |> nextSteps