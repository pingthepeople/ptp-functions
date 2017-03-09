#r "../packages/Dapper/lib/net45/Dapper.dll"

namespace IgaTracker 

module Db =

    open System.Data.SqlClient
    open System.Dynamic
    open System.Collections.Generic
    open Dapper

    type DateSelectArgs = {Date:string}
    type IdSelect = {Id:int}

    let dapperQuery<'Result> (query:string) (connection:SqlConnection) =
        connection.Query<'Result>(query)
    
    let dapperParametrizedQuery<'Result> (query:string) (param:obj) (connection:SqlConnection) : 'Result seq =
        connection.Query<'Result>(query, param)
    
    let dapperMapParametrizedQuery<'Result> (query:string) (param : Map<string,_>) (connection:SqlConnection) : 'Result seq =
        let expando = ExpandoObject()
        let expandoDictionary = expando :> IDictionary<string,obj>
        for paramValue in param do
            expandoDictionary.Add(paramValue.Key, paramValue.Value :> obj)
    
        connection |> dapperParametrizedQuery query expando
    
    let currentSessionYear cn = 
        cn |> dapperQuery<string> "SELECT TOP 1 Name FROM Session ORDER BY Name Desc" |> Seq.head

    let currentSessionId cn = 
        cn |> dapperQuery<int> "SELECT TOP 1 Id FROM Session ORDER BY Name Desc" |> Seq.head