module OrderManagement

open BitfinexAPI
open BitstampAPI
open KrakenAPI
open System.Net.Http
open Newtonsoft.Json
open DatabaseOperations
open DatabaseSchema

// ---------------------------
// Types and Event Definitions
// ---------------------------

// Common Types
type Currency = string
type Price = float
type OrderType = Buy | Sell
type Quantity = int
type Exchange = string
type OrderID = int
type TradeID = int

// Order Details Type
type OrderDetails = {
    Currency: Currency
    Price: Price
    OrderType: OrderType
    Quantity: Quantity
    Exchange: Exchange
}

// Event Types for Various Workflows
type OrderEmitted = OrderDetails list
type OrderInitialized = { OrderID: OrderID; OrderDetails: OrderDetails }

type TradeExecutionConfirmation = { OrderID: OrderID; TradeID: TradeID }

type FulfillmentDetails = | Filled | PartiallyFilled | OnlyOneSideFilled | NotFilled
type TradeExecuted = { OrderID: OrderID; OrderDetails: OrderDetails }
type OrderFulfillmentStatus = { OrderID: OrderID; FulfillmentDetails: FulfillmentDetails }

type UpdateTransactionVolume = { OrderID: OrderID; TransactionVolume: float }
type UpdateTransactionAmount = { OrderID: OrderID; TransactionAmount: float }
type OrderFulfillmentAction = | UpdateTransactionTotals of UpdateTransactionVolume * UpdateTransactionAmount | OrderOneSideFilled

type OrderOneSideFilled = { OrderID: OrderID; FulfillmentDetails: FulfillmentDetails }
type NotificationSentConfirmation = OrderID

type OrderUpdateEvent = { OrderID: OrderID; OrderDetails: OrderDetails }
type OrderStatusUpdateReceived = { OrderID: OrderID; ExchangeName: string }

type OrderProcessingError = { OrderID: OrderID; ErrorDetails: string }
type ErrorHandledConfirmation = { OrderID: OrderID; CorrectiveAction: string }

type DatabaseOperationRequest = { OperationType: string; Data: string }
type DatabaseOperationConfirmation = { OperationType: string; Result: string }

// Main Event Type
type Event =
    | OrderCreated of OrderID
    | TradeExecuted of TradeID option
    | OrderFulfillmentUpdated of FulfillmentStatus
    | TransactionTotalsUpdated of string option
    | UserNotificationSent of int option
    | OrderUpdatePushed of (OrderID * string) option
    | OrderErrorHandled of (OrderID * string) option
    | OrderOneSideFilled of OrderOneSideFilled
    | ErrorHandledConfirmation of ErrorHandledConfirmation
    | DatabaseOperationConfirmation of DatabaseOperationConfirmation
    | OrderStatusUpdateReceived of OrderStatusUpdateReceived
    | None

// -------------------------
// Helper Function Definitions
// -------------------------

open System

let generateOrderID () = Guid.NewGuid().ToString()
let generateTradeID () = Guid.NewGuid().ToString()

// Helper functions for Create Order Workflow
let captureOrderDetails (orderEmitted: OrderEmitted) : OrderDetails list = 
    // Capture order details from the emitted event
    orderEmitted

let initiateBuySellOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderID, string>> = 
    // Initiate buy or sell order based on exchange and order type
    async {
        try 
            match orderDetails.Exchange with
            | "Bitfinex" -> 
                let orderType = match orderDetails.OrderType with
                                | Buy -> "buy"
                                | Sell -> "sell"
                await BitfinexAPI.submitOrder orderType orderDetails.Currency (orderDetails.Quantity.ToString()) (orderDetails.Price.ToString()) |> Async.map (function
                    | Some response -> Result.Ok (response.Id) 
                    | None -> Result.Error "Failed to submit order to Bitfinex")
            | "Kraken" -> 
                let orderType = match orderDetails.OrderType with
                                | Buy -> "buy"
                                | Sell -> "sell"
                await KrakenAPI.submitOrder orderDetails.Currency orderType (orderDetails.Quantity.ToString()) (orderDetails.Price.ToString()) |> Async.map (function
                    | Some response -> Result.Ok (response.Id) 
                    | None -> Result.Error "Failed to submit order to Kraken")
            | "Bitstamp" -> 
                let action = match orderDetails.OrderType with
                            | Buy -> BitstampAPI.buyMarketOrder
                            | Sell -> BitstampAPI.sellMarketOrder
                await action orderDetails.Currency (orderDetails.Quantity.ToString()) None |> Async.map (function
                    | Some response -> Result.Ok (response.Id) 
                    | None -> Result.Error "Failed to submit order to Bitstamp")
            | _ -> 
                async.Return (Result.Error "Unsupported exchange")
        with
        | ex -> 
            return Result.Error (sprintf "An exception occurred: %s" ex.Message)
    }

// Helper function for Database Operations
let recordOrderInDatabaseAsync (orderDetails: OrderDetails) (orderID: string) : Async<Result<bool, string>> = 
    async {
        try
            let orderEntity = new OrderEntity()
            orderEntity.PartitionKey <- orderDetails.Exchange 
            orderEntity.RowKey <- Guid.NewGuid().ToString()
            orderEntity.OrderID <- orderID
            orderEntity.Currency <- orderDetails.Currency
            orderEntity.Price <- float orderDetails.Price
            orderEntity.OrderType <- match orderDetails.OrderType with Buy -> "Buy" | Sell -> "Sell"
            orderEntity.Quantity <- orderDetails.Quantity
            orderEntity.Exchange <- orderDetails.Exchange

            let result = addOrderToDatabase orderEntity
            if result then
                return Result.Ok true
            else
                return Result.Error "Failed to add order to database"
        with
        | ex -> 
            return Result.Error (sprintf "An exception occurred: %s" ex.Message)
    }


// Helper functions for Push Order Update Workflow
let connectToExchanges (exchange: string) : Async<Result<unit, string>> = 
    async {
        try
            match exchange with
            | "Bitfinex" | "Kraken" | "Bitstamp" -> return Result.Ok ()
            | _ -> return Result.Error "Unsupported exchange"
        with
        | ex -> return Result.Error (sprintf "An exception occurred: %s" ex.Message)
    }

let pushOrderUpdateFromExchange (orderUpdateEvent: OrderUpdateEvent) : Async<Result<unit, string>> =
    async {
        match orderUpdateEvent.OrderDetails.Exchange with
        | "Bitfinex" -> 
            let! result = BitfinexAPI.retrieveOrderTrades orderUpdateEvent.OrderDetails.Currency orderUpdateEvent.OrderID
            return result |> Option.map (fun _ -> Result.Ok ()) |> Option.defaultValue (Result.Error "Failed to retrieve trades from Bitfinex")
        | "Kraken" -> 
            let! result = KrakenAPI.queryOrderInformation (int64 orderUpdateEvent.OrderID)
            return result |> Option.map (fun _ -> Result.Ok ()) |> Option.defaultValue (Result.Error "Failed to query order information from Kraken")
        | "Bitstamp" -> 
            let! result = BitstampAPI.orderStatus (orderUpdateEvent.OrderID.ToString())
            return result |> Option.map (fun _ -> Result.Ok ()) |> Option.defaultValue (Result.Error "Failed to check order status on Bitstamp")
        | _ -> 
            return Result.Error "Unsupported exchange"
    }


// Helper functions for Trade Execution Workflow

// Helper functions for Order Fulfillment Workflow
let checkOrderFulfillment (orderDetails: OrderDetails) : FulfillmentDetails = Filled 
let updateTransactionTotals (orderId: OrderID, fulfillmentDetails: FulfillmentDetails) : bool = true
let userNotification (orderId: OrderID, message: string) : bool = true

// Helper functions for Update Transaction Totals Workflow
let createOrderWithRemainingAmount (orderId: OrderID) : bool = true

// Helper functions for User Notification Workflow
let sendEmailToUser (orderId: OrderID) : bool = true
let checkIfNotificationSent (orderId: OrderID) : bool = true

// -------------------------
// Workflow Implementations
// -------------------------

// Workflow: Create Order
let createOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderInitialized, string>> =
    async {
        let! result = initiateBuySellOrderAsync orderDetails
        match result with
        | Result.Ok orderID ->
            let! dbResult = recordOrderInDatabaseAsync orderDetails orderID
            match dbResult with
            | Result.Ok _ -> 
                return Result.Ok { OrderID = orderID; OrderDetails = orderDetails }
            | Result.Error errMsg ->
                return Result.Error errMsg
        | Result.Error errMsg ->
            return Result.Error errMsg
    }

let createOrders (ordersEmitted: OrderEmitted) : Result<OrderInitialized list, string> =
    ordersEmitted
    |> List.map createOrderAsync
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.fold (fun acc result ->
        match acc, result with
        | Result.Ok initList, Result.Ok orderInit -> 
            Result.Ok (orderInit :: initList)
        | Result.Error errMsg, _ | _, Result.Error errMsg ->
            Result.Error errMsg
        | Result.Ok _, Result.Ok _ -> acc
    ) (Result.Ok [])
    |> function
        | Result.Ok initList -> Result.Ok (List.rev initList)
        | Result.Error errMsg -> Result.Error errMsg

// Workflow: Push Order Update
let pushOrderUpdate (orderUpdateEvent: OrderUpdateEvent) : Async<Result<OrderStatusUpdateReceived, string>> =
    async {
        let! connectResult = connectToExchanges orderUpdateEvent.OrderDetails.Exchange
        match connectResult with
        | Result.Ok () ->
            let! pushResult = pushOrderUpdateFromExchange orderUpdateEvent
            match pushResult with
            | Result.Ok () ->
                return Result.Ok { OrderID = orderUpdateEvent.OrderID; ExchangeName = orderUpdateEvent.OrderDetails.Exchange }
            | Result.Error errMsg -> 
                return Result.Error errMsg
        | Result.Error errMsg -> 
            return Result.Error errMsg
    }

// Workflow: Trade Execution
let tradeExecution (orderInitialized: OrderInitialized) : TradeExecutionConfirmation option =
    match executeTrade orderInitialized.OrderDetails with
    | true ->
        match updateOrderStatusToExecuted orderInitialized.OrderID with
        | true -> Some { OrderID = orderInitialized.OrderID; TradeID = generateTradeID () }
        | false -> None // Error in updating order status
    | false -> None // Error in executing trade

// Workflow: Order Fulfillment
let orderFulfillment (tradeExecuted: TradeExecuted) : OrderFulfillmentStatus =
    let fulfillmentDetails = checkOrderFulfillment tradeExecuted.OrderDetails
    match updateTransactionTotals (tradeExecuted.OrderID, fulfillmentDetails) with
    | true ->
        match userNotification (tradeExecuted.OrderID, "Your order fulfillment status has been updated.") with
        | true -> { OrderID = tradeExecuted.OrderID; FulfillmentDetails = fulfillmentDetails }
        | false -> { OrderID = tradeExecuted.OrderID; FulfillmentDetails = NotFilled } // Error in user notification
    | false -> { OrderID = tradeExecuted.OrderID; FulfillmentDetails = NotFilled } // Error in updating transaction totals

// Workflow: Update Transaction Totals
let updateTransactionTotals (orderFulfillmentStatus: OrderFulfillmentStatus) : OrderFulfillmentAction option =
    match orderFulfillmentStatus.FulfillmentDetails with
    | Filled ->
        let updateVolume = { OrderID = orderFulfillmentStatus.OrderID; TransactionVolume = 100.0 } // Placeholder values
        let updateAmount = { OrderID = orderFulfillmentStatus.OrderID; TransactionAmount = 1000.0 } // Placeholder values
        Some (UpdateTransactionTotals (updateVolume, updateAmount))

    | PartiallyFilled ->
        let _ = createOrderWithRemainingAmount orderFulfillmentStatus.OrderID
        let updateVolume = { OrderID = orderFulfillmentStatus.OrderID; TransactionVolume = 50.0 } // Placeholder values
        let updateAmount = { OrderID = orderFulfillmentStatus.OrderID; TransactionAmount = 500.0 } // Placeholder values
        Some (UpdateTransactionTotals (updateVolume, updateAmount))

    | OnlyOneSideFilled ->
        Some (OrderOneSideFilled) // sends an OrderOneSideFilled event

    | NotFilled ->
        None // No action required for NotFilled status

// Workflow: User Notification When Only One Side of the Order is Filled
let userNotification (orderOneSideFilled: OrderOneSideFilled) : NotificationSentConfirmation option =
    match sendEmailToUser orderOneSideFilled.OrderID with
    | true -> 
        match checkIfNotificationSent orderOneSideFilled.OrderID with
        | true -> Some orderOneSideFilled.OrderID
        | false -> None // Notification not sent
    | false -> None // Email sending failed

