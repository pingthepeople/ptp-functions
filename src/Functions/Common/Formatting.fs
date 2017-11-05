module Ptp.Formatting

open Ptp.Core
open Ptp.Model

/// formats a bill number for print ("1004", "207")
let prettyPrintBillNumber (bill:Bill) = 
    bill.Name.Substring(2,4).TrimStart('0')

/// formats a bill name for print ("HB 1004")
let prettyPrintBillName (bill:Bill) = 
    sprintf "%s %s" (bill.Name.Substring(0,2)) (prettyPrintBillNumber bill)

/// a markdown-formatted link to the bill.
let webLink (bill:Bill) = 
    let name = prettyPrintBillName bill
    let session = bill.Link |> split "/" |> List.item 0
    let chamber = bill.Chamber.ToString().ToLower()
    let number = prettyPrintBillNumber bill
    sprintf "[%s](https://iga.in.gov/legislative/%s/bills/%s/%s)" name session chamber number
