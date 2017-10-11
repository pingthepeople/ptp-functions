module HttpTests

open Swensen.Unquote
open Xunit
open Xunit.Abstractions
open Ptp.Http
open Ptp.Core
open Chessie.ErrorHandling
open FSharp.Data

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
        
