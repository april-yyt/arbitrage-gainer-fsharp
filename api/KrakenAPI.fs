module KrakenAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json


//Types for API Responses

// kraken submit
type SubmitDescription = {
    order: string
}
type SubmitResult = {
    descr: SubmitDescription
    txid: string[]
}
type KrakenSubmitResponse = {
    error: string[]
    result: SubmitResult
}

// kraken order
type OrderDescription = {
    [<JsonProperty("pair")>]
    Pair: string

    [<JsonProperty("type")>]
    OrderType: string

    [<JsonProperty("ordertype")>]
    Ordertype: string

    [<JsonProperty("price")>]
    Price: string

    [<JsonProperty("price2")>]
    Price2: string

    [<JsonProperty("leverage")>]
    Leverage: string

    [<JsonProperty("order")>]
    Order: string

    [<JsonProperty("close")>]
    Close: string
}

type OrderInfo = {
    [<JsonProperty("refid")>]
    Refid: string

    [<JsonProperty("userref")>]
    Userref: int

    [<JsonProperty("status")>]
    Status: string

    [<JsonProperty("reason")>]
    Reason: string option

    [<JsonProperty("opentm")>]
    Opentm: float

    [<JsonProperty("closetm")>]
    Closetm: float

    [<JsonProperty("starttm")>]
    Starttm: int

    [<JsonProperty("expiretm")>]
    Expiretm: int

    [<JsonProperty("descr")>]
    Descr: OrderDescription

    [<JsonProperty("vol")>]
    Vol: string

    [<JsonProperty("vol_exec")>]
    Vol_exec: string

    [<JsonProperty("cost")>]
    Cost: string

    [<JsonProperty("fee")>]
    Fee: string

    [<JsonProperty("price")>]
    Price: string

    [<JsonProperty("stopprice")>]
    Stopprice: string

    [<JsonProperty("limitprice")>]
    Limitprice: string

    [<JsonProperty("misc")>]
    Misc: string

    [<JsonProperty("oflags")>]
    Oflags: string

    [<JsonProperty("trigger")>]
    Trigger: string

    [<JsonProperty("trades")>]
    Trades: string[]
}

type KrakenOrderResponse = {
    [<JsonProperty("error")>]
    Error: string[]

    [<JsonProperty("result")>]
    Result: Map<string, OrderInfo>
}


// Helper functions to parse the responses from Kraken
let parseKrakenSubmitResponse (jsonString: string) : Result<string, string> =
    try
        let parsedResponse = JsonConvert.DeserializeObject<KrakenSubmitResponse>(jsonString)
        match parsedResponse.result.txid with
        | [| txid |] -> Result.Ok txid
        | _ -> Result.Error "No transaction ID found or multiple IDs present."
    with
    | :? Newtonsoft.Json.JsonException as ex -> 
        Result.Error (sprintf "JSON parsing error: %s" ex.Message)

let parseKrakenOrderResponse (jsonString: string) : Result<(string * string * string), string> =
    try
        let parsedResponse = JsonConvert.DeserializeObject<KrakenOrderResponse>(jsonString)
        match parsedResponse.Result with
        | result when result.Count > 0 ->
            match Map.tryPick (fun key value -> Some (key, value)) result with
            | Some (orderId, orderInfo) -> Result.Ok (orderId, orderInfo.Vol, orderInfo.Vol_exec)
            | None -> Result.Error "No orders found."
        | _ -> Result.Error "Unexpected number of orders in the response."
    with
    | :? Newtonsoft.Json.JsonException as ex -> 
        Result.Error (sprintf "JSON parsing error: %s" ex.Message)


// Posting API requests

let private httpClient = new HttpClient()

let generateNonce () =
    let epoch = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
    let now = System.DateTime.UtcNow
    let unixTime = System.Convert.ToInt64((now - epoch).TotalMilliseconds)
    unixTime

let submitOrder (pair: string) (orderType: string) (volume: string) (price: string) =
    async {
        let url = "https://18656-testing-server.azurewebsites.net/order/place/0/private/AddOrder"
        let payload = sprintf "pair=%s&type=%s&ordertype=%s&price=%s&volume=%s" pair orderType "market" price volume
        let content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        let! response = httpClient.PostAsync(url, content) |> Async.AwaitTask
        if response.IsSuccessStatusCode then
            let! responseString = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return Some responseString
        else
            return None
    }

let queryOrdersInfo (transactionIds: string) (includeTrades: bool) (userRef: int option) =
    async {
        let url = "https://18656-testing-server.azurewebsites.net/order/status/0/private/QueryOrders"
        let nonceValue = generateNonce()
        let payload = sprintf "nonce=%d&txid=%s" nonceValue transactionIds
        let fullPayload = payload + (if includeTrades then "&trades=true" else "") + (userRef |> Option.map (sprintf "&userref=%d") |> Option.defaultValue "")
        let content = new StringContent(fullPayload, Encoding.UTF8, "application/x-www-form-urlencoded")
        
        httpClient.DefaultRequestHeaders.Clear()
        
        let! response = httpClient.PostAsync(url, content) |> Async.AwaitTask
        if response.IsSuccessStatusCode then
            let! responseString = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return Some responseString
        else
            return None
    }
