module Ptp.UpdateCanonicalData.Committees

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Ptp.Core
open Ptp.Model
open Ptp.Queries
open Ptp.Http
open Ptp.Database
open Ptp.Cache
open Ptp.UpdateCanonicalData_Common
open System

// COMMITTEES
let committeeModel (url,c:JsonValue) =

    let name = c?name.AsString()

    let chamber = 
        match c.TryGetProperty("chamber") with
        | Some prop -> 
            let name = prop?name.AsString()
            Enum.Parse(typedefof<Chamber>, name) :?> Chamber
        |None -> 
            Chamber.None
    
    let committeeType = 
        match url with
        | Contains("standing-") s   -> CommitteeType.Standing
        | Contains("interim-") s    -> CommitteeType.Interim
        | Contains("conference-") s -> CommitteeType.Conference
        | _ -> sprintf "Uncrecognized committee type: %s" url |> failwith
    
    { Committee.Id=0; 
      SessionId=0; 
      Chamber=chamber; 
      CommitteeType=committeeType;
      Name=name; 
      Link=url }

/// Fetch URLs for all committees in the current session year.
let fetchAllCommitteesFromAPI sessionYear = trial {
    let url = sprintf "/%s/committees" sessionYear
    let! pages = url |> fetchAllPages
    let link json = json?link.AsString()
    let! committeeUrls = pages |> deserializeAs link
    let! committeeList = committeeUrls |> fetchAllParallel
    let metadata = committeeList |> chooseBoth
    return metadata
    }

let filterOutKnownCommitteesQuery = sprintf "SELECT Link from Committee WHERE SessionId = %s" SessionIdSubQuery
let insertCommitteeCommand = sprintf """INSERT INTO Committee(Name,Link,Chamber,CommitteeType,SessionId) 
VALUES (@Name,@Link,@Chamber,@CommitteeType,%s)""" SessionIdSubQuery

/// Fetch full metadata for committess that we don't yet know about
let resolveNewCommittees (metadata:(string*JsonValue) list) = trial {
    let! knownCommitteeUrls = dbQuery<string> filterOutKnownCommitteesQuery
    let comparer (url,_) knownUrl = url = knownUrl
    let! newCommittees =    
        metadata 
        |> except' knownCommitteeUrls comparer
        |> deserializeAs committeeModel
    return (metadata,newCommittees)
    }

let persistNewCommittees (metadata,newCommittees) = trial {
    let! a' = newCommittees |> dbCommand insertCommitteeCommand  
    return (metadata, newCommittees)
    }

/// Invalidate the Redis cache key for committees
let invalidateCommitteeCache (metadata,newCommittees) = trial { 
    let! a' = newCommittees |> invalidateCache' CommitteesKey
    return metadata
    }

/// Log the addition of new committees
let halfAssDescribe x = 
    "Committees and memberships updated!" |> ok
    


// COMMITTEE MEMBERSHIPS

/// Determine the makeup of a given committee.
/// Standing committees meet througout the session.
/// Conference committees meet to work out the difference between house/senate versions of a bill.
let determineCommitteeComposition legislators (committee,json) =
    let committeeMembership (cid,lid,pos) = { Id=0; CommitteeId=cid; LegislatorId=lid; Position= pos; }
    let resolve strategy = strategy committee legislators json committeeMembership
    let one = resolve single
    let any = resolve multiple
    
    let standingCommittee () = 
        let chair =     one "chair" CommitteePosition.Chair
        let viceChair = one "viceChair" CommitteePosition.ViceChair
        let ranking =   one "rankingMinMember" CommitteePosition.RankingMinority
        let leadership = chair @ viceChair @ ranking
        let rankAndFileMembers =
            any "members" CommitteePosition.Member
            |> except' leadership (fun a b -> a.LegislatorId = b.LegislatorId)
            |> Seq.toList
        leadership @ rankAndFileMembers

    let conferenceCommittee () =
        let chair =     one "conferee_chair" CommitteePosition.Chair
        let conferees = any "conferees" CommitteePosition.Conferee
        let opp_confs = any "opp_conferees" CommitteePosition.Conferee
        let advisors =  any "advisors" CommitteePosition.Advisor
        let opp_advis = any "opp_advisors" CommitteePosition.Advisor
        chair @ conferees @ opp_confs @ advisors @ opp_advis
    
    if committee.Link.Contains("conference")
    then conferenceCommittee()
    else standingCommittee()

let comQuery = sprintf "SELECT Id, Link FROM Committee WHERE SessionId = %s" SessionIdSubQuery
let legQuery = sprintf "SELECT Id, Link FROM Legislator WHERE SessionId = %s" SessionIdSubQuery

let memQuery = sprintf """SELECT lc.Id, LegislatorId, CommitteeId, Position 
FROM LegislatorCommittee lc
JOIN Committee c on lc.CommitteeId = c.Id
WHERE c.SessionId = %s""" SessionIdSubQuery

let insertQuery = """INSERT INTO LegislatorCommittee 
(CommitteeId, LegislatorId, Position) 
VALUES (@CommitteeId, @LegislatorId, @Position)"""

let deleteQuery = """DELETE FROM LegislatorCommittee WHERE Id IN @Ids"""

let getKnownLegislatorsFromDb metadata = trial {
    let! committees = dbQuery<LinkAndId> comQuery
    let! legislators = dbQuery<LinkAndId> legQuery
    return (metadata, committees, legislators)
    }

let resolveMembershipsFromMetadata (metadata, committees, legislators) =
    let pairMetadataWithCommittee (link,json) =
        committees 
        |> Seq.find (fun c' -> c'.Link = link)
        |> fun c' -> (c',json)
    let op() =
        metadata
        |> Seq.map pairMetadataWithCommittee
        |> Seq.map (determineCommitteeComposition legislators)
        |> Seq.concat
    tryF' op DTOtoDomainConversionFailure

let getKnownMemberships allMemberships = trial {
    let! knownMemberships = dbQuery<CommitteeMember> memQuery
    return (allMemberships,knownMemberships)
    }

let matchPredicate a b = 
    a.CommitteeId = b.CommitteeId 
    && a.LegislatorId = b.LegislatorId
    && a.Position = b.Position

let getMembershipsToAdd (allMemberships, knownMemberships) = 
    allMemberships |> except' knownMemberships matchPredicate

let addNewMemberships (allMemberships, knownMemberships) = 
    (allMemberships, knownMemberships)
    |> getMembershipsToAdd
    |> dbCommand insertQuery

let getMembershipsToDelete (allMemberships, knownMemberships) = 
    knownMemberships |> except' allMemberships matchPredicate

let deleteOldMemberships (allMemberships, knownMemberships) = trial {
    let toDelete = 
        (allMemberships, knownMemberships)
        |> getMembershipsToDelete
    let! deleted = 
        toDelete
        |> Seq.map (fun toDelete -> toDelete.Id)
        |> Seq.toArray
        |> fun ids -> dbCommand deleteQuery {Ids=ids}
    return toDelete
    }

let updateMemberships (allMemberships, knownMemberships) = trial {
    let! added = (allMemberships, knownMemberships) |> addNewMemberships 
    let! deleted = (allMemberships, knownMemberships) |> deleteOldMemberships 
    return (added,deleted)
    }

let clearMembershipCache (added,deleted) = trial {
    let! a' = added |> invalidateCache' MembershipsKey
    let! d' = deleted |> invalidateCache' MembershipsKey
    return (added, deleted)
    }

/// Find, add, and log new committees
let updateCommittees =
    getCurrentSessionYear
    >> bind fetchAllCommitteesFromAPI
    >> bind resolveNewCommittees
    >> bind persistNewCommittees
    >> bind invalidateCommitteeCache
    >> bind getKnownLegislatorsFromDb
    >> bind resolveMembershipsFromMetadata
    >> bind getKnownMemberships
    >> bind updateMemberships
    >> bind clearMembershipCache
    >> bind halfAssDescribe