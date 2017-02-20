namespace IgaTracker

module Queries =

    [<Literal>]
    let InsertAction = """INSERT INTO Action(Description,Link,Date,ActionType,Chamber,BillId) 
VALUES (@Description,@Link,@Date,@ActionType,@Chamber,@BillId); 
SELECT CAST(SCOPE_IDENTITY() as int)"""

    [<Literal>]
    let SelectActionsRequiringNotification = """SELECT a.Id From Action a
JOIN UserBill ub on a.BillId = ub.BillId
WHERE a.Id in (@Ids)"""

    [<Literal>]
    let UpdateBillCommittees = """With BillCommittee_CTE (BillId, CommitteeId, Assigned)
As
(
	Select BillId, c.Id as CommitteeId, a.[Date] as Assigned
	from Action a
	Join Committee c 
		on c.Name = a.Description
		and c.Chamber = a.Chamber
	where a.ActionType = 4
)	
INSERT INTO BillCommittee 
	(BillId, CommitteeId, Assigned)
SELECT BillId, CommitteeId, Assigned
	FROM BillCommittee_CTE cte
WHERE NOT EXISTS(
	SELECT tbl.Id
	FROM BillCommittee tbl
	WHERE 
		tbl.BillId = cte.BillId
		AND tbl.CommitteeId = cte.CommitteeId
		AND tbl.Assigned = cte.Assigned)"""