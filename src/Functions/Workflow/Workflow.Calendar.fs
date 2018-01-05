module Ptp.Workflow.Calendar

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open Ptp.Cache

type ScheduledActionDTO = 
  {
      ActionType: ActionType;
      BillLink: string;
      CommitteeLink: string;
      CalendarLink: string;
      Date: System.DateTime;
      Chamber: Chamber;
      Location: string;
      Start: string;
      End: string;
      CustomStart: string;
  }

let query = """
SELECT TOP 1 Id 
FROM ScheduledAction 
WHERE Link = @Link"""

let fetchExistingEntity link =
    dbParameterizedQuery<int> query {Link=link}    

let ensureEntityNotPresent link seq =
    if Seq.isEmpty seq 
    then ok link
    else fail EntityAlreadyExists

let ensureNotAlreadyKnown link = 
    fetchExistingEntity link
    >>= ensureEntityNotPresent link

let billLinkSansVersion link = 
    link 
    |> split "/versions" 
    |> List.head

let chamberHeadings = 
    dict [ 
        "hb2head", ActionType.SecondReading; 
        "hb3head", ActionType.ThirdReading;
        "sb2head", ActionType.SecondReading; 
        "sb3head", ActionType.ThirdReading; ]

let chamberEvent link date actionType chamber bill =
  { 
    ScheduledActionDTO.ActionType = actionType;
    BillLink=(bill?link.AsString() |> billLinkSansVersion)
    CommitteeLink=null;
    CalendarLink=link;
    Date=date;
    Location=sprintf "%A Chamber" chamber;
    Chamber=chamber;
    Start=null;
    End=null;
    CustomStart=null;
  }

let chamberEvents link (json:JsonValue) h =
    let date = json?date.AsDateTime()
    let actionType = chamberHeadings.Item(h)
    let chamber = 
        if h.StartsWith("h") 
        then Chamber.House 
        else Chamber.Senate
    let toCalendarEvent = 
        chamberEvent link date actionType chamber
    
    match json.TryGetProperty(h) with
    | Some x -> 
       match x.TryGetProperty("bills") with
       | Some bills -> 
           bills.AsArray() 
           |> Array.map toCalendarEvent
           |> Array.toSeq
       | None -> Seq.empty<ScheduledActionDTO>
    | None -> Seq.empty<ScheduledActionDTO>
       
let resolveChamberEvents link json =
    let op() =
        chamberHeadings.Keys
        |> Seq.collect (chamberEvents link json)
    tryFail op DTOtoDomainConversionFailure

let committeeEvent calendarlink committeelink date chamber location startTime endTime customStart billLink =
    { 
      ActionType = ActionType.CommitteeReading;
      BillLink=billLink;
      CalendarLink=calendarlink;
      CommitteeLink=committeelink;
      Date=date;
      Location=location;
      Chamber=chamber;
      Start=startTime;
      CustomStart=customStart;
      End=endTime;
    }

let resoveCommitteeLink (json:JsonValue) =
    let op()=
        json?committee?link
            .AsString()
            .Replace("standing-","")
            .Replace("interim-","")
            .Replace("conference-","")        
    tryFail op DTOtoDomainConversionFailure

let fetchCommittee link =
    let query = "SELECT * FROM Committee WHERE Link = @Link"
    dbParameterizedQueryOne<Committee> query {Link=link}


let generateCommitteeEvents link (json:JsonValue) (committee:Committee) =
    let op() =
        let prettyPrintTime time =
            if System.String.IsNullOrWhiteSpace(time)
            then null
            else System.DateTime.Parse(time).ToString("h:mm tt")
 
        let date = json?meetingDate.AsDateTime()
        let location = json?location.AsString()
        let startTime = json?starttime.AsString() |> prettyPrintTime
        let endTime = json?endtime.AsString() |> prettyPrintTime
        let customStart = json?customstart.AsString()
        let chamber = committee.Chamber
        let committeelink = committee.Link
        let toMeeting = committeeEvent link committeelink date chamber location startTime endTime customStart
        let billLink json = json?bill.AsArray().[0]?link.AsString() |> billLinkSansVersion

        json?agenda.AsArray()
        |> Array.toSeq
        |> Seq.map billLink
        |> Seq.map toMeeting
    tryFail op DTOtoDomainConversionFailure

let resolveCommitteeEvents link (json:JsonValue) =
    resoveCommitteeLink json
    >>= fetchCommittee
    >>= generateCommitteeEvents link json

let resolveEvents link = trial {
    let! json = fetch link
    let! result =
        if (link.Contains("/calendars")) 
        then resolveChamberEvents link json
        else resolveCommitteeEvents link json
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

let insertEventQuery = (sprintf """
IF NOT EXISTS
    ( SELECT Id from ScheduledAction 
      WHERE Link=@CalendarLink
        AND Date=@Date
        AND ActionType=@ActionType
        AND Location=@Location
        AND Chamber=@Chamber
        AND Start=@Start
        AND [End]=@End
        AND CustomStart=@CustomStart
        AND BillId=(SELECT Id FROM Bill WHERE Link = @BillLink))
    INSERT INTO ScheduledAction
        (Link,Date,ActionType,Location,Chamber,Start,[End],CustomStart,CommitteeLink,BillId) 
        VALUES
            (@CalendarLink
            ,@Date
            ,@ActionType
            ,@Location
            ,@Chamber
            ,@Start
            ,@End
            ,@CustomStart
            ,@CommitteeLink
            ,(SELECT Id FROM Bill WHERE Link = @BillLink));""")

let getInsertedEventsQuery = """
SELECT * FROM ScheduledAction
WHERE Link = @Link"""

let insertEvents (events:ScheduledActionDTO seq) = trial {
    match (Seq.tryHead events) with
    | Some event ->
        let link = event.CalendarLink
        let! ignored = dbCommand insertEventQuery events
        let! inserted = dbParameterizedQuery<ScheduledAction> getInsertedEventsQuery {Link=link}
        return inserted
    | None -> 
        return Seq.empty<ScheduledAction>
}

let nextSteps link (result:Result<seq<ScheduledAction>, WorkFlowFailure>) = 
    match result with
    | Ok (sas, msgs) ->   
        let sendNotfications = 
            sas 
            |> Seq.map (fun sa -> sa.Id) 
            |> Seq.map GenerateCalendarNotification 
            |> NextWorkflow
        Next.Succeed(sendNotfications, msgs)
    | Bad ((UnknownBills bills)::msgs) ->
        let updateBills = bills |> mapNext UpdateBill
        Next.Succeed(updateBills, msgs)
    | Bad (EntityAlreadyExists::msgs) ->       
        Next.Succeed(terminalState, msgs)
    | Bad msgs ->
        msgs |> rollbackInsert "ScheduledAction" link   

let workflow link = 
    fun () ->
        ensureNotAlreadyKnown link
        >>= resolveEvents
        >>= ensureEventBillsKnown
        >>= insertEvents
        >>= (tryInvalidateCacheIfAny ScheduledActionsKey)
        |> nextSteps link