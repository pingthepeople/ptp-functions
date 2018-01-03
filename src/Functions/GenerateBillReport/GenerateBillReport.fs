module Ptp.GenerateBillReport

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open Ptp.Core
open Ptp.Model
open Ptp.Http
open Ptp.Database
open System.Net.Http

[<CLIMutable>]
type Body  = { Id : int }

[<Literal>]
let fetchBillStatusQuery = """
SELECT
	b.Name
	, b.Title
	, CASE WHEN LEN(b.Description) < 256 THEN b.Description ELSE LEFT(b.Description,256) + '...' END as 'Description'
	, STUFF( (SELECT '; ' + l.LastName 
                            FROM [Legislator] l
                            JOIN [LegislatorBill] lb 
                                ON lb.LegislatorId = l.Id
                                AND lb.BillId = b.Id                            
                            ORDER by lb.Position, l.LastName 
                            FOR XML PATH('')),
                            1, 1, '') as 'Authors'
	, CASE WHEN b.Chamber = 1 THEN 'House' ELSE 'Senate' END as 'OriginChamber'
	, (Select oc.Name from Committee oc Join BillCommittee obc on oc.Chamber = b.Chamber and b.Id = obc.BillID and oc.Id = obc.CommitteeId) as 'OriginCommittee'
	, (Select cc.Name from Committee cc Join BillCommittee cbc on cc.Chamber <> b.Chamber and b.Id = cbc.BillID and cc.Id = cbc.CommitteeId) as 'CrossoverCommittee'
	, STUFF( (SELECT '; ' + s.Name
                             FROM [Subject] s
							 Join [BillSubject] bs on s.Id = bs.SubjectId
							 WHERE bs.BillId = b.Id
							 --Order by s.Id
                             FOR XML PATH('')), 
                            1, 1, '') as 'Subjects'
	, replace(convert(varchar, ISNULL(aocr.Date,socr.Date), 102), '.', '-') as 'OriginCommitteeReading'
	, aocr.Description as 'OriginCommitteeReadingVote'
	, replace(convert(varchar, ISNULL(ao2r.Date,so2r.Date), 102), '.', '-') 'OriginSecondReading'
	, ao2r.Description as 'OriginSecondReadingVote'
	, replace(convert(varchar, ISNULL(ao3r.Date,so3r.Date), 102), '.', '-') 'OriginThirdReading'
	, ao3r.Description as 'OriginThirdReadingVote'
	, replace(convert(varchar, ISNULL(accr.Date,sccr.Date), 102), '.', '-') 'CrossoverCommitteeReading'
	, accr.Description 'CrossoverCommitteeReadingVote'
	, replace(convert(varchar, ISNULL(ac2r.Date,sc2r.Date), 102), '.', '-') 'CrossoverSecondReading'
	, ac2r.Description 'CrossoverSecondReadingVote'
	, replace(convert(varchar, ISNULL(ac3r.Date,sc3r.Date), 102), '.', '-') 'CrossoverThirdReading'
	, ac3r.Description 'CrossoverThirdReadingVote'
from Bill b
outer apply (Select Top 1 CAST([Date] AS DATE) [Date], Description from [Action] a where a.BillId = b.Id and ActionType = 1 and a.Chamber = b.Chamber) aocr
outer apply (Select Top 1 CAST([Date] AS DATE) [Date], Description from [Action] a where a.BillId = b.Id and ActionType = 2 and a.Chamber = b.Chamber) ao2r
outer apply (Select Top 1 CAST([Date] AS DATE) [Date], Description from [Action] a where a.BillId = b.Id and ActionType = 3 and a.Chamber = b.Chamber) ao3r
outer apply (Select Top 1 CAST([Date] AS DATE) [Date], Description from [Action] a where a.BillId = b.Id and ActionType = 1 and a.Chamber <> b.Chamber) accr
outer apply (Select Top 1 CAST([Date] AS DATE) [Date], Description from [Action] a where a.BillId = b.Id and ActionType = 2 and a.Chamber <> b.Chamber) ac2r
outer apply (Select Top 1 CAST([Date] AS DATE) [Date], Description from [Action] a where a.BillId = b.Id and ActionType = 3 and a.Chamber <> b.Chamber) ac3r
--
outer apply (Select Top 1 CAST([Date] AS DATE) [Date] from [ScheduledAction] sa where sa.BillId = b.Id and ActionType = 1 and sa.Chamber = b.Chamber) socr
outer apply (Select Top 1 CAST([Date] AS DATE) [Date] from [ScheduledAction] sa where sa.BillId = b.Id and ActionType = 2 and sa.Chamber = b.Chamber) so2r
outer apply (Select Top 1 CAST([Date] AS DATE) [Date] from [ScheduledAction] sa where sa.BillId = b.Id and ActionType = 3 and sa.Chamber = b.Chamber) so3r
outer apply (Select Top 1 CAST([Date] AS DATE) [Date] from [ScheduledAction] sa where sa.BillId = b.Id and ActionType = 1 and sa.Chamber <> b.Chamber) sccr
outer apply (Select Top 1 CAST([Date] AS DATE) [Date] from [ScheduledAction] sa where sa.BillId = b.Id and ActionType = 2 and sa.Chamber <> b.Chamber) sc2r
outer apply (Select Top 1 CAST([Date] AS DATE) [Date] from [ScheduledAction] sa where sa.BillId = b.Id and ActionType = 3 and sa.Chamber <> b.Chamber) sc3r
WHERE 
    b.SessionId = (SELECT TOP 1 Id FROM Session WHERE Active = 1)
    AND  b.Id IN (SELECT BillId FROM UserBill WHERE UserId = @Id)
ORDER BY b.Name
"""

let generateReport body = trial { 
    let! result = dbParameterizedQuery<BillStatus> fetchBillStatusQuery {Id=body.Id}
    return result
    }

let deserializeId = 
    validateBody<Body> "A user id is expected in the form '{ Id: INT }'"

let workflow req =
    (fun _ -> deserializeId req)
    >> bind generateReport

let Run(req: HttpRequestMessage, log: TraceWriter) =
    req
    |> workflow
    |> executeHttpWorkflow log HttpWorkflow.GenerateBillReport