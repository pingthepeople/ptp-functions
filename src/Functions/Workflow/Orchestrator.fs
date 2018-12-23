module Ptp.Workflow.Orchestrator

open Microsoft.Extensions.Logging
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Ptp.Common.Core
open Ptp.Workflow
open System

(* A diagram showing how how workflows are triggered.
   Once daily all legislators, subjects, committees, and bills are updated.
   Every 10 minutes, actions and committee/chamber calendars are updated.
   In the event that an action, or committee/chamber calendar refers to an 
     unkonwn bill or committee, an out-of-band update request is made for that
     bill or committee. New actions/events will be picked up ~10 minutes later.

                                                                                                |--------------------| 
                                                                                              |--------------------| |
                            |--------------------|            |-------------------|         |--------------------| |-|
Once Daily: [TIMER] ------> | Update Legislators | ----+----> | Update Committees | ------> | Update Committee X |-|   ---> [STOP]
                            |--------------------|     |      |-------------------|         |--------------------|  
                                                       |                ^                     
                                                       |                |                        
                                                       |                +------------------------------------------------------------------------------+
                                                       |                                                                                               |
                                                       |                                                             |---------------|                 |
                                                       |                                                           |---------------| |                 |
                                                       |      |-----------------|        |--------------|        |---------------| |-|                 |
                                                       +--->  | Update Subjects | -----> | Update Bills | -----> | Update Bill X |-|   ---> [STOP]     |
                                                              |-----------------|        |--------------|        |---------------|                     |
                                                                                                                           ^                           |
                                                                                                                           |                           |
                                                                                                                           +--------------------+      |
                                                                                                                                                |      |
                                                                                                                         //===============//    |      |
                                                  +-----------------------------+-------------------------------+-----> // Unknown Bill? // ----+      |
                                                  |                             |                               |      //===============//             |
                                                  |                             |                               |                                      |
                                                  |                             |                               |        //====================//      |
                                                  |                             |                               +-----> // Unknown Committee? // ------+ 
                                                  |                             |                               |      //====================//  
                                                  |                             |                               |  
                            |----------------|    |    |--------------------|   |    |-----------------------|  |
Every 10 Mins: [TIMER] ---> | Update Actions | ---+--> | Update Chamber Cal | --+--> | Update Committeee Cal |--+-----> [STOP]
                            |----------------|         |--------------------|        |-----------------------|


*)

let Execute (log: ILogger) (command: string) (nextCommand: ICollector<string>) (notifications:ICollector<string>) =
    
    let notifications = tryEnqueue notifications
    let nextCommand = enqueue nextCommand

    let chooseWorkflow msg =
        match msg with
        // Data updates
        | UpdateLegislators     -> Legislators.workflow
        | UpdateLegislator link -> Legislator.workflow link
        | UpdateCommittees      -> Committees.workflow
        | UpdateCommittee link  -> Committee.workflow link
        | UpdateSubjects        -> Subjects.workflow
        | UpdateBills           -> Bills.workflow
        | UpdateBill link       -> Bill.workflow link
        | UpdateActions         -> Actions.workflow
        | UpdateAction link     -> Action.workflow link
        | UpdateCalendars       -> Calendars.workflow
        | UpdateCalendar link   -> Calendar.workflow link
        | UpdateDeadBills       -> DeadBill.workflow
        // Notification generators
        | DailyRoundup          -> Roundup.workflow
        | GenerateActionNotification id     -> ActionNotification.workflow notifications id
        | GenerateCalendarNotification id   -> CalendarNotification.workflow notifications id
        | GenerateRoundupNotification id    -> RoundupNotification.workflow notifications id
        | GenerateDeadBillNotification id   -> DeadBillNotification.workflow notifications id   

    match deserializeQueueItem<Workflow> log command with
    | Some cmd ->
        cmd
        |> chooseWorkflow
        |> executeWorkflow log cmd
        |> enqueueNext log cmd nextCommand
        |> throwOnFail cmd
        |> ignore
    | None -> ()
