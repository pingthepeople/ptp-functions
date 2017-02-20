namespace IgaTracker

module Model =

    type Chamber = House=1 | Senate=2
    type ActionType = Unknown=0 | CommitteeReading=1 | SecondReading=2 | ThirdReading=3 | AssignedToCommittee=4

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
    type Bill = { 
        Id:int; 
        Name:string; 
        Link:string; 
        Title:string; 
        Description:string; 
        Topics:string; 
        Authors:string; 
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
        ReceiveDigestEmail:bool;
    }

    type MessageType = Email=1 | SMS=2

    [<CLIMutable>]
    type Message = {
        MessageType:MessageType;
        Recipient:string;
        Subject:string;
        Body:string
    }

