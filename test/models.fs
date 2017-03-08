#load "../shared/model.fs"
#r "../packages/Nunit/lib/net45/nunit.framework.dll"
#r "../packages/FsUnit/lib/net45/FsUnit.NUnit.dll"

open NUnit.Framework
open FsUnit

open IgaTracker.Model

module ``Bill model tests`` = 

    [<Test>]
    let ``A bill name should be formatted correctly`` =
        Bill.PrettyPrintName "HB1001" |> should equal "HB 1001"

    [<Test>]
    let ``A bill name should have leading zeroes removed`` =
        Bill.PrettyPrintName "SB0010" |> should equal "SB 10"

    [<Test>]
    let ``A bill number should have leading zeroes removed`` =
        Bill.ParseNumber "SB0010" |> should equal "10"

    [<Test>]
    let ``A bill web url is formatted properly`` = 
        let bill = { Bill.Name = "HB1001"; Chamber = Chamber.House; Id = 0; SessionId = 0; Link = "http://example.com"; Title = "test title"; Description = "test description"; Authors = "test authors";}
        bill.WebLink "2017" |> should equal "[HB 1001](https://iga.in.gov/legislative/2017/bills/house/1001)"

module ``Action model tests`` =
    let date = System.DateTime(2017,1,20)

    [<Test>]
    let ``Committee assignment is described properly`` =
        let action = { Action.ActionType=ActionType.AssignedToCommittee; Description="Committee Name"; Chamber=Chamber.House; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe |> should equal "was assigned to the House Committee on Committee Name"

    [<Test>]    
    let ``Committee reading is described properly`` =
        let action = {  Action.ActionType=ActionType.CommitteeReading; Description="passed;"; Chamber=Chamber.House; Date=date; Id= 0; BillId=0; Link="link" }
        action.Describe |> should equal "was read in committee in the House. The vote was: passed"

    [<Test>]    
    let ``Second reading is described properly`` =
        let action = {  Action.ActionType=ActionType.SecondReading; Description="enrolled; ordered engrossed"; Chamber=Chamber.Senate; Date=date; Id= 0; BillId=0; Link="link" }
        action.Describe |> should equal "had a second reading in the Senate. The vote was: enrolled; ordered engrossed"

    [<Test>]    
    let ``Third reading is described properly`` =
        let action = {  Action.ActionType=ActionType.ThirdReading; Description="passed; foo bar"; Chamber=Chamber.Senate; Date=date; Id= 0; BillId=0; Link="link" }
        action.Describe |> should equal "had a third reading in the Senate. The vote was: passed; foo bar"


module ``Scheduled Action model tests`` = 
    let date = System.DateTime(2017,1,20)

    [<Test>]
    let ``Committee reading is described properly`` =
        let action = { ActionType=ActionType.CommitteeReading; Start=""; End=""; Location="location"; Chamber=Chamber.House; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe false |> should equal "is scheduled for a committee reading on 1/20/2017 in location"

    [<Test>]
    let ``Second reading is described properly`` =
        let action = { ActionType=ActionType.SecondReading; Start=""; End=""; Location="location"; Chamber=Chamber.House; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe false |> should equal "is scheduled for a second reading on 1/20/2017 in location"

    [<Test>]
    let ``Third reading is described properly`` =
        let action = { ActionType=ActionType.ThirdReading; Start=""; End=""; Location="location"; Chamber=Chamber.House; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe false |> should equal "is scheduled for a third reading on 1/20/2017 in location"

    [<Test>]
    let ``Start/End times included when present`` =
        let action = { ActionType=ActionType.CommitteeReading; Start="09:30:00"; End="12:30:00"; Location="location"; Chamber=Chamber.House; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe false |> should equal "is scheduled for a committee reading on 1/20/2017 from 9:30 AM - 12:30 PM in location"

    [<Test>]
    let ``'House Chamber' location formatted properly`` =
        let action = { ActionType=ActionType.CommitteeReading; Start=""; End=""; Location="House Chamber"; Chamber=Chamber.House; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe false |> should equal "is scheduled for a committee reading on 1/20/2017 in the House Chamber"

    let ``'Senate Chamber' location formatted properly`` =
        let action = { ActionType=ActionType.CommitteeReading; Start=""; End=""; Location="Senate Chamber"; Chamber=Chamber.Senate; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe false |> should equal "is scheduled for a committee reading on 1/20/2017 in the Senate Chamber"
