module HttpTests

open Swensen.Unquote
open Xunit
open Xunit.Abstractions
open Ptp.Http
open Ptp.Core
open Chessie.ErrorHandling
open FSharp.Data
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
    member __.``parallel fetch with errors`` ()=
        let urls = [
            "https://api.status.iu.edu/notices";
            "https://werewrwer.aslkfjwefwef.com";
            "https://api.status.iu.edu/services";
            ]

        let firstWorkflowStep () = 
            urls |> fetchAllParallel
        
        let nextWorkflowStep results =
            results
            |> chooseBoth
            |> List.map fst
            |> String.concat ", "
            |> sprintf "happy dance: %s"
            |> ok
        
        let workflow = 
            firstWorkflowStep
            >> bind nextWorkflowStep
        
        workflow()
        |> (fun r -> r.ToString())
        |> output.WriteLine
    
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
            Circle(Radius 3)
            Rectangle(Length 3, Width 4)
            Square(Length 3)
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
