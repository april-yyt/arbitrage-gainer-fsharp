module BitfinexAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json

let private httpClient = new HttpClient()

let submitOrder (orderType: string) (symbol: string) (amount: string) (price: string) =
    async {
        let url = "https://18656-testing-server.azurewebsites.net/order/place/v2/auth/w/order/submit" // Updated URL
        // Adjust the body to match the mock API format
        let payload = sprintf "{\"type\": \"MARKET\", \"symbol\": \"%s\", \"amount\": \"%s\", \"price\": \"%s\"}" "t" + symbol amount price
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
        let url = sprintf "https://18656-testing-server.azurewebsites.net/order/status/auth/r/order/%s:%d/trades" "t" + symbol orderId // Updated URL
        let response = await httpClient.GetAsync(url) // Changed from PostAsync to GetAsync
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = response.Content.ReadAsStringAsync()
            return JsonConvert.DeserializeObject<_>(responseString)
        | false -> 
            return None
    }
