module CrossTradedCryptos

open System
open Azure
open Azure.Data.Tables
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Utils.Collections
open Suave.RequestErrors

// ---------------------------
// Types and Event Definitions
// ---------------------------

// TODO (M4): duplicate code with HistoricalSpreadCalculation; remove after integration with historical spread domain service
type CurrencyPair = {
    Currency1: string
    Currency2: string
}

// Representation of a currency pair traded at a given exchange
type PairAtExchange = {
    Exchange: string
    CurrencyPair: CurrencyPair
}

// Why the cross-traded cryptos were requested
type CryptosRequestCause = 
    | UserInvocation
    | TradingStarted

// Events for the workflows in this domain service
type CrossTradedCryptosRequested = { 
    InputExchanges: List<string> // Assumption: the input files will be of the format <exchangename>.txt
}

type CrossTradedCryptosUpdated = { UpdatedCrossTradedCryptos: CurrencyPair seq}

type Event =
    | CrossTradedCryptosRequested of CrossTradedCryptosRequested
    | CrossTradedCryptosUpdated of CrossTradedCryptosUpdated
    | CrossTradedCryptosUploaded

type CryptoDBEntry (pair: string) =
    interface ITableEntity with
        member val ETag = ETag "" with get, set
        member val PartitionKey = "" with get, set
        member val RowKey = "" with get, set
        member val Timestamp = Nullable() with get, set
    new() = CryptoDBEntry(null)
    member val CurrencyPair = pair with get, set
// -------
// Helpers
// -------

let validCurrencyPairsFromFile (exchange: string) =
    let pairs = System.IO.File.ReadLines(exchange + ".txt")
    let filteredPairs = pairs // Read pairs from input file line by line
                        |> Seq.filter (fun s -> s.Length = 6) // Ignore pairs that are > 6 letters (pair of 3-letter currencies)
                        |> Seq.map(fun s -> {Currency1 = s.[0..2]; Currency2 = s.[3..5]}) // Convert to CurrencyPairs
                        |> Seq.map(fun p -> {Exchange = exchange; CurrencyPair = p})
    filteredPairs

// Input: "Bitfinex", "Bitstamp", "Kraken"
let pairsTradedAtAllExchanges (exchanges: string list) =
    let exchangePairs = exchanges 
                        |> List.map(validCurrencyPairsFromFile)
                        |> Seq.concat
    let pairsAtAll = exchangePairs
                            |> Seq.countBy (fun x -> x.CurrencyPair) // Count how many exchanges this pair appears in
                            |> Seq.filter (fun t -> (snd t) = exchanges.Length) // Filter for pairs that appear at all the exchanges
                            |> Seq.map (fun t -> fst t) // Keep just the currency pairs from the countBy tuples
    pairsAtAll

let createDBEntryFromPair (pair: CurrencyPair) =
    CryptoDBEntry(pair.Currency1 + "-" + pair.Currency2)

// --------------------------
// DB Configuration Constants
// --------------------------
let storageConnString = "AzureStorageConnectionString" // This field will later use the connection string from the Azure console.
let tableClient = TableServiceClient storageConnString
let table = tableClient.GetTableClient "CrosstradedCurrencies"

// ----------
// Workflows
// ----------

// Retrieve crypto list and update cross-traded currencies.
//
// This is done by loading locally available files.
let updateCrossTradedCryptos (input: CrossTradedCryptosRequested) =
    let exchanges = input.InputExchanges
    { UpdatedCrossTradedCryptos = pairsTradedAtAllExchanges exchanges }

// Upload the cross-traded cryptocurrency pairs to the database.
let uploadCryptoPairsToDB (input: CrossTradedCryptosUpdated) =
    // ref: https://learn.microsoft.com/en-us/dotnet/fsharp/using-fsharp-on-azure/table-storage
    table.CreateIfNotExists () |> ignore 

    input.UpdatedCrossTradedCryptos
    |> Seq.map createDBEntryFromPair
    |> Seq.map (fun entry -> TableTransactionAction (TableTransactionActionType.Add, entry))
    |> table.SubmitTransaction
    |> ignore // Since data is being updated in this step, ignore the return value as per CQS
// ---------------------------
// REST API Endpoint Handlers
// ---------------------------
let crossTradedCryptos =
    request (fun r ->
    let updatedCryptos = updateCrossTradedCryptos({InputExchanges = ["Bitfinex"; "Bitstamp"; "Kraken"]})
    uploadCryptoPairsToDB updatedCryptos
    OK "Uploaded cross-traded currencies to database."
    )

let app =
    POST >=> choose
        [ path "/crosstradedcryptos" >=> crossTradedCryptos]
