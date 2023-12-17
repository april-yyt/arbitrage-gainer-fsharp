#!/usr/bin/env fsharpi

#if DOCKER
#r "/app/Newtonsoft.Json.dll"
#else
#r "/Users/audreyzhou/Desktop/18656MapReduce/bin/Debug/net6.0/Newtonsoft.Json.dll"
#endif

open Newtonsoft.Json
open System
open System.IO

type ArbitrageOpportunity = {
    Currency1: string
    Currency2: string
    NumberOfOpportunitiesIdentified: int
}
type CurrencyPair = {
    Currency1: string
    Currency2: string
}
type Price = Price of float
type Quantity = Quantity of float
type Exchange = string
type Time = int64

type Key = {
    CurrencyPair: CurrencyPair
    TimeKey: int64
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

let parseLine (line: string) =
    let parts = line.Split('\t')
    let key = JsonConvert.DeserializeObject<Key>(parts.[0])
    let value = JsonConvert.DeserializeObject<Quote seq>(parts.[1])
    (key, value)

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

let rec readLines () =
    match Console.ReadLine() with
    | null -> [] // End of input, terminate recursion
    | line -> line :: readLines () // Add line to list and continue reading

let processInputLines () = 
    seq {yield! readLines ()}
    |> Seq.toList
    |> List.map(fun line -> parseLine line)
    // Console.In
    // |> StreamReader
    // |> Seq.unfold (fun (reader: TextReader) ->
    //     let res = reader.ReadLine()
    //     match reader.ReadLine() with
    //     | null -> None
    //     | line -> 
    //         printfn "reached"
    //         Some (line, reader)) 
    // |> Seq.choose parseLine
    // |> Seq.toList
    

[<EntryPoint>]
let main argv = 
    processInputLines ()
    |> List.collect (fun (_, quotes) ->
        let selectedQuotes = selectHighestBidPerExchange quotes
        identifyArbitrageOpportunities selectedQuotes (Seq.toList quotes)
        )
    |> List.groupBy (fun op -> (op.Currency1, op.Currency2))
    |> List.iter (fun ((currency1, currency2), ops) -> 
        let num = Seq.sumBy (fun op -> op.NumberOfOpportunitiesIdentified) ops 
        printfn "%s-%s: %d" currency1 currency2 num
    )

    0 // Return an integer exit code

