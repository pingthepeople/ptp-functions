module Ptp.Workflow.CalendarNotification

open Chessie.ErrorHandling
open Ptp.Model
open Ptp.Database
open Ptp.Core
open Ptp.Messaging
open Ptp.Formatting

let formatBody (a:ScheduledAction) bill title =
    let eventType = 
        match a.ActionType with
        | ActionType.CommitteeReading -> "committee reading"
        | ActionType.SecondReading -> "second reading"
        | ActionType.ThirdReading -> "third reading"
        | _ -> "(some other event type?)"
    let eventDate = formatEventDate a.Date
    let eventTime = formatEventTime a.Start a.End a.CustomStart
    let eventLocation = formatEventLocation a.Location
    sprintf "%s is scheduled for a %s on %s%s in %s" title eventType eventDate eventTime eventLocation

let fetchActionQuery = "SELECT * FROM ScheduledAction WHERE Id = @Id"

let fetchAction id = 
    dbParameterizedQueryOne<ScheduledAction> fetchActionQuery {Id=id}
   
let generateActionNotifications action =
    let formatBody = formatBody action
    generateNotifications formatBody action.BillId
    
let workflow enqueueNotifications id =
    fun () ->
        fetchAction id
        >>= generateActionNotifications
        >>= enqueueNotifications
        |> workflowTerminates

