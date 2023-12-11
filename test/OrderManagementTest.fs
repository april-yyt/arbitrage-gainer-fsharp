module OrderManagementTest

open System
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running
open Newtonsoft.Json
open System.Net.Http
open System.Text
open BitfinexAPI
open KrakenAPI
open BitstampAPI
open TypesAndPatternMatching



let exchanges: string list = ["Bitfinex"; "Kraken"; "Bitstamp"]
let currencies: string list = ["BTC"; "ETH"; "LTC"; "XRP"; "DOT"]
let orderTypes: OrderType list = [Buy; Sell]


let random = System.Random()

let randomElement list = 
    let index = random.Next(List.length list)
    List.item index list

let createRandomOrderDetails() =
    { Exchange = randomElement exchanges
      Currency = randomElement currencies
      Quantity = random.Next(1, 10) 
      Price = random.NextDouble() * 10000.0
      OrderType = randomElement orderTypes }

let createRandomOrdersList n =
    List.init n (fun _ -> createRandomOrderDetails())



let initiateBuySellOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderID, string>> = 
    async {
        try 
            match orderDetails.Exchange with
            | "Bitfinex" -> 
                let orderType = "MARKET"
                let symbol = "t" + orderDetails.Currency
                let! responseOption = BitfinexAPI.submitOrder orderType symbol (orderDetails.Quantity.ToString()) (orderDetails.Price.ToString())
                match responseOption with
                | Some (response: BitfinexResponse) -> 
                    match response with
                    | head :: _ -> return Result.Ok (IntOrderID head.id)
                    
                    | [] -> return Result.Error "Empty Bitfinex response"
                | None -> return Result.Error "Failed to submit order to Bitfinex"
            | "Kraken" -> 
                let orderType = "market"
                let pair = "XX" + orderDetails.Currency
                let! responseOption = KrakenAPI.submitOrder pair orderType (orderDetails.Quantity.ToString()) (orderDetails.Price.ToString())
                match responseOption with
                | Some (response: KrakenResponse) -> 
                    match response.result.txid with
                    | txidHead :: _ -> return Result.Ok (StringOrderID txidHead)
                    | [] -> return Result.Error "No transaction ID in Kraken response"
                | None -> return Result.Error "Failed to submit order to Kraken"
            | "Bitstamp" -> 
                let action = match orderDetails.OrderType with
                                | Buy -> BitstampAPI.buyMarketOrder
                                | Sell -> BitstampAPI.sellMarketOrder
                let! responseOption = action orderDetails.Currency (orderDetails.Quantity.ToString()) None
                printfn "Bitstamp response: %A" responseOption
                match responseOption with
                | Some id -> return Result.Ok (StringOrderID id)
                | None -> return Result.Error "Failed to submit order to Bitstamp"
            | _ -> 
                return Result.Error "Unsupported exchange"
        with
        | ex -> 
            return Result.Error (sprintf "An exception occurred: %s" ex.Message)
    }



let createOrderAsync (orderDetails: OrderDetails) : Async<Result<OrderID, string>> =
    async {
        let! result = initiateBuySellOrderAsync orderDetails
        match result with
        | Result.Ok orderID ->
            // Database operations removed
            return Result.Ok orderID
        | Result.Error errMsg ->
            return Result.Error errMsg
    }

let createOrdersParallel (ordersEmitted: OrderDetails list) : Result<OrderID list, string> =
    ordersEmitted
    |> List.map createOrderAsync
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.fold (fun acc result ->
        match acc, result with
        | Result.Ok idList, Result.Ok orderID -> 
            Result.Ok (orderID :: idList)
        | Result.Error errMsg, _ | _, Result.Error errMsg ->
            Result.Error errMsg
        | Result.Ok _, Result.Ok _ -> acc
    ) (Result.Ok [])
    |> function
        | Result.Ok idList -> Result.Ok (List.rev idList)
        | Result.Error errMsg -> Result.Error errMsg

let createOrdersSequential (ordersEmitted: OrderDetails list) : Result<OrderID list, string> =
    ordersEmitted
    |> List.fold (fun acc orderDetail ->
        match acc with
        | Result.Error errMsg -> Result.Error errMsg
        | Result.Ok idList ->
            match createOrderAsync orderDetail |> Async.RunSynchronously with
            | Result.Ok orderID -> Result.Ok (orderID :: idList)
            | Result.Error errMsg -> Result.Error errMsg
    ) (Result.Ok [])
    |> function
        | Result.Ok idList -> Result.Ok (List.rev idList)
        | Result.Error errMsg -> Result.Error errMsg



let testBitfinexAPI() =
    BitfinexAPI.submitOrder "MARKET" "tDOTUSD" "150.0" "5.2529" |> Async.RunSynchronously

let testKrakenAPI() =
    KrakenAPI.submitOrder "XXFETZUSD" "sell" "22.45" "58.14" |> Async.RunSynchronously

let testBitstampAPI() =
    BitstampAPI.buyMarketOrder "fetusd" "22.45" None |> Async.RunSynchronously


type OrderManagementBenchmark() =

    [<Benchmark>]
    member this.BenchmarkCreateOrder() =
        let orderDetails = // Create a sample OrderDetails object
            { Exchange = "Bitfinex"
              Currency = "BTC"
              Quantity = 1 // Assuming Quantity is an int
              Price = 50000.0
              OrderType = Buy }
        createOrderAsync orderDetails |> Async.RunSynchronously

    // [<Benchmark>]
    // member this.BenchmarkCreateOrdersParallel() =
    //     let ordersEmitted =
    //         [ { Exchange = "Bitfinex"; Currency = "BTC"; Quantity = 1; Price = 50000.0; OrderType = Buy }
    //           { Exchange = "Kraken"; Currency = "ETH"; Quantity = 2; Price = 3000.0; OrderType = Sell } ]
    //     createOrdersParallel ordersEmitted |> ignore

    // [<Benchmark>]
    // member this.BenchmarkCreateOrdersSequential() =
    //     let ordersEmitted = 
    //         [ { Exchange = "Bitfinex"; Currency = "BTC"; Quantity = 1; Price = 50000.0; OrderType = Buy }
    //           { Exchange = "Kraken"; Currency = "ETH"; Quantity = 2; Price = 3000.0; OrderType = Sell } ]
    //     createOrdersSequential ordersEmitted |> ignore

    [<Benchmark>]
    member this.BenchmarkCreateOrdersParallel() =
        let ordersEmitted = createRandomOrdersList 20
        createOrdersParallel ordersEmitted |> ignore

    [<Benchmark>]
    member this.BenchmarkCreateOrdersSequential() =
        let ordersEmitted = createRandomOrdersList 20
        createOrdersSequential ordersEmitted |> ignore


    // [<Benchmark>]
    // member this.BenchmarkBitfinexAPISubmitOrder() =
    //     testBitfinexAPI()

    // [<Benchmark>]
    // member this.BenchmarkKrakenAPISubmitOrder() =
    //     testKrakenAPI()

    // [<Benchmark>]
    // member this.BenchmarkBitstampAPIBuyMarketOrder() =
    //     testBitstampAPI()

[<EntryPoint>]
let main argv =
    BenchmarkRunner.Run<OrderManagementBenchmark>() |> ignore
    0