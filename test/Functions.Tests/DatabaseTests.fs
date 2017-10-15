module DatabaseTests

open Swensen.Unquote
open Xunit
open Xunit.Abstractions
open Ptp.Database
open Ptp.Model
open System.Data.SqlClient
open Ptp.Core


type CommitteeTests(output:ITestOutputHelper) =

    [<Fact>] 
    member __.canQueryByMultipleIds()=
        
        let connectionString = "CHANGEME"

        let items = [{Id=741;Link="..."};{Id=742;Link="..."};{Id=743;Link="..."}]
        let ids = items |> Seq.map (fun item -> item.Id) |> Seq.toArray
        use sqlCon = new SqlConnection(connectionString)
        let result = 
            sqlCon
            |> dapperParameterizedQuery<Committee> "SELECT Name FROM Committee WHERE Id IN @Ids" {Ids=ids}
        result
        |> Seq.map (fun c -> c.Name)
        |> String.concat "\n"
        |> output.WriteLine