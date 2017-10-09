module CoreTests

open Swensen.Unquote
open Xunit
open Ptp.Core

let a = [1; 2; 3]
let b = [   2; 3; 4] 

[<Fact>]
let ``a except b``() =
    test <@ a |> except b (fun a b -> a=b) |> Seq.toList = [1;] @>

[<Fact>]
let ``b except a``() =
    test <@ b |> except a (fun a b -> a=b) |> Seq.toList = [4;] @>

[<Fact>]
let ``b except b``() =
    test <@ b |> except b (fun a b -> a=b) |> Seq.toList = [] @>

[<Fact>]
let ``a except empty``() =
    test <@ a |> except [] (fun a b -> a=b) |> Seq.toList = a @>