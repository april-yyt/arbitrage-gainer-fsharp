module ArbitrageOpportunity

open OrderManagement
open TradingStrategy

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
    Quote: Quote
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
let subscribeToRealTimeDataFeed (input: TradingStrategyActivated) = 
    true // placeholder for subscribing logic


// Workflow: Retrieve data from real-time data feed
let retrieveDataFromRealTimeFeed (input: QuoteFeedSubscribed) : MarketDataRetrieved = 
    true // placeholder for fetching data from feed and updating data at agent
    // The returned event MarketDataRetrieved is for one single Quote, 
    // this is under the assumption that the real time market data keeps coming
    // in separately (one quote for one currency pair each time). Each time a 
    // quote comes in, we do the trading immediately


// Workflow: Assess Real Time Arbitrage Opportunity
let strategy = tradingStrategyAgent.PostAndReply(GetParams)
let currentDailyVolume = volumeAgent.PostAndReply(CheckCurrentVolume)

let minPriceSpreadReached (ask: Quote) (bid: Quote) = 
    let priceSpread = bid.BidPrice - ask.AskPrice
    priceSpread >= strategy.minPriceSpread

let getAgentFromQuote (data: Quote) = 
    match data.Exchange with
    | Bitstamp -> bitstampAgent
    | Bitfinex -> bitfinexAgent
    | Kraken -> krakenAgent

// calculate order volume based on user provided limits
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
                Exchange = bid.Exchange;
                CurrencyPair = bid.CurrencyPair;
                AskPrice = bid.AskPrice;
                AskSize = bid.AskSize - worthwhileTransactionVolume;
                BidPrice = bid.BidPrice;
                BidSize = bid.BidSize - worthwhileTransactionVolume;
                Time = bid.Time;
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
// arbitrage opportunity and emit corresponding orders
let assessRealTimeArbitrageOpportunity (marketDataRetrieved: MarketDataRetrieved): OrderEmitted = 
    let quote = marketDataRetrieved.Quote
    let bitstampData = bitstampAgent.PostAndReply(RetrieveLatestData) 
    let bitfinexData = bitfinexAgent.PostAndReply(RetrieveLatestData)
    let krakenData = krakenAgent.PostAndReply(RetrieveLatestData)
    let allQuotes = bitstampData @ bitfinexData @ krakenData
    let quotes = allQuotes 
        |> List.filter(fun x -> quote.Exchange <> x.Exchange && quote.CurrencyPair = x.CurrencyPair) 
        |> List.fold (fun acc otherQuote ->
            acc @ identifyWorthwhileTransactions quote otherQuote @ identifyWorthwhileTransactions otherQuote quote
        ) []


// Workflow: Pause trading when trading strategy is deactivated
let unsubscribeRealTimeDataFeed (input: TradingStrategyDeactivated) = 
    true // Placeholder for unsubscribing logic




