module Ptp.Formatting

open Ptp.Core
open Ptp.Model

/// formats a bill number for print. Ex: "1004", "269"
let printBillNumber' (billName:string) = 
    billName.Substring(2,4).TrimStart('0')

/// formats a bill number for print. Ex: "1004", "269"
let printBillNumber (bill:Bill) = 
    printBillNumber' bill.Name

/// formats a bill name for print. Ex: "HB 1004", "SB 269"
let printBillName' (billName:string) = 
    sprintf "%s %s" (billName.Substring(0,2)) (printBillNumber' billName)

/// formats a bill name for print. Ex: "HB 1004", "SB 269"
let printBillName (bill:Bill) =
    printBillName' bill.Name

/// formats a bill name and title for print. Ex: "HB 1004 ('Biannual Budget')"
let printBillNameAndTitle bill =
    sprintf "%s ('%s')" (printBillName bill) (bill.Title.TrimEnd('.'))

/// a markdown-formatted link to the bill.
let webLink (bill:Bill) = 
    let session = bill.Link |> split "/" |> List.item 0
    let chamber = bill.Chamber.ToString().ToLower()
    let number = printBillNumber bill
    sprintf "https://iga.in.gov/legislative/%s/bills/%s/%s" session chamber number

/// A markdown bill link and title 
let markdownBillHrefAndTitle bill =
    sprintf "[%s](%s) ('%s')" (printBillName bill) (webLink bill) (bill.Title.TrimEnd('.'))

/// A simple email subject, 'Update on <bill name> (<bill title>)'
let formatSubject bill =
    sprintf "Update on %s" (printBillNameAndTitle bill)
