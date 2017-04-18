namespace IgaTracker

#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/StackExchange.Redis/lib/net45/StackExchange.Redis.dll"

#load "../shared/model.fs"
#load "../shared/http.fsx"
#load "../shared/db.fsx"
#load "../shared/cache.fsx"

module Bill =

    open System
    open System.Data.SqlClient
    open System.Dynamic
    open System.Collections.Generic
    open Dapper
    open FSharp.Data
    open FSharp.Data.JsonExtensions
    open IgaTracker.Model
    open IgaTracker.Http
    open IgaTracker.Db
    open IgaTracker.Cache
    open IgaTracker.Logging

    [<Literal>]
    let QuerySelectBillByName = """SELECT * FROM Bill 
WHERE Name = @Name AND SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)"""        

    [<Literal>]
    let QueryInsertBill = """INSERT INTO Bill(Name,Link,Title,Description,Authors,Chamber,SessionId) 
VALUES (@Name,@Link,@Title,@Description,@Authors,@Chamber,(SELECT TOP 1 Id FROM Session ORDER BY Name Desc)); 
SELECT * FROM Bill WHERE Name = @Name and SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)"""

    [<Literal>]
    let QueryUpdateBillByName = """UPDATE Bill
SET Title = @Title
	, Description = @Description
	, Authors = @Authors
WHERE Name = @Name AND SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc);
SELECT * FROM Bill WHERE Name = @Name AND SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc);"""

    let toModel (bill:JsonValue) = 
        let v = bill.TryGetProperty("printVersion")
        let printVersion =
            match v with
            | None      -> 1
            | Some x    -> x.AsInteger()
                    
        { Bill.Id=0; 
        SessionId=0; 
        Name=bill?billName.AsString(); 
        Link=bill?link.AsString(); 
        Title=bill?latestVersion?shortDescription.AsString(); 
        Description=bill?latestVersion?digest.AsString();
        Chamber=(if bill?originChamber.AsString() = "house" then Chamber.House else Chamber.Senate);
        Authors=bill?latestVersion?authors.AsArray() |> Array.toList |> List.map (fun a -> a?lastName.AsString()) |> List.sort |> String.concat ", ";
        IsDead=false;
        Version=printVersion }
    
    let insertBill bill cn = 
        let newBillModel = bill |> toModel
        cn |> dapperParameterizedQueryOne<Bill> QueryInsertBill newBillModel

    let updateBillToLatest id cn =
        let sessionYear = cn |> currentSessionYear
        let billName = cn |> dapperParameterizedQueryOne<string> "SELECT Name FROM Bill where Id = @Id" {Id=id}
        let billMetadata = get (sprintf "/%s/bills/%s" sessionYear (billName.ToLower()))
        let latestBillModel = billMetadata |> toModel
        let recordedBillModel = cn |> dapperParameterizedQueryOne<Bill> QuerySelectBillByName latestBillModel
        match latestBillModel.Version with
        | x when x = recordedBillModel.Version -> 
            recordedBillModel
        | _ -> 
            trackTrace "Bill" (sprintf "Updating metadata for %s from version %d to version %d" billName recordedBillModel.Version latestBillModel.Version)
            delete BillsKey
            cn |> dapperParameterizedQueryOne<Bill> QueryUpdateBillByName latestBillModel