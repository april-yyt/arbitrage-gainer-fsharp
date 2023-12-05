open System
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running
open Newtonsoft.Json
open System.Net.Http
open System.Text
open BitfinexAPI
open KrakenAPI
open BitstampAPI

let testBitfinexAPI() =
    BitfinexAPI.submitOrder "MARKET" "tDOTUSD" "150.0" "5.2529" |> Async.RunSynchronously

let testKrakenAPI() =
    KrakenAPI.submitOrder "XXFETZUSD" "sell" "22.45" "58.14" |> Async.RunSynchronously

let testBitstampAPI() =
    BitstampAPI.buyMarketOrder "fetusd" "22.45" None |> Async.RunSynchronously

type APIBenchmark() =

    [<Benchmark>]
    member this.BitfinexAPISubmitOrder() =
        testBitfinexAPI()

    [<Benchmark>]
    member this.KrakenAPISubmitOrder() =
        testKrakenAPI()

    [<Benchmark>]
    member this.BitstampAPIBuyMarketOrder() =
        testBitstampAPI()

[<EntryPoint>]
let main argv =
    BenchmarkRunner.Run<APIBenchmark>() |> ignore
    0 // return an integer exit code 0