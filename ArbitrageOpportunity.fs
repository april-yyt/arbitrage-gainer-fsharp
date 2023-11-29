module ArbitrageOpportunity

open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open OrderManagement
open TradingStrategy

let apiKey = "qC2Ix1WnmcpTMRP2TqQ8hVZsxihJq7Hq";
let private httpClient = new HttpClient()

// ---------------------------
// Types and Event Definitions
// ---------------------------

type Time = int
type Quote = {
    Exchange: Exchange
    CurrencyPair: CurrencyPair;
    BidPrice: Price;
    AskPrice: Price;                          
    BidSize: Quantity;
    AskSize: Quantity;
    Time: Time;
}
type UnprocessedQuote = {
    [<JsonProperty("ev")>]
    EventType: string
    [<JsonProperty("pair")>]
    CurrencyPair: string
    [<JsonProperty("bp")>]
    BidPrice: Quantity
    [<JsonProperty("bs")>]
    BidSize: Quantity
    [<JsonProperty("ap")>]
    AskPrice: Price
    [<JsonProperty("as")>]
    AskSize: Price
    [<JsonProperty("t")>]
    Time: Time
    [<JsonProperty("x")>]
    Exchange: int
    [<JsonProperty("r")>]
    ReceiveTime: Time
} 

type Event = 
    | QuoteFeedSubscribed
    | QuoteFeedUnsubscribed
    | MarketDataRetrieved
    | OrdersEmitted

type RealTimeDataFeedSubscribed = {
    CurrencyPairs: CurrencyPair list
}
type RealTimeDataFeedUnubscribed = {
    CurrencyPairs: CurrencyPair list
}
type MarketDataRetrieved = {
    Quotes: Quote list
}
type OrderEmitted = {
    Orders: OrderDetails list
}

type QuoteMessage = 
    | UpdateData of Quote
    | RetrieveLatestData of AsyncReplyChannel<Quote list>

// ------
// Agents: each agent stores a list consisting of the latest Quote for each currency pair
// ------

let bitstampAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop bitstampData =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateData newData -> 
                    // tentative 
                    let updatedBitstampData = newData :: (bitstampData |> List.filter(fun x -> x.CurrencyPair <> newData.CurrencyPair))
                    return! loop updatedBitstampData 
                | RetrieveLatestData replyChannel ->
                    replyChannel.Reply(bitstampData)
                    return! loop bitstampData
            }
        loop [])

let bitfinexAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop bitfinexData =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateData newData -> 
                    let updatedBitfinexData = newData :: (bitfinexData |> List.filter(fun x -> x.CurrencyPair <> newData.CurrencyPair))
                    return! loop updatedBitstampData 
                | RetrieveLatestData replyChannel ->
                    replyChannel.Reply(bitfinexData)
                    return! loop bitfinexData
            }
        loop [])

let krakenAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop krakenData =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateData newData -> 
                    let updatedKrakenData = newData :: (krakenData |> List.filter(fun x -> x.CurrencyPair <> newData.CurrencyPair))
                    return! loop updatedKrakenData 
                | RetrieveLatestData replyChannel ->
                    replyChannel.Reply(krakenData)
                    return! loop krakenData
            }
        loop [])

// ----------
// Workflows
// ----------

// Workflow: After trading strategy is activated, subscribe to real-time data feed

// Helper that fetches crypto pairs
let fetchCryptoPairsFromDB = 
// TODO: db operation
   |> List.fold (fun acc pair -> acc + ",XQ." + pair) ""

// Subscribe to real time data via Polygon
let subscribeToRealTimeDataFeed (input: TradingStrategyActivated) = 
    async {
        // connect to Polygon and authenticate
        let url = "wss://socket.polygon.io/crypto"
        let authPayload = {
            action = "auth",
            params = apiKey,
        }
        let authJson = JsonConvert.SerializeObject(authPayload)
        let authContent = new StringContent(authJson, Encoding.UTF8, "application/json")
        do! httpClient.PostAsync(url, authContent) |> Async.AwaitTask

        // connect to websocket
        let ws = new ClientWebSocket()
        do! ws.ConnectAsync(url, CancellationToken.None) |> Async.AwaitTask

        // subscribe
        let! cryptoPairs = fetchCryptoPairsFromDB
        let subscribePayload = {action = "subscribe", params = cryptoPairs};
        let subscribeJson = JsonConvert.SerializeObject(authPayload)
        let subscribeContent = new StringContent(authJson, Encoding.UTF8, "application/json")
        do! httpClient.PostAsync(url, subscribeContent) |> Async.AwaitTask
    }

// Workflow: Pause trading when trading strategy is deactivated
let unsubscribeRealTimeDataFeed (ws: ClientWebSocket) = 
    async {
        do! ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Trading Stopped", CancellationToken.None) 
            |> Async.AwaitTask
    }

// Workflow: Retrieve data from real-time data feed

// Helper that parses JSON array received from Polygon to a quote list
let parseJsonArray (res: string) : Quote list =
    let unprocessedQuotes = JsonConvert.DeserializeObject<UnprocessedQuote[]>(res) |> Array.toList
    List.map (fun quote -> {
        Exchange = getExchangeFromQuote quote
        CurrencyPair = {
            Currency1 = quote.CurrencyPair.[0..2]
            Currency2 = quote.CurrencyPair.[3..5]
        };
        BidPrice = quote.BidPrice;
        AskPrice = quote.AskPrice;                          
        BidSize = quote.BidSize;
        AskSize = quote.AskSize;
        Time = quote.Time;
    }) unprocessedQuotes

// Helper that continuously receives market data from websocket and assess 
// arbitrage opportunities when trading strategy is activated, 
let rec receiveMsgFromWSAndTrade (ws: ClientWebSocket) = 
    async {
        let tradingStrategyActivated = tradingStrategyAgent.PostAndReply(RetrieveTradingStrategyStatus)
        match tradingStrategyActivated with
        | true ->
            let buffer = ArraySegment<byte>(Array.zeroCreate 2048)
            let! response = ws.ReceiveAsync(buffer, CancellationToken.None) |> Async.AwaitTask
            let message = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, response.Count)
            let quotes = parseJsonArray message
            // assess arbitrage opportunity for all quotes 
            let retrievedMarketData = {Quotes = quotes}
            assessRealTimeArbitrageOpportunity retrievedMarketData
            return! receiveMsgFromWS ws
        | false -> unsubscribeRealTimeDataFeed ws
    }

// Continuously receives market data from websocket and trade
let retrieveDataFromRealTimeFeedAndTrade (input: QuoteFeedSubscribed) : MarketDataRetrieved = 
    async {
        do! receiveMsgFromWSAndTrade ws |> Async.AwaitTask
    }

// Workflow: Assess Real Time Arbitrage Opportunity

let strategy = tradingStrategyAgent.PostAndReply(GetParams)
let currentDailyVolume = volumeAgent.PostAndReply(CheckCurrentVolume)

// Helper that checks whether there is valid price spread
let minPriceSpreadReached (ask: Quote) (bid: Quote) = 
    let priceSpread = bid.BidPrice - ask.AskPrice
    priceSpread >= strategy.minPriceSpread

// Helper that returns the correct agent based on quote
let getAgentFromUnprocessedQuote (data: Quote) = 
    match data.Exchange with
    | 2 -> bitfinexAgent
    | 6 -> bitstampAgent
    | 23 -> krakenAgent

// Helper that returns the correct exchange based on quote
let getExchangeFromUnprocessedQuote (data: Quote) = 
    match data.Exchange with
    | 2 -> "Bitfinex"
    | 6 -> "Bitstamp"
    | 23 -> "Kraken"

// Helper that calculates order volume based on user provided limits
let calculateWorthwhileTransactionVolume (ask: Quote) (bid: Quote) = 
    let idealVolume = min ask.AskSize bid.BidSize
    let minProfitReached = idealVolume * priceSpread >= strategy.MinTransactionProfit
    match minProfitReached with
    | true -> 
        let priceSum = ask.AskPrice + bid.BidPrice
        let maxVolumeUnderTotalAmountLimit = min idealVolume strategy.MaxAmountTotal / priceSum
        let maxVolumeUnderDailyVolumeLimit = min idealVolume strategy.MaxDailyVolume - currentDailyVolume 
        min maxVolumeUnderTotalAmountLimit maxVolumeUnderDailyVolumeLimit
    | false -> 0

// Helper that identifies worthwhile orders for an ask-bid pair
let identifyWorthwhileTransactions (ask: Quote) (bid: Quote) = 
    match minPriceSpreadReached ask bid with
    | true ->   
        let worthwhileTransactionVolume = calculateWorthwhileTransactionVolume ask bid
        match worthwhileTransactionVolume with
        | worthwhileTransactionVolume when worthwhileTransactionVolume > 0 ->
            // assuming that we can keep trading a quote if the traded volume has not reached the ask/bid size yet
            // store the quote with remaining size back to agent
            let askAgent = getAgentFromQuote ask
            let remainingAskData = {
                Exchange = ask.Exchange;
                CurrencyPair = ask.CurrencyPair;
                AskPrice = ask.AskPrice;
                AskSize = ask.AskSize - worthwhileTransactionVolume;
                BidPrice = ask.BidPrice;
                BidSize = ask.BidSize - worthwhileTransactionVolume;
                Time = ask.Time;
            }
            askAgent.Post(UpdateData remainingAskData)

            let bidAgent = getAgentFromQuote bid
            let remainingBidData = {
                EventType = ask.EventType
                Exchange = bid.Exchange;
                CurrencyPair = bid.CurrencyPair;
                AskPrice = bid.AskPrice;
                AskSize = bid.AskSize - worthwhileTransactionVolume;
                BidPrice = bid.BidPrice;
                BidSize = bid.BidSize - worthwhileTransactionVolume;
                Time = bid.Time;
                ReceiveTime = bid.ReceiveTime
            }
            bidAgent.Post(UpdateData remainingBidData)

            let buyOrder = {
                Exchange = ask.Exchange;
                Currency = ask.CurrencyPair.Currency1;
                OrderType = Buy;
                Price = ask.AskPrice;
                Quantity = worthwhileTransactionVolume;
            }
            let sellOrder = {
                Exchange = bid.Exchange;
                Currency = bid.CurrencyPair.Currency1;
                OrderType = Sell;
                Price = bid.BidPrice;
                Quantity = worthwhileTransactionVolume;
            }
            [buyOrder, sellOrder]
        | _ -> []
    | false -> []

// Filter all quotes with same currency pair from other exchanges to identify 
// arbitrage opportunity and emit corresponding orders for each quote
let assessRealTimeArbitrageOpportunity (marketDataRetrieved: MarketDataRetrieved): OrderEmitted = 
    let quotes = marketDataRetrieved.Quotes
    quotes |> List.fold (fun acc quote ->
        let bitstampData = bitstampAgent.PostAndReply(RetrieveLatestData) 
        let bitfinexData = bitfinexAgent.PostAndReply(RetrieveLatestData)
        let krakenData = krakenAgent.PostAndReply(RetrieveLatestData)
        let allQuotes = bitstampData @ bitfinexData @ krakenData
        let orders = allQuotes 
            |> List.filter(fun x -> quote.Exchange <> x.Exchange && quote.CurrencyPair = x.CurrencyPair) 
            |> List.fold (fun acc otherQuote ->
                acc @ identifyWorthwhileTransactions quote otherQuote @ identifyWorthwhileTransactions otherQuote quote
            ) []
        acc @ orders
    )[]


// Following is a sample code that runs the trading algo, doesn't belong to any workflow
let sampleRun = 
    async {
        do! subscribeToRealTimeDataFeed |> Async.AwaitTask
        do! retrieveDataFromRealTimeFeedAndTrade |> Async.AwaitTask
    }
let main = 
    sampleRun |> Async.RunSynchronously


