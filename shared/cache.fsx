#load "./logging.fsx"
#r "../packages/Microsoft.ApplicationInsights/lib/net45/Microsoft.ApplicationInsights.dll"

#r "../packages/StackExchange.Redis/lib/net45/StackExchange.Redis.dll"

namespace IgaTracker

module Cache =

    open StackExchange.Redis
    open IgaTracker.Logging

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
