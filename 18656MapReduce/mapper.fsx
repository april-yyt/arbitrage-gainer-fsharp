#!/usr/bin/env fsharpi

#if DOCKER
#r "/app/Newtonsoft.Json.dll"
#else
#r "/Users/audreyzhou/Desktop/18656MapReduce/bin/Debug/net6.0/Newtonsoft.Json.dll"
#endif

open Newtonsoft.Json

type ArbitrageOpportunity = {
    Currency1: string
    Currency2: string
    NumberOfOpportunitiesIdentified: int
}
type CurrencyPair = {
    Currency1: string
    Currency2: string
}
type Currency = string
type Price = Price of float
type Quantity = Quantity of float
type Exchange = string
type Time = int64

type UnprocessedQuote = {
    [<JsonProperty("ev")>]
    EventType: string
    [<JsonProperty("pair")>]
    CurrencyPair: string
    [<JsonProperty("bp")>]
    BidPrice: float
    [<JsonProperty("bs")>]
    BidSize: float
    [<JsonProperty("ap")>]
    AskPrice: float
    [<JsonProperty("as")>]
    AskSize: float
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
    BidPrice: float;
    AskPrice: float;                          
    BidSize: float;
    AskSize: float;
    Time: Time;
}

type BucketKey = (CurrencyPair * int64)
type Key = {
    CurrencyPair: CurrencyPair
    TimeKey: int64
}
type BucketedQuotes = BucketKey * list<Quote>

let getExchangeFromUnprocessedQuote (data: UnprocessedQuote) = 
    match data.Exchange with
    | 2 -> "Bitfinex"
    | 6 -> "Bitstamp"
    | 23 -> "Kraken"
    | 1 -> "Coinbase"

let currencyPairFromStr (pairStr: string) : CurrencyPair = {
    Currency1 = pairStr.[0..2]
    Currency2 = pairStr.[4..6]
}

let processQuotes (unprocessedQuotes: UnprocessedQuote seq) = 
    unprocessedQuotes 
    |> Seq.map (fun quote -> {
            Exchange = getExchangeFromUnprocessedQuote quote
            CurrencyPair = currencyPairFromStr quote.CurrencyPair;
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
let regroupQuotesIntoBuckets (quotes: Quote seq) : BucketedQuotes list = 
    quotes 
    |> Seq.toList
    |> List.groupBy (fun quote -> (quote.CurrencyPair, toBucketKey quote.Time))

let json = System.IO.File.ReadAllText("historicalData.txt")
let modifiedJson = "[" + json + "]"
let unprocessedQuotes = JsonConvert.DeserializeObject<UnprocessedQuote seq>(modifiedJson) 
unprocessedQuotes 
|> processQuotes
|> regroupQuotesIntoBuckets 
|> List.iter (fun ((currency, time), quotes) -> 
    let key = {
        CurrencyPair = currency;
        TimeKey = time;
    }
    // let serializedKey = JsonConvert.SerializeObject(key)
    let serializedQuotes = JsonConvert.SerializeObject(quotes)
    let times = string time
    let reskey = key.CurrencyPair.Currency1 + "," + times
    printfn "%s\t%s" reskey (string ((List.length quotes)*2)))

// [<EntryPoint>] 
// let main argv = 
//     let json = System.IO.File.ReadAllText("historicalData.txt")
//     let modifiedJson = "[" + json + "]"
//     let unprocessedQuotes = JsonConvert.DeserializeObject<UnprocessedQuote seq>(modifiedJson) 
//     unprocessedQuotes 
//     |> processQuotes
//     |> regroupQuotesIntoBuckets 
//     |> Seq.toList
//     |> List.iter (fun ((currency, time), quotes) -> 
//         let key = {
//             CurrencyPair = currency;
//             TimeKey = time;
//         }
//         let serializedKey = JsonConvert.SerializeObject(key)
//         let serializedQuotes = JsonConvert.SerializeObject(quotes)
//         printfn "%s\t%s" serializedKey serializedQuotes
//     )
    
//     0