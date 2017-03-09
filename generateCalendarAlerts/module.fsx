#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"

#load "../shared/db.fsx"
#load "../shared/alert.fsx"

namespace IgaTracker

module GenerateCalendarAlerts =

    open System
    open System.Data.SqlClient
    open System.Dynamic
    open System.Collections.Generic
    open Dapper
    open IgaTracker.Model
    open IgaTracker.Db
    open IgaTracker.Alert

    // Format a nice description of the action
    let formatBody sessionYear (bill:Bill) (scheduledAction:ScheduledAction) includeLink =
        let billName =
            match includeLink with
            | true ->  bill.WebLink sessionYear
            | false -> Bill.PrettyPrintName bill.Name
        sprintf "%s ('%s') %s." billName (bill.Title.TrimEnd('.')) (scheduledAction.Describe includeLink)

    // Create action alert messages for people that have opted-in to receiving them
    let generateAlerts id =
        let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
        let sessionYear = cn |> currentSessionYear
        let action = cn |> dapperParametrizedQuery<ScheduledAction> "SELECT * FROM ScheduledAction WHERE Id = @Id" {Id=id} |> Seq.head
        let bill = cn |> dapperParametrizedQuery<Bill> "SELECT * FROM Bill WHERE Id = @Id" {Id=action.BillId} |> Seq.head
        let emailBody = formatBody sessionYear bill action true
        let smsBody = formatBody sessionYear bill action false
        cn |> generateAlertsForBill bill (emailBody,smsBody)