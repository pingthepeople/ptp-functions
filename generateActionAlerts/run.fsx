// Configure Database 

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

#load "../shared/model.fs"
#load "../shared/queries.fs"
#load "../shared/http.fsx"
#load "../shared/db.fsx"

open System
open System.Data.SqlClient
open System.Dynamic
open System.Collections.Generic
open Dapper
open FSharp.Data
open FSharp.Data.JsonExtensions
open IgaTracker.Model
open IgaTracker.Queries
open IgaTracker.Http
open IgaTracker.Db
open Newtonsoft.Json

// Find the user for whom we're generating a particular alert.
let locateUserToAlert users userBill =
    users |> Seq.find (fun u -> u.Id = userBill.UserId)

// Format a nice description of the action
let prettyPrint actionType chamber =
    match actionType with
    | ActionType.AssignedToCommittee -> sprintf "was assigned to the %A" chamber
    | ActionType.CommitteeReading -> sprintf "was read in committee in the %A. The vote was:" chamber
    | ActionType.SecondReading -> sprintf "had a second reading in the %A. The vote was:" chamber
    | ActionType.ThirdReading -> sprintf "had a third reading in the %A. The vote was:" chamber
    | _ -> "(some other event type?)"

// Format a nice message body
let body (bill:Bill) (action:Action) =
    sprintf "%s ('%s') %s %s. (@ %s)" bill.Name (bill.Title.TrimEnd('.')) (prettyPrint action.ActionType action.Chamber) action.Description (action.Date.ToString())
// Format a nice message subject
let subject (bill:Bill) =
    sprintf "Update on %s" bill.Name

// Generate email message models
let generateEmailMessages (bill:Bill) action users userBills =
    userBills 
    |> Seq.map (fun ub -> 
        locateUserToAlert users ub 
        |> (fun u -> {MessageType=MessageType.Email; Recipient=u.Email; Subject=(subject bill); Body=(body bill action)}))
// Generate SMS message models
let generateSmsMessages (bill:Bill) action users userBills = 
    userBills 
    |> Seq.map (fun ub -> 
        locateUserToAlert users ub 
        |> (fun u -> {MessageType=MessageType.SMS; Recipient=u.Mobile; Subject=(subject bill); Body=(body bill action)}))

// Fetch user/bill/action/ records from database to support message generation
let fetchUserBills (cn:SqlConnection) id =
    cn.Open()
    let action = cn |> dapperMapParametrizedQuery<Action> "SELECT * FROM Action WHERE Id = @Id" (Map["Id", id :> obj] ) |> Seq.head
    let bill = cn |> dapperMapParametrizedQuery<Bill> "SELECT * FROM Bill WHERE Id = @Id" (Map["Id", action.BillId :> obj] ) |> Seq.head
    let userBills = cn |> dapperMapParametrizedQuery<UserBill> "SELECT * FROM UserBill WHERE BillId = @Id" (Map["Id", action.BillId :> obj] )
    let userIds = userBills |> Seq.map (fun ub -> ub.UserId)
    let users = cn |> dapperMapParametrizedQuery<User> "SELECT * FROM [User] WHERE Id IN @Ids" (Map["Ids", userIds :> obj] )
    cn.Close()
    (bill, action, users, userBills)

// Create action alert messages for people that have opted-in to receiving them
let generateAlerts (cn:SqlConnection) id =
    let (bill, action, users, userBills) = id |> fetchUserBills cn
    let emailMessages = userBills |> Seq.filter(fun ub -> ub.ReceiveAlertEmail) |> generateEmailMessages bill action users
    let smsMessages = userBills |> Seq.filter(fun ub -> ub.ReceiveAlertSms) |>  generateSmsMessages bill action users
    (emailMessages, smsMessages)


#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"

open Microsoft.Azure.WebJobs.Host

let Run(actionId: string, notifications: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function 'generateActionAlerts' executed for action %s at %s" actionId (DateTime.Now.ToString()))
    let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))

    log.Info(sprintf "[%s] Generating action alerts ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
    let (emailMessages, smsMessages) = (Int32.Parse(actionId)) |> generateAlerts cn
    log.Info(sprintf "[%s] Generating action alerts [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))

    log.Info(sprintf "[%s] Enqueueing action alerts ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
    emailMessages |> Seq.iter (fun m -> 
        log.Info(sprintf "[%s]   Enqueuing email action alert to '%s' re: '%s'" (DateTime.Now.ToString("HH:mm:ss.fff")) m.Recipient m.Subject )
        notifications.Add(JsonConvert.SerializeObject(m)))
    smsMessages |> Seq.iter (fun m -> 
        log.Info(sprintf "[%s]   Enqueuing SMS action alert to '%s' re: '%s'" (DateTime.Now.ToString("HH:mm:ss.fff")) m.Recipient m.Subject)
        notifications.Add(JsonConvert.SerializeObject(m)))
    log.Info(sprintf "[%s] Enqueueing action alerts [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))
    