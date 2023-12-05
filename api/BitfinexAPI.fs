module BitfinexAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json
open System.Threading.Tasks

let private httpClient = new HttpClient()

let submitOrder (orderType: string) (symbol: string) (amount: string) (price: string) =
    async {
        let url = "https://18656-testing-server.azurewebsites.net/order/place/v2/auth/w/order/submit"
        let payload = sprintf "{\"type\": \"%s\", \"symbol\": \"t%s\", \"amount\": \"%s\", \"price\": \"%s\"}" orderType symbol amount price

        let content = new StringContent(payload, Encoding.UTF8, "application/json")
        let! response = httpClient.PostAsync(url, content) |> Async.AwaitTask
        if response.IsSuccessStatusCode then
            let! responseString = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return Some (JsonConvert.DeserializeObject<_>(responseString))
        else
            return None
    }

let retrieveOrderTrades (symbol: string) (orderId: int) =
    async {
        let url = sprintf "https://18656-testing-server.azurewebsites.net/order/status/auth/r/order/%st%d/trades" symbol orderId
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        if response.IsSuccessStatusCode then
            let! responseString = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return Some (JsonConvert.DeserializeObject<_>(responseString))
        else
            return None
    }
