module Ptp.Workflow.Roundup

open Ptp.Common.Core
open Ptp.Common.Database
open Chessie.ErrorHandling

let digestUsersQuery = """
( 
SELECT distinct u.Id
FROM users u
JOIN UserBill ub on ub.UserId = u.Id
JOIN Bill b ON ub.BillId = b.Id
JOIN Session s on b.SessionId = s.Id
WHERE 
    s.Active = 1
    AND u.DigestType = 1
    AND u.Email IS NOT NULL
)
UNION
(
SELECT distinct u.Id
FROM users u
WHERE u.DigestType = 2
    AND u.Email IS NOT NULL
)"""

let fetchDigestUsers() = 
    dbQuery<int> digestUsersQuery

let nextSteps result =
    match result with
    | Ok (users, msgs) ->
        let sendNotfications = 
            users 
            |> Seq.map GenerateRoundupNotification 
            |> NextWorkflow
        Next.Succeed(sendNotfications, msgs)
    | Bad msgs ->       
        Next.FailWith(msgs)

let workflow() =
    fetchDigestUsers()
    |> nextSteps