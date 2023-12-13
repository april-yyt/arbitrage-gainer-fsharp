module HistoricalSpreadCalc

// open ArbitrageOpportunity
open CrossTradedCryptos
open Types
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Utils.Collections
open Suave.RequestErrors
open System
open Azure
open Azure.Data.Tables
open Newtonsoft.Json
open System.IO
open System.Reflection
open ServiceBus
open Types

// ---------------------------
// Types and Event Definitions
// ---------------------------

// Event and Type Definitions for Historical Spread Calculation
type HistoricalSpreadRequestCause = UserInvocation | CrossTradedCryptosUpdated
type HistoricalSpreadCalculationRequested = HistoricalSpreadRequestCause

type ArbitrageOpportunity = {
    Currency1: string
    Currency2: string
    NumberOfOpportunitiesIdentified: int
}

type ArbitrageOpportunitiesIdentified = ArbitrageOpportunity list

type BucketedQuotes = {
    Quotes: list<Quote>
}

type ArbitrageOpEntry (pair: string, numOpportunities: int) =
    interface ITableEntity with
        member val ETag = ETag "" with get, set
        member val PartitionKey = "" with get, set
        member val RowKey = "" with get, set
        member val Timestamp = Nullable() with get, set
    new() = ArbitrageOpEntry(null, 0)
    member val CurrencyPair = pair with get, set
    member val NumberOpportunities = numOpportunities with get, set

// --------------------------
// DB Configuration Constants
// --------------------------
let storageConnString = "DefaultEndpointsProtocol=https;AccountName=18656team6;AccountKey=qJTSPfoWo5/Qjn9qFcogdO5FWeIYs9+r+JAp+6maOe/8duiWSQQL46120SrZTMusJFi1WtKenx+e+AStHjqkTA==;EndpointSuffix=core.windows.net" 
let tableClient = TableServiceClient storageConnString

// -------------------------
// Helper Function Definitions
// -------------------------

let currencyPairFromStr (pairStr: string) : CurrencyPair =
    {
        Currency1 = pairStr.[0..2]
        Currency2 = pairStr.[4..6]
    }

/// Helper that gets cross traded currencies from DB
let getCrossTradedCurrencies = 
    let table = tableClient.GetTableClient("CrosstradedCurrencies")
    let queryResults = table.Query<CryptoDBEntry>()
    queryResults 
    |> Seq.map (fun entity ->
        currencyPairFromStr entity.CurrencyPair
    )

let getExchangeFromUnprocessedQuote (data: UnprocessedQuote) = 
    match data.Exchange with
    | 2 -> "Bitfinex"
    | 6 -> "Bitstamp"
    | 23 -> "Kraken"
    | 1 -> "Coinbase"

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
let regroupQuotesIntoBuckets (quotes: Quote seq) = 
    quotes 
    |> Seq.groupBy (fun quote -> (quote.CurrencyPair, toBucketKey quote.Time))


/// Selects the quote with the highest bid price for each exchange
let selectHighestBidPerExchange (quotes: Quote seq) : Quote list =
    quotes
    |> Seq.groupBy (fun quote -> quote.Exchange)
    |> Seq.collect (fun (_, quotesByExchange) ->
        quotesByExchange
        |> Seq.maxBy (fun quote -> quote.BidPrice)
        |> Seq.singleton)
    |> Seq.toList

/// Identifies arbitrage opportunities within a list of quotes
let identifyArbitrageOpportunities (selectedQuotes: Quote list) (quotes: Quote list) : list<ArbitrageOpportunity> =
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

let writeResultToFile (opportunities: ArbitrageOpportunity list) = 
    let writer = new StreamWriter("historicalAribtrageOpportunitites.txt", true)
    opportunities 
    |> List.iter (fun op -> 
        let text = sprintf "%s%s %d" op.Currency1 op.Currency2 op.NumberOfOpportunitiesIdentified
        writer.WriteLine text
    )    
    writer.Flush()
    writer.Close()

// -------------------------
// Workflow Implementation
// -------------------------
let calculateHistoricalSpreadWorkflow (request: HistoricalSpreadCalculationRequested) : ArbitrageOpportunitiesIdentified =
    let json = System.IO.File.ReadAllText("historicalData.txt")
    let unprocessedQuotes = JsonConvert.DeserializeObject<UnprocessedQuote seq>(json)
    let buckets = 
        unprocessedQuotes 
        |> processQuotes
        |> regroupQuotesIntoBuckets 
    let opportunities =
        buckets
        |> Seq.toList
        |> List.collect (fun (_, quotes) ->
            let selectedQuotes = selectHighestBidPerExchange quotes
            identifyArbitrageOpportunities selectedQuotes (Seq.toList quotes)
            )
        |> List.groupBy (fun op -> (op.Currency1, op.Currency2))
        |> List.map (fun ((currency1, currency2), ops) -> 
            let num = Seq.sumBy (fun op -> op.NumberOfOpportunitiesIdentified) ops 
            { Currency1 = currency1; Currency2 = currency2; NumberOfOpportunitiesIdentified = num }
        )
    writeResultToFile opportunities
    opportunities


let persistOpportunitiesInDB ( ops: ArbitrageOpportunitiesIdentified ) =
    let historicalTable = tableClient.GetTableClient "HistoricalArbitrageOpportunities"
    historicalTable.CreateIfNotExists () |> ignore

    ops |> List.map (fun op ->ArbitrageOpEntry(op.Currency1 + "-" + op.Currency2, op.NumberOfOpportunitiesIdentified))
        |> List.map (fun entry -> TableTransactionAction (TableTransactionActionType.Add, entry))
        |> historicalTable.SubmitTransaction

// ---------------------------
// REST API Endpoint Handlers
// ---------------------------
let historicalSpread =
    request (fun r ->
        match System.IO.File.Exists "historicalData.txt" with
        | false -> BAD_REQUEST "Historical data file historicalData.txt not found."
        | _ ->
            let ops = calculateHistoricalSpreadWorkflow UserInvocation
            let dbResp = persistOpportunitiesInDB ops
            match dbRespContainsError dbResp with
            | true -> BAD_REQUEST "Error in uploading to database"
            | _ -> OK "Performed historical spread and uploaded arbitrage opportunities to database."
    )
