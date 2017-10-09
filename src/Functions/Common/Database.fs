module Ptp.Database

open Chessie.ErrorHandling
open Dapper
open Ptp.Logging
open System.Collections.Generic
open System.Data.SqlClient
open System.Dynamic
open Ptp.Core

type DateSelectArgs = {Date:string}
type IdSelect = {Id:int}
type IdListSelect = {Ids:int[]}

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
    
let dapperParameterizedQueryOne<'Result> (query:string) (param:obj) connection =
    connection |> dapperParametrizedQuery<'Result> query param |> Seq.head

let dapperQueryOne<'Result> (query:string) connection =
    connection |> dapperQuery<'Result> query |> Seq.head

let dapperCommand (query:string) (connection:SqlConnection) =
    connection.Execute(query) |> ignore

let dapperParameterizedCommand (query:string) (param:obj) (connection:SqlConnection) =
    connection.Execute(query, param) |> ignore

let currentSessionYear cn = 
    cn |> dapperQueryOne<string> "SELECT TOP 1 Name FROM Session ORDER BY Name Desc"

let currentSessionId cn = 
    cn |> dapperQueryOne<int> "SELECT TOP 1 Id FROM Session ORDER BY Name Desc"

// ROP
let dbQuery<'Result> (queryText:string) =
    let op() =
        sqlConnection() 
        |> dapperQuery<'Result> queryText
        |> Seq.cast<'Result>
    tryF' op (fun err -> DatabaseQueryError(QueryText(queryText), err))

let queryOne<'Result> (queryText:string) =
    let takeOne results = 
        results |> Seq.head |> ok

    dbQuery<'Result> queryText
    >>= takeOne

let dbCommand (commandText:string) items =
    let op() =
        sqlConnection() 
        |> dapperParameterizedCommand commandText items
        items
    tryF' op (fun e -> DatabaseCommandError (CommandText(commandText),e))

let getCurrentSessionYear () = 
    queryOne<string> "SELECT TOP 1 Name FROM Session ORDER BY Name DESC"