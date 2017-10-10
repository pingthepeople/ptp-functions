module Ptp.UpdateCanonicalData.Memberships

open Chessie.ErrorHandling
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open Ptp.Cache
open System
open Ptp.Queries
open Ptp.UpdateCanonicalData
open Ptp.UpdateCanonicalData_Common
open FSharp.Data

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

let fetchCommitteesFromApi (committees,legislators:LinkAndId seq) = trial {
        // fetch metadata for each committee, in parallel
        let! apiCommittees = 
            committees 
            |> Seq.map (fun c -> c.Link) 
            |> fetchAllParallel
      
        // pair up a committee with its corresponding metadata from the API
        let joinWithPair (url,json) =
            // find the API result that matches this committees link
            let committee = committees |> Seq.find (fun c -> url = c.Link)
            (committee, json)
        
        let pairs = 
            apiCommittees
            |> chooseJson'
            |> Seq.map joinWithPair

        return (pairs, legislators)
        }

let resolveMembershipsFromApi (committeeJson, legislators:LinkAndId seq) =
    let op() =
        committeeJson
        |> Seq.map (determineCommitteeComposition legislators)
        |> Seq.concat
    tryF' op DTOtoDomainConversionFailure

let comQuery = sprintf "SELECT Id, Link from Committee WHERE SessionId = %s" SessionIdSubQuery
let legQuery = sprintf "SELECT Id, Link from Legislator WHERE SessionId = %s" SessionIdSubQuery
let memQuery = sprintf "SELECT Id, LegislatorId, CommitteeId, Position from Membership WHERE SessionId = %s" SessionIdSubQuery
let insertQuery = "INSERT INTO LegislatorCommittee (CommitteeId, LegislatorId, Position) VALUES (@CommitteeId, @LegislatorId, @Position)"
let deleteQuery = "DELETE FROM LegislatorCommittee WHERE (CommitteeId, LegislatorId, Position) IN ((@CommitteeId, @LegislatorId, @Position))"

let getKnownCommitteesFromDb () = trial {
    let! committees = dbQuery<LinkAndId> comQuery
    let! legislators = dbQuery<LinkAndId> legQuery
    return (committees, legislators)
    }

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

let deleteOldMemberships (allMemberships, knownMemberships) =
    (allMemberships, knownMemberships)
    |> getMembershipsToDelete
    |> dbCommand deleteQuery

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

let describeNewMemberships (added, deleted) = 
    let addCount = added |> Seq.length
    let deleteCount = deleted |> Seq.length
    sprintf "Added %d new memberships; Deleted % old memberships" addCount deleteCount
    |> ok

let updateCommitteeMemberships =
    getKnownCommitteesFromDb
    >> bind fetchCommitteesFromApi
    >> bind resolveMembershipsFromApi
    >> bind getKnownMemberships
    >> bind updateMemberships
    >> bind clearMembershipCache  
    >> bind describeNewMemberships