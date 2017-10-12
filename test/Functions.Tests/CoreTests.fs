module CoreTests

open Swensen.Unquote
open Xunit
open Xunit.Abstractions
open Ptp.Core

let a = [1; 2; 3]
let b = [   2; 3; 4] 

type TestEntity = {Id:int; Val:int}

let a' = [ {Id=1; Val=1}; {Id=2; Val=2}; {Id=3; Val=3} ]
let b' = [                {Id=0; Val=2}; {Id=0; Val=3}; {Id=0; Val=4} ]

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

let key x = x.Val
let compare x y = x.Val = y.Val

[<Fact>]
let ``a' except b'``() =
    test <@ a' |> except' b' key |> Seq.toList = [{Id=1; Val=1}] @>

[<Fact>]
let ``b' except a'``() =
    test <@ b' |> except' a' key |> Seq.toList = [{Id=0; Val=4}] @>

[<Fact>]
let ``a' except a'``() =
    test <@ a' |> except' a' key |> Seq.toList = [] @>

[<Fact>]
let ``a intersect b``() =
    test <@ a |> intersect b |> Seq.toList = [2; 3;] @>

[<Fact>]
let ``b intersect a``() =
    test <@ b |> intersect a |> Seq.toList = [2; 3;] @>

[<Fact>]
let ``a intersect empty``() =
    test <@ a |> intersect [] |> Seq.toList = [] @>
