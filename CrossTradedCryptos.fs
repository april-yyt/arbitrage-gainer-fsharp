module CrossTradedCryptos

open System
open System.Collections.Generic
open System.IO
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
    InputExchanges: string seq // Assumption: the input files will be of the format <exchangename>.txt
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

let railwayMap (inputFunction: 'a -> 'b) twoTrackInput =
    match twoTrackInput with
    | Ok success -> Ok (inputFunction success)
    | Error failure -> Error failure

let validateFileExistence filename: Result<string, string> =
    match System.IO.File.Exists filename with
    | true -> Ok filename
    | false -> Error ("File to load (" + filename + ") doesn't exist")

// Currently, since we make the text 
let validateFileType (filename: string): Result<string, string> =
    match filename.EndsWith(".txt") with
    | true -> Ok filename
    | false -> Error "File is the wrong format; should be a txt file"

let validateInputFile =
    validateFileExistence >> bind validateFileType

let validateInputFiles (filenames: string seq) =
    let containsError = 
            filenames |> Seq.map(validateInputFile)
              |> Seq.exists(fun r -> match r with // Check if the validation resulted in any error
                                        | Error failure -> true
                                        | _ -> false)
    match containsError with
        | true -> Error "Input files validation failed."
        | false -> Ok { InputExchanges = filenames }
    
let exchangeToFilename (exchange: string) =
    exchange + ".txt"

let validateInputExchanges (exchanges: string seq) = 
    exchanges |> Seq.map exchangeToFilename
                |> validateInputFiles

let validCurrencyPairsFromFile (exchange: string) =
    let pairs = System.IO.File.ReadLines(exchangeToFilename exchange)
    let filteredPairs = pairs // Read pairs from input file line by line
                        |> Seq.filter (fun s -> s.Length = 6) // Ignore pairs that are > 6 letters (pair of 3-letter currencies)
                        |> Seq.map(fun s -> {Currency1 = s.[0..2]; Currency2 = s.[3..5]}) // Convert to CurrencyPairs
                        |> Seq.map(fun p -> {Exchange = exchange; CurrencyPair = p})
    filteredPairs

// Input: "Bitfinex", "Bitstamp", "Kraken"
let pairsTradedAtAllExchanges (exchanges: string seq) =
    let exchangePairs = exchanges
                        |> Seq.map(validCurrencyPairsFromFile)
                        |> Seq.concat
    let pairsAtAll = exchangePairs
                            |> Seq.countBy (fun x -> x.CurrencyPair) // Count how many exchanges this pair appears in
                            |> Seq.filter (fun t -> (snd t) = (Seq.length exchanges)) // Filter for pairs that appear at all the exchanges
                            |> Seq.map (fun t -> fst t) // Keep just the currency pairs from the countBy tuples
    pairsAtAll

let createDBEntryFromPair (pair: CurrencyPair) =
    CryptoDBEntry(pair.Currency1 + "-" + pair.Currency2)

let dbRespContainsError (resp: Response<IReadOnlyList<Response>>) : bool =
    let respList = resp.Value // Extract the readonlylist of responses
    respList |> Seq.map(fun r -> r.IsError) // Check if each response is an error
            |> Seq.contains true // Check if any response is an error

let outputPairsToFile (path: string) (pairs: CurrencyPair seq) =
    let pairsStrs = pairs |> Seq.map(fun p -> sprintf "%s-%s" p.Currency1 p.Currency2)
                        |> Seq.toList
    File.WriteAllLines(path, pairsStrs)

// --------------------------
// DB Configuration Constants
// --------------------------
let storageConnString = "DefaultEndpointsProtocol=https;AccountName=18656team6;AccountKey=qJTSPfoWo5/Qjn9qFcogdO5FWeIYs9+r+JAp+6maOe/8duiWSQQL46120SrZTMusJFi1WtKenx+e+AStHjqkTA==;EndpointSuffix=core.windows.net" 
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
let crossTradedCurrencies =
    request (fun r ->
    let input = {InputExchanges = ["Bitfinex"; "Bitstamp"; "Kraken"]}
    let updatedCryptos = validateInputExchanges input.InputExchanges
                        |> railwayMap updateCrossTradedCryptos
    match updatedCryptos with 
    | Error failure -> BAD_REQUEST failure
    | Ok success ->
        // Update for M4: output the pairs to a file in addition to persisting in DB
        outputPairsToFile "crossTradedCurrencies.txt" success.UpdatedCrossTradedCryptos
        let dbResp = uploadCryptoPairsToDB success
        match dbRespContainsError dbResp with
        | true -> BAD_REQUEST "Error in uploading to database"
        | _ -> OK "Uploaded cross-traded currencies to database."
    )

let app =
    GET >=> choose
        [ path "/crosstradedcurrencies" >=> crossTradedCurrencies]
let cfg = { defaultConfig with bindings = [ HttpBinding.createSimple HTTP "0.0.0.0" 8080  ] }
let _, webServer = startWebServerAsync cfg app
Async.Start (webServer) |> ignore
