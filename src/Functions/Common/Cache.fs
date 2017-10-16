module Ptp.Cache

open Ptp.Core
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

[<Literal>]
let LegislatorsKey = """laravel:legislators"""

[<Literal>]
let MembershipsKey = """laravel:memberships"""

let envPostfix = env "Redis.CacheKeyPostfix"

let config = 
    let cfg = 
        env "Redis.ConnectionString" 
        |> ConfigurationOptions.Parse
    cfg.ConnectTimeout <- 15000
    cfg

let delete key =
    let cacheKey = sprintf "%s%s" key envPostfix
    use muxer = config |> ConnectionMultiplexer.Connect
    let db = muxer.GetDatabase(0)
    cacheKey
    |> RedisKey.op_Implicit
    |> db.KeyDeleteAsync
    |> muxer.Wait
    |> ignore
    
let invalidateCache key seq =
    match (Seq.isEmpty seq) with
    | true -> ()
    | false -> delete key
    seq

let invalidateCache' key seq =
    let op() =
        invalidateCache key seq
    tryWarn op seq CacheInvalidationError