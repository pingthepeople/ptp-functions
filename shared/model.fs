namespace IgaTracker

module Model =

    let (|StartsWith|_|) (p:string) (s:string) =
        if s.StartsWith(p) then
            Some(s.Substring(p.Length))
        else
            None

    let timestamp() = System.DateTime.Now.ToString("HH:mm:ss.fff")
    let datestamp() = System.DateTime.Now.ToString("yyyy-MM-dd")
    let sqlConStr() = System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString")
    

//  Database models
    type Chamber = House=1 | Senate=2
    type ActionType = 
        | Unknown=0 
        | CommitteeReading=1 
        | SecondReading=2 
        | ThirdReading=3 
        | AssignedToCommittee=4
        | SignedByPresidentOfSenate=5
        | SignedByGovernor=6
        | VetoedByGovernor=7
        | VetoOverridden=8
        
    type DigestType = None=0 | MyBills=1 | AllBills=2

    [<CLIMutable>]
    type Session = { 
        Id:int; 
        Name:string; 
        Link:string; 
    }

    [<CLIMutable>]
    type Committee = { 
        Id:int; 
        Name:string; 
        Link:string; 
        Chamber:Chamber; 
        SessionId:int; 
    }

    [<CLIMutable>]
    type Subject = { 
        Id:int; 
        Name:string; 
        Link:string; 
        SessionId:int; 
    }

    [<CLIMutable>]
    type Bill = { 
        Id:int; 
        Name:string; 
        Link:string; 
        Title:string; 
        Description:string; 
        Authors:string;
        Chamber:Chamber; 
        SessionId:int; 
        IsDead:bool;
        Version:int;
    } with
        static member ParseNumber (billName:string) = billName.Substring(2,4).TrimStart('0')        
        static member PrettyPrintName (billName:string) = sprintf "%s %s" (billName.Substring(0,2)) (Bill.ParseNumber billName)
        member this.WebLink session = sprintf "[%s](https://iga.in.gov/legislative/%s/bills/%s/%s)" (Bill.PrettyPrintName this.Name) session (this.Chamber.ToString().ToLower()) (Bill.ParseNumber this.Name)

    [<CLIMutable>]
    type Action = {
        Id:int;
        Description:string;
        Link:string;
        Date:System.DateTime;
        ActionType:ActionType;
        Chamber:Chamber;
        BillId:int;
    } with
        static member FormatDescription title action =
            let desc = action.Description.TrimEnd(';')
            match action.ActionType with
            | ActionType.AssignedToCommittee -> sprintf "%s was assigned to the %A Committee on %s." title action.Chamber desc
            | ActionType.CommitteeReading -> sprintf "%s had a committee hearing in the %A. The vote was: %s." title action.Chamber desc
            | ActionType.SecondReading -> sprintf "%s had a second reading in the %A. The vote was: %s." title action.Chamber desc
            | ActionType.ThirdReading -> sprintf "%s had a third reading in the %A. The vote was: %s." title action.Chamber desc
            | ActionType.SignedByPresidentOfSenate -> sprintf "%s has been signed by the President of the Senate. It will now be sent to the Governor to be signed into law or vetoed." title
            | ActionType.SignedByGovernor -> sprintf "%s has been signed into law by the Governor." title
            | ActionType.VetoedByGovernor -> sprintf "%s has been vetoed by the Governor. The Assembly now has the option to override that veto." title
            | ActionType.VetoOverridden -> sprintf "The veto on %s has been overridden in the %A. The vote was: %s." title action.Chamber desc
            | _ -> "(some other event type?)"
        static member ParseType description =
            match description with
            | StartsWith "First reading: referred to Committee on " rest -> ActionType.AssignedToCommittee
            | StartsWith "Committee report" rest -> ActionType.CommitteeReading
            | StartsWith "Second reading" rest -> ActionType.SecondReading
            | StartsWith "Third reading" rest -> ActionType.ThirdReading
            | StartsWith "Signed by the President of the Senate" rest -> ActionType.SignedByPresidentOfSenate
            | StartsWith "Signed by the Governor" rest -> ActionType.SignedByGovernor
            | StartsWith "Vetoed by the Governor" rest -> ActionType.VetoedByGovernor
            | StartsWith "Veto overridden" rest -> ActionType.VetoOverridden
            | _ -> ActionType.Unknown
        static member ParseDescription description =
            match description with
            | StartsWith "First reading: referred to Committee on " rest -> rest
            | StartsWith "Committee report: " rest -> rest
            | StartsWith "Second reading: " rest -> rest
            | StartsWith "Third reading: " rest -> rest
            | StartsWith "Signed by the President of the Senate" rest -> rest
            | StartsWith "Signed by the Governor" rest -> rest
            | StartsWith "Vetoed by the Governor" rest -> rest
            | StartsWith "Veto overridden by the House; " rest -> rest
            | StartsWith "Veto overridden by the Senate; " rest -> rest
            | other -> other

    [<CLIMutable>]
    type ScheduledAction = {
        Id:int;
        Link:string;
        Date:System.DateTime;
        Start:string;
        End:string;
        Location:string;
        ActionType:ActionType;
        Chamber:Chamber;
        BillId:int;
    } with
        member this.Describe includeLink =
            let formatTimeOfDay time = System.DateTime.Parse(time).ToString("h:mm tt")
            let eventRoom = 
                match this.Location with 
                | "House Chamber" -> "the House Chamber"
                | "Senate Chamber" -> "the Senate Chamber"
                | other -> other
            let eventLocation = 
                match includeLink with
                | true -> sprintf "[%s](https://iga.in.gov/information/location_maps)" eventRoom
                | false -> eventRoom
            let eventDate = this.Date.ToString("M/d/yyyy")
            match this.ActionType with
            | ActionType.CommitteeReading when this.Start |> System.String.IsNullOrWhiteSpace -> sprintf "is scheduled for a committee hearing on %s in %s" eventDate eventLocation
            | ActionType.CommitteeReading -> sprintf "is scheduled for a committee hearing on %s from %s - %s in %s" eventDate (formatTimeOfDay this.Start) (formatTimeOfDay this.End) eventLocation
            | ActionType.SecondReading -> sprintf "is scheduled for a second reading on %s in %s" eventDate eventLocation 
            | ActionType.ThirdReading -> sprintf "is scheduled for a third reading on %s in %s" eventDate eventLocation
            | _ -> "(some other event type?)"

    [<CLIMutable>]
    type UserBill = {
        Id:int;
        BillId:int;
        UserId:int;
        ReceiveAlertEmail:bool;
        ReceiveAlertSms:bool;
    }
    
    [<CLIMutable>]
    type User = {
        Id:int;
        Name:string;
        Email:string;
        Mobile:string;
        DigestType:DigestType;
    }


    [<CLIMutable>]
    type BillSubject = {
        Id:int;
        BillId:int;
        SubjectId:int;
    }


//  Messaging / Data Processing Models

    type MessageType = Email=1 | SMS=2

    [<CLIMutable>]
    type Message = {
        MessageType:MessageType;
        Recipient:string;
        Subject:string;
        Body:string;
        Attachment:string;
    }

    [<CLIMutable>]
    type BillStatus = { 
        Name:string;
        Title:string;
        Description:string;
        Authors:string;
        OriginChamber:string;
        OriginCommittee:string;
        CrossoverCommittee:string;
        Subjects:string;
        OriginCommitteeReading:string;
        OriginCommitteeReadingVote:string;
        OriginSecondReading:string;
        OriginSecondReadingVote:string;
        OriginThirdReading:string;
        OriginThirdReadingVote:string;
        CrossoverCommitteeReading:string;
        CrossoverCommitteeReadingVote:string;
        CrossoverSecondReading:string;
        CrossoverSecondReadingVote:string;
        CrossoverThirdReading:string;
        CrossoverThirdReadingVote:string;
    }