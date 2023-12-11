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

type OrderUpdate = { OrderID: OrderID; OrderDetails: OrderDetails }
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
    async {
        try 
            match orderDetails.Exchange with
            | "Bitfinex" -> 
                let orderType = "MARKET"
                let symbol = "t" + orderDetails.Currency
                let! responseOption = BitfinexAPI.submitOrder orderType symbol (orderDetails.Quantity.ToString()) (orderDetails.Price.ToString())
                match responseOption with
                | Some (response: BitfinexResponse) -> 
                    match response with
                    | head :: _ -> return Result.Ok (IntOrderID head.id)
                    | [] -> return Result.Error "Empty Bitfinex response"
                | None -> return Result.Error "Failed to submit order to Bitfinex"
            | "Kraken" -> 
                let orderType = "market"
                let pair = "XX" + orderDetails.Currency
                let! responseOption = KrakenAPI.submitOrder pair orderType (orderDetails.Quantity.ToString()) (orderDetails.Price.ToString())
                match responseOption with
                | Some (response: KrakenResponse) -> 
                    match response.result.txid with
                    | txidHead :: _ -> return Result.Ok (StringOrderID txidHead)
                    | [] -> return Result.Error "No transaction ID in Kraken response"
                | None -> return Result.Error "Failed to submit order to Kraken"
            | "Bitstamp" -> 
                let action = match orderDetails.OrderType with
                                | Buy -> BitstampAPI.buyMarketOrder
                                | Sell -> BitstampAPI.sellMarketOrder
                let! responseOption = action orderDetails.Currency (orderDetails.Quantity.ToString()) None
                // printfn "Bitstamp response: %A" responseOption
                match responseOption with
                | Some id -> return Result.Ok (StringOrderID id)
                | None -> return Result.Error "Failed to submit order to Bitstamp"
            | _ -> 
                return Result.Error "Unsupported exchange"
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

let createOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderID, string>> =
    async {
        let! result = initiateBuySellOrderAsync orderDetails
        match result with
        | Result.Ok orderID ->
            // Database operations removed
            return Result.Ok orderID
        | Result.Error errMsg ->
            return Result.Error errMsg
    }


let createOrdersParallel (ordersEmitted: OrderDetails list) : Result<OrderID list, string> =
    ordersEmitted
    |> List.map createOrderAsync
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.fold (fun acc result ->
        match acc, result with
        | Result.Ok idList, Result.Ok orderID -> 
            Result.Ok (orderID :: idList)
        | Result.Error errMsg, _ | _, Result.Error errMsg ->
            Result.Error errMsg
        | Result.Ok _, Result.Ok _ -> acc
    ) (Result.Ok [])
    |> function
        | Result.Ok idList -> Result.Ok (List.rev idList)
        | Result.Error errMsg -> Result.Error errMsg

let createOrdersSequential (ordersEmitted: OrderDetails list) : Result<OrderID list, string> =
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
        | Result.Ok (FullyFulfilled transactionHistory) ->
            // for fully fulfilled orders, update the status in the database
            let updatedEntity = 
                transactionHistory
                |> List.map (fun t -> 
                    { new OrderEntity() with
                        OrderID = t.OrderID
                        // ... Set other properties as needed ...
                    })
            match updateOrderInDatabase updatedEntity with
            | true -> return Result.Ok (OrderFulfillmentUpdated Filled)
            | false -> return Result.Error "Failed to update order in database"

        | Result.Ok (PartiallyFulfilled (transactionHistory, remainingAmount)) ->
            // for partiallyFulfilled orders <- judged from returned response
            // emit one more order with the remaining amount (desired amount – booked amount)
            // store the realized transactions and the newly emitted order in the database
            let newOrderDetails = 
                { orderDetails with Quantity = remainingAmount }
            let! newOrderResult = initiateBuySellOrderAsync newOrderDetails
            match newOrderResult with
            | Result.Ok newOrderID ->
                let updatedEntity = 
                    transactionHistory
                    |> List.map (fun t -> 
                        { new OrderEntity() with
                            OrderID = t.OrderID
                            // ... Set other properties ...
                        })
                match updateOrderInDatabase updatedEntity with
                | true -> return Result.Ok (OrderFulfillmentUpdated PartiallyFilled)
                | false -> return Result.Error "Failed to update order in database"
            | Result.Error errMsg ->
                return Result.Error errMsg

        | Result.Ok (OneSideFilled transactionHistory) ->
            // notify the user via e-mail 
            // persist the transaction history in the database -> update the order status as one side filled
            match userNotification { OrderID = orderID; FulfillmentDetails = OnlyOneSideFilled } with
            | Some _ -> return Result.Ok (UserNotificationSent orderID)
            | None -> return Result.Error "Failed to send user notification"
            let updatedEntity = 
                transactionHistory
                |> List.map (fun t -> 
                    { new OrderEntity() with
                        OrderID = t.OrderID
                        // ... Set other properties ...
                    })
            match updateOrderInDatabase updatedEntity with
            | true -> return Result.Ok (OrderFulfillmentUpdated OneSideFilled)
            | false -> return Result.Error "Failed to update order in database"

        | Result.Error errMsg -> 
            return Result.Error errMsg
        | _ -> 
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
