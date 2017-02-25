// Configure Database 

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/http.fsx"
#load "../shared/db.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open FSharp.Data
open FSharp.Data.JsonExtensions
open IgaTracker.Model
open IgaTracker.Queries
open IgaTracker.Http
open IgaTracker.Db

type Link = {Link:string}
let addToDatabase date (cn:SqlConnection) allActions =

    // Determine the type of action this is. We only care about particular types.
    let determineActionType a = 
        match a?description.AsString() with
        | StartsWith "First reading: referred to Committee on " rest -> ActionType.AssignedToCommittee
        | StartsWith "Committee report" rest -> ActionType.CommitteeReading
        | StartsWith "Second reading" rest -> ActionType.SecondReading
        | StartsWith "Third reading" rest -> ActionType.ThirdReading
        | _ -> ActionType.Unknown
    // Pare down the description of the action.
    let determineDescription a =
        match a?description.AsString() with
        | StartsWith "First reading: referred to Committee on " rest -> rest
        | StartsWith "Committee report: " rest -> rest
        | StartsWith "Second reading: " rest -> rest
        | StartsWith "Third reading: " rest -> rest
        | other -> other
    // Determine which legislative chamber this action occurred in.
    let determineChamber a =
        match a?chamber?name.AsString() with
        | "House" -> Chamber.House
        | _ -> Chamber.Senate
    // Find the Id of the bill this action references     
    let determineBillId (bills:Bill seq) a =
        bills 
        |> Seq.find (fun b -> b.Name=a?billName?billName.AsString()) 
        |> (fun b -> b.Id)
    // Generate 'Action' domain model using data from API
    let toModel (bills:Bill seq) (action,actionType) = {
        Action.Id = 0;
        Date = action?date.AsDateTime();
        Link = action?link.AsString();
        ActionType = actionType
        Description = action |> determineDescription;
        Chamber = action |> determineChamber;
        BillId = action |> determineBillId bills;
    } 
    // Add this action to the database and capture the new record's ID
    let insertAction (action:Action) =
        cn 
            |> dapperParametrizedQuery<int> InsertAction action 
            |> Seq.head // inserted Action Id

    // Add records to the database for the action events that we care about.
    let addActionsToDb actions =
        // Fetch all existing bills (as a lookup table)
        let bills = cn |> dapperQuery<Bill> ("SELECT Id, Name FROM Bill")
        let links = cn |> dapperParametrizedQuery<string> ("SELECT Link FROM Action WHERE Date > @Date") {DateSelectArgs.Date=date}
        
        actions
        |> List.filter (fun a -> 
            // Find actions matching known bills
            (bills |> Seq.exists (fun b -> b.Name=a?billName?billName.AsString())) &&
            // Filter out the actions that are already in the database
            (links |> Seq.exists (fun l -> l=a?link.AsString()) |> not))
        // Determine the type of action this is
        |> List.map (fun a -> (a, (determineActionType a)))
        // Filter out all the unknown action types
        |> List.filter (fun t -> (snd t) <> ActionType.Unknown)
        // Map actions to a domain model and insert them into the database.
        |> List.map (fun t -> toModel bills t |> insertAction)
        // Determine the actions that require user notification
        |> (fun ids -> cn |> dapperMapParametrizedQuery<int> SelectActionsRequiringNotification (Map ["Ids", ids :> obj]))

    allActions |> addActionsToDb


#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
open Microsoft.Azure.WebJobs.Host

let Run(myTimer: TimerInfo, actions: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (DateTime.Now.ToString()))
    try
        let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
        let sessionYear = System.Environment.GetEnvironmentVariable("SessionYear")
        let date = DateTime.Now.ToString("yyyy-MM-dd")
        
        log.Info(sprintf "[%s] Fetching actions from API ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        let allActions = fetchAll (sprintf "/%s/bill-actions?minDate=%s" sessionYear date) 
        log.Info(sprintf "[%s] Fetching actions from API [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")) )

        log.Info(sprintf "[%s] Adding actions to database ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        let actionIdsRequiringAlert = allActions |> addToDatabase date cn
        log.Info(sprintf "[%s] Adding actions to database [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))

        log.Info(sprintf "[%s] Enqueue alerts for new actions ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        actionIdsRequiringAlert |> Seq.iter (fun id -> 
            log.Info(sprintf "[%s]  Enqueuing action %d" (DateTime.Now.ToString("HH:mm:ss.fff")) id)
            actions.Add(id.ToString()))
        log.Info(sprintf "[%s] Enqueue alerts for new actions [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))

        log.Info(sprintf "[%s] Updating bill/committee assignments ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
        cn.Execute(UpdateBillCommittees) |> ignore
        log.Info(sprintf "[%s] Updating bill/committee assignments [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))
    with
    | ex -> log.Error(sprintf "Encountered error: %s" (ex.ToString())) 