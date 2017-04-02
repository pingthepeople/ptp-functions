#load "../shared/model.fs"
#load "../generateDeadBillAlerts/run.fsx"
#r "../packages/Nunit/lib/net45/nunit.framework.dll"
#r "../packages/FsUnit/lib/net45/FsUnit.NUnit.dll"

open System
open NUnit.Framework
open FsUnit
open IgaTracker.Model
open Run
let genericAction = {Action.Chamber=Chamber.House; Date=System.DateTime(2000,1,1); ActionType=ActionType.CommitteeReading; Description="";  Id=0;BillId=0;Link="";}
let houseBill = {Bill.Chamber=Chamber.House; Name="HB1000"; Title="Title"; Description="Description"; Link="Link"; Authors="Authors"; Id=0; SessionId=0; IsDead=true;}
let senateBill = {Bill.Chamber=Chamber.Senate; Name="SB0001"; Title="Title"; Description="Description"; Link="Link"; Authors="Authors"; Id=0; SessionId=0; IsDead=true;}

[<TestFixture>]
type ``died in the House``()=

    [<Test>] 
    let ``because did not receive house committee reading``()=
        let mostRecentAction = Some({genericAction with ActionType=ActionType.AssignedToCommittee; Chamber=Chamber.House })
        formatBody "2017" houseBill mostRecentAction MessageType.SMS |> should equal "HB 1000 ('Title') has died in the House upon missing the deadline for a committee reading."

    [<Test>] 
    let ``because did not receive house second reading``()=
        let mostRecentAction = Some({genericAction with ActionType=ActionType.CommitteeReading; Chamber=Chamber.House})
        formatBody "2017" houseBill mostRecentAction MessageType.SMS |> should equal "HB 1000 ('Title') has died in the House upon missing the deadline for a second reading."

    [<Test>] 
    let ``because did not receive house third reading``()=
        let mostRecentAction = Some({genericAction with ActionType=ActionType.SecondReading; Chamber=Chamber.House})
        formatBody "2017" houseBill mostRecentAction MessageType.SMS |> should equal "HB 1000 ('Title') has died in the House upon missing the deadline for a third reading."        

    [<Test>] 
    let ``because did not receive house committee assignment (house bill)``()=
        let mostRecentAction = None
        formatBody "2017" houseBill mostRecentAction MessageType.SMS |> should equal "HB 1000 ('Title') has died in the House upon missing the deadline for a committee assignment."

    [<Test>] 
    let ``because did not receive house committee assignment (senate bill)``()=
        let mostRecentAction = Some({genericAction with ActionType=ActionType.ThirdReading; Chamber=Chamber.Senate})
        formatBody "2017" senateBill mostRecentAction MessageType.SMS |> should equal "SB 1 ('Title') has died in the House upon missing the deadline for a committee assignment."

[<TestFixture>]
type ``died in the Senate``()=

    [<Test>] 
    let ``because did not receive senate committee reading``()=
        let mostRecentAction = Some({genericAction with ActionType=ActionType.AssignedToCommittee; Chamber=Chamber.Senate})
        formatBody "2017" senateBill mostRecentAction MessageType.SMS |> should equal "SB 1 ('Title') has died in the Senate upon missing the deadline for a committee reading."

    [<Test>] 
    let ``because did not receive senate second reading``()=
        let mostRecentAction = Some({genericAction with ActionType=ActionType.CommitteeReading; Chamber=Chamber.Senate})
        formatBody "2017" senateBill mostRecentAction MessageType.SMS |> should equal "SB 1 ('Title') has died in the Senate upon missing the deadline for a second reading."

    [<Test>] 
    let ``because did not receive senate third reading``()=
        let mostRecentAction = Some({genericAction with ActionType=ActionType.SecondReading; Chamber=Chamber.Senate})
        formatBody "2017" senateBill mostRecentAction MessageType.SMS |> should equal "SB 1 ('Title') has died in the Senate upon missing the deadline for a third reading."        

    [<Test>] 
    let ``because did not receive senate committee assignment (house bill)``()=
        let mostRecentAction = Some({genericAction with ActionType=ActionType.ThirdReading; Chamber=Chamber.House})
        formatBody "2017" houseBill mostRecentAction MessageType.SMS |> should equal "HB 1000 ('Title') has died in the Senate upon missing the deadline for a committee assignment."

    [<Test>] 
    let ``because did not receive senate committee assignment (senate bill)``()=
        let mostRecentAction = None
        formatBody "2017" senateBill mostRecentAction MessageType.SMS |> should equal "SB 1 ('Title') has died in the Senate upon missing the deadline for a committee assignment."
