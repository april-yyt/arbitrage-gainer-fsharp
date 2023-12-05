module KrakenAPI

open System.Net.Http
open System.Text
open Newtonsoft.Json

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
            return Some (JsonConvert.DeserializeObject<_>(responseString))
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
            return Some (JsonConvert.DeserializeObject<_>(responseString))
        else
            return None
    }