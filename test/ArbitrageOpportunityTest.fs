module ArbitrageOpportunityTest

open Types
open ArbitrageOpportunity
open ServiceBus
open Newtonsoft.Json

[<EntryPoint>]
let main argv = 
    let strategy = { 
        TrackedCurrencies = 10;
        MinPriceSpread = 0.01;
        MinTransactionProfit = 0.01;
        MaxAmountTotal = 1000.00; // crypto quantity * price, buying and
        // selling, per transaction
        MaxDailyVolume = 5000.00; }
    sendMessageAsync ("tradingqueue" ,JsonConvert.SerializeObject(strategy))
    doRealTimeTrading |> Async.RunSynchronously

    0