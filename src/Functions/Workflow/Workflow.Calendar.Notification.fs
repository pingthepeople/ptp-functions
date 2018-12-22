module Ptp.Workflow.CalendarNotification

open Chessie.ErrorHandling
open Ptp.Common.Model
open Ptp.Common.Database
open Ptp.Common.Core
open Ptp.Workflow.Messaging
open Ptp.Common.Formatting

let formatBody (a:ScheduledAction,c:Committee option) bill title =
    let eventType = 
        match a.ActionType with
        | ActionType.CommitteeReading -> "hearing"
        | ActionType.SecondReading -> "second reading"
        | ActionType.ThirdReading -> "third reading"
        | _ -> "(some other event type?)"
    let readingBody =
        match c with
        | Some(comm) -> sprintf " by the %s" (formatCommitteeName comm.Chamber comm.Name)
        | None -> ""
    let eventDate = formatEventDate a.Date
    let eventTime = formatEventTime a.Start a.End a.CustomStart
    let eventLocation = formatEventLocation a.Location
    sprintf "%s is scheduled for a %s%s on %s%s in %s." title eventType readingBody eventDate eventTime eventLocation

let fetchActionQuery = "SELECT * FROM ScheduledAction WHERE Id = @Id"
let fetchCommitteeQuery = "SELECT * FROM Committee WHERE Link = @Link"

let fetchAction id = trial { 
    let! action = dbParameterizedQueryOne<ScheduledAction> fetchActionQuery {Id=id}
    let! committee = 
        match action.CommitteeLink with
        | null -> Seq.empty<Committee> |> ok
        | link -> dbParameterizedQuery<Committee> fetchCommitteeQuery {Link=link}
    let committeeOpt = committee |> Seq.tryHead
    return (action, committeeOpt)
}
   
let generateActionNotifications (action,committee) =
    let formatBody = formatBody (action,committee)
    generateNotifications formatBody action.BillId
    
let workflow enqueueNotifications id =
    fun () ->
        fetchAction id
        >>= generateActionNotifications
        >>= enqueueNotifications
        |> workflowTerminates

