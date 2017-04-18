#load "../shared/logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"

#load "../shared/queries.fs"
#load "../shared/db.fsx"
#load "../shared/csv.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open IgaTracker.Model
open IgaTracker.Queries
open IgaTracker.Db
open IgaTracker.Csv
open IgaTracker.Logging
open Newtonsoft.Json
open Microsoft.Azure.WebJobs.Host

[<CLIMutable>]
type DigestAction = {
    SessionName:string;
    BillName:string;
    Name:string;
    Title:string;
    BillChamber:Chamber;
    ActionChamber:Chamber;
    ActionType:ActionType;
    Description:string;
}

[<CLIMutable>]
type DigestScheduledAction = {
    SessionName:string;
    BillName:string;
    Title:string;
    BillChamber:Chamber;
    ActionChamber:Chamber;
    ActionType:ActionType;
    Date:DateTime;
    Start:string;
    End:string;
    Location:string;
}

let printSectionTitle actionType = 
    match actionType with 
    | ActionType.CommitteeReading -> "Committee Hearings"
    | ActionType.SecondReading -> "Second Readings"
    | ActionType.ThirdReading -> "Third Readings"
    | ActionType.SignedByPresidentOfSenate -> "Bills Sent to Governor"
    | ActionType.SignedByGovernor -> "Bills Signed by the Governor"
    | ActionType.VetoedByGovernor -> "Bills Vetoed by the Governor"
    | ActionType.VetoOverridden -> "Vetoes Overridden"
    | _ -> ""

// ACTIONS
let listAction (a:DigestAction) = 
    sprintf "* [%s](https://iga.in.gov/legislative/%s/bills/%s/%s) ('%s'): %s" (Bill.PrettyPrintName a.BillName) a.SessionName (a.BillChamber.ToString().ToLower()) (Bill.ParseNumber a.BillName) a.Title a.Description

let listActions (actions:DigestAction seq) =
    actions
    |> Seq.sortBy (fun a -> a.BillName)
    |> Seq.map listAction
    |> String.concat "\n"

let describeActions chamber actionType (actions:DigestAction seq) = 
    let sectionTitle = sprintf "###%s  " (printSectionTitle actionType)
    let section = 
        actions 
        |> Seq.filter (fun a -> a.ActionChamber = chamber && a.ActionType = actionType) 
        |> listActions

    match section with
    | EmptySeq -> []
    | _ -> [sectionTitle; section]

let describeActionsForChamber chamber (actions:DigestAction seq) = 
    let header = sprintf "##Today's %A Activity  " chamber
    match actions with
    | EmptySeq -> 
        [header] @ ["(None)"]
    | _ ->
        [header] 
        @ (actions |> describeActions chamber ActionType.CommitteeReading)
        @ (actions |> describeActions chamber ActionType.SecondReading)
        @ (actions |> describeActions chamber ActionType.ThirdReading)
        @ (actions |> describeActions chamber ActionType.SignedByPresidentOfSenate)
        @ (actions |> describeActions chamber ActionType.SignedByGovernor)
        @ (actions |> describeActions chamber ActionType.VetoedByGovernor)
        @ (actions |> describeActions chamber ActionType.VetoOverridden)

// SCHEDULED ACTIONS
let listScheduledAction sa =
    let item = sprintf "* [%s](https://iga.in.gov/legislative/%s/bills/%s/%s) ('%s'); %s ([map](https://iga.in.gov/information/location_maps))" (Bill.PrettyPrintName sa.BillName) sa.SessionName (sa.BillChamber.ToString().ToLower()) (Bill.ParseNumber sa.BillName) sa.Title sa.Location
    match sa.Start with
    | "" -> item
    | timed -> sprintf "%s, %s-%s" item (DateTime.Parse(sa.Start).ToString("t")) (DateTime.Parse(sa.End).ToString("t"))
    
let listScheduledActions (scheduledActions:DigestScheduledAction seq) =
    scheduledActions 
    |> Seq.sortBy (fun action -> action.BillName)
    |> Seq.map listScheduledAction
    |> String.concat "\n"

let describeScheduledActions actionType (actions:DigestScheduledAction seq) = 
    let actionsOfType = actions |> Seq.filter (fun action -> action.ActionType = actionType)
    match actionsOfType with
    | EmptySeq -> []
    | _ ->
        let sectionTitle = sprintf "###%s  " (printSectionTitle actionType)
        let section = actionsOfType |> listScheduledActions
        [sectionTitle; section]

let describeScheduledActionsForDay (date:DateTime,scheduledActions) = 
    let header = sprintf "##New Events for %s  " (date.ToString("D"))
    let committeReadings = scheduledActions |> describeScheduledActions ActionType.CommitteeReading
    let secondReadings = scheduledActions |> describeScheduledActions ActionType.SecondReading
    let thirdReadings = scheduledActions |> describeScheduledActions ActionType.ThirdReading
    [header] @ committeReadings @ secondReadings @ thirdReadings

let generateDigestMessage digestUser (salutation,actions,scheduledActions) filename =
    let houseActions = actions |> describeActionsForChamber Chamber.House
    let senateActions = actions |> describeActionsForChamber Chamber.Senate
    let upcomingActions = 
        scheduledActions 
        |> Seq.groupBy (fun scheduledAction -> scheduledAction.Date)
        |> Seq.sortBy (fun (date,scheduledActions) -> date)
        |> Seq.collect describeScheduledActionsForDay
        |> Seq.toList
    let body = [salutation] @ houseActions @ senateActions @ upcomingActions |> String.concat "\n\n"
    let subject = sprintf "Legislative Update for %s" (DateTime.Now.ToString("D")) 
    {Message.Recipient=digestUser.Email; Subject = subject; Body=body; MessageType=MessageType.Email; Attachment=filename}

let generateDigestMessageForAllBills (digestUser) filename cn =
    let salutation = "Hello! Here are the day's legislative activity and upcoming schedules for all bills in this legislative session."
    let actions = cn |> dapperMapParametrizedQuery<DigestAction> FetchAllActions (Map["Today", (datestamp()):>obj])
    let scheduledActions = cn |> dapperMapParametrizedQuery<DigestScheduledAction> FetchAllScheduledActions (Map["Today", (datestamp()):>obj])
    generateDigestMessage digestUser (salutation,actions,scheduledActions) filename

let generateDigestMessageForBills (digestUser:User) filename billIds cn = 
    let salutation = "Hello! Here are the day's legislative activity and upcoming schedules for the bills you are following in this legislative session."
    let actions = cn |> dapperMapParametrizedQuery<DigestAction> FetchActionsForBills (Map["Today", (datestamp()):>obj; "Ids", billIds:>obj])
    let scheduledActions = cn |> dapperParametrizedQuery<DigestScheduledAction> FetchScheduledActionsForBills (Map["Today", (datestamp()):>obj; "Ids", billIds:>obj])
    generateDigestMessage digestUser (salutation,actions,scheduledActions) filename

let generateSpreadsheetForBills (digestUser:User) storageConnStr billIds cn = 
    let userBillsSpreadsheetFilename = generateUserBillsSpreadsheetFilename digestUser.Id
    cn
    |> dapperMapParametrizedQuery<BillStatus> FetchBillStatusForBills (Map["Ids", billIds:>obj])
    |> postSpreadsheet storageConnStr userBillsSpreadsheetFilename
    userBillsSpreadsheetFilename

#r "../packages/Microsoft.Azure.WebJobs.Core/lib/net45/Microsoft.Azure.WebJobs.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host

let Run(user: string, notifications: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed for '%s' at %s" user (timestamp()))
    try
        let digestUser = JsonConvert.DeserializeObject<User>(user)
        // let digestUser = {User.Id=1;Name="John HOerr";Email="jhoerr@gmail.com";Mobile=null;DigestType=DigestType.MyBills}
        let trace = sprintf "[%s] Generating %A digest for %s ..." (timestamp()) digestUser.DigestType digestUser.Email
        trace |> trackTrace "generateDigest"
        trace |> log.Info

        let storageConnStr = System.Environment.GetEnvironmentVariable("AzureStorage.ConnectionString")
        let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
        let billIds = cn |> dapperMapParametrizedQuery<int> "SELECT BillId from UserBill WHERE UserId = @UserId" (Map["UserId", digestUser.Id:>obj])
        
        match digestUser.DigestType with 
        // nop: user has opted for a digest of 'my bills' but has not flagged any bills for tracking
        | DigestType.MyBills when Seq.isEmpty billIds -> printfn "User has not selected any bills "
        //  user has opted for a digest of 'my bills'
        | DigestType.MyBills -> 
            // generate a spreadsheet for the user and upload it to azure. save the filename.
            let filename = cn |> generateSpreadsheetForBills (digestUser) storageConnStr billIds
            // generate digest email message with attachment filename and queue for delivery
            cn |> generateDigestMessageForBills (digestUser) filename billIds |> JsonConvert.SerializeObject |> notifications.Add
        | DigestType.AllBills -> 
            // resolve the name of the pre-existing 'all bills' spreadsheet
            let filename = generateAllBillsSpreadsheetFilename()
            // generate digest email message with attachment filename and queue for delivery
            cn |> generateDigestMessageForAllBills (digestUser) filename |> JsonConvert.SerializeObject |> notifications.Add
        | _ -> raise (ArgumentException("Unrecognized digest type"))

        log.Info(sprintf "[%s] Generating %A digest for %s [OK]" (timestamp()) digestUser.DigestType digestUser.Email)
    with
    | ex -> 
        ex |> trackException "generateDigest"
        log.Error(sprintf "Encountered error: %s" (ex.ToString())) 
        reraise()