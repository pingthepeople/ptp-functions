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
let updateActions (cn:SqlConnection) session minDate =
    // Determine the type of action this is. We only care about particular types.
    let determineActionType a = 
        match a?description.AsString() with
        | StartsWith "First reading: referred to" rest -> ActionType.AssignedToCommittee
        | StartsWith "Committee report" rest -> ActionType.CommitteeReading
        | StartsWith "Second reading" rest -> ActionType.SecondReading
        | StartsWith "Third reading" rest -> ActionType.ThirdReading
        | _ -> ActionType.Unknown
    // Pare down the description of the action.
    let determineDescription a =
        match a?description.AsString() with
        | StartsWith "First reading: referred to" rest -> rest
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

    let toModel (bills:Bill seq) (action,actionType) = {
        Action.Id = 0;
        Date = action?date.AsDateTime();
        Link = action?link.AsString();
        ActionType = actionType
        Description = action |> determineDescription;
        Chamber = action |> determineChamber;
        BillId = action |> determineBillId bills;
    } 

    let actionNotInDatabase (action:Action) =
        let existingId = cn.Query<int>("SELECT  ISNULL((Select Id From Action where Link = @Link),0)", {Link=action.Link}) |> Seq.head
        existingId = 0

    // If we don't have a record of this action in the database, add one and enqueue an alert message 
    let insertAction (action:Action) =
        let parameters = 
            (Map[
                "Description", action.Description :> obj; 
                "Link", action.Link :> obj;
                "Date", action.Date :> obj;
                "ActionType", action.ActionType :> obj;
                "Chamber", action.Chamber :> obj;
                "BillId", action.BillId :> obj])
        cn 
            |> dapperMapParametrizedQuery<int> InsertAction parameters 
            |> Seq.head // inserted Action Id

    // Fetch all existing bills (as a lookup table)
    let bills = cn.Query<Bill>("SELECT Id, Name FROM Bill")

    // Add records to the database for the action events that we care about.
    let addActionsToDb actions =
        actions
        |> List.map (fun a -> (a, (determineActionType a)))
        |> List.filter (fun t -> (snd t) <> ActionType.Unknown)
        |> List.map (fun t -> toModel bills t)
        |> List.filter actionNotInDatabase
        |> List.map insertAction
        |> (fun ids -> dapperMapParametrizedQuery<int> SelectActionsRequiringNotification (Map["Ids", ids :> obj]) cn)

    fetchAll (sprintf "/%s/bill-actions?minDate=%s" session minDate) 
    |> addActionsToDb

// Add any missing bill/committee relationships
let updateBillCommitteeAssignments (cn:SqlConnection) = 
    cn.Open()
    cn.Execute(UpdateBillCommittees) |> ignore
    cn.Close()

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
open Microsoft.Azure.WebJobs.Host

let Run(myTimer: TimerInfo, actions: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function 'updateActions' executed at: %s" (DateTime.Now.ToString()))
    let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
    // Enueue new action Ids for alert processing
    updateActions cn (System.Environment.GetEnvironmentVariable("SessionYear")) (DateTime.Now.ToString("yyyy-MM-dd"))
    |> Seq.iter (fun actionId -> 
        log.Info(sprintf "Enqueuing action %d" actionId)
        actions.Add(actionId.ToString()))
    // Enueue new action Ids for alert processing
    updateBillCommitteeAssignments cn