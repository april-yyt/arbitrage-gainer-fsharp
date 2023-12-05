module BitstampAPI

open System
open System.Net.Http
open System.Text
open Newtonsoft.Json
open System.Diagnostics

let private httpClient = new HttpClient(BaseAddress = Uri("https://18656-testing-server.azurewebsites.net"))

let postRequest (url: string) (data: (string * string) list) =
    async {
        let content = new FormUrlEncodedContent(data |> Seq.map (fun (k, v) -> new System.Collections.Generic.KeyValuePair<string, string>(k, v)))

        let! response = Async.AwaitTask (httpClient.PostAsync(url, content)) 
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = Async.AwaitTask (response.Content.ReadAsStringAsync()) 
            Debug.WriteLine($"Response JSON: {responseString}")
            return Some(responseString)
        | false -> 
            Debug.WriteLine($"Error: {response.StatusCode}")
            return None
    }


let buyMarketOrder (marketSymbol: string) (amount: string) (clientOrderId: Option<string>) =
    let url = sprintf "/order/place/api/v2/buy/market/%s/" (marketSymbol.ToLower())
    let data = 
        [ "amount", amount; "price", "dummyValue" ]
        |> List.append (clientOrderId |> Option.map (fun id -> [ "client_order_id", id ]) |> Option.defaultValue [])
    Debug.WriteLine($"Sending buyMarketOrder to URL: {url} with data: {data}")
    postRequest url data

let sellMarketOrder (marketSymbol: string) (amount: string) (clientOrderId: Option<string>) =
    let url = sprintf "/order/place/api/v2/sell/market/%s/" (marketSymbol.ToLower())
    let data = 
        [ "amount", amount; "price", "dummyValue" ]
        |> List.append (clientOrderId |> Option.map (fun id -> [ "client_order_id", id ]) |> Option.defaultValue [])
    Debug.WriteLine($"Sending sellMarketOrder to URL: {url} with data: {data}")
    postRequest url data

let orderStatus (orderId: string) =
    let url = "/order/status/api/v2/order_status/"
    let data = [ "id", orderId ]
    Debug.WriteLine($"Checking orderStatus for orderId: {orderId}")
    postRequest url data
