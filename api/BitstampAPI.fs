module BitstampAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json
open System

let private httpClient = new HttpClient(BaseAddress = Uri("https://18656-testing-server.azurewebsites.net")) // Updated Base URL

let postRequest (url: string) (data: (string * string) list) =
    async {
        let content = new FormUrlEncodedContent(data)
        let response = await httpClient.PostAsync(url, content)
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = response.Content.ReadAsStringAsync()
            return Some(responseString) // Parse JSON response 
        | false -> 
            return None
    }

let buyMarketOrder (marketSymbol: string) (amount: string) (clientOrderId: Option<string>) =
    let url = sprintf "/order/place/api/v2/buy/market/%s/" marketSymbol.ToLower() // Updated URL
    let data = 
        [ "amount", amount; "price", "dummyValue" ] // Adjusted data format
        |> List.append (clientOrderId |> Option.map (fun id -> [ "client_order_id", id ]) |> Option.defaultValue [])
    postRequest url data

let sellMarketOrder (marketSymbol: string) (amount: string) (clientOrderId: Option<string>) =
    let url = sprintf "/order/place/api/v2/sell/market/%s/" marketSymbol.ToLower() // Updated URL
    let data = 
        [ "amount", amount; "price", "dummyValue" ] // Adjusted data format
        |> List.append (clientOrderId |> Option.map (fun id -> [ "client_order_id", id ]) |> Option.defaultValue [])
    postRequest url data

let orderStatus (orderId: string) =
    let url = "/order/status/api/v2/order_status/" // Updated URL
    let data = [ "id", orderId ]
    postRequest url data
