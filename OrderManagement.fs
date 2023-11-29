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
    }


let recordOrderInDatabaseAsync (orderDetails: OrderDetails) (orderID: OrderID) : Async<bool> = 
    async {
        let orderEntity = new OrderEntity()
        orderEntity.PartitionKey <- orderDetails.Exchange 
        orderEntity.RowKey <- Guid.NewGuid().ToString()
        orderEntity.OrderID <- orderEntity.RowKey 
        orderEntity.Currency <- orderDetails.Currency
        orderEntity.Price <- orderDetails.Price
        orderEntity.OrderType <- match orderDetails.OrderType with Buy -> "Buy" | Sell -> "Sell"
        orderEntity.Quantity <- orderDetails.Quantity
        orderEntity.Exchange <- orderDetails.Exchange

        // functions in DatabaseOperations      
        return addOrderToDatabase orderEntity
    }


// Helper functions for Trade Execution Workflow
let executeTrade (orderDetails: OrderDetails) : bool = true
let updateOrderStatusToExecuted (orderId: OrderID) : bool = true

// Helper functions for Order Fulfillment Workflow
let checkOrderFulfillment (orderDetails: OrderDetails) : FulfillmentDetails = Filled 
let updateTransactionTotals (orderId: OrderID, fulfillmentDetails: FulfillmentDetails) : bool = true
let userNotification (orderId: OrderID, message: string) : bool = true

// Helper functions for Update Transaction Totals Workflow
let createOrderWithRemainingAmount (orderId: OrderID) : bool = true

// Helper functions for User Notification Workflow
let sendEmailToUser (orderId: OrderID) : bool = true
let checkIfNotificationSent (orderId: OrderID) : bool = true

// Helper functions for Push Order Update Workflow
let connectToExchanges () : bool = true
let pushOrderUpdateFromExchange (orderUpdateEvent: OrderUpdateEvent) : bool = true

// Helper functions for Handle Order Error Workflow
let detectError (error: OrderProcessingError) : bool = true
let handleError (error: OrderProcessingError) : bool = true

// Helper functions for Database Operations Workflow
let connectToDatabase () : bool = true
let performDatabaseOperation (dbRequest: DatabaseOperationRequest) : bool = true

// -------------------------
// Workflow Implementations
// -------------------------

// Workflow: Create Order
let createOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderInitialized, string>> =
    async {
        let! result = initiateBuySellOrderAsync orderDetails
        match result with
        | Result.Ok orderID ->
            let! recorded = recordOrderInDatabaseAsync orderDetails orderID
            match recorded with
            | true -> 
                return Result.Ok { OrderID = orderID; OrderDetails = orderDetails }
            | false -> 
                return Result.Error "Failed to record order in database"
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

// Workflow: Push Order Update
let pushOrderUpdate (orderUpdateEvent: OrderUpdateEvent) : OrderStatusUpdateReceived option =
    match connectToExchanges () with
    | true ->
        match pushOrderUpdateFromExchange orderUpdateEvent with
        | true -> Some { OrderID = orderUpdateEvent.OrderID; ExchangeName = orderUpdateEvent.OrderDetails.Exchange }
        | false -> None // Update wasn't pushed
    | false -> None // Connection to exchanges failed

// Workflow: Handle Order Errors
let handleOrderError (orderError: OrderProcessingError) : ErrorHandledConfirmation option =
    match detectError orderError with
    | true ->
        match handleError orderError with
        | true -> Some { OrderID = orderError.OrderID; CorrectiveAction = "Action Taken" }
        | false -> None // Error not handled
    | false -> None // Error not detected

// Workflow: Database Operations
let databaseOperations (dbRequest: DatabaseOperationRequest) : DatabaseOperationConfirmation option =
    match connectToDatabase () with
    | true ->
        match performDatabaseOperation dbRequest with
        | true -> Some { OperationType = dbRequest.OperationType; Result = "Success" }
        | false -> None // Operation failed
    | false -> None // Connection to database failed
  