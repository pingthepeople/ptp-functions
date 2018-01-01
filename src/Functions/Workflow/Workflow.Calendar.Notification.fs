module Ptp.Workflow.CalendarNotification

open Chessie.ErrorHandling
open Ptp.Model
open Ptp.Database
open Ptp.Core
open Ptp.Messaging

let formatBody action includeLink title =
    let formatTimeOfDay time = System.DateTime.Parse(time).ToString("h:mm tt")
    let eventRoom = 
        match action.Location with 
        | "House Chamber" -> "the House Chamber"
        | "Senate Chamber" -> "the Senate Chamber"
        | room -> sprintf "State House %s" room
    let eventLocation = 
        match includeLink with
        | true -> sprintf "%s ([map](https://iga.in.gov/information/location_maps))" eventRoom
        | false -> eventRoom
    let eventDate = action.Date.ToString("dddd M/d/yyyy")
    match action.ActionType with
    | ActionType.CommitteeReading when action.Start |> System.String.IsNullOrWhiteSpace -> sprintf "%s is scheduled for a committee hearing on %s in %s" title eventDate eventLocation
    | ActionType.CommitteeReading -> sprintf "%s is scheduled for a committee hearing on %s from %s - %s in %s" title eventDate (formatTimeOfDay action.Start) (formatTimeOfDay action.End) eventLocation
    | ActionType.SecondReading -> sprintf "%s is scheduled for a second reading on %s in %s" title eventDate eventLocation 
    | ActionType.ThirdReading -> sprintf "%s is scheduled for a third reading on %s in %s" title eventDate eventLocation
    | _ -> "(some other event type?)"

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

