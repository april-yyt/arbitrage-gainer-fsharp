open Azure.Messaging.ServiceBus
open Azure.Identity

let namespace = "ArbitrageGainer.servicebus.windows.net"

let sendMessageAsync(queueName : string, messageContent: string) =
    let client = ServiceBusClient(namespace, DefaultAzureCredential())
    let sender = client.CreateSender(queueName)
    let serviceBusMessage = new ServiceBusMessage(messageContent : string)

    sender.SendMessageAsync(serviceBusMessage).Wait()

    sender.DisposeAsync().AsTask().Wait()
    client.DisposeAsync().AsTask().Wait()
        
let receiveMessageAsync(queueName : string) =
    let client = ServiceBusClient(namespace, DefaultAzureCredential())
    let receiver = client.CreateReceiver(queueName)
    let receivedMessage = receiver.ReceiveMessageAsync().Result

    receiver.DisposeAsync().AsTask().Wait()
    client.DisposeAsync().AsTask().Wait()

    match receivedMessage with
    | null -> ""
    | _ -> receivedMessage.Body.ToString()
