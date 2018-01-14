module HttpTests

open Swensen.Unquote
open Xunit
open Xunit.Abstractions
open Ptp.Http
open Ptp.Core
open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Newtonsoft.Json

let updateSubjects = UpdateSubjects
let updateBill = UpdateBill "/foo/bar"

type Radius = Radius of int
type Length = Length of int
type Width = Width of int

type Shape = 
    | Circle of Radius
    | Square of Length
    | Rectangle of Length * Width

type HttpTests(output:ITestOutputHelper) =

    [<Fact>]
    member __.``toString updateSubjects`` ()=
        updateSubjects.ToString()
        |> output.WriteLine        
    
    [<Fact>]
    member __.``serialize updateSubjects`` ()=
        updateSubjects
        |> JsonConvert.SerializeObject
        |> output.WriteLine

    [<Fact>]
    member __.``toString updateBill`` ()=
        updateBill.ToString()
        |> output.WriteLine
    
    [<Fact>]
    member __.``serialize updateBill`` ()=
        updateBill
        |> JsonConvert.SerializeObject
        |> output.WriteLine

    [<Fact>]
    member __.``serialize/deserialize updateSubject`` ()=
        let actual =
            updateSubjects
            |> JsonConvert.SerializeObject
            |> JsonConvert.DeserializeObject<Workflow>
        test <@ actual = updateSubjects @>

    [<Fact>]
    member __.``serialize/deserialize updateBill`` ()=
        let actual =
            updateBill
            |> JsonConvert.SerializeObject
            |> JsonConvert.DeserializeObject<Workflow>
        test <@ actual = updateBill @>

    [<Fact>]
    member __.``serialize DU`` ()=
        [
            UpdateAction "http://example.com"
            UpdateActions 
        ]
        |> List.map JsonConvert.SerializeObject
        |> List.iter output.WriteLine

    [<Fact>]
    member __.``serialize DU list`` ()=
        let expected =
            [
                Circle(Radius 3)
                Rectangle(Length 3, Width 4)
                Square(Length 3)
            ]

        let serialized = 
            expected
            |> JsonConvert.SerializeObject
        
        serialized |> output.WriteLine
        
        let actual =
            serialized
            |> JsonConvert.DeserializeObject<Shape list>

        test <@ actual = expected @>

    [<Fact>]
    member __.``fetch all`` ()=
        System.Environment.SetEnvironmentVariable("IgaApiKey", "CHANGEME")
        test <@ match fetchAll "/2018/legislators" with
                | Ok(_,[]) -> true
                | _ -> false
              @>