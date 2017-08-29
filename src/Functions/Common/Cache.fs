module Ptp.Cache

open Ptp.Logging
open StackExchange.Redis

[<Literal>]
let BillsKey = """laravel:bills"""

[<Literal>]
let SubjectsKey = """laravel:subjects"""

[<Literal>]
let CommitteesKey = """laravel:committees"""

[<Literal>]
let ActionsKey = """laravel:actions"""

[<Literal>]
let ScheduledActionsKey = """laravel:scheduled_actions"""

let delete (key:string) =
    let func() = 
        let muxer  = 
            System.Environment.GetEnvironmentVariable("Redis.ConnectionString")
            |> ConnectionMultiplexer.Connect
        let db = muxer.GetDatabase(0)
        (RedisKey.op_Implicit key) 
        |> db.KeyDeleteAsync
        |> muxer.Wait

    trackDependency "redis" key func |> ignore
    
let invalidateCache key seq =
    match (Seq.isEmpty seq) with
    | true -> ()
    | false -> delete key
