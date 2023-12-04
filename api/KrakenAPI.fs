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

let submitOrder (pair: string) (orderType: string) (volume: string) (price: string) =
    async {
        let url = "https://18656-testing-server.azurewebsites.net/order/place/0/private/AddOrder" // Updated URL
        let payload = sprintf "pair=%s&type=%s&ordertype=%s&price=%s&volume=%s" pair orderType "market" price volume // Adjusted payload
        let content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        let response = await httpClient.PostAsync(url, content)
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = response.Content.ReadAsStringAsync()
            return JsonConvert.DeserializeObject<_>(responseString) // Deserialize to appropriate type
        | false -> 
            return None // Handle error
    }

let queryOrdersInfo (transactionIds: string) (includeTrades: bool) (userRef: int option) =
    async {
        let url = "https://18656-testing-server.azurewebsites.net/order/status/0/private/QueryOrders" // Updated URL
        let nonceValue = generateNonce()
        let payload = sprintf "nonce=%s&txid=%s" nonceValue transactionIds
        let fullPayload = payload + (if includeTrades then "&trades=true" else "") + (userRef |> Option.map (sprintf "&userref=%d") |> Option.defaultValue "")
        let content = new StringContent(fullPayload, Encoding.UTF8, "application/x-www-form-urlencoded")
        
        httpClient.DefaultRequestHeaders.Clear()
        
        let response = await httpClient.PostAsync(url, content)
        match response.IsSuccessStatusCode with
        | true -> 
            let! responseString = await response.Content.ReadAsStringAsync()
            return Some (JsonConvert.DeserializeObject<_>(responseString)) 
        | false -> 
            return None // Handle error
    }
