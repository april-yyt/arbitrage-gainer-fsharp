module Types

open Newtonsoft.Json

type Result<'Success,'Failure> =
| Ok of 'Success
| Error of 'Failure

type ArbitrageOpportunity = {
    Currency1: string
    Currency2: string
    NumberOfOpportunitiesIdentified: int
}
type CurrencyPair = {
    Currency1: string
    Currency2: string
}
type Currency = string
type Price = float
type OrderType = Buy | Sell
type Quantity = int
type Exchange = string
type OrderID = int
type TradeID = int
type Time = int

type UnprocessedQuote = {
    [<JsonProperty("ev")>]
    EventType: string
    [<JsonProperty("pair")>]
    CurrencyPair: string
    [<JsonProperty("bp")>]
    BidPrice: Quantity
    [<JsonProperty("bs")>]
    BidSize: Quantity
    [<JsonProperty("ap")>]
    AskPrice: Price
    [<JsonProperty("as")>]
    AskSize: Price
    [<JsonProperty("t")>]
    Time: Time
    [<JsonProperty("x")>]
    Exchange: int
    [<JsonProperty("r")>]
    ReceiveTime: Time
} 
type Quote = {
    Exchange: Exchange
    CurrencyPair: CurrencyPair;
    BidPrice: Price;
    AskPrice: Price;                          
    BidSize: Quantity;
    AskSize: Quantity;
    Time: Time;
}

type OrderDetails = {
    Currency: Currency
    Price: Price
    OrderType: OrderType
    Quantity: Quantity
    Exchange: Exchange
}