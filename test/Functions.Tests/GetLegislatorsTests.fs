module GetLegislatorsTests

open Chessie.ErrorHandling
open Ptp.Model
open Swensen.Unquote
open System.Net
open Xunit
open System.Net.Http
open Newtonsoft.Json
open System

let TestLocation = {Address="1000 S Grant St"; City="Bloomington"; Zip="47071"; Year=2017} 

[<Fact>] 
let ``Location is required (no content)`` ()=
    let req = new HttpRequestMessage()
    let expected = Result.FailWith((HttpStatusCode.BadRequest, "Please provide a location in the form '{ Address:STRING, City:STRING, Zip:STRING, Year:INT (optional)}'"))
    test <@ GetLegislators.processRequest req = expected @>

[<Fact>] 
let ``Location is required (empty content)`` ()=
    let req = new HttpRequestMessage(Content=new StringContent(""))
    let expected = Result.FailWith((HttpStatusCode.BadRequest, "Please provide a location in the form '{ Address:STRING, City:STRING, Zip:STRING, Year:INT (optional)}'"))
    test <@ GetLegislators.processRequest req = expected @>

[<Fact>] 
let ``Address is required`` ()=
    let noAddr = {TestLocation with Address=""}
    let expected = Result.FailWith((HttpStatusCode.BadRequest, "Please provide an address"))
    test <@ noAddr |> GetLegislators.validateLocation = expected @>

[<Fact>] 
let ``City is required`` ()=
    let noCity = {TestLocation with City=""}
    let expected = Result.FailWith((HttpStatusCode.BadRequest, "Please provide a city"))
    test <@ noCity  |> GetLegislators.validateLocation = expected @>

[<Fact>] 
let ``Zip is required`` ()=
    let noZip = {TestLocation with Zip=""}
    let expected = Result.FailWith((HttpStatusCode.BadRequest, "Please provide a zip code"))
    test <@ noZip |> GetLegislators.validateLocation = expected @>

[<Fact>]
let ``Location is valid`` ()=
    test <@ TestLocation |> GetLegislators.validateLocation = Ok(TestLocation, []) @>

[<Fact>] 
let ``Location defaults to current year`` ()=
    let sut = { TestLocation with Year=0 }
    let expected = { TestLocation with Year=DateTime.Now.Year }
    test <@ sut |> GetLegislators.validateLocation = Ok(expected, []) @>

[<Fact>] 
let ``Location respects explicit year`` ()=
    test <@ TestLocation |> GetLegislators.validateLocation = Ok(TestLocation, []) @>

[<Fact>] 
let ``Location year can be next year`` ()=
    let future = { TestLocation with Year=DateTime.Now.AddYears(1).Year }
    test <@ future |> GetLegislators.validateLocation = Ok(future, []) @>

[<Fact>] 
let ``Location year cannot be past next year`` ()=
    let future = { TestLocation with Year=DateTime.Now.AddYears(2).Year }
    let expected = Result.FailWith((HttpStatusCode.BadRequest, "The year cannot be past next year"))
    test <@ future |> GetLegislators.validateLocation = expected @>
    
[<Fact>]    
let ``get legislators`` () =

    let expectedSenator =
        {
            Name="Senator Mark Stoops"; 
            Party=Party.Democratic; 
            Chamber=Chamber.Senate; 
            District=40; 
            Link="https://iga.in.gov/legislative/2017/legislators/legislator_mark_stoops_1107";
            Image="https://iga.in.gov/legislative/2017/portraits/legislator_mark_stoops_1107/" 
        }

    let expectedRepresentative =
        {
            Name="Representative Matt Pierce"; 
            Party=Party.Democratic; 
            Chamber=Chamber.House; 
            District=61; 
            Link="https://iga.in.gov/legislative/2017/legislators/legislator_matthew_pierce_708";
            Image="https://iga.in.gov/legislative/2017/portraits/legislator_matthew_pierce_708/" 
        }

    let req = new HttpRequestMessage(Content=new StringContent(TestLocation |> JsonConvert.SerializeObject))
    let noErrors = List.empty<(HttpStatusCode*string)>
    let expected = Ok([expectedSenator; expectedRepresentative], noErrors)

    test <@ GetLegislators.processRequest req = expected @>