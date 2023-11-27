module BitstampAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json
open System

let private httpClient = new HttpClient(BaseAddress = Uri("https://www.bitstamp.net"))

let postRequest (url: string) (data: (string * string) list) =
    async {
        let content = new FormUrlEncodedContent(data)
        let response = await httpClient.PostAsync(url, content)
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = response.Content.ReadAsStringAsync()
            return Some(responseString) // parse JSON response here
        | false -> 
            return None
    }

let buyMarketOrder (marketSymbol: string) (amount: string) (clientOrderId: Option<string>) =
    let url = sprintf "/api/v2/buy/market/%s/" marketSymbol
    let data = 
        [ "amount", amount ]
        |> List.append (clientOrderId |> Option.map (fun id -> [ "client_order_id", id ]) |> Option.defaultValue [])
    postRequest url data

let sellMarketOrder (marketSymbol: string) (amount: string) (clientOrderId: Option<string>) =
    let url = sprintf "/api/v2/sell/market/%s/" marketSymbol
    let data = 
        [ "amount", amount ]
        |> List.append (clientOrderId |> Option.map (fun id -> [ "client_order_id", id ]) |> Option.defaultValue [])
    postRequest url data

let orderStatus (orderId: string) =
    let url = "/api/v2/order_status/"
    let data = [ "id", orderId ]
    postRequest url data
