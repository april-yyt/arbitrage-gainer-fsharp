module OrderManagement

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
let captureOrderDetails (orderEmitted: OrderEmitted) : OrderDetails list = orderEmitted
let initiateBuySellOrder (orderDetails: OrderDetails) : OrderDetails = orderDetails
let recordOrderInDatabase (orderDetails: OrderDetails) : bool = true

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
let processSingleOrder (orderDetails: OrderDetails) : OrderCreationConfirmation =
    let initiatedOrderDetails = initiateBuySellOrder orderDetails
    match recordOrderInDatabase initiatedOrderDetails with
    | true -> generateOrderID ()
    | false -> "Error" // Placeholder for error handling

let createOrders (ordersEmitted: OrderEmitted) : OrderCreationConfirmation list =
    ordersEmitted
    |> List.collect captureOrderDetails
    |> List.map processSingleOrder

// Workflow: Trade Execution
let tradeExecutionWorkflow (orderInitialized: OrderInitialized) : TradeExecutionConfirmation option =
    match executeTrade orderInitialized.OrderDetails with
    | true ->
        match updateOrderStatusToExecuted orderInitialized.OrderID with
        | true -> Some { OrderID = orderInitialized.OrderID; TradeID = generateTradeID () }
        | false -> None // Error in updating order status
    | false -> None // Error in executing trade

// Workflow: Order Fulfillment
let orderFulfillmentWorkflow (tradeExecuted: TradeExecuted) : OrderFulfillmentStatus =
    let fulfillmentDetails = checkOrderFulfillment tradeExecuted.OrderDetails
    match updateTransactionTotals (tradeExecuted.OrderID, fulfillmentDetails) with
    | true ->
        match userNotification (tradeExecuted.OrderID, "Your order fulfillment status has been updated.") with
        | true -> { OrderID = tradeExecuted.OrderID; FulfillmentDetails = fulfillmentDetails }
        | false -> { OrderID = tradeExecuted.OrderID; FulfillmentDetails = NotFilled } // Error in user notification
    | false -> { OrderID = tradeExecuted.OrderID; FulfillmentDetails = NotFilled } // Error in updating transaction totals

// Workflow: Update Transaction Totals
let updateTransactionTotalsWorkflow (orderFulfillmentStatus: OrderFulfillmentStatus) : OrderFulfillmentAction option =
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
let userNotificationWorkflow (orderOneSideFilled: OrderOneSideFilled) : NotificationSentConfirmation option =
    match sendEmailToUser orderOneSideFilled.OrderID with
    | true -> 
        match checkIfNotificationSent orderOneSideFilled.OrderID with
        | true -> Some orderOneSideFilled.OrderID
        | false -> None // Notification not sent
    | false -> None // Email sending failed

// Workflow: Push Order Update
let pushOrderUpdateWorkflow (orderUpdateEvent: OrderUpdateEvent) : OrderStatusUpdateReceived option =
    match connectToExchanges () with
    | true ->
        match pushOrderUpdateFromExchange orderUpdateEvent with
        | true -> Some { OrderID = orderUpdateEvent.OrderID; ExchangeName = orderUpdateEvent.OrderDetails.Exchange }
        | false -> None // Update wasn't pushed
    | false -> None // Connection to exchanges failed

// Workflow: Handle Order Errors
let handleOrderErrorWorkflow (orderError: OrderProcessingError) : ErrorHandledConfirmation option =
    match detectError orderError with
    | true ->
        match handleError orderError with
        | true -> Some { OrderID = orderError.OrderID; CorrectiveAction = "Action Taken" }
        | false -> None // Error not handled
    | false -> None // Error not detected

// Workflow: Database Operations
let databaseOperationsWorkflow (dbRequest: DatabaseOperationRequest) : DatabaseOperationConfirmation option =
    match connectToDatabase () with
    | true ->
        match performDatabaseOperation dbRequest with
        | true -> Some { OperationType = dbRequest.OperationType; Result = "Success" }
        | false -> None // Operation failed
    | false -> None // Connection to database failed
