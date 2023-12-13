module OrderManagementTest

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
    | OrderInitiated of OrderID
    | OrderProcessed of OrderUpdate

type OrderEmitted = OrderDetails list

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
                    // respnse string 已经经过解析就是orderID
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
let processBitfinexResponse (jsonString: string) (originalOrderQuantity: float) : Result<FulfillmentStatus, string> =
    // passing in the response and historical order amt to compare and match execution status
    match BitfinexAPI.parseBitfinexOrderStatusResponse jsonString with
    | Result.Ok executedAmount ->
        if executedAmount = originalOrderQuantity then
            Result.Ok FullyFulfilled
        elif executedAmount > 0.0 then
            Result.Ok PartiallyFulfilled
        else
            Result.Ok OneSideFilled
    | Result.Error errMsg ->
        Result.Error errMsg

// Helper function to parse Kraken response and store in database
let processKrakenResponse (jsonString: string) : Result<FulfillmentStatus, string> =
    match KrakenAPI.parseKrakenOrderResponse jsonString with
    | Result.Ok (orderId, vol, vol_exec) ->
        let volFloat = float vol
        let volExecFloat = float vol_exec
        let status = 
            if volExecFloat >= volFloat then FullyFulfilled
            else if volExecFloat > 0.0 then PartiallyFulfilled
            else OneSideFilled
        Result.Ok status
    | Result.Error errMsg ->
        Result.Error errMsg

// Helper function to parse Bitstamp response and store in database
let processBitstampResponse (jsonString: string) : Result<FulfillmentStatus, string> =
    match BitstampAPI.parseResponseOrderStatus jsonString with
    | Result.Ok orderResponse ->
        let amountRemaining = float orderResponse.AmountRemaining
        let status = 
            if amountRemaining = 0.0 then FullyFulfilled
            else if amountRemaining > 0.0 then PartiallyFulfilled
            else OneSideFilled
        Result.Ok status
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
            // Database operations should be here, store id and order details in db
            return Result.Ok orderID
        | Result.Error errMsg ->
            return Result.Error errMsg
    }


// Workflow: Retrieve and handle Order Updates
let processOrderUpdate (orderID: OrderID) (orderDetails: OrderDetails) : Async<Result<Event, string>> =
    async {
        // Wait for 30 seconds to get the order status updates
        do! Async.Sleep(30000)

        match orderDetails.Exchange with
        | "Bitfinex" ->
            let! bitfinexResponseOption = BitfinexAPI.retrieveOrderTrades "DOTUSD" 1747566428
            match bitfinexResponseOption with
            | Some bitfinexResponse ->
                match processBitfinexResponse bitfinexResponse orderDetails.Quantity with
                | Result.Ok fulfillmentStatus ->
                    let orderUpdate = { OrderID = orderID; OrderDetails = orderDetails; FulfillmentStatus = fulfillmentStatus }
                    return Result.Ok (OrderFulfillmentUpdated fulfillmentStatus)
                | Result.Error errMsg ->
                    return Result.Error errMsg
            | None ->
                return Result.Error "Failed to retrieve Bitfinex trades"

        | "Kraken" ->
            let! krakenResponse = KrakenAPI.queryOrdersInfo "OU22CG-KLAF2-FWUDD7" true None
            match krakenResponse with
            | Some response ->
                match processKrakenResponse response with
                | Result.Ok fulfillmentStatus ->
                    let orderUpdate = { OrderID = orderID; OrderDetails = orderDetails; FulfillmentStatus = fulfillmentStatus }
                    return Result.Ok (OrderFulfillmentUpdated fulfillmentStatus)
                | Result.Error errMsg ->
                    return Result.Error errMsg
            | None ->
                return Result.Error "Failed to retrieve Kraken trades"

        | "Bitstamp" ->
            let! bitstampResponse = BitstampAPI.orderStatus "1234"
            match bitstampResponse with
            | Some response ->
                match processBitstampResponse response with
                | Result.Ok fulfillmentStatus ->
                    let orderUpdate = { OrderID = orderID; OrderDetails = orderDetails; FulfillmentStatus = fulfillmentStatus }
                    return Result.Ok (OrderFulfillmentUpdated fulfillmentStatus)
                | Result.Error errMsg ->
                    return Result.Error errMsg
            | None ->
                return Result.Error "Failed to retrieve Bitstamp trades"

        | _ -> 
            return Result.Error "Unsupported exchange"
    }

// Main Workflow of Order Management: to create and process orders
let createAndProcessOrders (ordersEmitted: OrderEmitted) : Async<Result<string, string>> =
    async {
        let resultsAsync = 
            ordersEmitted 
            |> List.map (fun orderDetails ->
                async {
                    let! createResult = createOrderAsync orderDetails
                    match createResult with
                    | Result.Ok orderID ->
                        let! updateResult = processOrderUpdate orderID orderDetails
                        return updateResult
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
            return Result.Ok "All orders were successfully created and processed."
        else 
            return Result.Error "There were errors in processing some orders."
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
