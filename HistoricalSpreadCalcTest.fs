open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running
open Newtonsoft.Json

type CurrencyPair = {
    Currency1: string
    Currency2: string
}
type Currency = string
type Price = float
type OrderType = Buy | Sell
type Quantity = float
type Exchange = string
type OrderID = int
type TradeID = int
type Time = int64

type UnprocessedQuote = {
    [<JsonProperty("ev")>]
    EventType: string
    [<JsonProperty("pair")>]
    CurrencyPair: string
    [<JsonProperty("bp")>]
    BidPrice: Price
    [<JsonProperty("bs")>]
    BidSize: Quantity
    [<JsonProperty("ap")>]
    AskPrice: Price
    [<JsonProperty("as")>]
    AskSize: Quantity
    [<JsonProperty("t")>]
    Time: Time
    [<JsonProperty("x")>]
    Exchange: int
    [<JsonProperty("r")>]
    ReceiveTime: Time
} 

type Quote = {
    Exchange: Exchange
    CurrencyPair: CurrencyPair;
    BidPrice: Price;
    AskPrice: Price;
    BidSize: Quantity;
    AskSize: Quantity;
    Time: Time;
}
type ArbitrageOpportunity = {
    Currency1: string
    Currency2: string
    NumberOfOpportunitiesIdentified: int
}

type BucketedQuotes = {
    Quotes: list<Quote>
}

let getExchangeFromUnprocessedQuote (data: UnprocessedQuote) = 
    match data.Exchange with
    | 2 -> "Bitfinex"
    | 6 -> "Bitstamp"
    | 23 -> "Kraken"

let processQuotes (unprocessedQuotes: UnprocessedQuote list) = 
    unprocessedQuotes 
        |> List.filter (fun quote -> quote.Exchange = 2 || quote.Exchange = 6 || quote.Exchange = 23)
        |> List.map (fun quote -> {
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
            }) 

// let bucketizeQuote (quote: Quote) : int =
//     // Convert timestamp to bucket number, assuming quote.Time is in milliseconds
//     (int)(quote.Time / 5)
let bucketSizeMs = 5L

let unixEpoch = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)

let toBucketKey (timestamp: int64) =
    let time = unixEpoch.AddMilliseconds(float timestamp)
    let timeSinceEpoch = (time.ToUniversalTime() - unixEpoch).TotalMilliseconds
    timeSinceEpoch / (float bucketSizeMs) |> int64

/// Regroups quotes into buckets of 5 milliseconds
[<Benchmark>]
let regroupQuotesIntoBuckets (quotes: Quote list) =
    quotes 
    |> List.groupBy (fun quote -> toBucketKey quote.Time)
    |> List.map (fun (bucketKey, bucketQuotes) -> { Quotes = bucketQuotes })
    

/// Selects the quote with the highest bid price for each exchange
[<Benchmark>]
let selectHighestBidPerExchange (quotes: list<Quote>) : list<Quote> =
    quotes
    |> List.groupBy (fun quote -> quote.Exchange)
    |> List.collect (fun (_, quotesByExchange) ->
        quotesByExchange
        |> List.maxBy (fun quote -> quote.BidPrice)
        |> List.singleton)

/// Identifies arbitrage opportunities within a list of quotes
[<Benchmark>]
let identifyArbitrageOpportunities (quotes: list<Quote>) : list<ArbitrageOpportunity> =
    let combinations = List.allPairs quotes quotes
    combinations
    |> List.choose (fun (quote1, quote2) ->
        match (quote1.CurrencyPair = quote2.CurrencyPair, quote1.Exchange <> quote2.Exchange) with
        | (true, true) ->
            let priceDifference = abs(quote1.BidPrice - quote2.AskPrice)
            match priceDifference > 0.01 with
            | true -> Some { Currency1 = quote1.CurrencyPair.Currency1; Currency2 = quote1.CurrencyPair.Currency2; NumberOfOpportunitiesIdentified = 1 }
            | false -> None
        | _ -> None)


[<EntryPoint>]
let main argv = 
    let json = System.IO.File.ReadAllText("historicalData.txt")
    let unprocessedQuotes = JsonConvert.DeserializeObject<UnprocessedQuote list>(json)
    let buckets = 
        unprocessedQuotes 
        |> processQuotes 
        |> regroupQuotesIntoBuckets 
    let opportunities =
        buckets
        |> List.collect (fun bucket ->
            let selectedQuotes = selectHighestBidPerExchange bucket.Quotes
            identifyArbitrageOpportunities selectedQuotes)
        |> List.groupBy (fun op -> (op.Currency1, op.Currency2))
        |> List.map (fun ((currency1, currency2), ops) -> 
            let num = List.sumBy (fun op -> op.NumberOfOpportunitiesIdentified) ops 
            printfn "%s%s %d" currency1 currency2 num
            { Currency1 = currency1; Currency2 = currency2; NumberOfOpportunitiesIdentified = num }
        )

    0

        // <Compile Include="database/DatabaseConfig.fs" />
        // <Compile Include="database/DatabaseSchema.fs" />
        // <Compile Include="database/DatabaseOperations.fs" />
        // <Compile Include="api/BitfinexAPI.fs" />
        // <Compile Include="api/BitstampAPI.fs" />
        // <Compile Include="api/KrakenAPI.fs" />
        // <Compile Include="TradingStrategy.fs" />
        // <Compile Include="OrderManagement.fs" />
        // <Compile Include="ArbitrageOpportunity.fs" />
        // <Compile Include="CrossTradedCryptos.fs" />
        // <Compile Include="HistoricalSpreadCalc.fs" />