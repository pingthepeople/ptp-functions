module Ptp.Formatting

open Ptp.Core
open Ptp.Model

/// formats a bill number for print. Ex: "1004", "269"
let printBillNumber (bill:Bill) = 
    bill.Name.Substring(2,4).TrimStart('0')

/// formats a bill name for print. Ex: "HB 1004", "SB 269"
let printBillName (bill:Bill) = 
    sprintf "%s %s" (bill.Name.Substring(0,2)) (printBillNumber bill)

/// formats a bill name and title for print. Ex: "HB 1004 ('Biannual Budget')"
let printBillNameAndTitle bill =
    sprintf "%s ('%s')" (printBillName bill) bill.Title

/// a markdown-formatted link to the bill.
let webLink (bill:Bill) = 
    let session = bill.Link |> split "/" |> List.item 0
    let chamber = bill.Chamber.ToString().ToLower()
    let number = printBillNumber bill
    sprintf "https://iga.in.gov/legislative/%s/bills/%s/%s" session chamber number