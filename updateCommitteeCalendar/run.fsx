// Configure Database 

#r "System.Data"
#r "../packages/Dapper/lib/net45/Dapper.dll"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"

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

let toModel (bills:Bill seq) (billname,meeting) = 
    {ScheduledAction.Id = 0;
    ActionType = ActionType.CommitteeReading;
    Date = meeting?meetingDate.AsDateTime();
    Start = meeting?starttime.AsString();
    End = meeting?endtime.AsString();
    Location = meeting?location.AsString();
    Link = meeting?link.AsString();
    BillId = bills |> Seq.find (fun b -> b.Name = billname) |> (fun b -> b.Id);}

let generateScheduledActions (sessionYear, date, (bills:Bill seq), links) = 
    // fetch committees occurring after today
    fetchAll (sprintf "/%s/meetings?minDate=%s" sessionYear date)
    // Filter out the meetings if they don't have an agenda with bills or we've already recorded it (based on the meeting link)
    |> List.filter (fun meeting -> links |> Seq.exists (fun link -> link = meeting?link.AsString()) |> not)
    // Fetch the meeting metadata (agenda with bills)
    // Unroll all the meeting's bills into a collection of (meeting,bill) tuples
    |> List.map (fun meeting -> get (meeting?link.AsString()))
    |> List.collect (fun metadata -> 
        metadata?agenda.AsArray() 
        |> Array.map (fun a -> (a?bill.AsArray().[0]?billName.AsString(), metadata))
        |> Array.toList)
    // Filter out bills that we're not tracking
    |> List.filter (fun (billname,meeting) -> bills |> Seq.exists(fun b -> b.Name = billname))
    // Map each (meeting,bill) to a ScheduledAction model.
    |> List.map (fun (billname,meeting) -> toModel bills (billname,meeting))

let addToDatabase cn scheduledActions =
    scheduledActions 
    // Insert the scheduled actions into the database. Capture new new record ids
    |> List.map (fun scheduledAction -> cn |> dapperParametrizedQuery<int> InsertScheduledAction scheduledAction |> Seq.head)
    // Filter the ids based on the bills that users want alerts on
    |> (fun ids -> cn |> dapperMapParametrizedQuery<int> SelectScheduledActionsRequiringNotification (Map ["Ids", ids :> obj]))

#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
open Microsoft.Azure.WebJobs.Host

let Run(myTimer: TimerInfo, scheduledActions: ICollector<string>, log: TraceWriter) =
    log.Info(sprintf "F# function executed at: %s" (DateTime.Now.ToString()))

    let cn = new SqlConnection(System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString"))
    let sessionYear = (System.Environment.GetEnvironmentVariable("SessionYear"))
    let date = DateTime.Now.AddDays(1.0).ToString("yyyy-MM-dd")
    
    let bills = cn |> dapperQuery<Bill> "SELECT Id,Name from Bill"
    let links = cn |> dapperParametrizedQuery<string> "SELECT Link from ScheduledAction WHERE Date = @Date" {DateSelectArgs.Date=date}

    log.Info(sprintf "[%s] Fetch committee meetings from API ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
    let scheduledActionModels = (sessionYear, date, bills, links) |> generateScheduledActions 
    log.Info(sprintf "[%s] Fetch committee meetings from API [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")) )

    log.Info(sprintf "[%s] Add scheduled actions to database ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
    let scheduledActionIdsRequringAlert = scheduledActionModels |> addToDatabase cn
    log.Info(sprintf "[%s] Add scheduled actions to database [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))

    log.Info(sprintf "[%s] Enqueue alerts for scheduled actions ..." (DateTime.Now.ToString("HH:mm:ss.fff")))
    scheduledActionIdsRequringAlert |> Seq.iter (fun id -> 
        log.Info(sprintf "[%s]  Enqueuing scheduled action %d" (DateTime.Now.ToString("HH:mm:ss.fff")) id)
        scheduledActions.Add(id.ToString()))
    log.Info(sprintf "[%s] Enqueue alerts for scheduled actions [OK]" (DateTime.Now.ToString("HH:mm:ss.fff")))    