module Ptp.Queries

    [<Literal>]
    let SessionIdSubQuery = """(SELECT TOP 1 Id FROM [Session] WHERE Active = 1)"""
    
    [<Literal>]
    let SelectActionsRequiringNotification = """SELECT a.Id, a.BillId, a.Description, a.ActionType, a.Chamber
FROM [Action] a
JOIN UserBill ub on a.BillId = ub.BillId
WHERE a.Id in @Ids and (ub.ReceiveAlertEmail = 1 or ub.ReceiveAlertSms = 1)
GROUP BY a.Id, a.BillId, a.Description, a.ActionType, a.Chamber"""

    [<Literal>]
    let SelectScheduledActionsRequiringNotification = """SELECT a.Id, a.BillId, a.ActionType, a.Chamber, a.Date, a.[Start], a.[End], a.Location
FROM ScheduledAction a
JOIN UserBill ub on a.BillId = ub.BillId
WHERE a.Id in @Ids and (ub.ReceiveAlertEmail = 1 or ub.ReceiveAlertSms = 1)
GROUP BY a.Id, a.BillId, a.ActionType, a.Chamber, a.Date, a.[Start], a.[End], a.Location"""

    [<Literal>]
    let SelectBillIdsAndNames = """SELECT Id, Name
FROM Bill b
WHERE b.SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)"""

    [<Literal>]
    let SelectCommitteeChamberNamesAndLinks = """SELECT Name, Link, Chamber 
FROM Committee 
WHERE SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)"""

    [<Literal>]
    let SelectActionLinksOccuringAfterDate = """SELECT Link
FROM Action 
WHERE Date >= @Date"""

    [<Literal>]
    let FetchAllBillStatus = """SELECT
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
"""

    [<Literal>]
    let FetchBillStatusForBills = FetchAllBillStatus + """ 
 WHERE 
    b.SessionId = (SELECT TOP 1 Id FROM Session WHERE Active = 1)
    AND b.Id IN @Ids
ORDER BY b.Name"""

    [<Literal>]
    let FetchBillStatusForUser = FetchAllBillStatus + """ 
 WHERE 
    b.SessionId = (SELECT TOP 1 Id FROM Session WHERE Active = 1)
    AND  b.Id IN (SELECT BillId FROM UserBill WHERE UserId = @Id)
 ORDER BY b.Name"""

 
    [<Literal>]
    let FetchDigestUsers = """SELECT u.Id, u.Name, u.DigestType, u.Email 
FROM [Users] u
WHERE 
	DigestType = 2 
	OR (
		DigestType = 1 
		AND EXISTS (SELECT 1 FROM UserBill ub WHERE ub.UserId = u.Id))"""

    [<Literal>]
    let FetchNewDeadBills = """DECLARE @date Date
SET @date = GetDate()
SELECT b.Id
FROM Bill b
WHERE
	b.IsDead = 0 
	AND b.SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)
	AND NOT ( 
		b.Chamber = 1 AND
		(
				(@date < '2017-02-21' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 1 AND a.Description like '%adopted%'))
			AND (@date < '2017-02-23' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 2 AND a.Description like '%engrossed%'))
			AND (@date < '2017-02-27' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 3 AND a.Description like '%passed%'))
			AND (@date < '2017-04-03' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 1 AND a.Description like '%adopted%'))
			AND (@date < '2017-04-05' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 2 AND a.Description like '%engrossed%'))
			AND (@date < '2017-04-06' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 3 AND a.Description like '%passed%'))
		)
	)
	AND	NOT (
		b.Chamber = 2 AND
		(
				(@date < '2017-01-19' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 4))
			AND	(@date < '2017-02-23' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 1 AND a.Description like '%adopted%'))
			AND (@date < '2017-02-27' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 2 AND a.Description like '%engrossed%'))
			AND (@date < '2017-02-28' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 3 AND a.Description like '%passed%'))
			AND (@date < '2017-04-03' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 1 AND a.Description like '%adopted%'))
			AND (@date < '2017-04-05' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 2 AND a.Description like '%engrossed%'))
			AND (@date < '2017-04-06' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 3 AND a.Description like '%passed%'))
		)
	)
	AND LEFT(Name,2) IN ('HB', 'SB')"""

    [<Literal>]
    let UpdateDeadBills = """UPDATE Bill SET IsDead = 1 WHERE Id IN @Ids; 
SELECT b.Id, b.Name, b.Title, b.Chamber, b.IsDead 
FROM Bill b
JOIN UserBill ub ON ub.BillId = b.Id
WHERE b.Id in @Ids and (ub.ReceiveAlertEmail = 1 or ub.ReceiveAlertSms = 1)
GROUP BY b.Id, b.Name, b.Title, b.Chamber, b.IsDead"""