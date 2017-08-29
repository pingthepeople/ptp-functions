module GetLegislatorsTests

open Ptp.Model
open Swensen.Unquote
open Xunit

let TestLocation = {Address="1000 S Grant St"; City="Bloomington"; Zip="47071"} 

[<Fact>]
let ``get legislators`` () =
    let expected = 
        [
            {
                Legislator.Id=0; 
                Name="Senator Mark Stoops"; 
                Party=Party.Democratic; 
                Chamber=Chamber.Senate; 
                District=40; 
                Url="https://iga.in.gov/legislative/2017/legislators/legislator_mark_stoops_1107";
                Image="https://iga.in.gov/legislative/2017/portraits/legislator_mark_stoops_1107/" 
            };
            {
                Legislator.Id=0; 
                Name="Representative Matt Pierce"; 
                Party=Party.Democratic; 
                Chamber=Chamber.House; 
                District=61; 
                Url="https://iga.in.gov/legislative/2017/legislators/legislator_matthew_pierce_708";
                Image="https://iga.in.gov/legislative/2017/portraits/legislator_matthew_pierce_708/"
            }
        ]

    test <@ GetLegislators.lookup TestLocation = expected @>

[<Fact>]
let ``bad request from fake address`` () =
    test <@ GetLegislators.lookup {TestLocation with Address = "1234 Foo St"} = [] @>

[<Fact>]
let ``bad request from fake city`` () =
    test <@ GetLegislators.lookup {TestLocation with City = "Zoolander"} = [] @>

[<Fact>]
let ``bad request from fake zip`` () =
    test <@ GetLegislators.lookup {TestLocation with Zip = "12345"} = [] @>
