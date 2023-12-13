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
type Price = Price of float
type OrderType = string
type Quantity = Quantity of float
type Exchange = string
type OrderID = int
type TradeID = int
type Time = int64

type UnprocessedQuote = {
    [<JsonProperty("ev")>]
    EventType: string
    [<JsonProperty("pair")>]
    CurrencyPair: string
    [<JsonProperty("bp")>]
    BidPrice: float
    [<JsonProperty("bs")>]
    BidSize: float
    [<JsonProperty("ap")>]
    AskPrice: float
    [<JsonProperty("as")>]
    AskSize: float
    [<JsonProperty("t")>]
    Time: Time
    [<JsonProperty("x")>]
    Exchange: int
    [<JsonProperty("r")>]
    ReceiveTime: Time
} 
type TradingStrategyParameters =
    { TrackedCurrencies: int
      MinPriceSpread: float
      MinTransactionProfit: float
      MaxAmountTotal: float // crypto quantity * price, buying and
      // selling, per transaction
      MaxDailyVolume: float } // quantity of cryptocurrency

type Quote = {
    Exchange: Exchange
    CurrencyPair: CurrencyPair;
    BidPrice: float;
    AskPrice: float;                          
    BidSize: float;
    AskSize: float;
    Time: Time;
}

type OrderDetails = {
    Currency: Currency
    Price: float
    OrderType: OrderType
    Quantity: float
    Exchange: Exchange
}