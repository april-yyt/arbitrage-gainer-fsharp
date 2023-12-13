module WebServer

open Suave
open Suave.Filters
open Suave.Operators
open CrossTradedCryptos
open TradingStrategy
open HistoricalSpreadCalc

let app =
  choose
    [ GET >=> choose
        [ path "/crosstradedcurrencies" >=> crossTradedCurrencies 
          path "/historicalspread" >=> historicalSpread ]
      POST >=> choose
        [ path "/tradingstrategy" >=> newTradingStrategy
          path "/tradingstart" >=> startTrading
          path "/tradingstop" >=> stopTrading ] ]

[<EntryPoint>]
let main args =
    // For Docker testing
    // let cfg = { defaultConfig with bindings = [ HttpBinding.createSimple HTTP "0.0.0.0" 8080  ] }
    let cfg = defaultConfig
    startWebServer cfg app
    0
