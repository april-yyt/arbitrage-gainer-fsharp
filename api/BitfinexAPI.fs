module BitfinexAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json
open System.Threading.Tasks
open FSharp.Data

// types for api response
type BitfinexSubmitOrderResponse = 
    int64 * string * int * obj * (int64 * obj * int64 * string * int64 * int64 * float * float * string * obj * obj * obj * int * string * obj * obj * float * int * int * int * obj * obj * obj * int * int * obj * obj * obj * string * obj * obj * obj) array * obj * string * string

type BitfinexOrderStatusResponse = 
    int64 * string * int64 * int64 * float * float * obj * obj * int * int * string * int64

let parseBitfinexSubmitResponse (jsonString: string) : Result<int64, string> =
    try
        let parsedResponse = JsonConvert.DeserializeObject<BitfinexSubmitOrderResponse[]>(jsonString)
        match parsedResponse with
        | [| (_, _, _, _, [| (orderId, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _) |], _, _, _) |] ->
            Result.Ok orderId
        | _ -> 
            Result.Error "Invalid response format or multiple orders found."
    with
    | :? Newtonsoft.Json.JsonException as ex -> 
        Result.Error (sprintf "JSON parsing error: %s" ex.Message)

let parseBitfinexOrderStatusResponse (jsonString: string) : Result<float, string> =
    try
        let parsedResponse = JsonConvert.DeserializeObject<BitfinexOrderStatusResponse[]>(jsonString)
        match parsedResponse with
        | [| (_, _, _, _, executedAmount, _, _, _, _, _, _, _) |] ->
            Result.Ok executedAmount
        | _ -> 
            Result.Error "Invalid response format or multiple orders found."
    with
    | :? Newtonsoft.Json.JsonException as ex -> 
        Result.Error (sprintf "JSON parsing error: %s" ex.Message)

let parseBitfinexResponse (jsonString: string) : Result<int, string> =
    printfn "Bitfinex Response: %s" jsonString
    Result.Ok 1747566428

let private httpClient = new HttpClient()

let submitOrder (orderType: string) (symbol: string) (amount: string) (price: string) =
    async {
        let url = "https://18656-testing-server.azurewebsites.net/order/place/v2/auth/w/order/submit"
        let payload = sprintf "{\"type\": \"%s\", \"symbol\": \"t%s\", \"amount\": \"%s\", \"price\": \"%s\"}" orderType symbol amount price

        let content = new StringContent(payload, Encoding.UTF8, "application/json")
        let! response = httpClient.PostAsync(url, content) |> Async.AwaitTask
        if response.IsSuccessStatusCode then
            let! responseString = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            // return Some (JsonConvert.DeserializeObject<_>(responseString))
            return Some responseString
        else
            return None
    }

let retrieveOrderTrades (symbol: string) (orderId: int) =
    async {
        let url = sprintf "https://18656-testing-server.azurewebsites.net/order/status/auth/r/order/%s:%d/trades" symbol orderId
        let requestContent = new StringContent("", Encoding.UTF8, "application/json") 
        let! response = httpClient.PostAsync(url, requestContent) |> Async.AwaitTask
        printfn "Bitfinex Trades Rsp: %A" response
        if response.IsSuccessStatusCode then
            let! responseString = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return Some responseString
        else
            return None
    }