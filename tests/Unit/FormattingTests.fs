module FormattingTests

open Swensen.Unquote
open Xunit
open Ptp.Common.Formatting

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

[<Fact>]
let ``generates message digest``()=
    let testBody = "this is the message body"
    let expectedHash = "95F1C48D1EDD2CB36B099F999E94FCC4BB0462FA4F8E6B650F0A94EEC4BA493E"
    test <@ sha256Hash testBody = expectedHash @>
