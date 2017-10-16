module GetLegislatorsTests

open Chessie.ErrorHandling
open Ptp.Model
open Ptp.Core
open Swensen.Unquote
open Xunit
open System.Net.Http
open Newtonsoft.Json
open System

let TestLocation = {Address="1000 S Grant St"; City="Bloomington"; Zip="47071"; Year=2017} 

[<Fact>] 
let ``Location is required (no content)`` ()=
    let req = new HttpRequestMessage()
    let expected = Result.FailWith((RequestValidationError("Please provide a location of ContentType 'application/json' in the form '{ Address:string, City:string, Zip:string, Year:int (optional)}'")))
    test <@ GetLegislators.deserializeBody req = expected @>

[<Fact>] 
let ``Location is required (empty content)`` ()=
    let req = new HttpRequestMessage(Content=new StringContent(""))
    let expected = Result.FailWith((RequestValidationError("Please provide a location of ContentType 'application/json' in the form '{ Address:string, City:string, Zip:string, Year:int (optional)}'")))
    test <@ GetLegislators.deserializeBody req = expected @>

[<Fact>] 
let ``Address is required`` ()=
    let noAddr = {TestLocation with Address=""}
    let expected = Result.FailWith((RequestValidationError("Please provide an address")))
    test <@ noAddr |> GetLegislators.validateRequest = expected @>

[<Fact>] 
let ``City is required`` ()=
    let noCity = {TestLocation with City=""}
    let expected = Result.FailWith((RequestValidationError("Please provide a city")))
    test <@ noCity  |> GetLegislators.validateRequest = expected @>

[<Fact>] 
let ``Zip is required`` ()=
    let noZip = {TestLocation with Zip=""}
    let expected = Result.FailWith((RequestValidationError("Please provide a zip code")))
    test <@ noZip |> GetLegislators.validateRequest = expected @>

[<Fact>] 
let ``Multiple validation`` ()=
    let noZip = {TestLocation with City=""; Zip=""}
    let expectedErrors = 
        [
            RequestValidationError("Please provide a city");
            RequestValidationError("Please provide a zip code")
        ]
    let expected = Result<Location,WorkFlowFailure>.FailWith(expectedErrors)
    test <@ noZip |> GetLegislators.validateRequest = expected @>

[<Fact>]
let ``Location is valid`` ()=
    test <@ TestLocation |> GetLegislators.validateRequest = Ok(TestLocation, []) @>

[<Fact>] 
let ``Location defaults to current year`` ()=
    let sut = { TestLocation with Year=0 }
    let expected = { TestLocation with Year=DateTime.Now.Year }
    test <@ sut |> GetLegislators.setReasonableDefaults = Ok(expected, []) @>

[<Fact>] 
let ``Location year can be next year`` ()=
    let future = { TestLocation with Year=DateTime.Now.AddYears(1).Year }
    test <@ future |> GetLegislators.validateRequest = Ok(future, []) @>

[<Fact>] 
let ``Location year cannot be past next year`` ()=
    let nextYear = DateTime.UtcNow.Year + 1
    let future = { TestLocation with Year=(nextYear+1) }
    let expected = Result.FailWith((RequestValidationError(sprintf "Year can't be past %d" nextYear)))
    test <@ future |> GetLegislators.validateRequest = expected @>
    
//[<Fact>]    
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
    let expectedBody = [expectedSenator; expectedRepresentative] |> JsonConvert.SerializeObject
    let expected = Ok(expectedBody, noErrors)

    test <@ (GetLegislators.workflow req)() = expected @>