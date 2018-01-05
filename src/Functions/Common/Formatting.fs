module Ptp.Formatting

open Ptp.Core
open Ptp.Model
open System


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

/// Format an event location (ex: "the House Chamber", "State House Room 130")
let formatEventLocation location = 
        match location with 
        | "House Chamber" -> "the House Chamber"
        | "Senate Chamber" -> "the Senate Chamber"
        | room -> sprintf "State House %s" room

/// Format an event date (ex: "Friday 1/5/2018")
let formatEventDate (date:System.DateTime) = date.ToString("dddd, d MMM yyyy")

/// Format a 12-hour time (ex: "10:30 AM", "3:30 PM")
let formatTimeOfDay time = System.DateTime.Parse(time).ToString("h:mm tt")

/// Format an event time (ex: "from 10:30 AM - 3:30 PM", "upon adjournment of Session")
let formatEventTime startTime endTime customStart = 
    if (hasValue startTime && hasValue endTime)
    then sprintf " from %s - %s" (formatTimeOfDay startTime) (formatTimeOfDay endTime)
    else if (hasValue startTime)
    then sprintf " starting at %s" (formatTimeOfDay startTime)
    else if (hasValue customStart)
    then sprintf " %s" (customStart.Replace("Upon", "upon").Replace("Adjournment", "adjournment"))
    else ""

let formatCommitteeName chamber committeeName = 
    sprintf "%A %s Committee" chamber committeeName
