#load "../generateActionAlerts/run.fsx"
#r "../packages/Nunit/lib/net45/nunit.framework.dll"
#r "../packages/FsUnit/lib/net45/FsUnit.NUnit.dll"

open NUnit.Framework
open FsUnit
open IgaTracker.Model

let bill = {Bill.Id=1; Name="HB1001"; Title="HB 1001 title"; Chamber=Chamber.House; Description="desc"; Authors="auth"; Link="link"; SessionId=0;}
let action = {Action.Id=1; BillId=1; ActionType=ActionType.CommitteeReading; Description="passed"; Chamber=Chamber.House; Date=System.DateTime(2017,1,20); Link="link"}
let users = 
    [{User.Id=1; Name="johnny"; Email="johnny@gmail.com"; Mobile="+11112223333"; DigestType=DigestType.MyBills };
    {User.Id=2; Name="susie"; Email="jimmy@gmail.com"; Mobile="+12223334444"; DigestType=DigestType.MyBills };]

module ``Given a user that wants to receive both email and SMS alerts`` =
    let userBills = [ {UserBill.Id = 0; UserId=1; BillId=1; ReceiveAlertEmail=true; ReceiveAlertSms=true}]
    
    let (actualEmails, actualSMSes) = generateAlertsForUserBills "2017" (bill, action, users, userBills)

    [<Test>]
    let ``It generates a single email`` = 
        actualEmails |> Seq.length |> should equal 1

    [<Test>]
    let ``Email properties are correct`` = 
        let actual = actualEmails |> Seq.head
        actual.MessageType |> should equal MessageType.Email
        actual.Recipient |> should equal "johnny@gmail.com"
        actual.Subject |> should equal "Update on HB 1001"
        actual.Body |> should equal "[HB 1001](https://iga.in.gov/legislative/2017/bills/house/1001) ('HB 1001 title') was read in committee in the House. The vote was: passed."
        actual.Attachment |> should equal ""
    
    [<Test>]
    let ``It generates a single SMS`` = 
        actualSMSes |> Seq.length |> should equal 1

    [<Test>]
    let ``SMS properties are correct`` = 
        let actual = actualSMSes |> Seq.head
        actual.MessageType |> should equal MessageType.SMS
        actual.Recipient |> should equal "+11112223333"
        actual.Subject |> should equal "Update on HB 1001"
        actual.Body |> should equal "HB 1001 ('HB 1001 title') was read in committee in the House. The vote was: passed."
        actual.Attachment |> should equal ""

module ``Given a user that only wants to receive email alerts`` =

    let userBills = [ {UserId=1; BillId=1; ReceiveAlertEmail=true; ReceiveAlertSms=false; UserBill.Id = 0; }]
    
    let (actualEmails, actualSMSes) = generateAlertsForUserBills "2017" (bill, action, users, userBills)

    [<Test>]
    let ``It generates a single email`` = 
        actualEmails |> Seq.length |> should equal 1

    [<Test>]
    let ``It does not generate an SMS`` = 
        actualSMSes |> Seq.length |> should equal 0

module ``Given a user that only wants to receive SMS alerts`` =
    let userBills = [ {UserId=1; BillId=1; ReceiveAlertEmail=false; ReceiveAlertSms=true; UserBill.Id = 0; }]
    
    let (actualEmails, actualSMSes) = generateAlertsForUserBills "2017" (bill, action, users, userBills)

    [<Test>]
    let ``It does not generate an email`` = 
        actualEmails |> Seq.length |> should equal 0

    [<Test>]
    let ``It does generate an SMS`` = 
        actualSMSes |> Seq.length |> should equal 1

module ``Given two users that wish to receive both email and SMS alerts`` =
    let userBills = [ 
        {UserId=1; BillId=1; ReceiveAlertEmail=true; ReceiveAlertSms=true; UserBill.Id = 0; }
        {UserId=2; BillId=1; ReceiveAlertEmail=true; ReceiveAlertSms=true; UserBill.Id = 0; }]
    
    let (actualEmails, actualSMSes) = generateAlertsForUserBills "2017" (bill, action, users, userBills)

    [<Test>]
    let ``It generates two emails`` = 
        actualEmails |> Seq.length |> should equal 2

    [<Test>]
    let ``It generates two SMSes`` = 
        actualSMSes |> Seq.length |> should equal 2
