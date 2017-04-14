#load "./logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "../packages/Dapper/lib/net45/Dapper.dll"

namespace IgaTracker 

module Db =

    open System.Data.SqlClient
    open System.Dynamic
    open System.Collections.Generic
    open IgaTracker.Logging
    open Dapper

    type DateSelectArgs = {Date:string}
    type IdSelect = {Id:int}

    let dapperQuery<'Result> (query:string) (connection:SqlConnection) =
        let func() = connection.Query<'Result>(query)
        trackDependency "database" query func
    
    let dapperParametrizedQuery<'Result> (query:string) (param:obj) (connection:SqlConnection) : 'Result seq =
        let func() = connection.Query<'Result>(query, param)
        trackDependency "database" query func
    
    let dapperMapParametrizedQuery<'Result> (query:string) (param : Map<string,_>) (connection:SqlConnection) : 'Result seq =
        let expando = ExpandoObject()
        let expandoDictionary = expando :> IDictionary<string,obj>
        for paramValue in param do
            expandoDictionary.Add(paramValue.Key, paramValue.Value :> obj)
        connection |> dapperParametrizedQuery query expando
    
    let dapperParameterizedQueryOne<'Result> (query:string) (param:obj) connection =
        connection |> dapperParametrizedQuery<'Result> query param |> Seq.head

    let dapperQueryOne<'Result> (query:string) connection =
        connection |> dapperQuery<'Result> query |> Seq.head

    let currentSessionYear cn = 
        cn |> dapperQueryOne<string> "SELECT TOP 1 Name FROM Session ORDER BY Name Desc"

    let currentSessionId cn = 
        cn |> dapperQueryOne<int> "SELECT TOP 1 Id FROM Session ORDER BY Name Desc"