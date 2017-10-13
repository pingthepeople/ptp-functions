module DatabaseTests

open Swensen.Unquote
open Xunit
open Xunit.Abstractions
open Ptp.Database
open Ptp.Model


type CommitteeTests(output:ITestOutputHelper) =

    [<Fact>] 
    member __.canQueryByMultipleIds()=
        
        System.Environment.SetEnvironmentVariable("SqlServer.ConnectionString", "CHANGEME")

        let items = [{Id=741;Link="..."};{Id=742;Link="..."};{Id=743;Link="..."}]
        let ids = items |> Seq.map (fun item -> item.Id) |> Seq.toArray
        let result = 
            (sqlConnection())
            |> dapperParametrizedQuery<Committee> "SELECT Name FROM Committee WHERE Id IN @Ids" {Ids=ids}
        result
        |> Seq.map (fun c -> c.Name)
        |> String.concat "\n"
        |> output.WriteLine