module ServiceBus

open Azure.Messaging.ServiceBus
open Azure.Identity
open Azure
open Azure.Data.Tables
open Azure.Identity

let ns = "ArbitrageGainer.servicebus.windows.net"

let sendMessageAsync(queueName : string, messageContent: string) =
    let client = ServiceBusClient(ns, DefaultAzureCredential())
    let sender = client.CreateSender(queueName)
    let serviceBusMessage = new ServiceBusMessage(messageContent : string)

    sender.SendMessageAsync(serviceBusMessage).Wait()

    sender.DisposeAsync().AsTask().Wait()
    client.DisposeAsync().AsTask().Wait()
        
let receiveMessageAsync(queueName : string) =
    let client = ServiceBusClient(ns, DefaultAzureCredential())
    let receiver = client.CreateReceiver(queueName)
    let receivedMessage = receiver.ReceiveMessageAsync().Result
    receiver.DisposeAsync().AsTask().Wait()
    client.DisposeAsync().AsTask().Wait()

    match receivedMessage with
    | null -> ""
    | _ -> receivedMessage.Body.ToString()



type OrderEntity() =
    inherit TableEntity()
    member val OrderID: string = null with get, set
    member val Currency: string = null with get, set
    member val Price: double = 0.0 with get, set
    member val OrderType: string = null with get, set
    member val Quantity: int = 0 with get, set
    member val Exchange: string = null with get, set
    member val Status: string = null with get, set
let tableName = "Orders"
let tableServiceClient = TableServiceClient(ns, DefaultAzureCredential())
let tableClient = tableServiceClient.GetTableClient(tableName) 


// Function to create table if it does not exist
let createTableIfNotExists () =
    if not (tableServiceClient.QueryTables(tableName).Any()) then
        tableServiceClient.CreateTable(tableName)
    else
        printfn "Table already exists"

createTableIfNotExists() // Call this function at the start

let addOrderToDatabase (order: OrderEntity) : bool =
    let response = tableClient.AddEntity order
    response.IsSucceeded

let getOrderFromDatabase (partitionKey: string, rowKey: string) : OrderEntity option =
    let response = tableClient.GetEntity<OrderEntity>(partitionKey, rowKey)
    if response.IsSucceeded then Some(response.Value) else None

let updateOrderInDatabase (order: OrderEntity) : bool =
    let response = tableClient.UpdateEntity(order, ETag.All, TableUpdateMode.Replace)
    response.IsSucceeded

let deleteOrderFromDatabase (partitionKey: string, rowKey: string) : bool =
    let response = tableClient.DeleteEntity(partitionKey, rowKey)
    response.IsSucceeded
