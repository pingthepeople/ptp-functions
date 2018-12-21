module Ptp.Workflow.DeadBill

open Chessie.ErrorHandling
open Ptp.Core
open Ptp.Database

[<Literal>]
let fetchNewDeadBillsQuery = """
SELECT b.Id
FROM Bill b
WHERE
	b.IsDead = 0 
	AND b.SessionId = (SELECT TOP 1 Id FROM Session ORDER BY Name Desc)
    AND LEFT(Name,2) IN ('HB', 'SB')
	AND NOT ( 
		b.Chamber = 1 AND
		(
				(@Date <= '2019-02-19' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 1 AND a.Description like '%adopted%'))   -- HB comm report
			AND (@Date <= '2019-02-21' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 2 AND a.Description like '%engrossed%')) -- HB 2nd reading
			AND (@Date <= '2019-02-25' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 3 AND a.Description like '%passed%'))    -- HB 3rd reading
			AND (@Date <= '2019-04-09' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 1 AND a.Description like '%adopted%'))   -- SB comm report
			AND (@Date <= '2019-04-11' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 2 AND a.Description like '%engrossed%')) -- SB 2nd reading
			AND (@Date <= '2019-04-15' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 3 AND a.Description like '%passed%'))    -- SB 3rd reading
		)
	)
	AND	NOT (
		b.Chamber = 2 AND
		(
				(@Date <= '2019-01-17' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 4))                                      -- assign to comm
			AND	(@Date <= '2019-02-21' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 1 AND a.Description like '%adopted%'))   -- SB comm report
			AND (@Date <= '2019-02-25' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 2 AND a.Description like '%engrossed%')) -- SB 2nd reading
			AND (@Date <= '2019-02-26' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 2 AND a.ActionType = 3 AND a.Description like '%passed%'))    -- SB 3rd reading
			AND (@Date <= '2019-04-11' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 1 AND a.Description like '%adopted%'))   -- HB comm report
			AND (@Date <= '2019-04-15' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 2 AND a.Description like '%engrossed%')) -- HB 2nd reading
			AND (@Date <= '2019-04-16' OR EXISTS (SELECT a.Id from Action a where a.BillId = b.Id AND a.Chamber = 1 AND a.ActionType = 3 AND a.Description like '%passed%'))    -- HB 3rd reading
		)
	)"""

[<Literal>]
let setDeadBillFlagCommand = """
UPDATE Bill
SET IsDead = 1
WHERE Id IN @Ids"""

let fetchNewDeadBills() = 
    dbParameterizedQuery<int> fetchNewDeadBillsQuery {Date=(datestamp())}

let setDeadBillFlag ids =
    dbCommandById setDeadBillFlagCommand ids

let nextSteps result =
    let nextWorkflow ids = 
        ids |> Seq.map GenerateDeadBillNotification
    result |> workflowContinues nextWorkflow

/// Flag bills that have recently died.
/// Trigger a notification workflow for each bill.
let workflow() =
    fetchNewDeadBills()
    >>= setDeadBillFlag
    |>  nextSteps