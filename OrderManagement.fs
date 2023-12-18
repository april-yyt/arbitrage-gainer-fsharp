module OrderManagement

open Azure
open Azure.Data.Tables
open Azure.Identity
open System
open NUnit.Framework
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running
open Newtonsoft.Json
open System.Net.Http
open System.Text
open System.Collections.Generic
open BitfinexAPI
open KrakenAPI
open BitstampAPI
open FSharp.Data
open ServiceBus
open FSharp.CloudAgent
open FSharp.CloudAgent.Messaging
open FSharp.CloudAgent.Connections
open System.Threading.Tasks
open Azure.Messaging.ServiceBusopen FSharp.CloudAgent
open FSharp.CloudAgent.Messaging
open FSharp.CloudAgent.Connections
open System.Threading.Tasks
open Azure.Messaging.ServiceBus

open Akka.FSharp
open Akka.Actor
open Akka.Cluster
open Akka.Remote
open Akka.Configuration


// -------------------------
// Types and Event Definitions
// -------------------------

// Type definitions
type Currency = string
type Price = float
type OrderType = string
type Quantity = float
type Exchange = string
type OrderID = string
type FulfillmentStatus = string
    
type OrderDetails = { 
    Currency: Currency
    Price: Price
    OrderType: OrderType
    Quantity: Quantity
    Exchange: Exchange
}

// pass in OrderDetails for creating a new order, extract orderID after order creation
// pass in orderID for querying order status(orderID and orderdetails.quantity for kraken)
// returns OrderUpdate for querying order status
type OrderUpdate = { OrderID: OrderID; OrderDetails: OrderDetails ; FulfillmentStatus: FulfillmentStatus ; RemainingQuantity: float }
type OrderStatusUpdate = {
    FulfillmentStatus: FulfillmentStatus
    RemainingQuantity: float
}

type Event = 
    | OrderFulfillmentUpdated of FulfillmentStatus
    | UserNotificationSent of OrderID
    | OrderInitiated of OrderID
    | OrderProcessed of OrderUpdate

type OrderEmitted = OrderDetails list

// --------------------------
// DB Configuration Constants
// --------------------------

let storageConnString = "DefaultEndpointsProtocol=https;AccountName=18656team6;AccountKey=qJTSPfoWo5/Qjn9qFcogdO5FWeIYs9+r+JAp+6maOe/8duiWSQQL46120SrZTMusJFi1WtKenx+e+AStHjqkTA==;EndpointSuffix=core.windows.net" // This field will later use the connection string from the Azure console.
let tableClient = TableServiceClient storageConnString
let table = tableClient.GetTableClient "OrderManagement"
table.CreateIfNotExists () |> ignore

// --------------------------
// DB Schema
// --------------------------

type OrderEntity(orderID: OrderID, currency: Currency, price: Price, orderType: OrderType, quantity: Quantity, exchange: Exchange, status: FulfillmentStatus, remainingQuantity: Quantity) =
    interface ITableEntity with
        member val ETag = ETag "" with get, set
        member val PartitionKey = exchange with get, set
        member val RowKey = orderID with get, set
        member val Timestamp = Nullable() with get, set
    new() = OrderEntity("", "", 0.0, "Buy", 0.0, "", "FullyFulfilled", 0.0)
    member val OrderID = orderID with get, set
    member val Currency = currency with get, set
    member val Price = price with get, set
    member val OrderType = orderType with get, set
    member val Quantity = quantity with get, set
    member val Exchange = exchange with get, set
    member val Status = status with get, set
    member val RemainingQuantity = remainingQuantity with get, set

// --------------------------
// DB Operations
// --------------------------

let addOrderToDatabase (orderDetails: OrderDetails, orderID: OrderID) : bool =
    table.CreateIfNotExists() |> ignore
    let order = OrderEntity(orderID, orderDetails.Currency, orderDetails.Price, orderDetails.OrderType, orderDetails.Quantity, orderDetails.Exchange, "OneSideFilled", orderDetails.Quantity)
    try
        table.AddEntity(order) |> ignore
        true 
    with
    | :? Azure.RequestFailedException as ex -> 
        printfn "Error adding entity: %s" ex.Message
        false 


let updateOrderStatus (exchange: Exchange, orderID: OrderID, newStatus: FulfillmentStatus, remainingQuantity: Quantity) : bool =
    let partitionKey = match exchange with | exchange -> exchange.ToString() 
    let rowKey = match orderID with | id -> id.ToString() 
    try
        let response = table.GetEntity<OrderEntity>(partitionKey, rowKey)
        let entity = response.Value
        entity.Status <- newStatus
        entity.RemainingQuantity <- remainingQuantity
        table.UpdateEntity(entity, ETag.All, TableUpdateMode.Replace) |> ignore
        true 
    with
    | :? Azure.RequestFailedException as ex -> 
        printfn "Error updating entity: %s" ex.Message
        false 


// -------------------------
// Helper Function Definitions
// -------------------------

// Helper Functions for Creating Testing OrderIDs
let random = Random()
let generateRandomID (length: int) (isNumeric: bool) =
    let chars = if isNumeric then "0123456789" else "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    let randomChars = Array.init length (fun _ -> chars.[random.Next(chars.Length)])
    String(randomChars)
    
let generateRandomFulfillmentStatus () =
    let statuses = ["FullyFulfilled"; "PartiallyFulfilled"; "OneSideFilled"]
    let index = random.Next(statuses.Length)
    statuses.[index]
    
let generateRandomRemainingQuantity (status: string) (maxQuantity: float) =
    match status with
    | "FullyFulfilled" -> 0.0
    | "PartiallyFulfilled" -> random.NextDouble() * maxQuantity
    | "OneSideFilled" -> maxQuantity
    | _ -> maxQuantity
    
let createOrderTesting (orderDetails: OrderDetails) : OrderID =
    match orderDetails.Exchange with
    | "Kraken" -> generateRandomID 15 false
    | "Bitstamp" -> generateRandomID 4 true
    | "Bitfinex" -> generateRandomID 9 true
    | _ -> "010101010"


let processOrderUpdateTesting (orderID: OrderID) (orderDetails: OrderDetails) : Async<Result<OrderStatusUpdate, string>> =
    async {
        printfn "Order update retrieval"
        do! Async.Sleep(3000)

        let simulatedFulfillmentStatus = generateRandomFulfillmentStatus ()
        let simulatedRemainingQuantity = generateRandomRemainingQuantity simulatedFulfillmentStatus orderDetails.Quantity

        let updateResult = updateOrderStatus (orderDetails.Exchange, orderID, simulatedFulfillmentStatus, simulatedRemainingQuantity)
        if updateResult then
            printfn "Order status updated in database"
            let orderStatusUpdate = {
                FulfillmentStatus = simulatedFulfillmentStatus
                RemainingQuantity = simulatedRemainingQuantity
            }
            return Result.Ok orderStatusUpdate
        else
            return Result.Error "Failed to update order in database"
    }


// Helper Function for submitting a new order on an exchange
// Involves Making API Calls and Parsing Data
let submitOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderID, string>> = 
    async {
        try 
            match orderDetails.Exchange with
            | "Bitfinex" -> 
                let orderType = "MARKET"
                let symbol = "t" + orderDetails.Currency
                let orderResult = BitfinexAPI.submitOrder orderType symbol (orderDetails.Quantity.ToString()) (orderDetails.Price.ToString()) |> Async.RunSynchronously
                match orderResult with
                | Some responseString ->
                    let result = BitfinexAPI.parseBitfinexResponse responseString
                    match result with
                    | Result.Ok orderID ->
                        let orderIDResult = orderID.ToString()
                        printfn "Order ID: %s" orderIDResult
                        return Result.Ok orderIDResult
                    | Result.Error errMsg ->
                        printfn "Error: %s" errMsg
                        return Result.Error errMsg
                | None ->
                    return Result.Error "Failed to submit order to Bitfinex"

            | "Kraken" ->
                printfn "processing kraken"
                printfn "pair %A" orderDetails.Currency
                let orderType = "market"
                let pair = "XX" + orderDetails.Currency
                let orderResult = KrakenAPI.submitOrder pair orderType (orderDetails.Quantity.ToString()) (orderDetails.Price.ToString()) |> Async.RunSynchronously
                match orderResult with
                | Some responseString ->
                    let result = KrakenAPI.parseKrakenSubmitResponse responseString
                    match result with
                    | Result.Ok txid ->
                        printfn "Transaction ID: %s" txid
                        return Result.Ok txid
                    | Result.Error errMsg ->
                        printfn "Error: %s" errMsg
                        return Result.Error errMsg
                | None ->
                    return Result.Error "Failed to submit order to Kraken"

            | "Bitstamp" -> 
                let action = match orderDetails.OrderType with
                                | "Buy" -> BitstampAPI.buyMarketOrder
                                | "Sell" -> BitstampAPI.sellMarketOrder
                let orderResult = action orderDetails.Currency (orderDetails.Quantity.ToString()) None |> Async.RunSynchronously
                match orderResult with
                | Some responseString ->
                    printfn "Response from Bitstamp: %s" responseString
                    return Result.Ok responseString
                | None ->
                    return Result.Error "Failed to submit order to Bitstamp"

            | _ -> 
                return Result.Error "Unsupported exchange"
        with
        | ex -> 
            return Result.Error (sprintf "An exception occurred: %s" ex.Message)
    }

// Helper function to parse Bitfinex response and store in database
let processBitfinexResponse (jsonString: string) (originalOrderQuantity: float) : Result<OrderStatusUpdate, string> =
    // passing in the response and historical order amt to compare and match execution status
    match BitfinexAPI.parseBitfinexOrderStatusResponse jsonString with
    | Result.Ok executedAmount ->
        let remainingAmount = originalOrderQuantity - executedAmount
        let statusUpdate = {
            FulfillmentStatus = 
                if executedAmount = originalOrderQuantity then "FullyFulfilled"
                elif executedAmount > 0.0 then "PartiallyFulfilled"
                else "OneSideFilled"
            RemainingQuantity = remainingAmount
        }
        Result.Ok statusUpdate
    | Result.Error errMsg ->
        Result.Error errMsg

// Helper function to parse Kraken response and store in database
let processKrakenResponse (jsonString: string) : Result<OrderStatusUpdate, string> =
    match KrakenAPI.parseKrakenOrderResponse jsonString with
    | Result.Ok (orderId, vol, vol_exec) ->
        let volFloat = float vol
        let volExecFloat = float vol_exec
        let remainingAmount = volFloat - volExecFloat
        let statusUpdate = {
            FulfillmentStatus = 
                if volExecFloat >= volFloat then "FullyFulfilled"
                else if volExecFloat > 0.0 then "PartiallyFulfilled"
                else "OneSideFilled"
            RemainingQuantity = remainingAmount
        }
        Result.Ok statusUpdate
    | Result.Error errMsg ->
        Result.Error errMsg


// Helper function to parse Bitstamp response and store in database
let processBitstampResponse (jsonString: string) : Result<OrderStatusUpdate, string> =
    match BitstampAPI.parseResponseOrderStatus jsonString with
    | Result.Ok orderResponse ->
        let amountRemaining = float orderResponse.AmountRemaining
        let statusUpdate = {
            FulfillmentStatus = 
                if amountRemaining = 0.0 then "FullyFulfilled"
                else if amountRemaining > 0.0 then "PartiallyFulfilled"
                else "OneSideFilled"
            RemainingQuantity = amountRemaining
        }
        Result.Ok statusUpdate
    | Result.Error errMsg ->
        Result.Error errMsg


// -------------------------
// Main Workflows
// -------------------------

// Workflow: Submitting Order to Exchange and Persisting the Orders in DataBase
let createOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderID, string>> =
    async {
        let! result = submitOrderAsync orderDetails
        match result with
        | Result.Ok orderID ->
            // return Result.Ok orderID
            // Database operation to store order details in db
            let dbResult = addOrderToDatabase (orderDetails, orderID) 
            match dbResult with
            | true -> 
                return Result.Ok orderID
            | false ->
                return Result.Error "Failed to add order to database"
        | Result.Error errMsg ->
            return Result.Error errMsg
    }


// Workflow: Retrieve and Handle Order Updates
let processOrderUpdate (orderID: OrderID) (orderDetails: OrderDetails) : Async<Result<Event, string>> =
    async {
        // Wait for 3 seconds to get the order status updates
        printfn "Sleeping for 3 Secs"
        do! Async.Sleep(3000)

        let updateOrderInDatabase (orderStatusUpdate: OrderStatusUpdate) =
            match updateOrderStatus (orderDetails.Exchange, orderID, orderStatusUpdate.FulfillmentStatus, orderStatusUpdate.RemainingQuantity) with
            | true ->
                Result.Ok (OrderFulfillmentUpdated orderStatusUpdate.FulfillmentStatus)
            | false ->
                Result.Error "Failed to update order in database"

        let handleStatusUpdate orderStatusUpdate =
            async {
                match orderStatusUpdate.FulfillmentStatus with
                | "FullyFulfilled"  ->
                    return updateOrderInDatabase orderStatusUpdate

                | "PartiallyFulfilled" ->
                    let newOrderDetails = { orderDetails with Quantity = orderStatusUpdate.RemainingQuantity }
                    let! createOrderResult = createOrderAsync newOrderDetails
                    match createOrderResult with
                    | Result.Ok _ ->
                        // Even if new order is created, we consider the current order as processed.
                        return Result.Ok (OrderProcessed { OrderID = orderID; OrderDetails = orderDetails; FulfillmentStatus = "FullyFulfilled"; RemainingQuantity = 0.0 })
                    | Result.Error errMsg ->
                        return Result.Error errMsg
                        
                | "OneSideFilled" ->
                    return updateOrderInDatabase orderStatusUpdate
                    
                |_ ->
                    return updateOrderInDatabase orderStatusUpdate
            }
            
        match orderDetails.Exchange with
        | "Bitfinex" ->
            let! bitfinexResponseOption = BitfinexAPI.retrieveOrderTrades "DOTUSD" 1747566428
            match bitfinexResponseOption with
            | Some bitfinexResponse ->
                match processBitfinexResponse bitfinexResponse orderDetails.Quantity with
                | Result.Ok orderStatusUpdate ->
                    return! handleStatusUpdate orderStatusUpdate
                | Result.Error errMsg ->
                    return Result.Error errMsg
            | None ->
                return Result.Error "Failed to retrieve Bitfinex trades"

        | "Kraken" ->
            let! krakenResponse = KrakenAPI.queryOrdersInfo "OU22CG-KLAF2-FWUDD7" true None
            match krakenResponse with
            | Some response ->
                match processKrakenResponse response with
                | Result.Ok orderStatusUpdate ->
                    return! handleStatusUpdate orderStatusUpdate
                | Result.Error errMsg ->
                    return Result.Error errMsg
            | None ->
                return Result.Error "Failed to retrieve Kraken trades"

        | "Bitstamp" ->
            let! bitstampResponse = BitstampAPI.orderStatus "1234"
            match bitstampResponse with
            | Some response ->
                match processBitstampResponse response with
                | Result.Ok orderStatusUpdate ->
                    return! handleStatusUpdate orderStatusUpdate
                | Result.Error errMsg ->
                    return Result.Error errMsg
            | None ->
                return Result.Error "Failed to retrieve Bitstamp trades"

        | _ -> 
            return Result.Error "Unsupported exchange"
    }

// Main Workflow of Order Management: to create and process orders
let createAndProcessOrders (ordersEmitted: OrderEmitted) : Async<Result<OrderUpdate list, string>> =
    async {
        let resultsAsync = 
            ordersEmitted 
            |> List.map (fun orderDetails ->
                async {
                    let! createResult = createOrderAsync orderDetails
                    match createResult with
                    | Result.Ok orderID ->
                        let! updateResult = processOrderUpdate orderID orderDetails
                        match updateResult with
                        | Result.Ok event ->
                            match event with
                            | OrderProcessed orderUpdate ->
                                return Result.Ok orderUpdate
                            | _ ->
                                return Result.Error "Received unexpected event type"
                        | Result.Error errMsg ->
                            return Result.Error errMsg
                    | Result.Error errMsg ->
                        return Result.Error errMsg 
                })
            |> Async.Parallel

        let! results = resultsAsync 

        // Separate successful results and errors
        let successes, errors = results |> Array.partition (fun result ->
            match result with 
            | Result.Ok _ -> true 
            | Result.Error _ -> false)

        if errors |> Array.isEmpty then 
            // If all operations were successful, return the list of OrderUpdate
            let successfulUpdates = successes |> Array.choose (function Result.Ok orderUpdate -> Some orderUpdate | _ -> None)
            return Result.Ok (successfulUpdates |> Array.toList)
        else 
            // If there were any errors, return the first error message
            let firstError = errors |> Array.choose (function Result.Error errMsg -> Some errMsg | _ -> None) |> Array.head
            return Result.Error firstError
    }


let sendOrderMessage (queueName: string) (orderUpdate: Event) =
    let json = JsonConvert.SerializeObject(orderUpdate)
    sendMessageAsync(queueName, json)
    Result.Ok ()


let exampleOrders : OrderEmitted = [
    { Currency = "BTCUSD"; Price = 10000.0; OrderType = "Buy"; Quantity = 1.0; Exchange = "Bitfinex" }
    { Currency = "ETHUSD"; Price = 500.0; OrderType = "Sell"; Quantity = 10.0; Exchange = "Kraken" }
    { Currency = "LTCUSD"; Price = 150.0; OrderType = "Buy"; Quantity = 20.0; Exchange = "Bitstamp" }
]


let rec handleOrder (orderDetails: OrderDetails) (orderID: OrderID) =
    printfn "Handling order ID: %s" orderID
    let messageContent = sprintf "OrderID: %s, Quantity: %f" orderID orderDetails.Quantity
    printfn "Sending message: %s" messageContent
    sendMessageAsync ("strategyqueue", messageContent)
    match addOrderToDatabase (orderDetails, orderID) with
    | true ->
        Console.WriteLine("Order with ID " + orderID + " added to database successfully.")
        let orderUpdateResult = processOrderUpdateTesting orderID orderDetails |> Async.RunSynchronously
        match orderUpdateResult with
        | Result.Ok orderStatusUpdate when orderStatusUpdate.FulfillmentStatus = "PartiallyFulfilled" ->
            let newOrderDetails = { orderDetails with Quantity = orderStatusUpdate.RemainingQuantity }
            let newOrderID = createOrderTesting newOrderDetails
            handleOrder newOrderDetails newOrderID
        | Result.Ok _ ->
            Console.WriteLine("Order update processed successfully for Order ID " + orderID)
        | Result.Error errMsg ->
            Console.WriteLine("Failed to process order update for Order ID " + orderID + ": " + errMsg)
    | false ->
        Console.WriteLine("Failed to add order with ID " + orderID + " to database.")

let runOrderManagementTesting () =
    let ordersEmitted = exampleOrders
    printfn "Orders emitted: %A" ordersEmitted
    ordersEmitted |> List.iter (fun orderDetails ->
        let orderID = createOrderTesting orderDetails
        match orderID with
        | "" -> 
            Console.WriteLine("Cannot process order: OrderID is unknown.")
        | id ->
            handleOrder orderDetails id
    )
    
let runOrderManagement (ordersEmitted: OrderEmitted) =
    printfn "Orders emitted: %A" ordersEmitted
    ordersEmitted |> List.iter (fun orderDetails ->
        let orderID = createOrderTesting orderDetails
        match orderID with
        | "" -> 
            Console.WriteLine("Cannot process order: OrderID is unknown.")
        | id ->
            handleOrder orderDetails id
    )
    

// Implementation of MailBox Agent
type OrderMessage = 
    | ProcessOrders of OrderEmitted
    | Stop

let orderAgentBasic = MailboxProcessor<OrderMessage>.Start(fun inbox ->
    let rec messageLoop () = async {
        let! msg = inbox.Receive()
        match msg with
        | ProcessOrders ordersEmitted ->
            runOrderManagement ordersEmitted
            return! messageLoop()
        | Stop ->
            printfn "Stopping order processing agent."
    }
    messageLoop()
)

// Basic Version of Receiving and Processing Orders
// Recursive Function to receive and process orders from "orderqueue"
let rec receiveAndProcessOrdersBasic () =
    async {
        printfn "Waiting for message from 'orderqueue'..."
        let! receivedMessageJson = async { return receiveMessageAsync "orderqueue" }
        printfn "Received message: %s" receivedMessageJson
        if not (String.IsNullOrEmpty receivedMessageJson) then
            try
                let ordersEmitted = JsonConvert.DeserializeObject<OrderEmitted>(receivedMessageJson)
                orderAgentBasic.Post(ProcessOrders ordersEmitted)
            with
            | ex ->
                printfn "An exception occurred: %s" ex.Message
    }


// -------------------------
// Implementation for Extra Credit Task2
// -------------------------

// #########################
// 1. Implementation using FSharp Cloud Agent
// #########################

// let orderCloudAgent (agentId: ActorKey) = MailboxProcessor<OrderMessage>.Start(fun inbox ->
//     let rec messageLoop () = async {
//         let! msg = inbox.Receive()
//         match msg with
//         | ProcessOrders ordersEmitted ->
//             runOrderManagement ordersEmitted
//             return! messageLoop()
//         | Stop ->
//             printfn "Stopping order processing agent."
//     }
//     messageLoop()
// )

// // Refactoring to CloudAgent
// let connStr = "Endpoint=sb://arbitragegainer.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=RX56IkxeBgdYjM6OoHXozGRw37tsUQrGk+ASbNEYcl0="
// let queueName = "orderqueue"
// let cloudConn = CloudConnection.WorkerCloudConnection(ServiceBusConnection connStr, Connections.Queue queueName)
// ConnectionFactory.StartListening(cloudConn, orderCloudAgent >> BasicCloudAgent) |> ignore
// let distributedPost = ConnectionFactory.SendToWorkerPool cloudConn

// let rec receiveAndProcessOrdersDistributed () =
//     async {
//         printfn "Waiting for message from 'orderqueue'..."
//         let! receivedMessageJson = async { return receiveMessageAsync "orderqueue" }
//         printfn "Received message: %s" receivedMessageJson
//         if not (String.IsNullOrEmpty receivedMessageJson) then
//             try
//                 let ordersEmitted = JsonConvert.DeserializeObject<OrderEmitted>(receivedMessageJson)
//                 distributedPost (ProcessOrders ordersEmitted) |> ignore
//                 // orderAgent.Post(ProcessOrders ordersEmitted)
//             with
//             | ex ->
//                 printfn "An exception occurred: %s" ex.Message
//     }


// #########################
// 2. Imlementation using Akka.NET
// #########################

// Initialize Actor
type OrderActor() = 
    inherit ReceiveActor()

    let runOrderManagement (ordersEmitted: OrderEmitted) =
        printfn "Orders emitted: %A" ordersEmitted
        ordersEmitted |> List.iter (fun orderDetails ->
            let orderID = createOrderTesting orderDetails
            match orderID with
            | "" -> 
                Console.WriteLine("Cannot process order: OrderID is unknown.")
            | id ->
                handleOrder orderDetails id
        )

    do
        base.Receive<OrderMessage>(fun message ->
            match message with
            | ProcessOrders ordersEmitted ->
                runOrderManagement ordersEmitted
            | Stop ->
                printfn "Stopping order processing actor."
        )

// Configure Actor System
let config = ConfigurationFactory.ParseString("""
    akka {  
        actor {
            provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
        }
        remote {
            dot-netty.tcp {
                port = 8085
                hostname = "localhost"
            }
        }
    }
    """)

let system = ActorSystem.Create("OrderSystem", config)
let orderActorRef = system.ActorOf<OrderActor>("orderActor")


let rec receiveAndProcessOrdersAkka () =
    async {
        printfn "Waiting for message from 'orderqueue'..."
        let! receivedMessageJson = async { return receiveMessageAsync "orderqueue" }
        printfn "Received message: %s" receivedMessageJson
        if not (String.IsNullOrEmpty receivedMessageJson) then
            try
                let ordersEmitted = JsonConvert.DeserializeObject<OrderEmitted>(receivedMessageJson)
                orderActorRef <! ProcessOrders ordersEmitted  // 使用 Actor 处理消息
            with
            | ex ->
                printfn "An exception occurred: %s" ex.Message
    }

// [<EntryPoint>]
// let main arg =
//
//     // If Using the Basic Mailbox Processor
//     async {
//         do! receiveAndProcessOrdersBasic ()
//     } |> Async.Start
//     
//     // If Using the FSharp Cloud Agent
//     // receiveAndProcessOrdersDistributed () |> Async.Start
//
//     // If Using Akka.NET
//     receiveAndProcessOrdersAkka () |> Async.Start
    
//     printfn "Press any key to exit..."
//     Console.ReadKey() |> ignore

//     // Shut Down Actor System
//     system.Terminate() |> Async.AwaitTask |> Async.RunSynchronously
    
//     0