module ServiceBus

open Azure.Messaging.ServiceBus
open Azure.Identity

let ns = "ArbitrageGainer.servicebus.windows.net"

let sendMessageAsync(queueName : string, messageContent: string) =
    let client = ServiceBusClient(ns, DefaultAzureCredential())
    let sender = client.CreateSender(queueName)
    let serviceBusMessage = new ServiceBusMessage(messageContent : string)

    sender.SendMessageAsync(serviceBusMessage).Wait()

    sender.DisposeAsync().AsTask().Wait()
    client.DisposeAsync().AsTask().Wait()
        
// let receiveMessageAsync(queueName : string) =
//     let client = ServiceBusClient(ns, DefaultAzureCredential())
//     let receiver = client.CreateReceiver(queueName)
//     let receivedMessage = receiver.ReceiveMessageAsync().Result
//     receiver.CompleteMessageAsync(receivedMessage).Wait()
//     receiver.DisposeAsync().AsTask().Wait()
//     client.DisposeAsync().AsTask().Wait()
//
//     match receivedMessage with
//     | null -> ""
//     | _ -> receivedMessage.Body.ToString()

let receiveMessageAsync(queueName : string) =
    let client = ServiceBusClient(ns, DefaultAzureCredential())
    let receiver = client.CreateReceiver(queueName)
    
    try
        let receivedMessage = receiver.ReceiveMessageAsync().Result
        match receivedMessage with
        | null -> 
            ""
        | message ->
            receiver.CompleteMessageAsync(message).Wait()
            message.Body.ToString()
    finally
        receiver.DisposeAsync().AsTask().Wait()
        client.DisposeAsync().AsTask().Wait()
