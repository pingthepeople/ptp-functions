#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Primitives"
#r "System.Net.Http.Formatting"
#r "System.Web.Http"
#r "Microsoft.WindowsAzure.Storage"
#r "../packages/FSharp.Data/lib/portable-net45+sl50+netcore45/FSharp.Data.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Formatting.Common.dll"
#r "../packages/FSharp.Formatting/lib/net40/FSharp.Markdown.dll"
#r "../packages/StrongGrid/lib/net452/StrongGrid.dll"

#if !COMPILED
#r "../packages/Microsoft.Azure.WebJobs/lib/net45/Microsoft.Azure.WebJobs.Host.dll"
#endif

open System
open System.IO
open System.Text
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open FSharp.Data
open FSharp.Formatting.Common
open FSharp.Markdown
open Microsoft.Azure.WebJobs.Host
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Blob
open StrongGrid

let get endpoint = 
    let standardHeaders = [ "Accept", "application/json"; "Authorization", "Token " + Environment.GetEnvironmentVariable("IgaApiKey") ]
    Http.RequestString("https://api.iga.in.gov/" + endpoint, httpMethod = "GET", headers = standardHeaders) |> JsonValue.Parse |>  (fun s -> s.ToString())

type Reading = Committee | Second | Third
type Bills = JsonProvider<"../sample/bills.json", ResolutionFolder=__SOURCE_DIRECTORY__>
type Bill = JsonProvider<"../sample/bill.json", ResolutionFolder=__SOURCE_DIRECTORY__>
type Actions = JsonProvider<"../sample/actions.json", ResolutionFolder=__SOURCE_DIRECTORY__>
type Calendars = JsonProvider<"../sample/calendars.json", ResolutionFolder=__SOURCE_DIRECTORY__>
type Calendar = JsonProvider<"../sample/calendar.json", ResolutionFolder=__SOURCE_DIRECTORY__>

let firstReadingConst = "First reading: referred to Committee on "
let committeeReportConst = "Committee report: "
let secondReadingConst  = "Second reading: "
let thirdReadingConst  = "Third reading: "

let fetchActions (day:string) =
    let rec fetchActionsRec (link:string) =
        let actions = get link |> Actions.Parse 
        let filteredActions = actions.Items |> Array.toList
        match actions.NextLink with
        | "" -> filteredActions
        | nextPage -> filteredActions @ (fetchActionsRec nextPage)
    fetchActionsRec (sprintf "/2017/bill-actions?minDate=%s&maxDate=%s" day day)

// FORMAT UPDATES FOR EMAIL

let linkTo (bill:string) =
    let name = bill.Substring(0,6)
    let house = if bill.StartsWith("H") then "house" else "senate"
    let number = Int32.Parse(name.Substring(2,4))
    sprintf "[%s](https://iga.in.gov/legislative/2017/bills/%s/%d)" name house number

let rec describeActions (actions:Actions.Item list) (chamber:string) (event:string) (description:string) =
    let acc = actions |> List.filter(fun a -> a.Chamber.Name.Equals(chamber) && a.Description.StartsWith(event)) |> List.sortBy (fun i -> i.BillName.BillName)
    let result = 
        match acc with
        | [] -> "(None)"
        | _ -> acc |> List.map(fun i -> (sprintf "* %s: %s  " (linkTo (i.BillName.BillName)) (i.Description.Replace(event, "")))) |> String.concat "\n" 
    sprintf "**%s %s**  \n\n%s" chamber description result

let list (items) =
    let result = items |> Seq.map (fun b ->  sprintf "* %s  " (linkTo (b.ToString()))) |> String.concat "\n"
    match result with
    | "" -> "(None)"
    | _ -> result

let describeCalendar (calendars:Calendars.Root) (chamber:string) = 
    let found = calendars.Items |> Array.tryFind (fun i -> (i.Chamber.ToLower()).Equals(chamber.ToLower()))
    match found with
    | None -> sprintf "###No upcoming calendar available for the %s" chamber
    | Some(x) -> 
        try
            let calendar = get x.Link |> Calendar.Parse
            let hb2bills = calendar.Hb2head.Bills |> Array.map (fun h -> h.BillName)
            let hb3bills = calendar.Hb3head.Bills |> Array.map (fun h -> h.BillName)
            let sb2bills = calendar.Sb2head.Bills |> Array.map (fun h -> h.BillName)
            let sb3bills = calendar.Sb3head.Bills |> Array.map (fun h -> h.BillName)
            sprintf "###%s %s Calendar\n\n**House bills on Second Reading**  \n\n%s\n\n**House bills on Third Reading**  \n\n%s\n\n**Senate bills on Second Reading**  \n\n%s\n\n**Senate bills on Third Reading**  \n\n%s" (calendar.Date.ToString("MM-dd-yyyy")) (chamber) (list hb2bills) (list hb3bills) (list sb2bills) (list sb3bills)
        with
        | ex -> sprintf "###No upcoming calendar available for the %s" chamber

// AZURE STORAGE
let storageContainer connStr = 
    let ref = CloudStorageAccount.Parse(connStr).CreateCloudBlobClient().GetContainerReference("artifacts")    
    ref.CreateIfNotExists() |> ignore
    ref

let getLatestDocument connStr path =
    let container = storageContainer connStr
    let blobs = container.ListBlobs() 
    let blob = blobs |> Seq.sortByDescending (fun b -> b.Uri.ToString()) |> Seq.head :?> CloudBlockBlob
    blob.DownloadToFile(path, FileMode.OpenOrCreate) |> ignore

let postDocument connStr path =
    let container = storageContainer connStr
    let stream = File.OpenRead(path)
    let blob = container.GetBlockBlobReference(Path.GetFileName(path))
    blob.UploadFromStream(stream)
    stream.Close()

// UPDATE SPREADSHEET
let updateArtifact (artifact:string[][]) (billName:string) (chamber:string) (reading:Reading) (date:DateTime) (result:string) =
    let bill = billName.Substring(0,6)
    let inOriginChamber = (bill.StartsWith("H") && chamber = "House") || (bill.StartsWith("S") && chamber = "Senate")
    let row = artifact |> Array.tryFind (fun r -> r.[0].Equals(bill))
    match row with
    | None -> 
        printfn "Found no row matching %s" bill
        ignore
    | Some(r) ->
        let column = 
            match reading with
            | Committee when inOriginChamber -> 6
            | Committee when not inOriginChamber -> 12
            | Second when inOriginChamber -> 8
            | Second when not inOriginChamber -> 14
            | Third when inOriginChamber -> 10
            | Third when not inOriginChamber -> 16
            | _ -> raise (Exception("unrecognized Reading!"))
        r.[column] <- date.ToString("MM/dd/yyyy")
        r.[column+1] <- result
        ignore

let updateCalendar (artifact:string[][]) (calendars:Calendars.Root) (chamber:string) =
    let found = calendars.Items |> Array.tryFind (fun i -> (i.Chamber.ToLower()).Equals(chamber.ToLower()))
    match found with
    | None -> ignore
    | Some(x) -> 
        try
            let calendar = get x.Link |> Calendar.Parse
            let hb2bills = calendar.Hb2head.Bills |> Array.map (fun h -> h.BillName) |> Array.iter (fun b -> updateArtifact artifact b chamber Reading.Second calendar.Date "" |> ignore)
            let hb3bills = calendar.Hb3head.Bills |> Array.map (fun h -> h.BillName) |> Array.iter (fun b -> updateArtifact artifact b chamber Reading.Third calendar.Date "" |> ignore)
            let sb2bills = calendar.Sb2head.Bills |> Array.map (fun h -> h.BillName) |> Array.iter (fun b -> updateArtifact artifact b chamber Reading.Second calendar.Date "" |> ignore)
            let sb3bills = calendar.Sb3head.Bills |> Array.map (fun h -> h.BillName) |> Array.iter (fun b -> updateArtifact artifact b chamber Reading.Third calendar.Date "" |> ignore)
            ignore           
        with
        | ex -> ignore

let updateAction (artifact:string[][]) (actions:Actions.Item list) =
    actions |> List.filter(fun a -> a.Description.StartsWith(committeeReportConst)) |> List.iter (fun a -> updateArtifact artifact (a.BillName.BillName) (a.Chamber.Name) Reading.Committee a.Date (a.Description.Replace(committeeReportConst, "")) |> ignore)
    actions |> List.filter(fun a -> a.Description.StartsWith(secondReadingConst)) |> List.iter (fun a -> updateArtifact artifact (a.BillName.BillName) (a.Chamber.Name) Reading.Second a.Date (a.Description.Replace(secondReadingConst, "")) |> ignore)
    actions |> List.filter(fun a -> a.Description.StartsWith(thirdReadingConst)) |> List.iter (fun a -> updateArtifact artifact (a.BillName.BillName) (a.Chamber.Name) Reading.Third a.Date (a.Description.Replace(thirdReadingConst, "")) |> ignore)

let update connStr (outputPath:string) (actions:Actions.Item list) (calendars:Calendars.Root) =
    let tempPath = Path.GetTempFileName()
    getLatestDocument connStr tempPath |> ignore
    let artifact = File.ReadAllLines(tempPath) |> Array.map (fun i -> i.Split([|"\",\""|], StringSplitOptions.None) |> Array.map (fun j -> j.Trim([|'"'|])) )
    updateAction artifact actions |> ignore
    updateCalendar artifact calendars "House" |> ignore
    updateCalendar artifact calendars "Senate" |> ignore 
    let content = artifact |> Array.map(fun r -> r |> Array.map(fun c -> sprintf "\"%s\"" c) |> String.concat ",")
    File.WriteAllLines(outputPath, content)
    postDocument connStr outputPath |> ignore
    File.Delete(tempPath)


// EMAIL

let encodeAttachmentContent (outputPath:string) =
    use stream = File.Open(outputPath, FileMode.Open)
    let buffer = Array.zeroCreate (int stream.Length)
    stream.Read(buffer, 0, buffer.Length) |> ignore
    stream.Close()
    Convert.ToBase64String(buffer)

let describe (actions:Actions.Item list) (calendars:Calendars.Root) =
    [
        "Hello! Please find attached today's legislative update.";
        "You can download the attached CSV file and open it in Excel, or view it in your browser (Google Sheets reccommended.) Let's go get 'em!";
        "##Today's Activity";
        describeActions actions "House" firstReadingConst "First Readings";
        describeActions actions "House" committeeReportConst "Committee Reports";
        describeActions actions "House" secondReadingConst "Second Readings";
        describeActions actions "House" thirdReadingConst "Third Readings";
        describeActions actions "Senate" firstReadingConst "First Readings";
        describeActions actions "Senate" committeeReportConst "Committee Reports";
        describeActions actions "Senate" secondReadingConst "Second Readings";
        describeActions actions "Senate" thirdReadingConst "Third Readings";
        "##Upcoming Activity";
        describeCalendar calendars "House";
        describeCalendar calendars "Senate";
    ]    

let emailResult (attachmentPath:string) (body:string) apiKey (recipients:string) = 
    let client = new StrongGrid.Client(apiKey)
    let recipients = recipients.Split([|';'|]) |> Array.map (fun r -> new Model.MailAddress(r,r))
    let toAddress = new Model.MailAddress("jhoerr@gmail.com", "John Hoerr")
    let fromAddress = new Model.MailAddress("jhoerr@gmail.edu", "John Hoerr")
    let attch = [ new Model.Attachment(FileName = Path.GetFileName(attachmentPath), Type="text/csv", Content = (encodeAttachmentContent attachmentPath)) ]
    let textContent = body 
    let htmlContent = body |> Markdown.Parse |> Markdown.WriteHtml
    let subject = sprintf "IGA legislative update for %s" (DateTime.Now.ToString("MM-dd-yyyy"))
    client.Mail.SendToMultipleRecipientsAsync(recipients, fromAddress, subject, htmlContent, textContent, attachments=attch, trackOpens=false, trackClicks=false).Wait()


// AZURE FUNCTION ENTRY POINT
let Run(myTimer: TimerInfo, log: TraceWriter) =
    log.Info(sprintf "F# Timer trigger function executed at: %s" (DateTime.Now.ToString()))
    try
        log.Info("Execution started")
        let today = DateTime.Now.ToString("yyyy-MM-dd")
        let tomorrow = DateTime.Now.AddDays(1.0).ToString("yyyy-MM-dd")
        let connStr = Environment.GetEnvironmentVariable("AzureStorage.ConnectionString")
        let sendGridApiKey = Environment.GetEnvironmentVariable("SendGridApiKey")
        let emailRecipients = Environment.GetEnvironmentVariable("EmailRecipients")

        let actions = fetchActions today
        let calendars = get ("2017/calendars?minDate=" + tomorrow) |> Calendars.Parse
        let outputPath = Path.Combine(Path.GetTempPath(), sprintf "iga-%s.csv" today)
        
        update connStr outputPath actions calendars |> ignore

        let body = describe actions calendars |> String.concat "\n\n" 
        emailResult outputPath body sendGridApiKey emailRecipients

        log.Info("Execution completed successfully")
    with
    | ex -> log.Error(sprintf "Caught exception: %s" (ex.ToString()))