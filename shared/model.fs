namespace IgaTracker

module Model =

//  Database models

    type Chamber = House=1 | Senate=2
    type ActionType = Unknown=0 | CommitteeReading=1 | SecondReading=2 | ThirdReading=3 | AssignedToCommittee=4
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
    } with
        static member ParseNumber (billName:string) = billName.Substring(2,4).TrimStart('0')        
        static member PrettyPrintName (billName:string) = sprintf "%s %s" (billName.Substring(0,2)) (Bill.ParseNumber billName)
        member this.WebUrl session = sprintf "[%s](https://iga.in.gov/legislative/%s/bills/%s/%s)" (Bill.PrettyPrintName this.Name) session (this.Chamber.ToString().ToLower()) (Bill.ParseNumber this.Name)

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
        member this.Describe = 
            let desc = this.Description.TrimEnd(';')
            match this.ActionType with
            | ActionType.AssignedToCommittee -> sprintf "was assigned to the %A Committee on %s" this.Chamber desc
            | ActionType.CommitteeReading -> sprintf "was read in committee in the %A. The vote was: %s" this.Chamber desc
            | ActionType.SecondReading -> sprintf "had a second reading in the %A. The vote was: %s" this.Chamber desc
            | ActionType.ThirdReading -> sprintf "had a third reading in the %A. The vote was: %s" this.Chamber desc
            | _ -> "(some other event type?)"

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
            | ActionType.CommitteeReading when this.Start |> System.String.IsNullOrWhiteSpace -> sprintf "is scheduled for a committee reading on %s in %s" eventDate eventLocation
            | ActionType.CommitteeReading -> sprintf "is scheduled for a committee reading on %s from %s-%s in %s" eventDate (formatTimeOfDay this.Start) (formatTimeOfDay this.End) eventLocation
            | ActionType.SecondReading -> sprintf "is scheduled for a second reading in the %s on %s" eventLocation eventDate
            | ActionType.ThirdReading -> sprintf "is scheduled for a third reading in the %s on %s" eventLocation eventDate
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
        OriginChamber:Chamber;
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