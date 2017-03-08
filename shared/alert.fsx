#load "../shared/model.fs"
#load "../shared/db.fsx"

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"

namespace IgaTracker 

module Alert =

    open System
    open System.Data.SqlClient
    open System.Dynamic
    open System.Collections.Generic
    open Dapper
    open IgaTracker.Model
    open IgaTracker.Db

    // Find the user for whom we're generating a particular alert.
    let locateUserToAlert (users:User seq) userBill =
        users |> Seq.find (fun u -> u.Id = userBill.UserId)

    // Format a nice message subject
    let formatSubject (bill:Bill) =
        sprintf "Update on %s" (Bill.PrettyPrintName bill.Name)

    // Generate email message models
    let generateEmailMessages (bill:Bill) body users userBills =
        userBills 
        |> Seq.map (fun ub -> 
            locateUserToAlert users ub 
            |> (fun u -> {MessageType=MessageType.Email; Recipient=u.Email; Subject=(formatSubject bill); Body=body; Attachment=""}))
   
    // Generate SMS message models
    let generateSmsMessages (bill:Bill) body users userBills = 
        userBills 
        |> Seq.map (fun ub -> 
            locateUserToAlert users ub 
            |> (fun u -> {MessageType=MessageType.SMS; Recipient=u.Mobile; Subject=(formatSubject bill); Body=body; Attachment=""}))

    let generateUserAlerts (bill,users,userBills) (emailBody,smsBody) =
        let emailMessages = userBills |> Seq.filter(fun ub -> ub.ReceiveAlertEmail) |> generateEmailMessages bill emailBody users
        let smsMessages = userBills |> Seq.filter(fun ub -> ub.ReceiveAlertSms) |>  generateSmsMessages bill smsBody users
        (emailMessages, smsMessages)

    let generateAlertsForBill (bill:Bill) (emailBody,smsBody) cn =
        let userBills = cn |> dapperParametrizedQuery<UserBill> "SELECT * FROM UserBill WHERE BillId = @Id" {Id=bill.Id}
        let userIds = userBills |> Seq.map (fun ub -> ub.UserId)
        let users = cn |> dapperMapParametrizedQuery<User> "SELECT * FROM [Users] WHERE Id IN @Ids" (Map["Ids",userIds:>obj])
        generateUserAlerts (bill,users,userBills) (emailBody,smsBody)
