#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Primitives"
#r "System.Net.Http.Formatting"
#r "System.Web.Http"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/StrongGrid/lib/net452/StrongGrid.dll"
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"

open FSharp.Data
open Microsoft.Azure.WebJobs.Host
open StrongGrid
open System
open System.IO
open System.Text
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks

let get endpoint = 
    let standardHeaders = [ "Accept", "application/json"; "Authorization", "Token " + Environment.GetEnvironmentVariable("IgaApiKey") ]
    Http.RequestString("https://api.iga.in.gov/" + endpoint, httpMethod = "GET", headers = standardHeaders) |> JsonValue.Parse |>  (fun s -> s.ToString())

type Bills = JsonProvider<"../sample/bills.json", ResolutionFolder=__SOURCE_DIRECTORY__>
type Bill = JsonProvider<"../sample/bill.json", ResolutionFolder=__SOURCE_DIRECTORY__>
type Actions = JsonProvider<"../sample/actions.json", ResolutionFolder=__SOURCE_DIRECTORY__>
type Calendars = JsonProvider<"../sample/calendars.json", ResolutionFolder=__SOURCE_DIRECTORY__>
type Calendar = JsonProvider<"../sample/calendar.json", ResolutionFolder=__SOURCE_DIRECTORY__>

let firstReadingConst = "First reading: referred to Committee on "
let committeeReportConst = "Committee report: "
let secondReadingConst  = "Second reading: "
let thirdReadingConst  = "Third reading: "


let clean (str:string) = String.Format("\"{0}\"", str.Replace('“', '\"').Replace('”', '\"').Replace("\"", "\"\""))
let authors (bill:Bill.Root) = bill.LatestVersion.Authors |> Array.toList |> List.map (fun a -> a.LastName) |> List.sort |> String.concat ", "
let topics (bill:Bill.Root) = bill.LatestVersion.Subjects |> Array.toList |> List.map (fun a -> a.Entry) |> String.concat ", "

let getDate (actions:Actions.Root) (chamber:string) (event:string) = 
    let action = actions.Items |> Array.tryFind (fun a -> a.Chamber.Name.Equals(chamber) && a.Description.StartsWith(event))
    match action with
    | None -> ""
    | Some(a) -> a.Date.ToShortDateString()

let reconcile (calendarDate:DateTime) (actionDate:string) =
    let resolvedActionDate = 
        match actionDate with
        | "" -> DateTime.MinValue
        | x -> DateTime.Parse(x)
    
    let resolvedDate = if calendarDate > resolvedActionDate then calendarDate else resolvedActionDate
    if resolvedDate = DateTime.MinValue then "" else resolvedDate.ToShortDateString()

let getResult (actions:Actions.Root) (chamber:string) (event:string) = 
    let action = actions.Items |> Array.tryFind (fun a -> a.Chamber.Name.Equals(chamber) && a.Description.StartsWith(event))
    match action with
    | None -> ""
    | Some(a) -> a.Description.Replace(event,"")

let getOriginCommittee (actions:Actions.Root) =
    let action = actions.Items |> Array.tryFind (fun a -> a.Description.StartsWith(firstReadingConst))
    match action with
    | None -> ""
    | Some(a) -> a.Description.Replace(firstReadingConst,"")

let fetchBill (billName:string) (houseCalendar:Calendar.Root) (senateCalendar:Calendar.Root) (outputPath:string)=
    async {
        let billTask = Task.Run((fun () -> get ("2017/bills/" + billName) |> Bill.Parse))
        let actionsTask = Task.Factory.StartNew<Actions.Root>((fun () -> get ("2017/bills/" + billName + "/actions") |> Actions.Parse),  TaskCreationOptions.LongRunning)
        Async.AwaitTask (Task.WhenAll(billTask, actionsTask)) |> ignore
        let bill = billTask.Result
        let actions = actionsTask.Result

        let originChamber = if bill.OriginChamber.Equals("house") then "House" else "Senate"
        let crossoverChamber = if originChamber.Equals("House") then "Senate" else "House"

        let originCalendar = if originChamber.Equals("House") then houseCalendar else senateCalendar
        let crossoverCalendar = if originChamber.Equals("House") then senateCalendar else houseCalendar

        let origin2ndReadingCalendar = if originChamber.Equals("House") then houseCalendar.Hb2head.Bills else senateCalendar.Sb2head.Bills
        let origin3rdReadingCalendar = if originChamber.Equals("House") then houseCalendar.Hb3head.Bills else senateCalendar.Sb3head.Bills
        let crossover2ndReadingCalendar = if originChamber.Equals("House") then houseCalendar.Sb2head.Bills else senateCalendar.Hb2head.Bills
        let crossover3rdReadingCalendar = if originChamber.Equals("House") then houseCalendar.Sb2head.Bills else senateCalendar.Hb3head.Bills

        let origin2ndReadingDate = if origin2ndReadingCalendar |> Array.exists (fun b -> b.BillName.StartsWith(bill.BillName)) then originCalendar.Date else DateTime.MinValue
        let origin3rdReadingDate = if origin3rdReadingCalendar |> Array.exists (fun b -> b.BillName.StartsWith(bill.BillName)) then originCalendar.Date else DateTime.MinValue
        let crossover2ndReadingDate = if crossover2ndReadingCalendar |> Array.exists (fun b -> b.BillName.StartsWith(bill.BillName)) then crossoverCalendar.Date else DateTime.MinValue
        let crossover3rdReadingDate = if crossover3rdReadingCalendar |> Array.exists (fun b -> b.BillName.StartsWith(bill.BillName)) then crossoverCalendar.Date else DateTime.MinValue

        let fields = [
            clean bill.BillName;
            clean bill.LatestVersion.ShortDescription;
            clean bill.LatestVersion.Digest;
            clean (authors bill);
            clean (getOriginCommittee actions);
            clean (topics bill);
            clean (getDate actions originChamber committeeReportConst);
            clean (getResult actions originChamber committeeReportConst);
            clean (reconcile origin2ndReadingDate (getDate actions originChamber secondReadingConst));
            clean (getResult actions originChamber secondReadingConst);
            clean (reconcile origin3rdReadingDate (getDate actions originChamber thirdReadingConst));
            clean (getResult actions originChamber thirdReadingConst);
            clean (getDate actions crossoverChamber committeeReportConst);
            clean (getResult actions crossoverChamber committeeReportConst);
            clean (reconcile crossover2ndReadingDate (getDate actions crossoverChamber secondReadingConst));
            clean (getResult actions crossoverChamber secondReadingConst);
            clean (reconcile crossover3rdReadingDate (getDate actions crossoverChamber thirdReadingConst));
            clean (getResult actions crossoverChamber thirdReadingConst);]
        let text = fields |> String.concat ","
        File.AppendAllText(outputPath, text + "\n")
    }

let buildSpreadsheet (houseCalendar:Calendar.Root) (senateCalendar:Calendar.Root) (outputPath:string) = 
    let mutable page = 1
    let mutable pageCount = 100 
    let mutable query = "2017/bills?type=BILL"
    File.WriteAllText(outputPath, "Bill,Title,Description,Authors,Original Committee,Topics,Origin Committee Reading,Vote,Second Reading,Vote,Third Reading,Vote,Crossover Committee Reading,Vote,Second Reading,Vote,Third Reading,Vote\n")
    while page <= pageCount do
        let bills = get query |> Bills.Parse
        pageCount <- bills.PageCount
        page <- page + 1
        query <- bills.NextLink
        bills.Items |> Array.iter (fun b -> Async.RunSynchronously(fetchBill b.BillName houseCalendar senateCalendar outputPath))

let outputPath = Path.Combine(Path.GetTempPath(), (sprintf "iga_%s" (DateTime.Now.ToString("yyyy-MM-dd"))))
let allCalendars = get "2017/calendars" |> Calendars.Parse
let houseCalendar = allCalendars.Items |> Array.find (fun c -> c.Edition.Equals("First") && c.Chamber.Equals("house")) |> (fun c -> (get c.Link) |> Calendar.Parse)
let senateCalendar = allCalendars.Items |> Array.find (fun c -> c.Edition.Equals("First") && c.Chamber.Equals("senate")) |> (fun c -> (get c.Link) |> Calendar.Parse)
buildSpreadsheet houseCalendar senateCalendar outputPath