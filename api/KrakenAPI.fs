module KrakenAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json

let private httpClient = new HttpClient()

let submitOrder (pair: string) (orderType: string) (volume: string) (price: string) =
    async {
        let url = "https://api.kraken.com/0/private/AddOrder"
        let payload = sprintf "pair=%s&type=%s&ordertype=%s&price=%s&volume=%s" pair orderType price volume
        let content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        httpClient.DefaultRequestHeaders.Add("API-Key", "<API-KEY>")
        httpClient.DefaultRequestHeaders.Add("API-Sign", "<MSG-SIGNATURE>")
        
        let response = await httpClient.PostAsync(url, content)
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = response.Content.ReadAsStringAsync()
        | false -> 
            return JsonConvert.DeserializeObject<_>(responseString) // Deserialize to appropriate type
            return None // Handle error
    }

let queryOrderInformation (nonce: int64) =
    async {
        let url = "https://api.kraken.com/0/private/OpenOrders"
        let payload = sprintf "nonce=%d" nonce
        let content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        httpClient.DefaultRequestHeaders.Add("API-Key", "<API-KEY>")
        httpClient.DefaultRequestHeaders.Add("API-Sign", "<MSG-SIGNATURE>")
        
        let response = await httpClient.PostAsync(url, content)
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = response.Content.ReadAsStringAsync()
            return JsonConvert.DeserializeObject<_>(responseString) // Deserialize to appropriate type
        | false -> 
            return None // Handle error
    }
