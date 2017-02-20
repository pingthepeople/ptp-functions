// Configure Database 

#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/DapperExtensions/lib/net45/DapperExtensions.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/http.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open DapperExtensions
open FSharp.Data
open FSharp.Data.JsonExtensions
open IgaTracker.Model
open IgaTracker.Queries
open IgaTracker.Http

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
    let determineBillId bills a =
        bills 
        |> Seq.find (fun b -> b.Name=a?billName?billName.AsString()) 
        |> (fun b -> b.Id)

    let toModel bills (action,actionType) = {
        Action.Id = 0;
        Date = action?date.AsDateTime();
        Link = action?link.AsString();
        ActionType = actionType
        Description = action |> determineDescription;
        Chamber = action |> determineChamber;
        BillId = action |> determineBillId bills;
    } 

    let recordNotInDatabase (action:Action) =
        let existingId = cn.Query<int>("SELECT  ISNULL((Select Id From Action where Link = @Link),0)", {Link=action.Link}) |> Seq.head
        existingId = 0

    // If we don't have a record of this action in the database, add one and enqueue an alert message 
    let addIfNotExists (action:Action) =
        cn.Open()
        cn.Insert<Action>(action) |> ignore
        cn.Close() 
        action.Id;

    // Fetch all existing bills (as a lookup table)
    cn.Open();
    let bills = cn.GetList<Bill>()
    cn.Close(); 

    // Add records to the database for the action events that we care about.
    let addActionsToDb actions =
        actions
        |> List.map (fun a -> (a, (determineActionType a)))
        |> List.filter (fun t -> (snd t) <> ActionType.Unknown)
        |> List.map (toModel bills)
        |> List.filter recordNotInDatabase
        |> List.map addIfNotExists

    fetchAll (sprintf "%s/bill-actions?minDate=%s" session minDate) 
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
    |> List.iter (fun actionId -> actions.Add(actionId.ToString()))
    // Enueue new action Ids for alert processing
    updateBillCommitteeAssignments cn