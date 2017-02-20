// Configure Database 

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

let locateUserToAlert users userBill =
    users |> Seq.find (fun u -> u.Id = userBill.UserId)

// Format a nice message body
let describe (bill:Bill) (action:Action) =
    sprintf "%s|%A|%s @ %s" bill.Name action.ActionType action.Description (action.Date.ToString())  
let generateEmailMessages (bill:Bill) action users userBills =
    userBills 
    |> Seq.map (fun ub -> 
        locateUserToAlert users ub 
        |> (fun u -> {MessageType=MessageType.Email; Recipient=u.Email; Subject=bill.Name; Body=(describe bill action)}))
let generateSmsMessages (bill:Bill) action users userBills = 
    userBills 
    |> Seq.map (fun ub -> 
        locateUserToAlert users ub 
        |> (fun u -> {MessageType=MessageType.SMS; Recipient=u.Mobile; Subject=bill.Name; Body=action.Description}))

let fetchUserBills (cn:SqlConnection) id =
    cn.Open()
    let action = cn |> dapperMapParametrizedQuery<Action> "SELECT * FROM Action WHERE Id = @Id" (Map["Id", id :> obj] ) |> Seq.head
    let bill = cn |> dapperMapParametrizedQuery<Bill> "SELECT * FROM Bill WHERE Id = @Id" (Map["Id", action.BillId :> obj] ) |> Seq.head
    let userBills = cn |> dapperMapParametrizedQuery<UserBill> "SELECT * FROM UserBill WHERE BillId = @Id" (Map["Id", action.BillId :> obj] )
    let userIds = userBills |> Seq.map (fun ub -> ub.UserId.ToString()) |> String.concat ","
    let users = cn |> dapperMapParametrizedQuery<User> "SELECT * FROM [User] WHERE Id IN (@Ids)" (Map["Ids", userIds :> obj] )
    cn.Close()
    (bill, action, users, userBills)

let generateAlerts (cn:SqlConnection) id =
    let (bill, action, users, userBills) = id |> fetchUserBills cn
    let emailMessages = userBills |> Seq.filter(fun ub -> ub.ReceiveAlertEmail) |> generateEmailMessages bill action users
    let smsMessages = userBills |> Seq.filter(fun ub -> ub.ReceiveAlertSms) |>  generateSmsMessages bill action users
    (emailMessages, smsMessages)

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#r "../packages/WindowsAzure.ServiceBus/lib/net45-full/Microsoft.ServiceBus.dll"

open Microsoft.Azure.WebJobs.Host
open Microsoft.ServiceBus.Messaging

let Run(actionId: string, notifications: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function 'generateActionAlerts' executed for action %s at %s" actionId (DateTime.Now.ToString()))
    let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
    let (emailMessages, smsMessages) = (Int32.Parse(actionId)) |> generateAlerts cn
    emailMessages |> List.iter (fun m -> log.Info(sprintf "Enqueuing email to '%s' re: '%s'" m.Recipient m.Subject))
    emailMessages |> List.iter (fun m -> notifications.Add(JsonConvert.SerializeObject(m)))
    smsMessages |> List.iter (fun m -> log.Info(sprintf "Enqueuing SMS to '%s' re: '%s'" m.Recipient m.Subject))
    smsMessages |> List.iter (fun m -> notifications.Add(JsonConvert.SerializeObject(m)))