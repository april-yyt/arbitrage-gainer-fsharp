module CrossTradedCryptos

// open HistoricalSpreadCalculation

// ---------------------------
// Types and Event Definitions
// ---------------------------

// TODO: duplicate code; remove after integration with historical spread domain service
type CurrencyPair = {
    Currency1: string
    Currency2: string
}

// Why the cross-traded cryptos were requested
type CryptosRequestCause = 
    | UserInvocation
    | TradingStarted

// A representation of each entry in the crypto list that is loaded.
// The actual loading will occur later in the project with third-party
// connections.
type CryptoListEntry = {
    Currency: string
    TradedAtBitfinex: bool
    TradedAtBitstamp: bool
    TradedAtKraken: bool
}

// Events for the workflows in this domain service
type CrossTradedCryptosRequested = { 
    CryptosRequestCause: CryptosRequestCause 
    CryptoList: CryptoListEntry list
}

type CrossTradedCryptosUpdated = { UpdatedCrossTradedCryptos: CurrencyPair list}

type Event =
    | CrossTradedCryptosRequested of CrossTradedCryptosRequested
    | CrossTradedCryptosUpdated of CrossTradedCryptosUpdated
    | CrossTradedCryptosUploaded

// Message type used for the cross-traded cryptos agent
type CrossTradedCryptosMessage =
    | UpdateCrossTradedCryptos of CurrencyPair
    | RetrieveCrossTradedCryptos of AsyncReplyChannel<CurrencyPair list>

// -------
// Helpers
// -------

// Creates a new CurrencyPair object given 2 currencies as strings
let newCurrencyPair c1 c2 = 
    {
        Currency1 = c1
        Currency2 = c2
    }

// Determines if 2 CurrencyPairs are equal. An implementation was needed
// instead of using the built-in equality operator because the order of currencies
// shouldn't matter in determining equality.
let currencyPairsEqual pairs =
    (((fst pairs).Currency1 = (snd pairs).Currency1) && ((fst pairs).Currency2 = (snd pairs).Currency2)) ||
    (((fst pairs).Currency1 = (snd pairs).Currency2) && ((fst pairs).Currency2 = (snd pairs).Currency1))

// Helper to determine if a currency is traded at all exchanges.
let currencyTradedAtAllExchanges (input: CryptoListEntry) : bool =
    input.TradedAtBitfinex && input.TradedAtBitstamp && input.TradedAtKraken

// ----------
// Workflows
// ----------

// Retrieve crypto list and update cross-traded cryptocurrencies.
//
// This is done locally using the cross-traded cryptos agent.
let updateCrossTradedCryptos (input: CrossTradedCryptosRequested) =
    let cryptoList = input.CryptoList
    let currenciesTradedAtAll = 
        cryptoList
        |> List.filter currencyTradedAtAllExchanges
        |> List.map (fun x -> x.Currency)
    // Create pairs from the list of currencies traded at all exchanges
    let pairs = List.allPairs currenciesTradedAtAll currenciesTradedAtAll
    let currencyPairs = 
        pairs
        |> List.filter (fun (x, y) -> (x <> y)) // filter out the duplicate pairs
        |> List.map (fun (x, y) -> newCurrencyPair x y)  // turn into currency pairs
    { UpdatedCrossTradedCryptos = currencyPairs }

// Upload the cross-traded cryptocurrency pairs to the database.
//
// This is a placeholder function. In the next milestone, the currency pair string
// will be uploaded to the third party cross-traded cryptocurrencies database, rather
// than just printed as they are here.
// Assumption about future functionality: duplicate database entries will not be added.
let uploadCryptoPairsToDB (input: CrossTradedCryptosUpdated) =
    let rec printer (lst: CurrencyPair list) =
        match lst.Length with
        | 0 -> ()
        | _ -> 
            printfn "%s-%s" lst.Head.Currency1 lst.Head.Currency2
            printer lst.Tail
    printer input.UpdatedCrossTradedCryptos
    CrossTradedCryptosUploaded

type PairAtExchange = {
    Exchange: string
    CurrencyPair: CurrencyPair
}

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
                            |> Seq.map(fun t -> fst t) // Keep just the currency pairs from the countBy tuples
    pairsAtAll

