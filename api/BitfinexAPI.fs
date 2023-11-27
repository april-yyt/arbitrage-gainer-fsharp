module BitfinexAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json

let private httpClient = new HttpClient()

let submitOrder (symbol: string) (amount: string) (price: string) (orderType: string) =
    async {
        let url = "https://api.bitfinex.com/v2/auth/w/order/submit"
        let payload = sprintf "{\"type\": \"%s\", \"symbol\": \"%s\", \"amount\": \"%s\", \"price\": \"%s\"}" orderType symbol amount price
        let content = new StringContent(payload, Encoding.UTF8, "application/json")
        let response = await httpClient.PostAsync(url, content)
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = response.Content.ReadAsStringAsync()
            return JsonConvert.DeserializeObject<_>(responseString) 
        | false -> 
            return None 
    }

let retrieveOrderTrades (symbol: string) (orderId: int) =
    async {
        let url = sprintf "https://api.bitfinex.com/v2/auth/r/order/%s:%d/trades" symbol orderId
        let response = await httpClient.PostAsync(url, null)
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = response.Content.ReadAsStringAsync()
            return JsonConvert.DeserializeObject<_>(responseString)
        | false -> 
            return None
    }
