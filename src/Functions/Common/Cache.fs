module Ptp.Common.Cache

open Chessie.ErrorHandling
open Ptp.Common.Core
open StackExchange.Redis

[<Literal>]
let BillsKey = """bills"""

[<Literal>]
let SubjectsKey = """subjects"""

[<Literal>]
let CommitteesKey = """committees"""

[<Literal>]
let ActionsKey = """actions"""

[<Literal>]
let ScheduledActionsKey = """scheduled_actions"""

[<Literal>]
let LegislatorsKey = """legislators"""

[<Literal>]
let MembershipsKey = """memberships"""

let envPostfix = env "Redis.CacheKeyPostfix"

let config = 
    let cfg = 
        env "Redis.ConnectionString" 
        |> ConfigurationOptions.Parse
    cfg.ConnectTimeout <- 15000
    cfg

let invalidateCache key =
    let cacheKey = sprintf "%s:%s" envPostfix key 
    use muxer = config |> ConnectionMultiplexer.Connect
    let db = muxer.GetDatabase(0)
    cacheKey
    |> RedisKey.op_Implicit
    |> db.KeyDeleteAsync
    |> muxer.Wait
    |> ignore

let tryInvalidateCache key a =
    let op() = invalidateCache key
    tryTee op a CacheInvalidationError

let tryInvalidateCacheIfAny key seq =
    match (Seq.isEmpty seq) with
    | true -> ok seq
    | false -> tryInvalidateCache key seq