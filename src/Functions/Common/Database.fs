module Ptp.Database

open Chessie.ErrorHandling
open Dapper
open System.Collections.Generic
open System.Data.SqlClient
open System.Dynamic
open Ptp.Core

type DateSelectArgs = {Date:string}
type IdSelect = {Id:int}
type IdListSelect = {Ids:int[]}
type LinksListSelect = {Links:string[]}

let sqlConStr() = System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString")
let sqlConnection() = new SqlConnection(sqlConStr())

let expand (param : Map<string,_>) =
    let expando = ExpandoObject()
    let expandoDictionary = expando :> IDictionary<string,obj>
    for paramValue in param do
        expandoDictionary.Add(paramValue.Key, paramValue.Value :> obj)
    expando

let dapperQuery<'Result> (query:string) (connection:SqlConnection) =
    connection.Query<'Result>(query)
    
let dapperParametrizedQuery<'Result> (query:string) (param:obj) (connection:SqlConnection) : 'Result seq =
    connection.Query<'Result>(query, param)
    
let dapperMapParametrizedQuery<'Result> (query:string) (param : Map<string,_>) (connection:SqlConnection) : 'Result seq =
    let expando = ExpandoObject()
    let expandoDictionary = expando :> IDictionary<string,obj>
    for paramValue in param do
        expandoDictionary.Add(paramValue.Key, paramValue.Value :> obj)
    connection |> dapperParametrizedQuery query (expand param)
    
let dapperParameterizedCommand (query:string) (param:obj) (connection:SqlConnection) =
    connection.Execute(query, param) |> ignore

// ROP
let dbQuery<'Result> (queryText:string) =
    let op() =
        sqlConnection() 
        |> dapperQuery<'Result> queryText
        |> Seq.cast<'Result>
    tryF' op (fun err -> DatabaseQueryError(QueryText(queryText), err))

let dbParameterizedQuery<'Result> (queryText:string) (param:obj)=
    let op() =
        sqlConnection() 
        |> dapperParametrizedQuery<'Result> queryText param
        |> Seq.cast<'Result>
    tryF' op (fun err -> DatabaseQueryError(QueryText(queryText), err))

let expectOne query results = 
    match Seq.tryHead results with
    | None -> 
        let msg = "Expected query to return one value but got none."
        fail (DatabaseQueryError(QueryText(query), msg))
    | Some value -> 
        ok value

let dbQueryOne<'Result> (queryText:string) = trial {
    let! results = dbQuery<'Result> queryText
    let! ret = results |> expectOne queryText
    return ret
    }

let dbParameterizedQueryOne<'Result> (queryText:string) (param:obj) = trial {
    let! results = dbParameterizedQuery<'Result> queryText param
    let! ret = results |> expectOne queryText
    return ret
    }

let dbCommand (commandText:string) items = 
    let op() =
        sqlConnection() 
        |> dapperParameterizedCommand commandText items
        items
    tryF' op (fun e -> DatabaseCommandError (CommandText(commandText),e))

let getCurrentSessionYear () =
    dbQueryOne<string> "SELECT TOP 1 Name FROM Session WHERE Active = 1"

let dbQueryById<'Result> (queryText:string) ids = 
    let idList = {Ids=(Seq.toArray ids)}
    dbParameterizedQuery<'Result> queryText idList

let dbQueryByLinks<'Result> (queryText:string) links = 
    let linksList = {Links=(Seq.toArray links)}
    dbParameterizedQuery<'Result> queryText linksList

let dbCommandById<'Result> (queryText:string) ids = 
    let idList = {Ids=(Seq.toArray ids)}
    dbCommand queryText idList
