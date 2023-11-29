module HistoricalSpreadCalculation
open FSharp.Data.JsonProvider

open ArbitrageOpportunity
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Utils.Collections
open Suave.RequestErrors
open System
open Azure
open Azure.Data.Tables

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

type CurrencyPair = {
    Currency1: string
    Currency2: string
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

type BucketedQuotes = {
    Quotes: list<Quote>
}

type HistoricalValues = JsonProvider<"./historicalData.txt">

type ArbitrageOpEntry (pair: string, numOpportunities: string) =
    interface ITableEntity with
        member val ETag = ETag "" with get, set
        member val PartitionKey = "" with get, set
        member val RowKey = "" with get, set
        member val Timestamp = Nullable() with get, set
    new() = ArbitrageOpEntry(null, null)
    member val CurrencyPair = pair with get, set
    member val NumberOpportunities = numOpportunities with get, set
// -------------------------
// Helper Function Definitions
// -------------------------

let currencyPairFromStr (pairStr: string) =
    {
        Currency1 = pairStr.[0..2]
        Currency2 = pairStr.[4..6]
    }

/// Creates quote from historical value JSON
let quoteFromHistVal (histVal: HistoricalValues.Root) : Quote =
    {
        Exchange = histVal.ev
        CurrencyPair = (currencyPairFromStr histVal.pair)
        BidPrice = histVal.bp
        AskPrice = histVal.ap
        BidSize = histVal.bs
        AskSize = histVal.as // TODO: since "as" is protected, how do we access this element?
        Time = histVal.t
    }

/// Loads historical values from a file
let loadHistoricalValuesFile () : list<Quote> = 
    // file reading logic
    let loadedValues = HistoricalValues.GetSample()
                        |> Array.map quoteFromHistVal
    // Format: (CurrencyPair, Exchange, BidPrice, AskPrice)
    Array.toList loadedValues

let bucketizeQuote (quote: Quote) : int =
    // Convert timestamp to bucket number, assuming quote.Time is in milliseconds
    quote.Time / 5

/// Regroups quotes into buckets of 5 milliseconds
let regroupQuotesIntoBuckets (quotes: list<Quote>) : list<BucketedQuotes> =
    quotes
    |> List.groupBy bucketizeQuote
    |> List.map (fun (bucketKey, bucketQuotes) -> { Quotes = bucketQuotes })

/// Selects the quote with the highest bid price for each exchange
let selectHighestBidPerExchange (quotes: list<Quote>) : list<Quote> =
    quotes
    |> List.groupBy (fun quote -> quote.Exchange)
    |> List.collect (fun (_, quotesByExchange) -> 
        quotesByExchange 
        |> List.maxBy (fun quote -> quote.BidPrice)
        |> List.singleton)

/// Identifies arbitrage opportunities within a list of quotes
let identifyArbitrageOpportunities (quotes: list<Quote>) : list<ArbitrageOpportunity> =
    let combinations = List.allPairs quotes quotes
    combinations
    |> List.choose (fun (quote1, quote2) ->
        match (quote1.CurrencyPair = quote2.CurrencyPair, quote1.Exchange <> quote2.Exchange) with
        | (true, true) -> 
            let priceDifference = abs (quote1.BidPrice - quote2.AskPrice)
            match priceDifference > 0.01m with
            | true -> Some { Currency1 = fst quote1.CurrencyPair; Currency2 = snd quote1.CurrencyPair; NumberOfOpportunitiesIdentified = 1 }
            | false -> None
        | _ -> None)

// --------------------------
// DB Configuration Constants
// --------------------------
let storageConnString = "AzureStorageConnectionString" // This field will later use the connection string from the Azure console.
let tableClient = TableServiceClient storageConnString
let table = tableClient.GetTableClient "HistoricalArbitrageOpportunities"

// -------------------------
// Workflow Implementation
// -------------------------

let calculateHistoricalSpreadWorkflow (request: HistoricalSpreadCalculationRequested) : ArbitrageOpportunitiesIdentified option =
    let historicalValues = loadHistoricalValuesFile ()
    let buckets = regroupQuotesIntoBuckets historicalValues
    let opportunities = 
        buckets 
        |> List.collect (fun bucket -> 
            let selectedQuotes = selectHighestBidPerExchange bucket.Quotes
            identifyArbitrageOpportunities selectedQuotes)
        |> List.groupBy (fun op -> (op.Currency1, op.Currency2))
        |> List.map (fun ((currency1, currency2), ops) -> 
            { Currency1 = currency1; Currency2 = currency2; NumberOfOpportunitiesIdentified = List.sumBy (fun op -> op.NumberOfOpportunitiesIdentified) ops })

    match opportunities with
    | [] -> None  // No opportunities identified
    | _ -> Some opportunities  // Successfully identified opportunities

let persistOpportunitiesInDB ( ops: ArbitrageOpportunitiesIdentified ) =
    table.CreateIfNotExists () |> ignore

    ops |> List.map (fun op -> (op.Currency1 + "-" + op.Currency2, op.NumberOfOpportunitiesIdentified))
        |> List.map (fun entry -> TableTransactionAction (TableTransactionActionType.Add, entry))
        |> table.SubmitTransaction
        |> ignore

// ---------------------------
// REST API Endpoint Handlers
// ---------------------------
let historicalSpread =
    request (fun r ->
    let ops = calculateHistoricalSpreadWorkflow UserInvocation
    match ops with
    | None -> ()
    | _ -> persistOpportunitiesInDB ops
    OK "Performed historical spread and uploaded arbitrage opportunities to database."
    )

let app =
    POST >=> choose
        [ path "/historicalspread" >=> historicalSpread]