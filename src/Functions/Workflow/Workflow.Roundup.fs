module Ptp.Workflow.Roundup

open Ptp.Common.Core
open Ptp.Common.Database
open Chessie.ErrorHandling

let digestUsersQuery = """
SELECT Id 
FROM users 
WHERE DigestType in (1,2)
    AND Email IS NOT NULL"""

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