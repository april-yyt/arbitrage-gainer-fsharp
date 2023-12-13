module OrderManagementTest

open System
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
type OrderUpdate = { OrderID: OrderID; OrderDetails: OrderDetails ; FulfillmentStatus: FulfillmentStatus }

type Event = 
    | OrderFulfillmentUpdated of FulfillmentStatus
    | UserNotificationSent of OrderID

type OrderEmitted = OrderDetails list



// -------------------------
// Helper Function Definitions
// -------------------------

let parseBitfinexResponse (jsonString: string) : Result<int, string> =
    printfn "Bitfinex Response: %s" jsonString
    Result.Ok 1747566428
//     printfn "Bitfinex Response: %s" jsonString
//     let jsonValue = JsonValue.Parse(jsonString)
//     match jsonValue with
//     | JsonValue.Array items ->
//         match items with
//         | [| _; _; _; _; JsonValue.Array (innerItems : JsonValue[]); _; _ |] ->
//             match innerItems with
//             | [| JsonValue.Array (orderDetails : JsonValue[]) |] ->
//                 match orderDetails with
//                 | [| JsonValue.Number orderID; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _ |] ->
//                     // OrderID 是最内层数组的第一个数字元素
//                     Result.Ok 1747566428
//                 | _ -> Result.Error "Invalid Bitfinex order details format"
//             | _ -> Result.Error "Invalid Bitfinex inner items format"
//         | _ -> Result.Error "Invalid Bitfinex response format"
//     | _ -> Result.Error "Invalid JSON format"

let parseBitstampResponse (jsonString: string) =
    printfn "Bitstamp Response: %s" jsonString
    Result.Ok 1234
    // let jsonValue = JsonValue.Parse(jsonString)
    // match jsonValue with
    // | JsonValue.Record fields ->
    //     let id = fields?["id"].AsString()
    //     let market = fields?["market"].AsString()
    //     let datetime = fields?["datetime"].AsString()
    //     let price = fields?["price"].AsString()
    //     let amount = fields?["amount"].AsString()
    //     // ... 提取其他字段 ...
    //     printfn "Order ID: %s, Market: %s, DateTime: %s, Price: %s, Amount: %s" id market datetime price amount
    // | _ -> printfn "Invalid JSON format"

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
                    let result = parseBitfinexResponse responseString
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
                    // 这里的处理取决于 Bitstamp API 返回的内容格式
                    // 假设 responseString 包含订单ID
                    return Result.Ok (StringOrderID responseString)
                | None ->
                    return Result.Error "Failed to submit order to Bitstamp"

            | _ -> 
                return Result.Error "Unsupported exchange"
        with
        | ex -> 
            return Result.Error (sprintf "An exception occurred: %s" ex.Message)
    }


// should contain submitting order to exchange and storing order info in db
// temporarily removing db operations
let createOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderID, string>> =
    async {
        let! result = submitOrderAsync orderDetails
        match result with
        | Result.Ok orderID ->
            // Database operations should be here, store id and order details in db
            return Result.Ok orderID
        | Result.Error errMsg ->
            return Result.Error errMsg
    }


// Helper function to parse Bitfinex response and store in database
let processBitfinexResponse (response: JsonValue) (originalOrderQuantity: float) : Result<Event, string> =
    // passing in the response and historical order amt to compare and match execution status
    match response with
    | JsonArray trades ->
        if List.isEmpty trades then
            Result.Ok (OneSideFilled [])
        else
            let transactionHistory, totalExecAmount = trades |> List.fold (fun (accHist, accAmt) trade ->
                match trade with
                | JsonArray [| JsonNumber id; JsonString symbol; JsonNumber mts; JsonNumber order_id; JsonNumber exec_amount; JsonNumber exec_price; _ |] ->
                    ({ OrderID = order_id.ToString(); Currency = symbol; Price = exec_price }, exec_amount) :: accHist, accAmt + exec_amount
                | _ -> accHist, accAmt
            ) ([], 0.0)
            if totalExecAmount = originalOrderQuantity then
                Result.Ok (FullyFulfilled transactionHistory)
            elif totalExecAmount > 0.0 then
                Result.Ok (PartiallyFulfilled (transactionHistory, originalOrderQuantity - totalExecAmount))
            else
                Result.Ok (OneSideFilled transactionHistory)
    | _ -> Result.Error "Invalid response format"


// Helper function to parse Kraken response and store in database
let processKrakenResponse (response: JsonValue) : Result<bool, string> =
    match response?result with
    | JsonObject orders ->
        orders |> Seq.fold (fun acc (_, orderDetails) ->
            match orderDetails with
            | JsonObject order ->
                let vol = order?vol |> float
                let volExec = order?vol_exec |> float
                let status = if volExec >= vol then FullyFulfilled else if volExec > 0.0 then PartiallyFulfilled else Unfulfilled
                // TODO: double check the logic for one side filled orders!
            | _ -> acc
        ) true |> function
            | true -> Result.Ok true
            | false -> Result.Error "Failed to process Kraken response"
    | _ -> Result.Error "Invalid response format"


// Helper function to parse Bitstamp response and store in database
let processBitstampResponse (response: JsonValue) : Result<bool, string> =
    match response with
    | JsonObject order ->
        let amountRemaining = order?amount_remaining |> float
        let status = if amountRemaining = 0.0 then FullyFulfilled else if amountRemaining < (order?amount |> float) then PartiallyFulfilled else Unfulfilled
        // TODO: double check the logic for one side filled orders!
    | _ -> Result.Error "Invalid response format"


// -------------------------
// Main Workflows
// -------------------------

// takes in the OrderDetails list from last bounded context, Sequentially create the orders
let createOrders (ordersEmitted: OrderDetails list) : Result<OrderID list, string> =
    ordersEmitted
    |> List.fold (fun acc orderDetail ->
        match acc with
        | Result.Error errMsg -> Result.Error errMsg
        | Result.Ok idList ->
            match createOrderAsync orderDetail |> Async.RunSynchronously with
            | Result.Ok orderID -> Result.Ok (orderID :: idList)
            | Result.Error errMsg -> Result.Error errMsg
    ) (Result.Ok [])
    |> function
        | Result.Ok idList -> Result.Ok (List.rev idList)
        | Result.Error errMsg -> Result.Error errMsg

// Workflow: Retrieve and handle Order Updates
let processOrderUpdate (orderID: OrderID) (orderDetails: OrderDetails) : Async<Result<Event, string>> =
    async {
        // Wait for 30 seconds to get the order status updates
        do! Task.Delay(30000) |> Async.AwaitTask

        // Retrieve and process the order status
        let! processingResult = 
            match orderDetails.Exchange with
            | "Bitfinex" ->
                let! bitfinexResponse = BitfinexAPI.retrieveOrderTrades orderDetails.Currency orderID
                processBitfinexResponse bitfinexResponse orderDetails.Quantity |> Async.Return
            | "Kraken" ->
                let! krakenResponse = KrakenAPI.queryOrderInformation (int64 orderID)
                processKrakenResponse krakenResponse |> Async.Return
            | "Bitstamp" ->
                let! bitstampResponse = BitstampAPI.orderStatus (orderID.ToString())
                processBitstampResponse bitstampResponse |> Async.Return
            | _ -> 
                async.Return (Result.Error "Unsupported exchange")

        match processingResult with
        | Result.Ok fulfillmentStatus ->
            // Create an OrderUpdate instance based on the fulfillment status
            let orderUpdate = { OrderID = orderID; OrderDetails = orderDetails; FulfillmentStatus = fulfillmentStatus }
            return Result.Ok (OrderFulfillmentUpdated fulfillmentStatus)

        // match processingResult with
        // | Result.Ok (FullyFulfilled transactionHistory) ->
        //     // Database operations are commented out
        //     (*
        //     let updatedEntity = 
        //         transactionHistory
        //         |> List.map (fun t -> 
        //             { new OrderEntity() with
        //                 OrderID = t.OrderID
        //             })
        //     match updateOrderInDatabase updatedEntity with
        //     | true -> return Result.Ok (OrderFulfillmentUpdated Filled)
        //     | false -> return Result.Error "Failed to update order in database"
        //     *)
        //     return Result.Ok (OrderFulfillmentUpdated Filled)

        // | Result.Ok (PartiallyFulfilled (transactionHistory, remainingAmount)) ->
        //     // Database operations are commented out
        //     (*
        //     let newOrderDetails = 
        //         { orderDetails with Quantity = remainingAmount }
        //     let! newOrderResult = initiateBuySellOrderAsync newOrderDetails
        //     match newOrderResult with
        //     | Result.Ok newOrderID ->
        //         let updatedEntity = 
        //             transactionHistory
        //             |> List.map (fun t -> 
        //                 { new OrderEntity() with
        //                     OrderID = t.OrderID
        //                 })
        //         match updateOrderInDatabase updatedEntity with
        //         | true -> return Result.Ok (OrderFulfillmentUpdated PartiallyFilled)
        //         | false -> return Result.Error "Failed to update order in database"
        //     | Result.Error errMsg ->
        //         return Result.Error errMsg
        //     *)
        //     return Result.Ok (OrderFulfillmentUpdated PartiallyFilled)

        // | Result.Ok (OneSideFilled transactionHistory) ->
        //     // Database operations and user notification are commented out
        //     (*
        //     match userNotification { OrderID = orderID; FulfillmentDetails = OnlyOneSideFilled } with
        //     | Some _ -> return Result.Ok (UserNotificationSent orderID)
        //     | None -> return Result.Error "Failed to send user notification"
        //     let updatedEntity = 
        //         transactionHistory
        //         |> List.map (fun t -> 
        //             { new OrderEntity() with
        //                 OrderID = t.OrderID
        //             })
        //     match updateOrderInDatabase updatedEntity with
        //     | true -> return Result.Ok (OrderFulfillmentUpdated OneSideFilled)
        //     | false -> return Result.Error "Failed to update order in database"
        //     *)
        //     return Result.Ok (OrderFulfillmentUpdated OneSideFilled)

        | Result.Error errMsg -> 
            return Result.Error errMsg
        | _ -> 
            return Result.Error "Failed to retrieve order status"
    }


// Main Workflow of Order Management: to create and process orders
let createAndProcessOrders (ordersEmitted: OrderEmitted) : Async<Result<Event list, string>> =
    async {
        let results = 
            ordersEmitted 
            |> List.map (fun orderDetails ->
                async {
                    let! createResult = createOrderAsync orderDetails
                    match createResult with
                    | Result.Ok orderInitialized ->
                        let! updateResult = processOrderUpdate orderInitialized.OrderID orderDetails
                        return updateResult
                    | Result.Error errMsg ->
                        return Result.Error errMsg
                }
            )
            |> Async.Parallel

        // Aggregate results into a single list of events
        let events = 
            results 
            |> Array.fold (fun acc result ->
                match acc, result with
                | Result.Ok eventsList, Result.Ok event -> Result.Ok (event :: eventsList)
                | Result.Error errMsg, _ | _, Result.Error errMsg -> Result.Error errMsg
                | Result.Ok _, Result.Ok _ -> acc
            ) (Result.Ok [])
        return events
    }


[<TestFixture>]
type OrderManagementTests() =

    [<Test>]
    member this.``Bitfinex Order Creation and Processing Test`` () =
        let orderDetails = { Currency = "DOTUSD"; Price = 10000.0; OrderType = Buy; Quantity = 0.01; Exchange = "Bitfinex" }
        let result = createAndProcessOrders [orderDetails] |> Async.RunSynchronously
        Assert.IsNotNull(result)

    [<Test>]
    member this.``Kraken Order Creation and Processing Test`` () =
        let orderDetails = { Currency = "DOTUSD"; Price = 10000.0; OrderType = Buy; Quantity = 0.01; Exchange = "Kraken" }
        let result = createAndProcessOrders [orderDetails] |> Async.RunSynchronously
        Assert.IsNotNull(result)

    [<Test>]
    member this.``Bitstamp Order Creation and Processing Test`` () =
        let orderDetails = { Currency = "DOTUSD"; Price = 10000.0; OrderType = Buy; Quantity = 0.01; Exchange = "Bitstamp" }
        let result = createAndProcessOrders [orderDetails] |> Async.RunSynchronously
        Assert.IsNotNull(result)
