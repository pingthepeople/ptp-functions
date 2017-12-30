module Ptp.Workflow.Calendar

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open Ptp.Formatting
open Ptp.Cache
open Ptp.Workflow.Common
open Newtonsoft.Json

type ScheduledActionDTO = 
  {
      ActionType: ActionType;
      BillLink: string;
      CalendarLink: string;
      Date: System.DateTime;
      Chamber: Chamber;
      Location: string;
      Start: string;
      End: string;
  }

let query = "SELECT TOP 1 sa.Id FROM ScheduledAction WHERE Link = @Link"

let fetchExistingEntity link =
    dbParameterizedQuery<int> query {Link=link}    

let ensureEntityNotPresent link seq =
    match seq with
    | EmptySeq -> ok link
    | _ -> fail EntityAlreadyExists

let ensureNotAlreadyKnown link = 
    fetchExistingEntity link
    >>= ensureEntityNotPresent link

let calendarHeadings = 
    dict [ 
        "hb2head", ActionType.SecondReading; 
        "hb3head", ActionType.ThirdReading;
        "sb2head", ActionType.SecondReading; 
        "sb3head", ActionType.ThirdReading; ]

let calendarEvent link date actionType chamber bill =
  { 
    ScheduledActionDTO.ActionType = actionType;
    BillLink=bill?link.AsString();
    CalendarLink=link;
    Date=date;
    Location=sprintf "%A Chamber" chamber;
    Chamber=chamber;
    Start="";
    End="";
  }

let calendarEvents link (json:JsonValue) h =
    let date = json?date.AsDateTime()
    let actionType = calendarHeadings.Item(h)
    let chamber = 
        if h.StartsWith("h") 
        then Chamber.House 
        else Chamber.Senate
    let toCalendarEvent = 
        calendarEvent link date actionType chamber
    
    match json.TryGetProperty(h) with
    | Some x -> 
       match x.TryGetProperty("bills") with
       | Some bills -> 
           bills.AsArray() 
           |> Array.map toCalendarEvent
           |> Array.toSeq
       | None -> Seq.empty<ScheduledActionDTO>
    | None -> Seq.empty<ScheduledActionDTO>
       
let resolveCalendarEvents link json =
    calendarHeadings.Keys
    |> Seq.collect (calendarEvents link json)
    |> ok

let meetingEvent link date location startTime endTime billLink =
    { 
      ActionType = ActionType.CommitteeReading;
      BillLink=billLink;
      CalendarLink=link;
      Date=date;
      Location=location;
      Chamber=Chamber.None;
      Start=startTime;
      End=endTime;
    }

let resolveMeetingEvents link (json:JsonValue) =
    let actionType = ActionType.CommitteeReading
    let date = json?meetingDate.AsDateTime()
    let location = json?location.AsString()
    let startTime = json?startTime.AsString()
    let endTime = json?endTime.AsString()
    let toMeeting = meetingEvent link date location startTime endTime

    json?agenda.AsArray()
    |> Array.toSeq
    |> Seq.map (fun j -> j?bill.AsArray().[0])
    |> Seq.map (fun j -> j?link.AsString())
    |> Seq.map toMeeting
    |> ok

let resolveEvents link = trial {
    let! json = fetch link
    let! result =
        if (link.Contains("/calendars")) 
        then resolveCalendarEvents link json
        else resolveMeetingEvents link json
    return result
    }

let fetchUnknownBills calEvents = trial {
    let links = 
        calEvents 
        |> Seq.map (fun sa -> sa.BillLink) 
        |> Seq.distinct
    let! res = queryAndFilterKnownLinks "Bill" links
    return res
}

let resolveUnknownBills calEvents links =
    match links with
    | EmptySeq -> calEvents |> ok
    | _ -> links |> UnknownBills |> fail

// ensure that all bills referenced by these scheduled 
let ensureEventBillsKnown calEvents =
    fetchUnknownBills calEvents
    >>= (resolveUnknownBills calEvents)

let insertCalendarEvent = """
   INSERT INTO ScheduledAction
        (Description,Link,Date,ActionType,Chamber,Start,End,BillId) 
    VALUES
        (@Description
        ,@CalendarLink
        ,@Date
        ,@ActionType
        ,@Chamber
        ,@Start
        ,@End
        ,(SELECT Id FROM Bill WHERE Link = @BillLink))
"""

let queryInserted = """
    SELECT Id from ScheduledAction sa
    JOIN Bill b on sa.BillId = b.Id
    JOIN UserBill ub on b.Id = ub.BillId
         AND (ub.ReceiveAlertEmail = 1 
              OR ub.ReceiveAlertSms = 1)
    WHERE Link = @CalendarLink
    """

let insertEvents (events:ScheduledActionDTO seq) = trial {
    let link = events |> Seq.head |> (fun e -> e.CalendarLink)
    let! dtos = dbCommand insertCalendarEvent events
    let! inserted = dbParameterizedQuery<ScheduledAction> queryInserted link
    return inserted
}

let invalidateCalendarCache = 
    tryInvalidateCacheIfAny ScheduledActionsKey

let nextSteps (result:Result<seq<ScheduledAction>, WorkFlowFailure>) = 
    match result with
    | Ok (sas, msgs) ->   
        let sendNotfications = 
            sas 
            |> Seq.map (fun sa -> sa.Id) 
            |> Seq.map SendCalendarNotification 
            |> NextWorkflow
        Next.Succeed(sendNotfications, msgs)
    | Bad ((UnknownBills bills)::msgs) ->
        let updateBills = bills |> mapNext UpdateBill
        Next.Succeed(updateBills, msgs)
    | Bad (EntityAlreadyExists::msgs) ->       
        Next.Succeed(terminalState, msgs)
    | Bad msgs ->
        Next.FailWith(msgs)

let formatBody event =
    let formatTimeOfDay time = System.DateTime.Parse(time).ToString("h:mm tt")
    let eventRoom = 
        match event.Location with 
        | "House Chamber" -> "the House Chamber"
        | "Senate Chamber" -> "the Senate Chamber"
        | other -> other
    let eventDate = event.Date.ToString("M/d/yyyy")
    match event.ActionType with
    | ActionType.CommitteeReading when isEmpty event.Start -> sprintf "is scheduled for a committee hearing on %s in %s" eventDate eventRoom
    | ActionType.CommitteeReading -> sprintf "is scheduled for a committee hearing on %s from %s - %s in %s" eventDate (formatTimeOfDay event.Start) (formatTimeOfDay event.End) eventRoom
    | ActionType.SecondReading -> sprintf "is scheduled for a second reading on %s in %s" eventDate eventRoom 
    | ActionType.ThirdReading -> sprintf "is scheduled for a third reading on %s in %s" eventDate eventRoom
    | _ -> "(some other event type?)"

let workflow link = 
    fun () ->
        ensureNotAlreadyKnown link
        >>= resolveEvents
        >>= ensureEventBillsKnown
        >>= insertEvents
        >>= invalidateCalendarCache
        |> nextSteps