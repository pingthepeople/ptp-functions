module Ptp.UpdateCanonicalData.Memberships

open Chessie.ErrorHandling
open FSharp.Data
open FSharp.Data.JsonExtensions
open Microsoft.Azure.WebJobs.Host
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open Ptp.Cache
open Ptp.Logging
open System
open FSharp.Collections.ParallelSeq
open FSharp.Data.HttpResponseHeaders

[<Literal>]
let DeleteCommitteeMemberships = "DELETE FROM LegislatorCommittee WHERE CommitteeId IN @Ids"

[<Literal>] 
let InsertCommitteeMemberships = "INSERT INTO LegislatorCommittee (CommitteeId, LegislatorId, Position) values (@CommitteeId, @LegislatorId, @Position)"

let getCommitteesAndLegislators cn sessionId =
    let op() =
        let c = cn |> dapperQuery<Committee> (sprintf "SELECT * from Committee WHERE SessionId = %d" sessionId) |> Seq.toList
        let l = cn |> dapperQuery<Legislator> (sprintf "SELECT * from Legislator WHERE SessionId = %d" sessionId) |> Seq.toList
        (c,l)
    tryF op "Fetch all committees and legislators"

let toCommitteeMembership (committee : Committee) (legislators : Legislator list) position link = 
    let legislator = legislators |> List.tryFind (fun l -> l.Link = link)
    match legislator with
    | Some l -> [{ Id=0; Position= position; CommitteeId=committee.Id; LegislatorId=l.Id; }]
    | None   -> []

let resolveList (committee : Committee) (legislators : Legislator list) position (jsonValue:JsonValue option) = 
    match jsonValue with 
    | None -> []
    | Some value -> 
        value.AsArray()
        |> Array.toList 
        |> List.map (fun m -> m?link.AsString())
        |> List.collect (fun l -> toCommitteeMembership committee legislators position l)

let resolve (committee : Committee) (legislators : Legislator list) position (jsonValue:JsonValue option) = 
    match jsonValue with 
    | None -> []
    | Some value -> 
        match value.TryGetProperty("link") with
        | None -> []
        | Some link -> 
            link.AsString()
            |> toCommitteeMembership committee legislators position        


let tryConvert propertyName committee legislators position (json:JsonValue) =
    json.TryGetProperty(propertyName)
    |> resolve committee legislators position

let toConferenceCommitteeMemberships (committee : Committee) (legislators : Legislator list) (json:JsonValue) =
    let chair =
        json.TryGetProperty("conferee_chair")
        |> resolve committee legislators CommitteePosition.Chair
    let conferees = 
        json.TryGetProperty("conferees")
        |> resolveList committee legislators CommitteePosition.Conferee
    let opp_conferees = 
        json.TryGetProperty("opp_conferees")
        |> resolveList committee legislators CommitteePosition.Conferee
    let advisors = 
        json.TryGetProperty("advisors")
        |> resolveList committee legislators CommitteePosition.Advisor
    let opp_advisors = 
        json.TryGetProperty("opp_advisors")
        |> resolveList committee legislators CommitteePosition.Advisor

    chair @ conferees @ opp_conferees @ advisors @ opp_advisors

let toStandingCommitteeMemberships (committee : Committee) (legislators : Legislator list) (json:JsonValue) =
    let chair =
        json.TryGetProperty("chair")
        |> resolve committee legislators CommitteePosition.Chair
    let viceChair = 
        json.TryGetProperty("viceChair")
        |> resolve committee legislators CommitteePosition.ViceChair
    let ranking = 
        json.TryGetProperty("rankingMinMember")
        |> resolve committee legislators CommitteePosition.RankingMinority
    let members = 
        json.TryGetProperty("members")
        |> resolveList committee legislators CommitteePosition.Member

    chair @ viceChair @ ranking @ members

let resolveMemberships toMemberships (legislators : Legislator list) (committee : Committee) : (CommitteeMember list) =
    tryGet committee.Link
    |> toMemberships committee legislators 

let getLatestMemberships (log:TraceWriter) (committees : Committee list, legislators : Legislator list) = 
    let op() =
        let conferenceMemberships = 
            committees
            |> List.filter (fun c -> c.Link.Contains("conference"))
            |> PSeq.collect (fun c -> resolveMemberships toConferenceCommitteeMemberships legislators c)
            |> PSeq.toList
        let standingMemberships =
            committees
            |> List.filter (fun c -> c.Link.Contains("conference") |> not)
            |> PSeq.collect (fun c -> resolveMemberships toStandingCommitteeMemberships legislators  c)
            |> PSeq.toList 
        conferenceMemberships @ standingMemberships
    tryF op "Resolve memberships"

let updateRecords cn (memberships : CommitteeMember list)=
    let op() =
        let committeeIds = 
            memberships
            |> List.map (fun m -> m.CommitteeId)
            |> List.distinct
            |> List.toArray
        cn |> dapperParametrizedQuery<int> DeleteCommitteeMemberships {Ids=committeeIds} |> ignore
        cn |> dapperParameterizedCommand InsertCommitteeMemberships memberships
        memberships
    tryF op "Update membership records"

let clearMembershipCache memberships =
    let op() = 
        memberships |> invalidateCache MembershipsKey
    tryF op "Invalidate committee memberships cache"

let updateMemberships (log:TraceWriter) cn sessionId =
    let UpdateMemberships = "UpdateCanonicalData / Refresh committee memberships"
    log.Info(sprintf "[START] %s" UpdateMemberships )
    getCommitteesAndLegislators cn sessionId
    >>= getLatestMemberships log
    >>= updateRecords cn
    >>= clearMembershipCache
    |> haltOnFail log UpdateMemberships
