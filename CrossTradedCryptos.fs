module CrossTradedCryptos

type CryptosRequestCause = 
    | UserInvocation
    | TradingStarted

type CurrencyPair = {
    Currency1: string
    Currency2: string
}

let newCurrencyPair c1 c2 = 
    {
        Currency1 = c1
        Currency2 = c2
    }

let currencyPairsEqual pairs =
    (((fst pairs).Currency1 = (snd pairs).Currency1) && ((fst pairs).Currency2 = (snd pairs).Currency2)) ||
    (((fst pairs).Currency1 = (snd pairs).Currency2) && ((fst pairs).Currency2 = (snd pairs).Currency1))

type CryptoListEntry = {
    Currency: string
    TradedAtBitfinex: bool
    TradedAtBitstamp: bool
    TradedAtKraken: bool
}

type CrossTradedCryptosRequested = { 
    CryptosRequestCause: CryptosRequestCause 
    CryptoList: list<CryptoListEntry>
}

type CrossTradedCryptosUpdated = { UpdatedCrossTradedCryptos: list<CurrencyPair>}

type Event =
    | CrossTradedCryptosRequested of CrossTradedCryptosRequested
    | CrossTradedCryptosUpdated of CrossTradedCryptosUpdated
    | CrossTradedCryptosUploaded




type CryptoListMessage =
    | UpdateCrossTradedCryptos of CurrencyPair
    | RetrieveCrossTradedCryptos of AsyncReplyChannel<list<CurrencyPair>>

let cryptoListAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop crossTradedCryptos =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateCrossTradedCryptos newPair -> 
                    let updatedCryptoPairs = newPair :: crossTradedCryptos
                    return! loop updatedCryptoPairs 
                | RetrieveCrossTradedCryptos replyChannel ->
                    replyChannel.Reply(crossTradedCryptos)
                    return! loop crossTradedCryptos
            }
        loop [])

let currencyTradedAtAllExchanges (input: CryptoListEntry) : bool =
    input.TradedAtBitfinex && input.TradedAtBitstamp && input.TradedAtKraken

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

// This is a placeholder function. In the next milestone, the currency pair string
// will be uploaded to the third party cross-traded cryptocurrencies database, rather
// than just printed as they are here.
// Assumption about future functionality: duplicate database entries will not be added.
let uploadCryptoPairsToDB (input: CrossTradedCryptosUpdated) =
    let rec printer (lst: List<CurrencyPair>) =
        match lst.Length with
        | 0 -> ()
        | _ -> 
            printfn "%s-%s" lst.Head.Currency1 lst.Head.Currency2
            printer lst.Tail
    printer input.UpdatedCrossTradedCryptos
    CrossTradedCryptosUploaded
    