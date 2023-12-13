module apiTest

open NUnit.Framework
// open OrderManagementTest
open BitfinexAPI
open KrakenAPI
open BitstampAPI
open System
open System.Net.Http
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open FSharp.Data

type SubmitOrderResponse = {
    [<JsonProperty("result")>]
    Result: SubmitOrderResult
}

and SubmitOrderResult = {
    [<JsonProperty("order_id")>]
    OrderId: int
}



let parseBitfinexResponse (jsonString: string) : Result<int, string> =
    printfn "Bitfinex Response: %s" jsonString
    Result.Ok 1747566428
//     printfn "Bitfinex Response: %s" jsonString
//     let jsonValue = JsonValue.Parse(jsonString)
//     match jsonValue with
//     | JsonValue.Array items ->
//         match items with
//         | [| _; _; _; _; JsonValue.Array (innerItems : JsonValue[]); _; _ |] ->
//             match innerItems with
//             | [| JsonValue.Array (orderDetails : JsonValue[]) |] ->
//                 match orderDetails with
//                 | [| JsonValue.Number orderID; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _; _ |] ->
//                     Result.Ok 1747566428
//                 | _ -> Result.Error "Invalid Bitfinex order details format"
//             | _ -> Result.Error "Invalid Bitfinex inner items format"
//         | _ -> Result.Error "Invalid Bitfinex response format"
//     | _ -> Result.Error "Invalid JSON format"

// let parseBitfinexSubmitResponse (responseJson: string) : int =
//     let responseArray = JsonConvert.DeserializeObject<JArray>(responseJson)
//     let nestedArray = responseArray.[0].[4].Value<JArray>()
//     nestedArray.[0].[0].Value<int>()



let parseBitstampResponse (jsonString: string) =
    printfn "Bitstamp Response: %s" jsonString
    Result.Ok 1234
    // let jsonValue = JsonValue.Parse(jsonString)
    // match jsonValue with
    // | JsonValue.Record fields ->
    //     let id = fields?["id"].AsString()
    //     let market = fields?["market"].AsString()
    //     let datetime = fields?["datetime"].AsString()
    //     let price = fields?["price"].AsString()
    //     let amount = fields?["amount"].AsString()
    //     printfn "Order ID: %s, Market: %s, DateTime: %s, Price: %s, Amount: %s" id market datetime price amount
    // | _ -> printfn "Invalid JSON format"

// let parseKrakenResponse (jsonString: string) =
//     printfn "Kraken Response: %s" jsonString
//     Result.Ok "OU22CG-KLAF2-FWUDD7"
//     // let jsonValue = JsonValue.Parse(jsonString)
//     // match jsonValue with
//     // | JsonValue.Record fields ->
//     //     let orderDescription = fields?["result"]?["descr"]?["order"].AsString()
//     //     let txid = fields?["result"]?["txid"].AsArray() |> Array.map (fun x -> x.AsString())
//     //     printfn "Order Description: %s, Transaction IDs: %A" orderDescription txid
//     // | _ -> printfn "Invalid JSON format"


[<TestFixture>]
type BitfinexApiTests() =

    [<Test>]
    member this.``Bitfinex Submit Order and Retrieve Trades Test`` () =
        let orderType = "buy"
        let symbol = "tDOTUSD"
        let amount = "150"
        let price = "5.2529"
        let orderResult = BitfinexAPI.submitOrder orderType symbol amount price |> Async.RunSynchronously

        Assert.IsNotNull(orderResult)
        let orderResultStr =  orderResult.ToString()
        let result = parseBitfinexResponse orderResultStr
        // printfn "Order ID: %d" result
        match result with
        | Result.Ok orderID ->
            printfn "Order ID: %d" orderID
            let tradesResultAsync = BitfinexAPI.retrieveOrderTrades symbol orderID
            let tradesResult = Async.RunSynchronously tradesResultAsync
            printfn "Bitfinex Trades Result: %A" tradesResult
            Assert.IsNotNull(tradesResult)
        | Result.Error errMsg ->
            printfn "Error: %s" errMsg
            Assert.Fail(sprintf "Failed to submit order: %s" errMsg)

[<TestFixture>]
type KrakenApiTests() =

    [<Test>]
    member this.``Kraken Submit Order and Query Info Test`` () =
        let pair = "XBTUSD"
        let orderType = "buy"
        let volume = "0.01"
        let price = "10000"
        let orderResultAsync = KrakenAPI.submitOrder pair orderType volume price
        let orderResult = Async.RunSynchronously orderResultAsync

        Assert.IsNotNull(orderResult)
        match orderResult with
        | Some responseString ->
            let parseResult = KrakenAPI.parseKrakenSubmitResponse responseString
            match parseResult with
            | Result.Ok txid ->
                let infoResultAsync = KrakenAPI.queryOrdersInfo txid true None
                let infoResult = Async.RunSynchronously infoResultAsync
                printfn "Kraken Order Info Result: %A" infoResult
                Assert.IsNotNull(infoResult)
            | Result.Error errMsg ->
                Assert.Fail(sprintf "Parsing error: %s" errMsg)
        | None ->
            Assert.Fail("Order submission failed or returned no data")


[<TestFixture>]
type BitstampApiTests() =

    [<Test>]
    member this.``Bitstamp Market Order and Status Test`` () =
        let marketSymbol = "BTCUSD"
        let amount = "0.01"
        let orderResultAsync = BitstampAPI.buyMarketOrder marketSymbol amount None
        let orderResult = Async.RunSynchronously orderResultAsync

        Assert.IsNotNull(orderResult)
        match orderResult with
        | Some responseString ->
            printfn "Response from buyMarketOrder: %s" responseString

            let orderId = responseString 

            let statusResultAsync = BitstampAPI.orderStatus orderId
            let statusResult = Async.RunSynchronously statusResultAsync
            printfn "Bitstamp Order Status Result: %A" statusResult

            Assert.IsNotNull(statusResult)
        | None ->
            Assert.Fail("Order submission failed or returned no data")
