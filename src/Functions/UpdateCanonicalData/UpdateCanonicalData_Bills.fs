module Ptp.UpdateCanonicalData.Bills

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Model
open Ptp.Queries
open Ptp.Http
open Ptp.Database
open Ptp.Cache
open Ptp.Logging
open Ptp.UpdateCanonicalData_Common
open System

let billModel (bill:JsonValue) = 
    let printVersion =
        match bill.TryGetProperty("printVersion") with
        | None      -> 1
        | Some x    -> x.AsInteger()
                    
    { Bill.Id=0; 
    SessionId=0; 
    Name=bill?billName.AsString(); 
    Link=bill?link.AsString(); 
    Title=bill?latestVersion?shortDescription.AsString(); 
    Description=bill?latestVersion?digest.AsString();
    Chamber=(if bill?originChamber.AsString() = "house" then Chamber.House else Chamber.Senate);
    Authors="";
    IsDead=false;
    Version=printVersion;
    ApiUpdated = bill?latestVersion?updated.AsDateTime()}

let resolveLastUpdateTimestamp results = 
    let head = results |> Seq.tryHead
    match head with
    | Some datetime -> ok datetime
    | None -> ok (DateTime(2000,1,1))

let getLastUpdateTimestamp sessionYear = trial {
    let queryText = sprintf "GET MAX(ApiUpdated) FROM Bills WHERE SessionId = %s" SessionIdSubQuery
    let! results = dbQuery<DateTime> queryText
    let lastUpdate = 
        match results |> Seq.tryHead with
        | Some datetime -> datetime
        | None -> DateTime(2000,1,1)
    return (sessionYear, lastUpdate)
    }

let fetchRecentlyUpdatedBillsFromApi (sessionYear, lastUpdate) = trial {
    // get a listing of all bills
    let url = sprintf "/%s/bills" sessionYear
    let! pages = url |> fetchAllPages 
    // parse the url for each bill
    let billLink json = json?link.AsString()
    let! billUrls = pages |> deserializeAs billLink
    // grab the full bill metadata from each bill url
    let! metadata = billUrls |> fetchAllParallel
    // find the recently updated metadata based on the 'latestVersion.updated' timestamp
    let wasRecentlyUpdated json = 
        json?latestVersion?updated.AsDateTime() > lastUpdate
    let recentlyUpdated = metadata |> chooseSnd |> Seq.filter wasRecentlyUpdated
    return recentlyUpdated
    }

// UPDATE BILL RECORDS

let getKnownBillsQuery = "SELECT Id, Link FROM Bills WHERE Link IN (@Links)"

let insertBillCommand = sprintf """INSERT INTO Bill(Name,Link,Title,Description,Chamber,SessionId) 
VALUES (@Name,@Link,@Title,@Description,@Chamber,%s)""" SessionIdSubQuery

let updateBillCommand = """
-- Create temporary update table
CREATE TABLE #BillUpdate(
    Id int,
    Name nvarchar(256),
    Title nvarchar(256),
    Description nvarchar(max),
	ApiUpdated DATETIME)

-- Populate update table
INSERT INTO #BillUpdate(Id,Name,Title,Description,Version,ApiUpdated) 
VALUES (@Id,@Name,@Title,@Description,@Version,@ApiUpdated)

-- Update existing records by joining into update table
UPDATE b
SET 
    b.Name = u.Name,
    b.Title = u.Title,
    b.Description = u.Description,
    b.Version = u.Version
    b.ApiUpdated = u.ApiUpdated
FROM Bills b
JOIN #BillUpdate u ON b.Id = u.Id

-- Cleanup
DROP TABLE #BillUpdate
"""

let getKnownBills metadata = trial {
    let billLink json = json?link.AsString()
    let! billLinks = metadata |> deserializeAs billLink
    let billLinksArray = billLinks |> Seq.toArray
    let! result = dbParameterizedQuery<LinkAndId> getKnownBillsQuery {Links=billLinksArray}
    return result
    }

let matchOnLink (a:Bill) b = 
    a.Link = b.Link

let addNewBills knownBills latestBills =
    let toAdd = latestBills |> except'' knownBills (fun a -> a.Link) (fun (b:Bill) -> b.Link)
    dbCommand insertBillCommand toAdd

let getUpdateModels knownBills latestBills =
    latestBills 
    |> intersect' knownBills matchOnLink
    |> Seq.map(fun i ->
        let latest = latestBills |> Seq.find (fun l -> l.Link = i.Link)
        let known = knownBills |> Seq.find (fun k -> k.Link = i.Link)
        { latest with Id=known.Id })

let updatedExistingBills knownBills latestBills = 
    let toUpdate = getUpdateModels knownBills latestBills
    dbCommand updateBillCommand toUpdate

let addOrUpdateBills metadata = trial {
    let! latestBills = metadata |> deserializeAs billModel
    let! knownBills = getKnownBills metadata
    let! a' = addNewBills knownBills latestBills
    let! b' = updatedExistingBills knownBills latestBills
    return (metadata,knownBills)
    }

// UPDATE BILL / SUBJECT RELATIONSHIPS

let getKnownSubjectsQuery = sprintf "SELECT Id,Link From [Subject] WHERE SessionId = %s" SessionIdSubQuery
let getKnownBillSubjectsQuery = "SELECT Id, BillId, SubjectId FROM BillSubjects WHERE BillId IN @Ids"
let insertBillSubjectsCommand = "INSERT INTO BillSubject (BillId,SubjectId) VALUES (@BillId,@SubjectId)"
let deleteBillSubjectsCommand = "DELETE FROM BillSubjects WHERE Id IN @Ids"

let findBill bills metadata =
    let billLink = metadata?link.AsString()
    bills |> Seq.find (fun b -> b.Link = billLink)

let parseLatestBillSubjects subjects bills json = 
    let bill = json |> findBill bills
    let toBillSubject (bid,lid,pos) = { Id=0; BillId=bid; SubjectId=lid; }
    let any = multiple bill subjects (json?latestVersion) toBillSubject
    let billSubjects =    any "subjects" 0
    billSubjects |> List.toSeq

let matchOnSubjectAndBill b = (b.SubjectId,b.BillId)

let addBillSubjects known latest =
    let toAdd = latest |> except' known matchOnSubjectAndBill
    dbCommand insertBillSubjectsCommand toAdd

let deleteBillSubjects known latest =
    let toDelete = known |> except' latest matchOnSubjectAndBill
    dbCommand deleteBillSubjectsCommand toDelete
    
let reconcileBillSubjects (metadata,knownBills) = trial {
    let! knownSubjects = dbQuery<LinkAndId> getKnownSubjectsQuery
    let parseLatestBillSubjects = parseLatestBillSubjects knownSubjects knownBills
    let latestBillSubjects = metadata |> Seq.collect parseLatestBillSubjects
    let knownBillIds = knownBills |> Seq.map (fun b -> b.Id) |> Seq.toArray
    let! knownBillSubjects = dbParameterizedQuery<BillSubject> getKnownSubjectsQuery {Ids=knownBillIds}
    let! a' = addBillSubjects knownBillSubjects latestBillSubjects
    let! b' = deleteBillSubjects knownBillSubjects latestBillSubjects
    return (metadata,knownBills)
    }

// UPDATE BILL / LEGISLATOR RELATIONSHIPS

let getKnownLegislatorsQuery = sprintf "SELECT Id,Link From [Legislator] WHERE SessionId = %s" SessionIdSubQuery
let getKnownBillMemberQuery = "SELECT Id, BillId, LegislatorId FROM BillLegislator WHERE BillId IN @Ids"
let insertBillMemberCommand = "INSERT INTO BillLegislator (BillId,LegislatorId) VALUES (@BillId,@LegislatorId)"
let deleteBillMemberCommand = "DELETE FROM BillLegislator WHERE Id IN @Ids"

let parseLatestBillMembers legislators bills json = 
    let bill = json |> findBill bills
    let committeeMembership (bid,lid,pos) = { Id=0; BillId=bid; LegislatorId=lid; Position= pos; }
    let any = multiple bill legislators json committeeMembership
    let authors =    any "authors" BillPosition.Author
    let coAuthors =  any "coauthors" BillPosition.CoAuthor
    let sponsors =   any "sponsors" BillPosition.Sponsor
    let coSponsors = any "cosponsors" BillPosition.CoSponsor
    authors @ coAuthors @ sponsors @ coSponsors
    |> List.toSeq

let matchBillMember b = (b.LegislatorId,b.BillId,b.Position)

let addBillMembers known latest =
    let toAdd = latest |> except' known matchBillMember
    dbCommand insertBillMemberCommand toAdd

let deleteBillMembers known latest =
    let toDelete = known |> except' latest matchBillMember
    dbCommand deleteBillMemberCommand toDelete

let reconcileBillMembers (metadata,knownBills) = trial {
    let! knownLegislators = dbQuery<LinkAndId> getKnownLegislatorsQuery
    let parseLatestBillMembers = parseLatestBillMembers knownLegislators knownBills
    let latestBillMembers = metadata |> Seq.collect parseLatestBillMembers
    let knownBillIds = knownBills |> Seq.map (fun b -> b.Id) |> Seq.toArray
    let! knownBillMembers = dbParameterizedQuery<BillMember> getKnownSubjectsQuery {Ids=knownBillIds}
    let! a' = addBillMembers knownBillMembers latestBillMembers
    let! b' = deleteBillMembers knownBillMembers latestBillMembers
    return (metadata,knownBills)
    }

let invalidateBillCache (metadata,knownBills) = 
    metadata |> invalidateCache' BillsKey

/// Log the addition of any new bill subjects
let describeNewBills metadata = 
    let describer json = json?billName.AsString()
    metadata |> describeNewItems describer

let updateBills =
    getCurrentSessionYear
    >> bind getLastUpdateTimestamp
    >> bind fetchRecentlyUpdatedBillsFromApi
    >> bind addOrUpdateBills
    >> bind reconcileBillSubjects
    >> bind reconcileBillMembers
    >> bind invalidateBillCache
    >> bind describeNewBills