module GetLegislatorsTests

open Chessie.ErrorHandling
open Ptp.Model
open Ptp.Core
open Ptp.GetLegislators
open Swensen.Unquote
open Xunit
open System.Net.Http
open Newtonsoft.Json
open System

let TestLocation = {Address="1000 S Grant St"; City="Bloomington"; Zip="47071" } 

[<Fact>] 
let ``Location is required (no content)`` ()=
    let req = new HttpRequestMessage()
    let expected = Result.FailWith((RequestValidationError(deserializeLocationError)))
    test <@ deserializeLocation req = expected @>

[<Fact>] 
let ``Location is required (empty content)`` ()=
    let req = new HttpRequestMessage(Content=new StringContent(""))
    let expected = Result.FailWith((RequestValidationError(deserializeLocationError)))
    test <@ deserializeLocation  req = expected @>

[<Fact>] 
let ``Address is required`` ()=
    let noAddr = {TestLocation with Address=""}
    let expected = Result.FailWith((RequestValidationError("Please provide an address")))
    test <@ noAddr |> validateRequest = expected @>

[<Fact>] 
let ``City is required`` ()=
    let noCity = {TestLocation with City=""}
    let expected = Result.FailWith((RequestValidationError("Please provide a city")))
    test <@ noCity  |> validateRequest = expected @>

[<Fact>] 
let ``Zip is required`` ()=
    let noZip = {TestLocation with Zip=""}
    let expected = Result.FailWith((RequestValidationError("Please provide a zip code")))
    test <@ noZip |> validateRequest = expected @>

[<Fact>] 
let ``Multiple validation`` ()=
    let noZip = {TestLocation with City=""; Zip=""}
    let expectedErrors = 
        [
            RequestValidationError("Please provide a city");
            RequestValidationError("Please provide a zip code")
        ]
    let expected = Result<Location,WorkFlowFailure>.FailWith(expectedErrors)
    test <@ noZip |> validateRequest = expected @>

[<Fact>]
let ``Location is valid`` ()=
    test <@ TestLocation |> validateRequest = Ok(TestLocation, []) @>

//[<Fact>]
(*
let ``get legislators`` () =
    
    let expectedSenator =
        {
            Id=0; /// this will change!
            Name="Senator Mark Stoops"; 
            Party=Party.Democratic; 
            Chamber=Chamber.Senate; 
            District=40; 
            Link="https://iga.in.gov/legislative/2017/legislators/legislator_mark_stoops_1107";
            Image="https://iga.in.gov/legislative/2017/portraits/legislator_mark_stoops_1107/" 
        }

    let expectedRepresentative =
        {
            Id=0; /// this will change!
            Name="Representative Matt Pierce"; 
            Party=Party.Democratic; 
            Chamber=Chamber.House; 
            District=61; 
            Link="https://iga.in.gov/legislative/2017/legislators/legislator_matthew_pierce_708";
            Image="https://iga.in.gov/legislative/2017/portraits/legislator_matthew_pierce_708/" 
        }

    let req = new HttpRequestMessage(Content=new StringContent(TestLocation |> JsonConvert.SerializeObject))
    let noErrors = List.empty<WorkFlowFailure>
    let expectedBody = {Senator=expectedSenator; Representative=expectedRepresentative}
    let expected = Ok(expectedBody, noErrors)

    test <@ (workflow req)() = expected @>
*)