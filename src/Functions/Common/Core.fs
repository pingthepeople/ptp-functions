module Ptp.Core

open System

let (|StartsWith|_|) (p:string) (s:string) =
    if s.StartsWith(p) then
        Some(s.Substring(p.Length))
    else
        None
    
let (|EmptySeq|_|) a = 
    if Seq.isEmpty a then Some () else None

let isEmpty str = str |> String.IsNullOrWhiteSpace
let timestamp() = System.DateTime.Now.ToString("HH:mm:ss.fff")
let datestamp() = System.DateTime.Now.ToString("yyyy-MM-dd")
let sqlConStr() = System.Environment.GetEnvironmentVariable("SqlServer.ConnectionString")
