module UpdateCanonicalDataTests

open Swensen.Unquote
open Xunit

let toTitleCase = 
    System.Globalization.CultureInfo("en-US", false)
        .TextInfo
        .ToTitleCase

[<Fact>]
let ``to title case``() =
    test <@ toTitleCase "welfare, snap Program" = "Welfare, Snap Program" @>