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
// Type def for error handling
type Result<'Success,'Failure> =
| Ok of 'Success
| Error of 'Failure

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
let bind inputFunction twoTrackInput =
    match twoTrackInput with
    | Ok success -> inputFunction success
    | Error failure -> Error failure

let mapBind (inputFunction: 'a -> 'b) twoTrackInput =
    match twoTrackInput with
    | Ok success -> Ok (inputFunction success)
    | Error failure -> Error failure

let validateFileExistence filename: string =
    match System.IO.File.Exists filename with
    | true -> Ok filename
    | false -> Error "File to load (" + filename + ") doesn't exist"

// Currently, since we make the text 
let validateFileType filename: string =
    match filename.EndsWith(".txt") with
    | true -> Ok filename
    | false -> Error "File is the wrong format; should be a txt file"

let validateInputFile =
    validateFileExistence >> bind validateFileType

let validCurrencyPairsFromFile (exchange: string) =
    let filename = exchange + ".txt"
    let res = validateInputFile filename
    match res with
    | Error failure -> Error failure
    | _ ->
        let pairs = System.IO.File.ReadLines(filename)
        let filteredPairs = pairs // Read pairs from input file line by line
                            |> Seq.filter (fun s -> s.Length = 6) // Ignore pairs that are > 6 letters (pair of 3-letter currencies)
                            |> Seq.map(fun s -> {Currency1 = s.[0..2]; Currency2 = s.[3..5]}) // Convert to CurrencyPairs
                            |> Seq.map(fun p -> {Exchange = exchange; CurrencyPair = p})
        Success filteredPairs

// Input: "Bitfinex", "Bitstamp", "Kraken"
let pairsTradedAtAllExchanges (exchanges: string list) =
    let exchangePairs = exchanges
                        |> List.map(validCurrencyPairsFromFile)
                        |> Seq.concat
    // Check if any of the validCurrencyPairsFromFile resulted in a failure; if so, return another failure
    match List.contains exchangePairs Error with 
    | true -> Error "Pair identification failed"
    | _ ->
        let pairsAtAll = exchangePairs
                                |> Seq.countBy (fun x -> x.CurrencyPair) // Count how many exchanges this pair appears in
                                |> Seq.filter (fun t -> (snd t) = exchanges.Length) // Filter for pairs that appear at all the exchanges
                                |> Seq.map (fun t -> fst t) // Keep just the currency pairs from the countBy tuples
        Success pairsAtAll

let createDBEntryFromPair (pair: CurrencyPair) =
    CryptoDBEntry(pair.Currency1 + "-" + pair.Currency2)

let dbRespContainsError (resp: Response<IReadOnlyList<Response>>) : bool =
    respList = resp.Value // Extract the readonlylist of responses
    respList |> Seq.map(fun r -> r.IsError) // Check if each response is an error
            |> Seq.contains true // Check if any response is an error


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

// ---------------------------
// REST API Endpoint Handlers
// ---------------------------
let crossTradedCryptos =
    request (fun r ->
    input = {InputExchanges = ["Bitfinex"; "Bitstamp"; "Kraken"]}
    let twoTrackUpdatedCryptos = mapBind updateCrossTradedCryptos
    let updatedCryptos = twoTrackUpdatedCryptos input
    match updatedCryptos with 
    | Error failure -> BAD_REQUEST failure
    | _ ->
        let dbResp = uploadCryptoPairsToDB updatedCryptos
        match dbRespContainsError dbResp with
        | true -> BAD_REQUEST "Error in uploading to database"
        | _ -> OK "Uploaded cross-traded currencies to database."
    )

let app =
    POST >=> choose
        [ path "/crosstradedcryptos" >=> crossTradedCryptos]
