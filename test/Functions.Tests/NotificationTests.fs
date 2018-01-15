module NotificationTests

open Swensen.Unquote
open Xunit
open Xunit.Abstractions
open Ptp.Http
open Ptp.Core
open Ptp.Model
open Ptp.Notification
open Chessie.ErrorHandling
open Newtonsoft.Json

let testBody = "this is the message body"
let expectedHash = "95F1C48D1EDD2CB36B099F999E94FCC4BB0462FA4F8E6B650F0A94EEC4BA493E"
let testMsg = 
  { MessageType = MessageType.Email
    Subject = "Email to foo regarding bar"
    Body = testBody
    Recipient = ""
    Attachment = "" }
let testMail = {testMsg with MessageType=MessageType.Email; Recipient="user@ptp.org"}
let testSms =  {testMsg with MessageType=MessageType.SMS; Recipient="+12345678901"}

// successful functions
let sendMsgOk x = ok ""
let logDeliveryOk (msg,x) = ok (msg,Some("inserted"))

[<Fact>]
let ``fails if notification already sent``() =
    let logDelivery (msg,x) = ok (msg,None)
    let testWorkflow = Function.workflow logDelivery sendMsgOk sendMsgOk testMail
    test <@ testWorkflow() = Result.Bad([NotificationAlreadyDelivered]) @>

[<Fact>]
let ``send email msg ok``()=
    let sendSmsFail x = failwith "Should have sent email"
    let testWorkflow = Function.workflow logDeliveryOk sendSmsFail sendMsgOk testMail
    test <@ testWorkflow() = Result.Ok("",[]) @>
    
[<Fact>]
let ``send email msg fail``()=
    let sendEmailFail x = "fail!" |> NotificationDeliveryError |> fail
    let testWorkflow = Function.workflow logDeliveryOk sendMsgOk sendEmailFail testMail 
    test <@ testWorkflow() = Result.Bad([NotificationDeliveryError("fail!")]) @>

[<Fact>]
let ``send sms msg ok``()=
    let sendEmailFail x = failwith "Should have sent sms"
    let testWorkflow = Function.workflow logDeliveryOk sendMsgOk sendEmailFail testSms
    test <@ testWorkflow() = Result.Ok("",[]) @>
    
[<Fact>]
let ``send sms msg fail``()=
    let sendSmsFail x = "fail!" |> NotificationDeliveryError |> fail
    let testWorkflow = Function.workflow logDeliveryOk sendSmsFail sendMsgOk testSms
    test <@ testWorkflow() = Result.Bad([NotificationDeliveryError("fail!")]) @>
    