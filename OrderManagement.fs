module OrderManagement

// Types
type Currency = string
type Price = float
type OrderType = Buy | Sell
type Quantity = int
type Exchange = string

type OrderDetails = {
    Currency: Currency
    Price: Price
    OrderType: OrderType
    Quantity: Quantity
    Exchange: Exchange
}

type FulfillmentStatus =
    | Filled
    | PartiallyFilled
    | OnlyOneSideFilled
    | NotFilled

type OrderFulfillment =
    | OrderFulfilled of FulfillmentStatus
    | OrderUnfulfilled

type OrderID = int
type TradeID = int

// Events
type Event =
    | OrderCreated of OrderID
    | TradeExecuted of TradeID option
    | OrderFulfillmentUpdated of FulfillmentStatus
    | TransactionTotalsUpdated of string option
    | UserNotificationSent of int option
    | OrderUpdatePushed of (OrderID * string) option
    | OrderErrorHandled of (OrderID * string) option
    | None


// events that involves side effects

// Helper Functions (Placeholders for actual implementations)
let generateOrderID () = // Logic to generate unique OrderID
    1001 // Placeholder for actual ID generation logic

let generateTradeID () = // Logic to generate unique TradeID
    2001 // Placeholder for actual ID generation logic

let recordOrder (orderDetails: OrderDetails) = // Logic to record order in database
    true // Placeholder for actual database recording success

let executeTrade (orderDetails: OrderDetails) = // Logic to execute trade with external systems
    true // Placeholder for actual trade execution success

let notifyUser (message: string) = // Logic to notify user of order status
    true // Placeholder for actual user notification logic

let pushUpdateToExchange (orderDetails: OrderDetails) = // Logic to push update to exchange
    true // Placeholder for actual update push to exchange

let handleError (errorDetails: string) = // Logic to handle any errors
    true // Placeholder for actual error handling logic

// Workflows
let createOrder (orderDetails: OrderDetails) : Event =
    if recordOrder orderDetails then
        OrderCreated (generateOrderID ())
    else
        None 

let tradeExecution (orderDetails: OrderDetails) : Event =
    if executeTrade orderDetails then
        TradeExecuted (Some (generateTradeID ()))
    else
        TradeExecuted None

let orderFulfillment (orderDetails: OrderDetails) : OrderFulfillment =
    // Check an order's fulfillment status in a database or via an API.
    OrderFulfilled Filled  // Assuming the order is always fulfilled for the example.

let updateTransactionTotals (fulfillmentStatus: FulfillmentStatus) : Event =
    match fulfillmentStatus with
    | Filled | PartiallyFilled -> TransactionTotalsUpdated (Some "Transaction totals updated successfully.")
    | OnlyOneSideFilled -> TransactionTotalsUpdated (Some "Only one side of the order filled; user notified.")
    | NotFilled -> TransactionTotalsUpdated None 

let userNotificationOneSideFilled (orderOneSideFilled: FulfillmentStatus) : Event =
    if notifyUser "One side of your order has been filled." then
        UserNotificationSent (Some (generateOrderID ()))
    else
        None

let pushOrderUpdate (orderDetails: OrderDetails) : Event =
    if pushUpdateToExchange orderDetails then
        OrderUpdatePushed (Some (generateOrderID (), orderDetails.Exchange))
    else
        None

let handleOrderError (errorDetails: string) : Event =
    if handleError errorDetails then
        OrderErrorHandled (Some (generateOrderID (), "The error has been handled successfully."))
    else
        None
