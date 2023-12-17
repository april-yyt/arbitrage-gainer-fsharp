module Email

open System.Threading
open SendGrid
open SendGrid.Helpers.Mail
open ServiceBus
open Azure.Messaging.ServiceBus
open Azure.Identity


let sendEmailAsync (emailContent: string) =
    async {
        let client = SendGridClient("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")
        let from = EmailAddress("yutongya@andrew.cmu.edu", "Example User")
        let subject = "New Message from Service Bus"
        let toAddress = EmailAddress("aprilytyang@gmail.com", "Recipient User")
        let plainTextContent = emailContent
        let htmlContent = $"<strong>{emailContent}</strong>"
        let msg = MailHelper.CreateSingleEmail(from, toAddress, subject, plainTextContent, htmlContent)
        return! client.SendEmailAsync(msg) |> Async.AwaitTask
    }

let checkForMessagesAsync (queueName: string) =
    async {
        let client = ServiceBusClient(ns, DefaultAzureCredential())
        let receiver = client.CreateReceiver(queueName)
        
        while true do
            let! receivedMessage = receiver.ReceiveMessageAsync() |> Async.AwaitTask
            match receivedMessage with
            | null -> () // No message, continue
            | message ->
                let emailContent = message.Body.ToString()
                sendEmailAsync emailContent |> ignore
                do! receiver.CompleteMessageAsync(message) |> Async.AwaitTask

        receiver.DisposeAsync().AsTask().Wait()
        client.DisposeAsync().AsTask().Wait()
    }
