// Configure Database 

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/db.fsx"

namespace IgaTracker

module UpdateActions =

    open System
    open System.Data.SqlClient
    open System.Dynamic
    open System.Collections.Generic
    open Dapper
    open FSharp.Data
    open FSharp.Data.JsonExtensions
    open IgaTracker.Model
    open IgaTracker.Queries
    open IgaTracker.Db

    let toActionModel (action,bill:Bill) = {
        Action.Id = 0;
        Date = action?date.AsDateTime();
        Link = action?link.AsString();
        ActionType = action?description.AsString() |> Action.ParseType;
        Description = action?description.AsString() |> Action.ParseDescription;
        Chamber = System.Enum.Parse(typeof<Chamber>, action?chamber?name.AsString()) :?> Chamber
        BillId = bill.Id;
    }

    let unknownActionTypes (action:Action) = action.ActionType <> ActionType.Unknown

    let addToDatabase date (cn:SqlConnection) allActions =

        let bills = cn |> dapperQuery<Bill> SelectBillIdsAndNames
        let links = cn |> dapperParametrizedQuery<string> SelectActionLinksOccuringAfterDate {DateSelectArgs.Date=date}

        let addActionToDbAndGetId (action:Action) = cn |> dapperParametrizedQuery<int> InsertAction action |> Seq.head
        let fetchActionsRequiringAlert insertedIds = cn |> dapperMapParametrizedQuery<Action> SelectActionsRequiringNotification (Map ["Ids", insertedIds :> obj])

        let unrecordedActions action = links |> Seq.exists (fun link -> link = action?link.AsString()) |> not
        let actionAndBill action = (action, bills |> Seq.find (fun bill -> bill.Name = action?billName?billName.AsString()))

        allActions
            |> List.filter unrecordedActions
            |> List.map (actionAndBill >> toActionModel >> addActionToDbAndGetId)
            |> fetchActionsRequiringAlert    
