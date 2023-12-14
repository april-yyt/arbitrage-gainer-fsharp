module ArbitrageOpportunity

open Types
open TradingStrategy
open HistoricalSpreadCalc
open System
open System.Threading
open System.Net.Http
open System.Net.WebSockets
open Newtonsoft.Json
open ServiceBus
open Azure
open Azure.Data.Tables
open System.IO

let wsUrl = "wss://one8656-live-data.onrender.com"
let apiKey = "qC2Ix1WnmcpTMRP2TqQ8hVZsxihJq7Hq"
let private httpClient = new HttpClient()

// ---------------------------
// Types and Event Definitions
// ---------------------------


type Event = 
    | QuoteFeedSubscribed
    | QuoteFeedUnsubscribed
    | MarketDataRetrieved
    | OrdersEmitted

type RealTimeDataFeedSubscribed = {
    CurrencyPairs: CurrencyPair seq
}
type RealTimeDataFeedUnubscribed = {
    CurrencyPairs: CurrencyPair seq
}
type MarketDataRetrieved = {
    Quotes: Quote seq
    TradingStrategy: TradingStrategyParameters
}
type OrderEmitted = {
    Orders: OrderDetails seq
}

type QuoteMessage = 
    | UpdateData of Quote
    | RetrieveLatestData of AsyncReplyChannel<Quote list>

type StatusMessage = 
    | UpdateStatus of bool
    | RetrieveStatus of AsyncReplyChannel<bool>

type ServiceInfo = {
    Name: string
}
type RemoteServiceError = {
    Service: ServiceInfo
    Exception: System.Exception
}
type AssessArbitrageOpportunityError = 
    | RemoteService of RemoteServiceError

// ---------------------------
// Error Handling Types and Adaptorss
// ---------------------------

type Result<'Success, 'Failure> = 
    | Ok of 'Success
    | Error of 'Failure

let bind inputFn twoTrackInput = 
    match twoTrackInput with
    | Ok success -> inputFn success
    | Error failure -> Error failure

// let bindAsync inputFn twoTrackInput = 
//     let! res = twoTrackInput
//     match res with
//     | Ok success -> return! inputFn success
//     | Error failure -> Error failure

let map inputFn res = 
    match res with
    | Ok success -> Ok (inputFn success)
    | Error failure -> Error failure

// let strategy = tradingStrategyAgent.PostAndReply(GetParams)
// let currentDailyVolume = volumeAgent.PostAndReply(CheckCurrentVolume)



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
                    return! loop updatedBitfinexData 
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

let tradingStatusAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop tradingStatus =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateStatus newStatus -> 
                    return! loop newStatus 
                | RetrieveStatus replyChannel ->
                    replyChannel.Reply(tradingStatus)
                    return! loop tradingStatus
            }
        loop false)

// --------------------------
// DB Configuration Constants
// --------------------------
// let storageConnString = "AzureStorageConnectionString" // This field will later use the connection string from the Azure console.
// let tableClient = TableServiceClient storageConnString
// let table = tableClient.GetTableClient "HistoricalArbitrageOpportunities"

// ----------
// Workflows
// ----------

// Workflow: After trading strategy is activated, subscribe to real-time data feed

// Helper that fetches crypto pairs
// let fetchCryptoPairsFromDB = 
//     let numOfTrackedCurrencies = strategy.TrackedCurrencies
//     table.Query<ArbitrageOpEntry> ()
//     |> Seq.cast<ArbitrageOpEntry>
//     |> Seq.sortByDescending (fun arbitrageOp -> arbitrageOp.NumberOpportunities)
//     |> Seq.take numOfTrackedCurrencies
//     |> Seq.fold (fun acc pair -> acc + ",XQ." + pair) ""

// Helper that authenticates Polygon
// let authenticatePolygon = 
//     async {
//         try 
//             let authPayload = {
//                 action = "auth",
//                 params = apiKey
//             }
//             let authJson = JsonConvert.SerializeObject(authPayload)
//             let authContent = new StringContent(authJson, Encoding.UTF8, "application/json")
//             let! res = httpClient.PostAsync(wsUrl, authContent) |> Async.AwaitTask
//             return Ok res
//         with
//         | ex -> 
//             return Error "Authentication Error"
//     }



// Helper that makes subscription request to Polygon
// let subscribeData = 
//     async {
//         try
//             let cryptoPairs = fetchCryptoPairsFromDB
//             let subscribePayload = {action = "subscribe", params = cryptoPairs};
//             let subscribeJson = JsonConvert.SerializeObject(subscribePayload)
//             let subscribeContent = new StringContent(subscribeJson, Encoding.UTF8, "application/json")
//             let! res = httpClient.PostAsync(wsUrl, subscribeContent) |> Async.AwaitTask
//             return Ok res
//         with
//         | ex -> 
//             return Error "Subscription Error"
//     }

// Subscribe to real time data via Polygon
// let subscribeToRealTimeDataFeed (input: TradingStrategyActivated) = 
//     async {
//         let! res = 
//             authenticatePolygon
//             |> bindAsync connectWebSocket
//             |> bindAsync subscribeData
//         return res
//     }



// Helper that connects to moc websocket
let connectWebSocket = 
    async {
        try
            let ws = new ClientWebSocket()
            do! ws.ConnectAsync(new System.Uri("wss://one8656-live-data.onrender.com"), CancellationToken.None) |> Async.AwaitTask
            printfn "Connected to WebSocket"
            return Ok ws
        with 
        | ex -> 
            return Error "WebSocket Connection Error"
    }

// Workflow: Retrieve data from real-time data feed

// Helper that returns the correct exchange based on quote
let getExchangeFromUnprocessedQuote (data: UnprocessedQuote) = 
    match data.Exchange with
    | 2 -> "Bitfinex"
    | 6 -> "Bitstamp"
    | 23 -> "Kraken"
    | _ -> "Others"

// Helper that gets Top N Currencies from DB
let getTopNCurrencies (n: int): CurrencyPair seq = 
    let storageConnString = "DefaultEndpointsProtocol=https;AccountName=18656team6;AccountKey=qJTSPfoWo5/Qjn9qFcogdO5FWeIYs9+r+JAp+6maOe/8duiWSQQL46120SrZTMusJFi1WtKenx+e+AStHjqkTA==;EndpointSuffix=core.windows.net" 
    let tableClient = TableServiceClient storageConnString
    let table = tableClient.GetTableClient("HistoricalArbitrageOpportunities")
    let queryResults = table.Query<ArbitrageOpEntry>()
    queryResults 
    |> Seq.map (fun entity ->
        currencyPairFromStr entity.CurrencyPair
    )
    |> Seq.take n

// Helper that parses JSON array received from Polygon to a quote list
let parseJsonArray (res: string) : Quote seq =
    let unprocessedQuotes = JsonConvert.DeserializeObject<UnprocessedQuote seq>(res)
    unprocessedQuotes 
        |> Seq.filter (fun quote -> quote.Exchange = 2 || quote.Exchange = 6 || quote.Exchange = 23)
        |> Seq.map (fun quote -> {
                Exchange = getExchangeFromUnprocessedQuote quote
                CurrencyPair = {
                    Currency1 = quote.CurrencyPair.[0..2]
                    Currency2 = quote.CurrencyPair.[4..6]
                };
                BidPrice = quote.BidPrice;
                AskPrice = quote.AskPrice;                          
                BidSize = quote.BidSize;
                AskSize = quote.AskSize;
                Time = quote.Time;
                // Time = Int64.Parse(quote.Time.TrimEnd('L'));
            }) 
        // |> Seq.filter (fun quote -> Seq.contains quote.CurrencyPair getTopNCurrencies)

// Workflow: Assess Real Time Arbitrage Opportunity

// Helper that checks whether there is valid price spread
let minPriceSpreadReached (ask: Quote) (bid: Quote) (strategy: TradingStrategyParameters) = 
    let priceSpread = bid.BidPrice - ask.AskPrice
    priceSpread >= strategy.MinPriceSpread

// Helper that returns the correct agent based on quote
let getAgentFromQuote (data: Quote) = 
    match data.Exchange with
    | "Bitstamp" -> bitstampAgent
    | "Bitfinex" -> bitfinexAgent
    | "Kraken" -> krakenAgent

// Helper that calculates order volume based on user provided limits
let calculateWorthwhileTransactionVolume (ask: Quote) (bid: Quote) (strategy: TradingStrategyParameters) = 
    let currentDailyVolume = volumeAgent.PostAndReply(CheckCurrentVolume)
    let idealVolume = min ask.AskSize bid.BidSize
    let priceSpread = bid.BidPrice - ask.AskPrice
    let minProfitReached = idealVolume * priceSpread >= strategy.MinTransactionProfit
    match minProfitReached with
    | true -> 
        let priceSum = ask.AskPrice + bid.BidPrice
        let maxVolumeUnderTotalAmountLimit = min idealVolume (strategy.MaxAmountTotal / priceSum)
        let maxVolumeUnderDailyVolumeLimit = min idealVolume ((strategy.MaxDailyVolume - currentDailyVolume) / 2.0)
        min maxVolumeUnderTotalAmountLimit maxVolumeUnderDailyVolumeLimit
    | false -> 0

// Helper that identifies worthwhile orders for an ask-bid pair
let identifyWorthwhileTransactions (ask: Quote) (bid: Quote) (strategy: TradingStrategyParameters)= 
    match minPriceSpreadReached ask bid strategy with
    | true ->   
        let worthwhileTransactionVolume = calculateWorthwhileTransactionVolume ask bid strategy
        // save remaining quote to agents
        let askAgent = getAgentFromQuote ask
        let remainingAskData = {
            Exchange = ask.Exchange;
            CurrencyPair = ask.CurrencyPair;
            AskPrice = ask.AskPrice;
            AskSize = ask.AskSize - worthwhileTransactionVolume;
            BidPrice = ask.BidPrice;
            BidSize = ask.BidSize;
            Time = ask.Time;
        }
        askAgent.Post(UpdateData remainingAskData)

        let bidAgent = getAgentFromQuote bid
        let remainingBidData = {
            Exchange = bid.Exchange;
            CurrencyPair = bid.CurrencyPair;
            AskPrice = bid.AskPrice;
            AskSize = bid.AskSize;
            BidPrice = bid.BidPrice;
            BidSize = bid.BidSize - worthwhileTransactionVolume;
            Time = bid.Time;
        }
        bidAgent.Post(UpdateData remainingBidData)

        match worthwhileTransactionVolume with
        | worthwhileTransactionVolume when worthwhileTransactionVolume > 0 ->
            volumeAgent.Post(UpdateVolume (worthwhileTransactionVolume * 2.0))

            let buyOrder = {
                Exchange = ask.Exchange;
                Currency = ask.CurrencyPair.Currency1;
                OrderType = "Buy";
                Price = ask.AskPrice;
                Quantity = worthwhileTransactionVolume;
            }
            let sellOrder = {
                Exchange = bid.Exchange;
                Currency = bid.CurrencyPair.Currency1;
                OrderType = "Sell";
                Price = bid.BidPrice;
                Quantity = worthwhileTransactionVolume;
            }
            [buyOrder; sellOrder]
        | _ -> []
    | false -> []

// Filter all quotes with same currency pair from other exchanges to identify 
// arbitrage opportunity and emit corresponding orders for each quote
let assessRealTimeArbitrageOpportunity (marketDataRetrieved: MarketDataRetrieved)= 
    let quotes = marketDataRetrieved.Quotes 
    let strategy = marketDataRetrieved.TradingStrategy
    
    quotes |> Seq.fold (fun acc quote ->
        let bitstampData = bitstampAgent.PostAndReply(RetrieveLatestData) 
        let bitfinexData = bitfinexAgent.PostAndReply(RetrieveLatestData)
        let krakenData = krakenAgent.PostAndReply(RetrieveLatestData)
        let allQuotes = bitstampData @ bitfinexData @ krakenData
        allQuotes
        |> Seq.iter (fun quote ->
            printfn "   C: %s Ex: %s AS: %f AP: %f BS: %f BP: %f T: %A" quote.CurrencyPair.Currency1 quote.Exchange quote.AskSize quote.AskPrice quote.BidSize quote.BidPrice quote.Time
        )
        let orders = 
            allQuotes 
            |> Seq.filter(fun x -> quote.Exchange <> x.Exchange && quote.CurrencyPair = x.CurrencyPair) 
            |> Seq.fold (fun acc otherQuote ->
                acc @ identifyWorthwhileTransactions quote otherQuote strategy @ identifyWorthwhileTransactions otherQuote quote strategy
            ) []
        acc @ orders
    ) []

// Helper to output arbitrage opportunities to a txt file
let writeOpportunitiesToFile (ops: OrderDetails list) =
    let writer = new StreamWriter("identifiedArbitrageOpportunities.txt", false)
    ops
    |> List.iter (fun op ->
        let text = JsonConvert.SerializeObject(op)
        writer.WriteLine text
        )
    writer.Flush()
    writer.Close()

// Helper that continuously receives market data from websocket and assess 
// arbitrage opportunities when trading strategy is activated, 
let rec receiveMsgFromWSAndTrade (ws: ClientWebSocket) (tradingStrategy: TradingStrategyParameters) = 
    async {
        let tradingStarted = tradingStatusAgent.PostAndReply(RetrieveStatus)
        match tradingStarted with
        | true ->
            let buffer = ArraySegment<byte>(Array.zeroCreate 2048)
            let! response = ws.ReceiveAsync(buffer, CancellationToken.None) |> Async.AwaitTask
            let message = System.Text.Encoding.UTF8.GetString(buffer.Array, buffer.Offset, response.Count)
            printfn "msg: %s" message
            try 
                let quotes = parseJsonArray message
                quotes
                |> Seq.iter (fun quote -> 
                    let agent = getAgentFromQuote quote
                    agent.Post(UpdateData quote)
                )

                // assess arbitrage opportunity for all quotes 
                let retrievedMarketData = {Quotes = quotes; TradingStrategy = tradingStrategy}
                let orders = assessRealTimeArbitrageOpportunity retrievedMarketData
                writeOpportunitiesToFile orders
                match List.length orders with
                | 0 -> None
                | _ ->
                    // send stringified orders to Order Bounded Context 
                    sendMessageAsync("orderqueue", JsonConvert.SerializeObject(orders))
                    // let msg = receiveMessageAsync "orderqueue"
                    let msg = JsonConvert.SerializeObject(orders)
                    printfn "%s" msg
                return! receiveMsgFromWSAndTrade ws tradingStrategy
            with 
            | ex -> 
                return! receiveMsgFromWSAndTrade ws tradingStrategy
        | false -> return None
    }

// Continuously receives market data from websocket and trade
let retrieveDataFromRealTimeFeedAndTrade (ws: ClientWebSocket) (strategy: TradingStrategyParameters) = 
    async {
        try
            let! res = receiveMsgFromWSAndTrade ws strategy
            return Ok res
        with
        | ex -> 
            return Error "WebSocket Message Reception Error"
    }


// Workflow: Pause trading when trading strategy is deactivated
let pauseTrading = 
    tradingStatusAgent.Post(UpdateStatus false)

// Main workflow: do real time trading
let doRealTimeTrading = 
    async {
        // receive messages from Trading Strategy Bounded Context, 
        // which will either be "stop trading", or stringified trading strategy 
        let msg = receiveMessageAsync "tradingqueue"
        match msg with
        | "stop trading" ->
            pauseTrading
        | _ ->
            let! res = connectWebSocket
            match res with
            | Ok ws -> 
                tradingStatusAgent.Post(UpdateStatus true)
                let tradingStrategy = JsonConvert.DeserializeObject<TradingStrategyParameters>(msg)
                let! res = retrieveDataFromRealTimeFeedAndTrade ws tradingStrategy
                res |> ignore
            | Error errstr -> Error errstr |> ignore
    }
    


