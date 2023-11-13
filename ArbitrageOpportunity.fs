module ArbitrageOpportunity

open OrderManagement
open TradingStrategy

type Time = int
type MarketQuote = {
    Exchange: Exchange
    CurrencyPair: CurrencyPair;
    BidPrice: Price;
    AskPrice: Price;                          
    BidSize: Quantity;
    AskSize: Quantity;
    Time: Time;
}

type Event = 
    | MarketQuoteFeedSubscribed
    | MarketQuoteFeedUnsubscribed
    | MarketDataRetrieved
    | OrdersEmitted

type MarketQuoteFeedSubscribed = {
    CurrencyPairs: list<CurrencyPair>
}
type MarketQuoteFeedUnsubscribed = {
    CurrencyPairs: list<CurrencyPair>
}
type MarketDataRetrieved = {
    Quote: MarketQuote
}
type OrderEmitted = {
    Orders: list<OrderDetails>
}

type MarketQuoteMessage = 
    | UpdateQuote of MarketQuote
    | RetrieveQuote of AsyncReplyChannel<list<MarketQuote>>

// Agents: each agent stores a list consisting of the latest MarketQuote for each currency pair
let bitstampAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop bitstampData =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateData newData -> 
                    // tentative 
                    let updatedBitstampData = newData :: (bitstampData |> List.filter(fun x -> x.CurrencyPair != newData.CurrencyPair))
                    return! loop updatedBitstampData 
                | RetrieveData replyChannel ->
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
                    let updatedBitfinexData = newData :: (bitfinexData |> List.filter(fun x -> x.CurrencyPair != newData.CurrencyPair))
                    return! loop updatedBitstampData 
                | RetrieveData replyChannel ->
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
                    let updatedKrakenData = newData :: (krakenData |> List.filter(fun x -> x.CurrencyPair != newData.CurrencyPair))
                    return! loop updatedKrakenData 
                | RetrieveData replyChannel ->
                    replyChannel.Reply(krakenData)
                    return! loop krakenData
            }
        loop [])


// Workflow: After trading strategy is activated, subscribe to real-time data feed
let subscribeToMarketQuoteFeed (input: TradingStrategyActivated) = 
    true // placeholder for subscribing logic

// Workflow: Retrieve data from real-time data feed
let retrieveDataFromRealTimeFeed (input: MarketQuoteFeedSubscribed) : MarketDataRetrieved = 
    true // placeholder for fetching data from feed and updating data at agent
    // The returned event MarketDataRetrieved is for one single MarketQuote, 
    // this is under the assumption that the real time market data keeps coming
    // in separately (one quote for one currency pair each time). Each time a 
    // quote comes in, we do the trading immediately

// Workflow: Assess Real Time Arbitrage Opportunity
let strategy = tradingStrategyAgent.PostAndReply(GetParams)
let currentDailyVolume = volumeAgent.PostAndReply(CheckCurrentVolume)

let minPriceSpreadReached (ask: MarketQuote) (bid: MarketQuote) = 
    let priceSpread = bid.BidPrice - ask.AskPrice
    priceSpread >= strategy.minPriceSpread

let getAgentFromMarketQuote (data: MarketQuote) = 
    match data with
    | data when data.Exchange = Bitstamp -> bitstampAgent
    | data when data.Exchange = Bitfinex -> bitfinexAgent
    | data when data.Exchange = Kraken -> krakenAgent

let calculateWorthwhileTransactionVolume (ask: MarketQuote) (bid: MarketQuote) = 
    let idealVolume = min ask.AskSize bid.BidSize
    let minProfitReached = idealVolume * priceSpread >= strategy.MinTransactionProfit
    match minProfitReached with
    | true -> 
        let priceSum = ask.AskPrice + bid.BidPrice
        let maxVolumeUnderTotalAmountLimit = min idealVolume strategy.MaxAmountTotal / priceSum
        let maxVolumeUnderDailyVolumeLimit = min idealVolume strategy.MaxDailyVolume - currentDailyVolume 
        min maxVolumeUnderTotalAmountLimit maxVolumeUnderDailyVolumeLimit
    | false -> 0

let identifyWorthwhileTransactions (ask: MarketQuote) (bid: MarketQuote) = 
    match minPriceSpreadReached ask bid with
    | true ->   
        let worthwhileTransactionVolume = calculateWorthwhileTransactionVolume ask bid
        match worthwhileTransactionVolume with
        | worthwhileTransactionVolume when worthwhileTransactionVolume > 0 ->
            let askAgent = getAgentFromMarketQuote ask
            let remainingAskData = {
                Exchange = ask.Exchange;
                Currency = ask.Currency;
                AskPrice = ask.AskPrice;
                AskSize = ask.AskSize - worthwhileTransactionVolume;
                BidPrice = ask.BidPrice;
                BidSize = ask.BidSize - worthwhileTransactionVolume;
            }
            askAgent.Post(UpdateData remainingAskData)

            let bidAgent = getAgentFromMarketQuote bid
            let remainingBidData = {
                Exchange = bid.Exchange;
                Currency = bid.Currency;
                AskPrice = bid.AskPrice;
                AskSize = bid.AskSize - worthwhileTransactionVolume;
                BidPrice = bid.BidPrice;
                BidSize = bid.BidSize - worthwhileTransactionVolume;
            }
            bidAgent.Post(UpdateData remainingBidData)

            let buyOrder = {
                Exchange = ask.Exchange;
                Currency = ask.Currency;
                OrderType = Buy;
                Price = ask.AskPrice;
                Quantity = worthwhileTransactionVolume;
            }
            let sellOrder = {
                Exchange = bid.Exchange;
                Currency = bid.Currency;
                OrderType = Sell;
                Price = bid.BidPrice;
                Quantity = worthwhileTransactionVolume;
            }
            [buyOrder, sellOrder]
        | _ -> []
    | false -> []

let assessArbitrageOpportunity (quote: MarketQuote) (otherQuotes: list<MarketQuote>): OrdersEmitted = 
    otherQuotes |> List.fold (fun acc otherQuote ->
        match otherQuote with
        | otherQuote when quote.CurrencyPair = quote.CurrencyPair -> acc
        | otherQuote when quote.CurrencyPair != quote.CurrencyPair ->
            acc @ identifyWorthwhileTransactions quote otherQuote @ identifyWorthwhileTransactions otherQuote quote
    ) []

let assessRealTimeArbitrageOpportunity (marketDataRetrieved: MarketDataRetrieved): OrdersEmitted = 
    let bitstampData = bitstampAgent.PostAndReply(RetrieveBitstampData)
    let bitfinexData = bitstampAgent.PostAndReply(RetrieveBitfinexData)
    let krakenData = krakenAgent.PostAndReply(RetrieveKrakenData)
    let quote = marketDataRetrieved.Quote
    let quotes = 
        match quote with
        | quote when quote.Exchange = Bitstamp -> bitfinexData @ krakenData
        | quote when quote.Exchange = Bitfinex -> bitstampData @ krakenData
        | quote when quote.Exchange = Kraken -> bitfinexData @ bitstampData
    assessArbitrageOpportunity quote quotes


// Workflow: Pause trading when trading strategy is deactivated
let unsubscribeMarketQuoteFeed (input: TradingStrategyDeactivated) = 
    true // Placeholder for unsubscribing logic




