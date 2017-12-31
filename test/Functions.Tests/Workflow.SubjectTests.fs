module UpdateCanonicalData_SubjectsTests

open Chessie.ErrorHandling
open Ptp.Model
open Ptp.Workflow.Subjects
open Swensen.Unquote
open Xunit
open System.Net.Http

let defSubject = {Subject.Name=""; Link=""; Id=0; SessionId=0;}

[<Fact>] 
let ``Location is required (no content)`` ()=            
    let a = {defSubject with Subject.Name="Subject a"; Link="/subject/a";}
    let b = {defSubject with Subject.Name="Subject b"; Link="/subject/b";}
    let c = {defSubject with Subject.Name="Subject c"; Link="/subject/c";}
    let fromApi = [a;b;c]
    let fromDb = [ {LinkAndId.Id = 1; Link="/subject/b"} ]
    let expected = Result.Succeed([a;c])
    test <@ filterOutKnownSubjects (fromApi,fromDb) = expected @>    