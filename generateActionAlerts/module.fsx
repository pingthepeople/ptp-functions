
#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/db.fsx"

namespace IgaTracker 

module GenerateActionAlerts =

    open System
    open System.Data.SqlClient
    open System.Dynamic
    open System.Collections.Generic
    open Dapper
    open IgaTracker.Model
    open IgaTracker.Queries
    open IgaTracker.Db

    let locateUserToAlert (users:User seq) userBill =
        users |> Seq.find (fun u -> u.Id = userBill.UserId)

    // Format a nice description of the action
    let body sessionYear (bill:Bill) (action:Action) includeLinks =
        let billName =
            match includeLinks with 
            | true -> bill.WebLink sessionYear
            | false -> Bill.PrettyPrintName bill.Name
        sprintf "%s ('%s') %s." billName (bill.Title.TrimEnd('.')) (action.Describe)

    // Format a nice message subject
    let subject (bill:Bill) =
        sprintf "Update on %s" (Bill.PrettyPrintName bill.Name)

    // Generate email message models
    let generateEmailMessages sessionYear (bill:Bill) action users userBills =
        userBills 
        |> Seq.map (fun ub -> 
            locateUserToAlert users ub 
            |> (fun u -> {MessageType=MessageType.Email; Recipient=u.Email; Subject=(subject bill); Body=(body sessionYear bill action true); Attachment=""}))
    // Generate SMS message models
    let generateSmsMessages sessionYear (bill:Bill) action users userBills = 
        userBills 
        |> Seq.map (fun ub -> 
            locateUserToAlert users ub 
            |> (fun u -> {MessageType=MessageType.SMS; Recipient=u.Mobile; Subject=(subject bill); Body=(body sessionYear bill action false); Attachment=""}))

    let generateAlertsForUserBills sessionYear (bill, action, users, userBills) =
        let emailMessages = userBills |> Seq.filter(fun ub -> ub.ReceiveAlertEmail) |> generateEmailMessages sessionYear bill action users
        let smsMessages = userBills |> Seq.filter(fun ub -> ub.ReceiveAlertSms) |>  generateSmsMessages sessionYear bill action users
        (emailMessages, smsMessages)
        
    // Fetch user/bill/action/ records from database to support message generation
    let fetchUserBills (cn:SqlConnection) id =
        cn.Open()
        let action = cn |> dapperParametrizedQuery<Action> "SELECT * FROM Action WHERE Id = @Id" {Id=id} |> Seq.head
        let bill = cn |> dapperParametrizedQuery<Bill> "SELECT * FROM Bill WHERE Id = @Id" {Id=action.BillId} |> Seq.head
        let userBills = cn |> dapperParametrizedQuery<UserBill> "SELECT * FROM UserBill WHERE BillId = @Id" {Id=action.BillId}
        let userIds = userBills |> Seq.map (fun ub -> ub.UserId)
        let users = cn |> dapperMapParametrizedQuery<User> "SELECT * FROM [Users] WHERE Id IN @Ids" (Map["Ids",userIds:>obj])
        cn.Close()
        (bill, action, users, userBills)

    // Create action alert messages for people that have opted-in to receiving them
    let generateAlerts id =
        let sessionYear = Environment.GetEnvironmentVariable("SessionYear")
        let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
        id |> fetchUserBills cn |> generateAlertsForUserBills sessionYear      
