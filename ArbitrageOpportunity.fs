module ArbitrageOpportunity

open OrderManagement
open TradingStrategy

type RealTimeData = {
    Exchange: Exchange
    Currency: Currency;
    BidPrice: Price;
    AskPrice: Price;                          
    BidSize: Quantity;
    AskSize: Quantity;
}

type Event = 
    | RealTimeDataFeedSubscribed
    | RealTimeDataFeedUnsubscribed
    | MarketDataRetrieved
    | OrdersEmitted

type RealTimeDataFeedSubscribed = {
    Currencies: list<Currency>
}
type RealTimeDataFeedUnsubscribed = {
    Currencies: list<Currency>
}
type MarketDataRetrieved = {
    Data: RealTimeData
}
type OrderEmitted = {
    Orders: list<OrderDetails>
}

type RealTimeDataMessage = 
    | UpdateData of RealTimeData
    | RetrieveData of AsyncReplyChannel<list<RealTimeData>>

// Agents: each agent stores a list consisting of the latest RealTimeData for each currency
let bitstampAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop bitstampData =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateData newData -> 
                    // tentative 
                    let updatedBitstampData = newData :: (bitstampData |> List.filter(fun x -> x.Currency != newData.Currency))
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
                    let updatedBitfinexData = newData :: (bitfinexData |> List.filter(fun x -> x.Currency != newData.Currency))
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
                    let updatedKrakenData = newData :: (krakenData |> List.filter(fun x -> x.Currency != newData.Currency))
                    return! loop updatedKrakenData 
                | RetrieveData replyChannel ->
                    replyChannel.Reply(krakenData)
                    return! loop krakenData
            }
        loop [])


// Workflow: After trading strategy is activated, subscribe to real-time data feed
let subscribeToRealTimeDataFeed (input: TradingStrategyActivated) = 
    true // placeholder for subscribing logic

// Workflow: Retrieve data from real-time data feed
let retrieveDataFromRealTimeFeed (input: RealTimeDataFeedSubscribed) : MarketDataRetrieved = 
    true // placeholder for fetching data from feed and updating data at agent

// Workflow: Assess Real Time Arbitrage Opportunity
let strategy = tradingStrategyAgent.PostAndReply(GetParams)
let currentDailyVolume = volumeAgent.PostAndReply(CheckCurrentVolume)

let minPriceSpreadReached (ask: RealTimeData) (bid: RealTimeData) = 
    let priceSpread = bid.BidPrice - ask.AskPrice
    priceSpread >= strategy.minPriceSpread

let getAgentFromRealTimeData (data: RealTimeData) = 
    match data with
    | data when data.Exchange = Bitstamp -> bitstampAgent
    | data when data.Exchange = Bitfinex -> bitfinexAgent
    | data when data.Exchange = Kraken -> krakenAgent

let calculateWorthwhileTransactionVolume (ask: RealTimeData) (bid: RealTimeData) = 
    let idealVolume = min ask.AskSize bid.BidSize
    let minProfitReached = idealVolume * priceSpread >= strategy.MinTransactionProfit
    match minProfitReached with
    | true -> 
        let priceSum = ask.AskPrice + bid.BidPrice
        let maxVolumeUnderTotalAmountLimit = min idealVolume strategy.MaxAmountTotal / priceSum
        let maxVolumeUnderDailyVolumeLimit = min idealVolume strategy.MaxDailyVolume - currentDailyVolume 
        min maxVolumeUnderTotalAmountLimit maxVolumeUnderDailyVolumeLimit
    | false -> 0

let identifyWorthwhileTransactions (ask: RealTimeData) (bid: RealTimeData) = 
    match minPriceSpreadReached ask bid with
    | true ->   
        let worthwhileTransactionVolume = calculateWorthwhileTransactionVolume ask bid
        match worthwhileTransactionVolume with
        | worthwhileTransactionVolume when worthwhileTransactionVolume > 0 ->
            let askAgent = getAgentFromRealTimeData ask
            let remainingAskData = {
                Exchange = ask.Exchange;
                Currency = ask.Currency;
                AskPrice = ask.AskPrice;
                AskSize = ask.AskSize - worthwhileTransactionVolume;
                BidPrice = ask.BidPrice;
                BidSize = ask.BidSize - worthwhileTransactionVolume;
            }
            askAgent.Post(UpdateData remainingAskData)

            let bidAgent = getAgentFromRealTimeData bid
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

let assessRealTimeArbitrageOpportunity (input: MarketDataRetrieved): OrdersEmitted = 
    let bitstampData = bitstampAgent.PostAndReply(RetrieveBitstampData)
    let bitfinexData = bitstampAgent.PostAndReply(RetrieveBitfinexData)
    let krakenData = krakenAgent.PostAndReply(RetrieveKrakenData)
    let quotes = 
        match input with
        | input when input.Exchange = Bitstamp -> bitfinexData @ krakenData
        | input when input.Exchange = Bitfinex -> bitstampData @ krakenData
        | input when input.Exchange = Kraken -> bitfinexData @ bitstampData
    quotes |> List.fold (fun acc quote ->
        match quote with
        | quote when quote.Currency = input.Currency -> acc
        | quote when quote.Currency != input.Currency ->
            acc @ identifyWorthwhileTransactions input quote @ identifyWorthwhileTransactions quote input
    ) []

// Workflow: Pause trading when trading strategy is deactivated
let unsubscribeRealTimeDataFeed (input: TradingStrategyDeactivated) = 
    true // Placeholder for unsubscribing logic




