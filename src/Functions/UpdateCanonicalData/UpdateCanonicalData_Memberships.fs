module Ptp.UpdateCanonicalData.Memberships

open Chessie.ErrorHandling
open FSharp.Data
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open Ptp.Cache
open System
open FSharp.Collections.ParallelSeq
open Ptp.Queries
open Ptp.UpdateCanonicalData
open Ptp.UpdateCanonicalData_Common

/// Determine the makeup of a given committee.
/// Standing committees meet througout the session.
/// Conference committees meet to work out the difference between house/senate versions of a bill.
let determineCommitteeComposition committee legislators (json:JsonValue) =
    let committeeMembership (cid,lid,pos) = { Id=0; CommitteeId=cid; LegislatorId=lid; Position= pos; }
    let resolve strategy = strategy committee legislators json committeeMembership
    let one = resolve single
    let any = resolve multiple
    
    let standingCommittee () = 
        let chair =     one "chair" CommitteePosition.Chair
        let viceChair = one "viceChair" CommitteePosition.ViceChair
        let ranking =   one "rankingMinMember" CommitteePosition.RankingMinority
        let members =   any "members" CommitteePosition.Member
        chair @ viceChair @ ranking @ members

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

let resolveMemberships legislators (committee:LinkAndId) =
    committee.Link
    |> tryGet 
    |> determineCommitteeComposition committee legislators 

let getCurrentMemberships (committees,legislators) =
    let op() =
        committees
        |> PSeq.collect (resolveMemberships legislators)
        |> PSeq.toList
    tryF' op (fun e -> APIQueryError(QueryText("Resolve committee memberships"),e))

let comQuery = sprintf "SELECT Id, Link from Committee WHERE SessionId = %s" SessionIdSubQuery
let legQuery = sprintf "SELECT Id, Link from Legislator WHERE SessionId = %s" SessionIdSubQuery
let memQuery = sprintf "SELECT Id, LegislatorId, CommitteeId, Position from Membership WHERE SessionId = %s" SessionIdSubQuery
let insertQuery = "INSERT INTO LegislatorCommittee (CommitteeId, LegislatorId, Position) VALUES (@CommitteeId, @LegislatorId, @Position)"
let deleteQuery = "DELETE FROM LegislatorCommittee WHERE (CommitteeId, LegislatorId, Position) IN ((@CommitteeId, @LegislatorId, @Position))"

let getCurrentCommittees () = trial {
    let! committees = dbQuery<LinkAndId> comQuery
    let! legislators = dbQuery<LinkAndId> legQuery
    return (committees, legislators)
    }

let getKnownMemberships () = trial {
    let! knownMemberships = dbQuery<CommitteeMember> memQuery
    return knownMemberships
    }

let matchPredicate a b = 
    a.CommitteeId = b.CommitteeId 
    && a.LegislatorId = b.LegislatorId
    && a.Position = b.Position

let addNewMemberships (currentMemberships, knownMemberships) = 
    let toAdd = currentMemberships |> except' knownMemberships matchPredicate
    dbCommand insertQuery toAdd

let deleteOldMemberships (currentMemberships, knownMemberships) =
    let toDelete = knownMemberships |> except' currentMemberships matchPredicate
    dbCommand deleteQuery toDelete

let updateMemberships currentMems = trial {
    let! knownMems = getKnownMemberships()
    let! added = (currentMems, knownMems) |> addNewMemberships 
    let! deleted = (currentMems, knownMems) |> deleteOldMemberships 
    return (added,deleted)
    }

let clearMembershipCache (added,deleted) = trial {
    let! a' = added |> invalidateCache' MembershipsKey
    let! d' = deleted |> invalidateCache' MembershipsKey
    return (added, deleted)
    }

let describeNewMemberships (added, deleted) = 
    let addCount = added |> Seq.length
    let deleteCount = deleted |> Seq.length
    sprintf "Added %d new memberships; Deleted % old memberships" addCount deleteCount
    |> ok

let updateCommitteeMemberships =
    getCurrentCommittees
    >> bind getCurrentMemberships
    >> bind updateMemberships
    >> bind clearMembershipCache  
    >> bind describeNewMemberships