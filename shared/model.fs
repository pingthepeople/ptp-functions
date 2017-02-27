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
    }

    [<CLIMutable>]
    type Action = {
        Id:int;
        Description:string;
        Link:string;
        Date:System.DateTime;
        ActionType:ActionType;
        Chamber:Chamber;
        BillId:int;
    } 

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
    } 

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