module CoreTests

open Swensen.Unquote
open Xunit
open Ptp.Core

let a = [1; 2; 3]
let b = [   2; 3; 4] 

[<Fact>]
let ``a except b``() =
    test <@ a |> except b |> Seq.toList = [1;] @>

[<Fact>]
let ``b except a``() =
    test <@ b |> except a |> Seq.toList = [4;] @>

[<Fact>]
let ``b except b``() =
    test <@ b |> except b |> Seq.toList = [] @>

[<Fact>]
let ``a except empty``() =
    test <@ a |> except [] |> Seq.toList = a @>