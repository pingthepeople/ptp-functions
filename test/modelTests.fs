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
        let bill = { Bill.Name = "HB1001"; Chamber = Chamber.House; Id = 0; SessionId = 0; Link = "http://example.com"; Title = "test title"; Description = "test description"; Authors = "test authors"; IsDead=false }
        bill.WebLink "2017" |> should equal "[HB 1001](https://iga.in.gov/legislative/2017/bills/house/1001)"

module ``Action description formatting`` =
    let date = System.DateTime(2017,1,20)
    let defaultAction = { Action.ActionType=ActionType.AssignedToCommittee; Description=""; Chamber=Chamber.House; Date=date; Id=0; BillId=0; Link="" }

    [<Test>]
    let ``Committee assignment`` =
        let action = { defaultAction with Action.ActionType=ActionType.AssignedToCommittee; Description="Committee Name"; }
        action |> Action.FormatDescription "title" |> should equal "title was assigned to the House Committee on Committee Name."

    [<Test>]    
    let ``Committee hearing`` =
        let action = { defaultAction with Action.ActionType=ActionType.CommitteeReading; Description="passed;"; }
        action |> Action.FormatDescription "title"  |> should equal "title had a committee hearing in the House. The vote was: passed."

    [<Test>]    
    let ``Second reading`` =
        let action = { defaultAction with Action.ActionType=ActionType.SecondReading; Description="enrolled; ordered engrossed"; Chamber=Chamber.Senate;}
        action |> Action.FormatDescription "title"  |> should equal "title had a second reading in the Senate. The vote was: enrolled; ordered engrossed."

    [<Test>]    
    let ``Third reading`` =
        let action = { defaultAction with Action.ActionType=ActionType.ThirdReading; Description="passed; foo bar"; Chamber=Chamber.Senate;}
        action |> Action.FormatDescription "title"  |> should equal "title had a third reading in the Senate. The vote was: passed; foo bar."

    [<Test>]    
    let ``Signed by president of Senate`` =
        let action = { defaultAction with Action.ActionType=ActionType.SignedByPresidentOfSenate; Description=""; }
        action |> Action.FormatDescription "title"  |> should equal "title has been signed by the President of the Senate. It will now be sent to the Governor to be signed into law or vetoed."

    [<Test>]    
    let ``Signed by governor`` =
        let action = { defaultAction with Action.ActionType=ActionType.SignedByGovernor; Description=""; }
        action |> Action.FormatDescription "title"  |> should equal "title has been signed into law by the Governor."

    [<Test>]    
    let ``Vetoed by governor`` =
        let action = { defaultAction with Action.ActionType=ActionType.VetoedByGovernor; Description=""; Chamber=Chamber.House }
        action |> Action.FormatDescription "title"  |> should equal "title has been vetoed by the Governor. The Assembly now has the option to override that veto."

    [<Test>]    
    let ``Vetoed override in House`` =
        let action = { defaultAction with Action.ActionType=ActionType.VetoOverridden; Description="foo bar"; Chamber=Chamber.House }
        action |> Action.FormatDescription "title" |> should equal "The veto on title has been overridden in the House. The vote was: foo bar."

    [<Test>]    
    let ``Vetoed override in Senate`` =
        let action = { defaultAction with Action.ActionType=ActionType.VetoOverridden; Description="foo bar"; Chamber=Chamber.Senate }
        action |> Action.FormatDescription "title" |> should equal "The veto on title has been overridden in the Senate. The vote was: foo bar."

module ``Action type parsing`` =
    let date = System.DateTime(2017,1,20)

    [<Test>]
    let ``Parse signing by president of Senate`` =
        let actionText = "Signed by the President of the Senate"
        actionText |> Action.ParseType |> should equal ActionType.SignedByPresidentOfSenate

    [<Test>]
    let ``Parse signing by Governor`` =
        let actionText = "Signed by the Governor"
        actionText |> Action.ParseType |> should equal ActionType.SignedByGovernor

    [<Test>]
    let ``Parse veto by Governor`` =
        let actionText = "Vetoed by the Governor"
        actionText |> Action.ParseType |> should equal ActionType.VetoedByGovernor

    [<Test>]
    let ``Parse veto override in House`` =
        let actionText = "Veto overridden by the House; Roll Call 90: yeas 65, nays 29"
        actionText |> Action.ParseType |> should equal ActionType.VetoOverridden

    [<Test>]
    let ``Parse veto override in Senate`` =
        let actionText = "Veto overridden by the Senate; Roll Call 105: yeas 49, nays 1"
        actionText |> Action.ParseType |> should equal ActionType.VetoOverridden

module ``Action description parsing`` =
    
    [<Test>]
    let ``Parse signing by president of Senate`` =
        let actionText = "Signed by the President of the Senate"
        actionText |> Action.ParseDescription |> should equal ""

    [<Test>]
    let ``Parse signing by Governor`` =
        let actionText = "Signed by the Governor"
        actionText |> Action.ParseDescription |> should equal ""

    [<Test>]
    let ``Parse veto by Governor`` =
        let actionText = "Vetoed by the Governor"
        actionText |> Action.ParseDescription |> should equal ""

    [<Test>]
    let ``Parse veto override in House`` =
        let actionText = "Veto overridden by the House; Roll Call 90: yeas 65, nays 29"
        actionText |> Action.ParseDescription |> should equal "Roll Call 90: yeas 65, nays 29"

    [<Test>]
    let ``Parse veto override in Senate`` =
        let actionText = "Veto overridden by the Senate; Roll Call 105: yeas 49, nays 1"
        actionText |> Action.ParseDescription |> should equal "Roll Call 105: yeas 49, nays 1"

module ``Scheduled Action model tests`` = 
    let date = System.DateTime(2017,1,20)

    [<Test>]
    let ``Committee hearing is described properly`` =
        let action = { ActionType=ActionType.CommitteeReading; Start=""; End=""; Location="location"; Chamber=Chamber.House; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe false |> should equal "is scheduled for a committee hearing on 1/20/2017 in location"

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
        action.Describe false |> should equal "is scheduled for a committee hearing on 1/20/2017 from 9:30 AM - 12:30 PM in location"

    [<Test>]
    let ``'House Chamber' location formatted properly`` =
        let action = { ActionType=ActionType.CommitteeReading; Start=""; End=""; Location="House Chamber"; Chamber=Chamber.House; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe false |> should equal "is scheduled for a committee hearing on 1/20/2017 in the House Chamber"

    let ``'Senate Chamber' location formatted properly`` =
        let action = { ActionType=ActionType.CommitteeReading; Start=""; End=""; Location="Senate Chamber"; Chamber=Chamber.Senate; Date=date; Id=0; BillId=0; Link="link" }
        action.Describe false |> should equal "is scheduled for a committee hearing on 1/20/2017 in the Senate Chamber"
