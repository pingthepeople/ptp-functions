module FormattingTests

open Swensen.Unquote
open Xunit
open Ptp.Formatting

[<Fact>]
let ``formats start/end times``() =
    test <@ formatEventTime "13:00" "15:00" "" = " from 1:00 PM - 3:00 PM" @>

[<Fact>]
let ``formats custom time``() =
    test <@ formatEventTime "" "" "Upon Adjournment of Session" = " upon adjournment of Session" @>

[<Fact>]
let ``formats start time``() =
    test <@ formatEventTime "13:00" "" "" = " starting at 1:00 PM" @>

[<Fact>]
let ``formats date``() =
    test <@ formatEventDate (System.DateTime(2018,1,5)) = "Friday, 5 Jan 2018" @>
