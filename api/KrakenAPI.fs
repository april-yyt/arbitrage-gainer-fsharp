module KrakenAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json

let private httpClient = new HttpClient()

let generateNonce () =
    let epoch = new System.DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc)
    let now = System.DateTime.UtcNow
    let unixTime = System.Convert.ToInt64((now - epoch).TotalMilliseconds)
    unixTime

let createApiSignature urlPath payload nonce apiKey apiSecret = "API-SIGNATURE"// Function to create API signature


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

let queryOrdersInfo (transactionIds: string) (includeTrades: bool) (userRef: int option) (apiKey: string) (apiSecret: string) =
    async {
        let url = "https://api.kraken.com/0/private/QueryOrders"
        let nonceValue = generateNonce()
        let payload = sprintf "nonce=%s&txid=%s" nonceValue transactionIds
        let fullPayload = payload + (if includeTrades then "&trades=true" else "") + (userRef |> Option.map (sprintf "&userref=%d") |> Option.defaultValue "")
        let signature = createApiSignature url fullPayload nonceValue apiKey apiSecret
        let content = new StringContent(fullPayload, Encoding.UTF8, "application/x-www-form-urlencoded")
        
        httpClient.DefaultRequestHeaders.Clear()
        httpClient.DefaultRequestHeaders.Add("API-Key", apiKey)
        httpClient.DefaultRequestHeaders.Add("API-Sign", signature)
        
        let response = await httpClient.PostAsync(url, content)
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = await response.Content.ReadAsStringAsync()
            return Some (JsonConvert.DeserializeObject<_>(responseString)) 
        | false -> 
            return None // Handle error
    }