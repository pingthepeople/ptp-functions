module CommitteeTests

open Swensen.Unquote
open Xunit
open Xunit.Abstractions
open Chessie.ErrorHandling
open Ptp.Http

type CommitteeTests(output:ITestOutputHelper) =

    //[<Fact>] 
    member __.``parallel fetch with errors`` ()=
        System.Environment.SetEnvironmentVariable("IgaApiKey", "CHANGEME")

        let urls = [
            "/2017/standing-committees/committee_agriculture_and_natural_resources_3100";
            "/2017/conference-committees/committee_conference_committee_for_hb_1001";
            "/2017/interim-committees/committee_i_agriculture_and_natural_resources_interim_study_committee_on";
            ] 

        urls 
        |> seq
        |> fetchAllParallel
        |> (fun r -> r.ToString())
        |> output.WriteLine
        