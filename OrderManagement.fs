module OrderManagementTest

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


// -------------------------
// Types and Event Definitions
// -------------------------

// Type definitions
type Currency = string
type Price = float
type OrderType = Buy | Sell
type Quantity = float
type Exchange = string
type OrderID = IntOrderID of int | StringOrderID of string
type FulfillmentStatus = 
    | FullyFulfilled
    | PartiallyFulfilled
    | OneSideFilled


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
        member val PartitionKey = "" with get, set
        member val RowKey = "" with get, set
        member val Timestamp = Nullable() with get, set
    new() = OrderEntity(StringOrderID(""), "", 0.0, Buy, 0.0, "", FullyFulfilled, 0.0)
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

// let addOrder (orderID: OrderID, currency: Currency, price: Price, orderType: OrderType, quantity: Quantity, exchange: Exchange, status: FulfillmentStatus, remainingQuantity: Quantity) =
//     let order = OrderEntity(orderID, currency, price, orderType, quantity, exchange, status, remainingQuantity)
//     try
//         table.AddEntity(order) |> ignore
//         true 
//     with
//     | :? Azure.RequestFailedException as ex -> 
//         printfn "Error adding order: %s" ex.Message
//         false 


// let order1 = addOrder (StringOrderID "Order001", "BTCUSD", 10000.0, Buy, 1.0, "Bitfinex", OneSideFilled, 1.0)
// let order2 = addOrder (StringOrderID "Order002", "ETHUSD", 500.0, Sell, 10.0, "Kraken", PartiallyFulfilled, 5.0)
// let order3 = addOrder (StringOrderID "Order003", "LTCUSD", 150.0, Buy, 20.0, "Bitstamp", FullyFulfilled, 0.0)


let addOrderToDatabase (orderDetails: OrderDetails, orderID: OrderID) : bool =
    table.CreateIfNotExists() |> ignore
    let order = OrderEntity(orderID, orderDetails.Currency, orderDetails.Price, orderDetails.OrderType, orderDetails.Quantity, orderDetails.Exchange, OneSideFilled, orderDetails.Quantity)
    try
        table.AddEntity(order) |> ignore
        true 
    with
    | :? Azure.RequestFailedException as ex -> 
        printfn "Error adding entity: %s" ex.Message
        false 


let updateOrderStatus (orderID: OrderID, newStatus: FulfillmentStatus, remainingQuantity: Quantity) : bool =
    let partitionKey = match orderID with | IntOrderID id -> id.ToString() | StringOrderID id -> id
    let rowKey = match orderID with | IntOrderID id -> id.ToString() | StringOrderID id -> id
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

// Helper Function for submitting a new order on an exchange
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
                        printfn "Order ID: %d" orderID
                        return Result.Ok (IntOrderID orderID)
                    | Result.Error errMsg ->
                        printfn "Error: %s" errMsg
                        return Result.Error errMsg
                | None ->
                    return Result.Error "Failed to submit order to Bitfinex"

            | "Kraken" -> 
                let orderType = "market"
                let pair = "XX" + orderDetails.Currency
                let orderResult = KrakenAPI.submitOrder pair orderType (orderDetails.Quantity.ToString()) (orderDetails.Price.ToString()) |> Async.RunSynchronously
                match orderResult with
                | Some responseString ->
                    let result = KrakenAPI.parseKrakenSubmitResponse responseString
                    match result with
                    | Result.Ok txid ->
                        printfn "Transaction ID: %s" txid
                        return Result.Ok (StringOrderID txid)
                    | Result.Error errMsg ->
                        printfn "Error: %s" errMsg
                        return Result.Error errMsg
                | None ->
                    return Result.Error "Failed to submit order to Kraken"

            | "Bitstamp" -> 
                let action = match orderDetails.OrderType with
                                | Buy -> BitstampAPI.buyMarketOrder
                                | Sell -> BitstampAPI.sellMarketOrder
                let orderResult = action orderDetails.Currency (orderDetails.Quantity.ToString()) None |> Async.RunSynchronously
                match orderResult with
                | Some responseString ->
                    printfn "Response from Bitstamp: %s" responseString
                    return Result.Ok (StringOrderID responseString)
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
                if executedAmount = originalOrderQuantity then FullyFulfilled
                elif executedAmount > 0.0 then PartiallyFulfilled
                else OneSideFilled
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
                if volExecFloat >= volFloat then FullyFulfilled
                else if volExecFloat > 0.0 then PartiallyFulfilled
                else OneSideFilled
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
                if amountRemaining = 0.0 then FullyFulfilled
                else if amountRemaining > 0.0 then PartiallyFulfilled
                else OneSideFilled
            RemainingQuantity = amountRemaining
        }
        Result.Ok statusUpdate
    | Result.Error errMsg ->
        Result.Error errMsg


// -------------------------
// Main Workflows
// -------------------------

// should contain submitting order to exchange and storing order info in db
// temporarily removing db operations
let createOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderID, string>> =
    async {
        let! result = submitOrderAsync orderDetails
        match result with
        | Result.Ok orderID ->
            // return Result.Ok orderID
            // Database operation to store order details in db
            match addOrderToDatabase (orderDetails, orderID) with
            | true -> 
                return Result.Ok orderID
            | false ->
                return Result.Error "Failed to add order to database"
        | Result.Error errMsg ->
            return Result.Error errMsg
    }


// Workflow: Retrieve and handle Order Updates
let processOrderUpdate (orderID: OrderID) (orderDetails: OrderDetails) : Async<Result<Event, string>> =
    async {
        // Wait for 30 seconds to get the order status updates
        do! Async.Sleep(30000)

        let updateOrderInDatabase (orderStatusUpdate: OrderStatusUpdate) =
            match updateOrderStatus (orderID, orderStatusUpdate.FulfillmentStatus, orderStatusUpdate.RemainingQuantity) with
            | true ->
                Result.Ok (OrderFulfillmentUpdated orderStatusUpdate.FulfillmentStatus)
            | false ->
                Result.Error "Failed to update order in database"

        let handleStatusUpdate orderStatusUpdate =
            match orderStatusUpdate.FulfillmentStatus with
            | FullyFulfilled ->
                updateOrderInDatabase orderStatusUpdate
            | PartiallyFulfilled ->
                let newOrderDetails = { orderDetails with Quantity = orderStatusUpdate.RemainingQuantity }
                createOrderAsync newOrderDetails |> Async.Ignore
                Result.Ok (OrderProcessed { OrderID = orderID; OrderDetails = orderDetails; FulfillmentStatus = FullyFulfilled; RemainingQuantity = 0.0 })
            | OneSideFilled ->
                updateOrderInDatabase orderStatusUpdate
                sendEmailAsync "Your order was only partially filled." |> Async.Ignore
                Result.Ok (UserNotificationSent orderID)


                    // let orderUpdate = { OrderID = orderID; OrderDetails = orderDetails; FulfillmentStatus = fulfillmentStatus }
        match orderDetails.Exchange with
        | "Bitfinex" ->
            let! bitfinexResponseOption = BitfinexAPI.retrieveOrderTrades "DOTUSD" 1747566428
            match bitfinexResponseOption with
            | Some bitfinexResponse ->
                match processBitfinexResponse bitfinexResponse orderDetails.Quantity with
                | Result.Ok orderStatusUpdate ->
                    handleStatusUpdate orderStatusUpdate
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
                    handleStatusUpdate orderStatusUpdate
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
                    handleStatusUpdate orderStatusUpdate
                | Result.Error errMsg ->
                    return Result.Error errMsg
            | None ->
                return Result.Error "Failed to retrieve Bitstamp trades"

        | _ -> 
            return Result.Error "Unsupported exchange"
    }

// Main Workflow of Order Management: to create and process orders
let createAndProcessOrders (ordersEmitted: OrderEmitted) : Async<Result<Event, string>> =
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

        // Check if all operations were successful
        let allSuccess = results |> Array.forall (fun result -> 
            match result with 
            | Result.Ok _ -> true 
            | Result.Error _ -> false)

        if allSuccess then 
            return Result.Ok (OrderProcessed { OrderID = IntOrderID 0; OrderDetails = { Currency = ""; Price = 0.0; OrderType = Buy; Quantity = 0.0; Exchange = "" }; FulfillmentStatus = FullyFulfilled; RemainingQuantity = 0.0 })
        else 
            return Result.Error "There were errors in processing some orders."
    }


let sendOrderMessage (queueName: string) (orderUpdate: Event) =
    let json = JsonConvert.SerializeObject(orderUpdate)
    sendMessageAsync(queueName, json)
    Result.Ok ()


let exampleOrders : OrderEmitted = [
    { Currency = "BTCUSD"; Price = 10000.0; OrderType = Buy; Quantity = 1.0; Exchange = "Bitfinex" }
    { Currency = "ETHUSD"; Price = 500.0; OrderType = Sell; Quantity = 10.0; Exchange = "Kraken" }
    { Currency = "LTCUSD"; Price = 150.0; OrderType = Buy; Quantity = 20.0; Exchange = "Bitstamp" }
]

let getOrderID (orderDetails: OrderDetails) : OrderID =
    match orderDetails.Exchange with
    | "Kraken" -> StringOrderID "OU22CG-KLAF2-FWUDD7"
    | "Bitstamp" -> StringOrderID "1234"
    | "Bitfinex" -> StringOrderID "174756642"
    | _ -> StringOrderID "Unknown"

// Main entry point function
let runOrders () =
    let ordersEmitted = exampleOrders
    let result = createAndProcessOrders ordersEmitted |> Async.RunSynchronously
    match result with
    | Result.Ok orderUpdate ->
        match sendOrderMessage "strategyqueue" orderUpdate with
        | Result.Ok _ ->
            Console.WriteLine("Orders processed successfully, and message sent.")
        | Result.Error errMsg ->
            Console.WriteLine("Error sending message: " + errMsg)
    | Result.Error errMsg ->
        Console.WriteLine("Error processing orders: " + errMsg)


// testing main, takes in ordersEmitted, sends message to the queue
let runOrderManagement () =
    let ordersEmitted = exampleOrders
    printfn "Orders emitted: %A" ordersEmitted
    // iter thru the orders
    ordersEmitted |> List.iter (fun orderDetails ->
        let orderID = getOrderID orderDetails
        let messageContent = sprintf "OrderID: %A, Quantity: %f" orderID orderDetails.Quantity
        sendMessageAsync ("tradingqueue", messageContent)
    )

// [<TestFixture>]
// type OrderManagementTests() =

//     // [<Test>]
//     // member this.``Bitfinex Order Creation and Processing Test`` () =
//     //     let orderDetails = { Currency = "DOTUSD"; Price = 10000.0; OrderType = Buy; Quantity = 0.01; Exchange = "Bitfinex" }
//     //     let result = createAndProcessOrders [orderDetails] |> Async.RunSynchronously
//     //     Assert.IsNotNull(result)

//     [<Test>]
//     member this.``Kraken Order Creation and Processing Test`` () =
//         let orderDetails = { Currency = "DOTUSD"; Price = 10000.0; OrderType = Buy; Quantity = 0.01; Exchange = "Kraken" }
//         let result = createAndProcessOrders [orderDetails] |> Async.RunSynchronously
//         Assert.IsNotNull(result)

//     [<Test>]
//     member this.``Bitstamp Order Creation and Processing Test`` () =
//         let orderDetails = { Currency = "DOTUSD"; Price = 10000.0; OrderType = Buy; Quantity = 0.01; Exchange = "Bitstamp" }
//         let result = createAndProcessOrders [orderDetails] |> Async.RunSynchronously
//         Assert.IsNotNull(result)

//     [<Test>]
//     member this.``Run Test`` () =
//         runOrderManagement ()

    // <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.0.0" />


// [<EntryPoint>]
// let main arg =
//     runOrderManagement ()
//     // sendMessageAsync ("orderqueue", "testing orders")
//     0 