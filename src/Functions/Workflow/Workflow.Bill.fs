module Ptp.Workflow.Bill

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Common.Core
open Ptp.Common.Model
open Ptp.Common.Http
open Ptp.Common.Database
open Ptp.Common.Cache
open Ptp.Workflow.Common
open System

// ADD/UPDATE BILL

let billModel (bill:JsonValue) = 
    let name = bill?billName.AsString()
    let link = bill?link.AsString()
    let title = bill?latestVersion?shortDescription.AsString()
    let description = bill?latestVersion?digest.AsString()

    let printVersion =
        match bill.TryGetProperty("printVersion") with
        | None      -> 1
        | Some x    -> x.AsInteger()

    let chamber =
        if bill?originChamber.AsString() = "house" 
        then Chamber.House 
        else Chamber.Senate

    let apiUpdated = 
        let updated = bill?latestVersion?updated
        if updated = JsonValue.Null
        then bill?latestVersion?created.AsDateTime()
        else updated.AsDateTime()

    { Bill.Id=0; 
    SessionId=0; 
    Name=name; 
    Link=link; 
    Title=title; 
    Description=description;
    Chamber=chamber;
    Authors="";
    IsDead=false;
    Version=printVersion;
    ApiUpdated = apiUpdated }

let fetchBillMetadata link =
    fetch link

let deserializeBillModel json = trial {
    let! bill = json |> deserializeOneAs billModel
    return (bill,json)
    }

let addOrUpdateBillQuery= (sprintf """
UPDATE Bill
SET 
    Name = @Name
    , Title = @Title
    , Description = @Description
    , Version = @Version
    , ApiUpdated = @ApiUpdated
WHERE Link = @Link
IF @@ROWCOUNT=0
BEGIN
    INSERT INTO Bill
    (Name,Link,Title,Description,Chamber,SessionId) 
    VALUES (@Name,@Link,@Title,@Description,@Chamber,%s)
END
SELECT * FROM Bill WHERE Link = @Link
""" SessionIdSubQuery)

let addOrUpdateBillRecord (bill:Bill, json) = trial {
    let! b = dbParameterizedQueryOne<Bill> addOrUpdateBillQuery bill
    return (b,json)
    }

// UPDATE BILL / SUBJECT RELATIONSHIPS

let getSessionSubjectsQuery = (sprintf """
SELECT Id,Link From [Subject] 
WHERE SessionId = %s""" SessionIdSubQuery)

let getKnownBillSubjectsQuery = """
SELECT Id, BillId, SubjectId 
FROM BillSubject 
WHERE BillId = @Id"""

let insertBillSubjectCommand = """
IF NOT EXISTS 
    ( SELECT Id FROM BillSubject 
      WHERE BillId=@BillId 
        AND SubjectId=@SubjectId )
BEGIN
    INSERT INTO BillSubject 
    (BillId, SubjectId) 
    VALUES (@BillId, @SubjectId)
END"""

let deleteBillSubjectsCommand = """
DELETE FROM BillSubject 
WHERE Id IN @Ids"""

let findBill bills metadata =
    let billLink = metadata?link.AsString()
    bills |> Seq.find (fun b -> b.Link = billLink)

let parseLatestBillSubjects subjects (bill:Bill) json =
    let billLinkId = {Id=bill.Id; Link=bill.Link}
    let toBillSubject (bid,lid,pos) = { Id=0; BillId=bid; SubjectId=lid; }
    let any = multiple billLinkId subjects (json?latestVersion) toBillSubject
    any "subjects" 0 |> List.toSeq

let matchOnSubjectAndBill b = (b.SubjectId,b.BillId)

let addBillSubjects known latest =
    let toAdd = latest |> except' known matchOnSubjectAndBill
    toAdd |> Seq.map (dbCommand insertBillSubjectCommand) |> collect

let deleteBillSubjects known latest =
    let toDelete = 
        known 
        |> except' latest matchOnSubjectAndBill
        |> Seq.map (fun td -> td.Id)
    dbCommandById deleteBillSubjectsCommand toDelete
    
let reconcileBillSubjects (bill:Bill,json) = trial {
    let! knownSubjects = dbQuery<LinkAndId> getSessionSubjectsQuery
    let latestBillSubjects = parseLatestBillSubjects knownSubjects bill json
    let! knownBillSubjects = 
        dbParameterizedQuery<BillSubject> getKnownBillSubjectsQuery bill
    let! a' = addBillSubjects knownBillSubjects latestBillSubjects
    let! b' = deleteBillSubjects knownBillSubjects latestBillSubjects
    return (bill,json)
    }

// UPDATE BILL / LEGISLATOR RELATIONSHIPS

let getSessionLegislatorsQuery = (sprintf """
SELECT Id,Link From [Legislator] 
WHERE SessionId = %s""" SessionIdSubQuery)

let getKnownBillMemberQuery = """
SELECT Id, BillId, LegislatorId, Position 
FROM LegislatorBill 
WHERE BillId = @Id"""

let insertBillMemberCommand = """
IF NOT EXISTS 
    ( SELECT Id FROM LegislatorBill 
      WHERE BillId=@BillId 
        AND LegislatorId=@LegislatorId 
        AND Position=@Position )
BEGIN
    INSERT INTO LegislatorBill 
    (BillId,LegislatorId,Position) 
    VALUES (@BillId,@LegislatorId,@Position)
END"""

let deleteBillMemberCommand = """
DELETE FROM LegislatorBill 
WHERE Id IN @Ids"""

let parseLatestBillMembers legislators (bill:Bill) json = 
    let billLinkId = {Id=bill.Id; Link=bill.Link}
    let committeeMembership (bid,lid,pos) = 
        { Id=0; BillId=bid; LegislatorId=lid; Position= pos; }
    let any = multiple billLinkId legislators json committeeMembership
    let authors =    any "authors" BillPosition.Author
    let coAuthors =  any "coauthors" BillPosition.CoAuthor
    let sponsors =   any "sponsors" BillPosition.Sponsor
    let coSponsors = any "cosponsors" BillPosition.CoSponsor
    authors @ coAuthors @ sponsors @ coSponsors |> List.toSeq

let matchBillMember b = (b.LegislatorId,b.BillId,b.Position)

let addBillMembers known latest =
    let toAdd = latest |> except' known matchBillMember
    dbCommand insertBillMemberCommand toAdd

let deleteBillMembers known latest =
    let toDelete = 
        known 
        |> except' latest matchBillMember
        |> Seq.map (fun td -> td.Id)
        |> Seq.toArray
    dbCommandById deleteBillMemberCommand toDelete

let reconcileBillMembers (bill:Bill,json) = trial {
    let! knownLegislators = dbQuery<LinkAndId> getSessionLegislatorsQuery
    let latestBillMembers = parseLatestBillMembers knownLegislators bill json
    let! knownBillMembers = 
        dbParameterizedQuery<BillMember> getKnownBillMemberQuery bill
    let! a' = addBillMembers knownBillMembers latestBillMembers
    let! b' = deleteBillMembers knownBillMembers latestBillMembers
    return (bill,json)
    }

let invalidateBillCache = 
    tryInvalidateCache BillsKey

let evaluateResult result = 
    match result with
    | Ok (_, msgs) ->   
        Next.Succeed(terminalState, msgs)
    | Bad ([APIBillNotAvailable(b)]) ->
        Next.Succeed(terminalState,[APIBillNotAvailable(b)])
    | Bad msgs ->       
        Next.FailWith(msgs)

let workflow link = 
    fun () ->
        fetchBillMetadata link
        >>= deserializeBillModel
        >>= addOrUpdateBillRecord
        >>= reconcileBillSubjects
        >>= reconcileBillMembers
        >>= invalidateBillCache
        |> evaluateResult