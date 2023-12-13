module HistoricalSpreadCalcTest

open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running
open Newtonsoft.Json
open System
open System.IO
open System.Reflection
open ServiceBus
open Types
open HistoricalSpreadCalc


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
    | 1 -> "Coinbase"

let processQuotesSeq (unprocessedQuotes: UnprocessedQuote seq) = 
    unprocessedQuotes 
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
        }) 


let toBucketKey (timestamp: int64) =
    let bucketSizeMs = 5L
    let unixEpoch = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
    let time = unixEpoch.AddMilliseconds(float timestamp)
    let timeSinceEpoch = (time.ToUniversalTime() - unixEpoch).TotalMilliseconds
    timeSinceEpoch / (float bucketSizeMs) |> int64

/// Regroups quotes into buckets of 5 milliseconds

let regroupQuotesIntoBucketsSeq (quotes: Quote seq) = 
    quotes 
    |> Seq.groupBy (fun quote -> (quote.CurrencyPair, toBucketKey quote.Time))

/// Selects the quote with the highest bid price for each exchange
let selectHighestBidPerExchangeSeq (quotes: Quote seq) : Quote list =
    quotes
    |> Seq.groupBy (fun quote -> quote.Exchange)
    |> Seq.collect (fun (_, quotesByExchange) ->
        quotesByExchange
        |> Seq.maxBy (fun quote -> quote.BidPrice)
        |> Seq.singleton)
    |> Seq.toList

/// Identifies arbitrage opportunities within a list of quotes
let identifyArbitrageOpportunitiesSeq (selectedQuotes: Quote list) (quotes: Quote list) : list<ArbitrageOpportunity> =
    let combinations = List.allPairs selectedQuotes quotes
    combinations
    |> List.choose (fun (quote1, quote2) ->
        match (quote1.CurrencyPair = quote2.CurrencyPair, quote1.Exchange <> quote2.Exchange) with
        | (true, true) ->
            let priceDifference = quote1.BidPrice - quote2.AskPrice
            match priceDifference > 0.01 with
            | true -> Some { Currency1 = quote1.CurrencyPair.Currency1; Currency2 = quote1.CurrencyPair.Currency2; NumberOfOpportunitiesIdentified = 1 }
            | false -> None
        | _ -> None)

let calculateHistoricalSpreadSeq = 
    let writer = new StreamWriter("historicalAribtrageOpportunitites.txt", true)
    let json = System.IO.File.ReadAllText(@"/Users/audreyzhou/arbitrageGainer/historicalData.txt")
    let unprocessedQuotes = JsonConvert.DeserializeObject<UnprocessedQuote seq>(json)
    let buckets = 
        unprocessedQuotes 
        |> processQuotesSeq
        |> regroupQuotesIntoBucketsSeq 
    
    let opportunities =
        // buckets
        // |> Seq.iter(fun ((currencyPair, bucketKey), quotes) ->
        //     printfn "currencyPair: %s, bucket: %A" (currencyPair.Currency1 + currencyPair.Currency2) bucketKey
        //     quotes
        //     |> Seq.iter(fun quote-> 
        //         printfn "    Ex: %s AS: %f AP: %f BS: %f BP: %f T: %A" quote.Exchange quote.AskSize quote.AskPrice quote.BidSize quote.BidPrice quote.Time))
        buckets
        |> Seq.toList
        |> List.collect (fun (_, quotes) ->
            let selectedQuotes = selectHighestBidPerExchangeSeq quotes
            identifyArbitrageOpportunitiesSeq selectedQuotes (Seq.toList quotes)
            )
        |> List.groupBy (fun op -> (op.Currency1, op.Currency2))
        |> List.map (fun ((currency1, currency2), ops) -> 
            let num = Seq.sumBy (fun op -> op.NumberOfOpportunitiesIdentified) ops 
            let text = sprintf "%s%s %d" currency1 currency2 num
            // writer.WriteLine text
            { Currency1 = currency1; Currency2 = currency2; NumberOfOpportunitiesIdentified = num }
        )
    // writer.Flush()
    // writer.Close()
    opportunities

// type HistoricalSpreadCalcBenchMark() = 
//     member private this.GetFilePath(relativePath: string) =
//         let projectRootPath = @"/Users/audreyzhou/arbitrageGainer" 
//         Path.Combine(projectRootPath, relativePath)

//     member private this.ReadHistoricalData() =
//         let filePath = this.GetFilePath("historicalData.txt")
//         File.ReadAllText(filePath)
//     member this.BenchmarkCalculateHistoricalSpreadSeq() = 
//         let json = this.ReadHistoricalData()
//         calculateHistoricalSpreadSeq json

// [<EntryPoint>]
let main argv = 
    calculateHistoricalSpreadWorkflow UserInvocation |> ignore
    0
