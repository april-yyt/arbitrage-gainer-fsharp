module HistoricalSpreadCalculation

open ArbitrageOpportunity

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

type BucketedQuotes = {
    Quotes: list<Quote>
}

// -------------------------
// Helper Function Definitions
// -------------------------

/// Loads historical values from a file
let loadHistoricalValuesFile () : list<Quote> = 
    // file reading logic
    // Format: (CurrencyPair, Exchange, BidPrice, AskPrice)
    []

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
        if quote1.CurrencyPair = quote2.CurrencyPair && quote1.Exchange <> quote2.Exchange then
            let priceDifference = abs (quote1.BidPrice - quote2.AskPrice)
            if priceDifference > 0.01m then
                Some { Currency1 = fst quote1.CurrencyPair; Currency2 = snd quote1.CurrencyPair; NumberOfOpportunitiesIdentified = 1 }
            else None
        else None)

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
