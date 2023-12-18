module WebServer

open Suave
open Suave.Filters
open Suave.Operators
open CrossTradedCryptos
open TradingStrategy
open ArbitrageOpportunity
open OrderManagement
open HistoricalSpreadCalc
open Akka.FSharp
open Akka.Actor
open Akka.Cluster
open Akka.Remote
open Akka.Configuration

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
    async {
        do! doRealTimeTrading ()
    }
    |> Async.Start
    async {
        do! listenForVolumeUpdate ()
    }
    |> Async.Start
    async {
        do! receiveAndProcessOrdersAkka ()
    }
    |> Async.Start
    // let orderActorRef = system.ActorOf<OrderActor>("orderActor")
    // receiveAndProcessOrdersAkka orderActorRef |> Async.Start
    // printfn "Press any key to exit..."
    // Console.ReadKey() |> ignore
    // system.Terminate() |> Async.AwaitTask |> Async.RunSynchronously

    let cfg = defaultConfig
    startWebServer cfg app
    0


