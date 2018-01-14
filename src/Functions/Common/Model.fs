module Ptp.Model

open System
open Ptp.Core 

//  Database models
type Chamber = None=0 | House=1 | Senate=2
type Party = Republican=1 | Democratic=2 | Independent=3
type CommitteePosition = Member=1 | RankingMinority = 2 | ViceChair=3 | Chair=4 | Advisor=5 | Conferee=6
type CommitteeType = Interim=1 | Standing=2 | Conference=3
type BillPosition = Author=1 | CoAuthor= 2 | Sponsor=3 | CoSponsor=4 

[<CLIMutable>]
type Legislator = 
    {
        Id:int;
        SessionId:int;
        FirstName:string;
        LastName:string;
        Chamber:Chamber;
        District:int;
        Party:Party;
        Link:string;
        Image:string;
        WebUrl:string;
    }

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
    CommitteeType:CommitteeType;
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
    ApiUpdated:DateTime;
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

type CommitteeLink = CommitteeLink of string
type VoteResult = VoteResult of string

type Action' = 
    | FirstReading of Chamber * CommitteeLink
    | CommitteeReport of Chamber * VoteResult
    | SecondReading of Chamber * VoteResult
    | ThirdReading of Chamber * VoteResult
    | ReturnedWithAmendments of Chamber
    | MotionToConcur of Chamber
    | MotionToDissent of Chamber
    | ConfCommitteeReport of Chamber * VoteResult
    | SignedBySpeaker
    | SignedByPresident
    | SignedByGovernor
    | VetoedByGovernor
    | VetoOverridden


[<CLIMutable>]
type ScheduledAction = {
    Id:int;
    Link:string;
    Date:System.DateTime;
    Start:string;
    End:string;
    CustomStart:string;
    Location:string;
    ActionType:ActionType;
    Chamber:Chamber;
    BillId:int;
    CommitteeLink:string;
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

[<CLIMutable>]
type CommitteeMember = {
    Id:int;
    LegislatorId:int;
    CommitteeId:int;
    Position:CommitteePosition;
}

[<CLIMutable>]
type BillMember = {
    Id:int;
    LegislatorId:int;
    BillId:int;
    Position:BillPosition;
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
type NotificationLog = {
    MessageType:MessageType;
    Recipient:string;
    Subject:string;
    Digest:string;
}

[<CLIMutable>]
type Recipient = {
    Email:string;
    Mobile:string;
    ReceiveAlertEmail:bool;
    ReceiveAlertSms:bool;
    HouseDistrict:int;
    SenateDistrict:int;
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

// DTOs

type Location = 
    {
        Address:string;
        City:string;
        Zip:string;
    }

type Body = 
    {
        Id:int;
        Name:string;
        Chamber:Chamber;
        District:int;
        Party:Party;
        Link:string;
        Image:string;
    }

type Representation = 
    {
        Senator:Legislator; 
        Representative:Legislator
    }

type LinkAndId = {Id:int; Link:string}