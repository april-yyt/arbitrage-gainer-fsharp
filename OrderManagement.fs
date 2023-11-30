module OrderManagement

open BitfinexAPI
open BitstampAPI
open KrakenAPI
open DatabaseOperations
open DatabaseSchema
open System
open System.Threading.Tasks

// ---------------------------
// Types and Event Definitions
// ---------------------------

// Type definitions
type Currency = string
type Price = float
type OrderType = Buy | Sell
type Quantity = int
type Exchange = string
type OrderID = int
type TradeID = int

type OrderDetails = {
    Currency: Currency
    Price: Price
    OrderType: OrderType
    Quantity: Quantity
    Exchange: Exchange
}

type OrderUpdateEvent = { OrderID: OrderID; OrderDetails: OrderDetails }
type OrderStatusUpdateReceived = { OrderID: OrderID; ExchangeName: string }


// Main Event Type
type Event =
    | OrderCreated of OrderInitialized
    | OrderUpdateProcessed of OrderStatusUpdateReceived
    | OrderFulfillmentUpdated of FulfillmentStatus
    | TransactionTotalsUpdated of UpdateTransactionVolume * UpdateTransactionAmount
    | UserNotificationSent of NotificationSentConfirmation
    | OrderErrorHandled of OrderProcessingError
    | DatabaseOperationCompleted of DatabaseOperationConfirmation
    | OtherEvent

// -------------------------
// Helper Function Definitions
// -------------------------

// Helper Function for submitting a new order on an exchange
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

// Helper function for retrieving the order status updates from the exchange
let processOrderUpdate (orderUpdateEvent: OrderUpdateEvent) : Async<Result<OrderStatusUpdateReceived, string>> =
    async {
        // Step 1: Wait for a delay before retrieving order status
        do! Task.Delay(30000) |> Async.AwaitTask

        // Step 2: Retrieve order status from the respective exchange
        let result = 
            match orderUpdateEvent.OrderDetails.Exchange with
            | "Bitfinex" -> await BitfinexAPI.retrieveOrderTrades orderUpdateEvent.OrderDetails.Currency orderUpdateEvent.OrderID
            | "Kraken" -> await KrakenAPI.queryOrderInformation (int64 orderUpdateEvent.OrderID)
            | "Bitstamp" -> await BitstampAPI.orderStatus (orderUpdateEvent.OrderID.ToString())
            | _ -> return Result.Error "Unsupported exchange"

        // Step 3: Process the result
        match result with
        | Some status ->
            // Additional processing based on the order status, including database updates and user notifications
            // ...
            return Result.Ok { OrderID = orderUpdateEvent.OrderID; ExchangeName = orderUpdateEvent.OrderDetails.Exchange }
        | None -> 
            return Result.Error "Failed to retrieve order status"
    }


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

// Workflow: Retrieve and handle Order Updates
let processOrderUpdate (orderID: OrderID) (orderDetails: OrderDetails) : Async<Result<Event, string>> =
    async {
        // Wait for 30 seconds to get the order status updates
        do! Task.Delay(30000) |> Async.AwaitTask

        // Retrieve the order status from the respective exchange
        let result = 
            match orderDetails.Exchange with
            | "Bitfinex" -> await BitfinexAPI.retrieveOrderTrades orderDetails.Currency orderID
            | "Kraken" -> await KrakenAPI.queryOrderInformation (int64 orderID)
            | "Bitstamp" -> await BitstampAPI.orderStatus (orderID.ToString())
            | _ -> return Result.Error "Unsupported exchange"

        // Process the retrieved order status
        match result with
        | Some status ->
            match status with
            | FullyFulfilled -> 
                // 1. For fully fulfilled orders, store the transaction history in the database
                let updatedEntity = 
                    { new OrderEntity() with
                        OrderID = orderID.ToString()
                        // Set other properties as needed
                    }
                if updateOrderInDatabase updatedEntity then
                    return Result.Ok (OrderFulfillmentUpdated Filled)
                else
                    return Result.Error "Failed to update order in database"
            | PartiallyFulfilled ->
                // 2. For partially fulfilled orders, issue a new order and update the database
                let remainingAmount = // Calculate the remaining amount
                let newOrderDetails = 
                    { orderDetails with Quantity = remainingAmount }
                // Issue a new order
                let! newOrderResult = initiateBuySellOrderAsync newOrderDetails
                match newOrderResult with
                | Result.Ok newOrderID ->
                    // Update the database
                    // ...
                    return Result.Ok (OrderFulfillmentUpdated PartiallyFilled)
                | Result.Error errMsg ->
                    return Result.Error errMsg
            | OneSideFilled ->
                // 3. For orders with only one side filled, notify the user and update the database
                if userNotification { OrderID = orderID; FulfillmentDetails = OnlyOneSideFilled } |> Option.isSome then
                    return Result.Ok (UserNotificationSent orderID)
                else
                    return Result.Error "Failed to send user notification"
            | NotFilled ->
                // 4. For unfilled orders, choose an appropriate action
                // ...
                return Result.Ok (OrderFulfillmentUpdated NotFilled)
        | None -> 
            return Result.Error "Failed to retrieve order status"
    }


// Workflow: User Notification When Only One Side of the Order is Filled, to be in more details during Milestone IV.
let userNotification (orderOneSideFilled: OrderOneSideFilled) : NotificationSentConfirmation option =
    match sendEmailToUser orderOneSideFilled.OrderID with
    | true -> 
        match checkIfNotificationSent orderOneSideFilled.OrderID with
        | true -> Some orderOneSideFilled.OrderID
        | false -> None // Notification not sent
    | false -> None // Email sending failed



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

