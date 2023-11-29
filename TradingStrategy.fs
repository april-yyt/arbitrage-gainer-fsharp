module TradingStrategy

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Utils.Collections
open Suave.RequestErrors

// ---------------------------
// Types and Event Definitions
// ---------------------------

// Message type used for the volume update agent
type VolumeMessage =
    | UpdateVolume of float
    | CheckCurrentVolume of AsyncReplyChannel<float>
    | Reset

// User-input trading strategy parameters
type TradingStrategyParameters =
    { TrackedCurrencies: int
      MinPriceSpread: float
      MinTransactionProfit: float
      MaxAmountTotal: float // crypto quantity * price, buying and
      // selling, per transaction
      MaxDailyVolume: float } // quantity of cryptocurrency

// Message type used for the trading strategy agent
type TradingStrategyMessage =
    | UpdateStrategy of TradingStrategyParameters
    | GetParams of AsyncReplyChannel<TradingStrategyParameters>
    | Activate
    | Deactivate
    | GetStatus of AsyncReplyChannel<bool>

// The Event discriminated union type is not currently used in this module; however, it will likely
// be helpful for linking the bounded contexts together in future milestones.
type Event =
    | DailyTransactionsVolumeUpdated
    | TotalTransactionsAmountUpdated
    | TradingStrategyDeactivated
    | TradingParametersInputed
    | TradingStrategyAccepted
    | TradingStrategyActivated
    | NewDayBegan

// Events for workflow inputs and outputs
type DailyTransactionsVolumeUpdated =
    { TradeBookedValue: float
      DailyReset: bool }

type TotalTransactionsAmountUpdated = { TradeBookedValue: float }

type Cause =
    | MaximalDailyTransactionVolumeReached
    | MaximalTotalTransactionAmountReached
    | UserInvocation

type TradingStrategyDeactivated = { Cause: Cause }

type TradingParametersInputed =
    { TrackedCurrencies: int
      MinPriceSpread: float
      MinTransactionProfit: float
      MaxAmountTotal: float
      MaxDailyVolume: float }

type TradingStrategyAccepted =
    { AcceptedStrategy: TradingStrategyParameters }

type TradingStrategyActivated =
    { ActivatedStrategy: TradingStrategyParameters }

type NewDayBegan =
    { PrevDayDeactivated: TradingStrategyDeactivated option
      TradingStrategy: TradingStrategyParameters }

// TODO: duplicate code; delete after integration with order management
type OrderID = int
type UpdateTransactionVolume = { OrderID: OrderID; TransactionVolume: float }
type UpdateTransactionAmount = { OrderID: OrderID; TransactionAmount: float }

// -------
// Agents
// -------

// Agent used to maintain and update the current running total of daily transaction volume.
let volumeAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop currVolume =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateVolume newVolume -> return! loop (currVolume + newVolume)
                | CheckCurrentVolume replyChannel ->
                    replyChannel.Reply(currVolume)
                    return! loop currVolume // Volume is not updated when reply is sent
                | Reset -> return! loop 0.0
            }

        loop 0.0 // Initial volume: 0.0
    )


// Helper to create initial values for a trading strategy; used by the trading strategy agent.
let initTradingParams : TradingStrategyParameters =
    { TrackedCurrencies = 0
      MinPriceSpread = 0.0
      MinTransactionProfit = 0.0
      MaxAmountTotal = 0.0
      MaxDailyVolume = 0.0 }

// Agent used to store, update, activate, and deactivate the current trading strategy.
let tradingStrategyAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop currParams activated =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateStrategy newParams -> return! loop newParams activated
                | GetParams replyChannel ->
                    replyChannel.Reply(currParams)
                    return! loop currParams activated
                | Activate -> return! loop currParams true
                | Deactivate -> return! loop currParams false
                | GetStatus replyChannel ->
                    replyChannel.Reply(activated)
                    return! loop currParams activated
            }

        loop initTradingParams false // The new trading strategy should be deactivated until
                                     // explicitly activated with an Activate message.
    )

// ----------
// Workflows
// ----------

// Helper for connecting the Order Management and Trading Strategy BCs. This will be finessed for Milestone 4.
let processNewTransactionVolume (updateTransactionVol: UpdateTransactionVolume) : TotalTransactionsAmountUpdated =
    {
        TradeBookedValue = updateTransactionVol.TransactionVolume
    }

// Helper for connecting the Order Management and Trading Strategy BCs. This will be finessed for Milestone 4.
let processNewTransactionAmount (updateTransactionAmt: UpdateTransactionAmount) : DailyTransactionsVolumeUpdated =
    {
        TradeBookedValue = updateTransactionAmt.TransactionAmount
        DailyReset = false
    }

// Processing an update to the transactions daily volume
let updateTransactionsVolume (input: DailyTransactionsVolumeUpdated) : TradingStrategyDeactivated option =
    let tradeBookedVolume = input.TradeBookedValue

    match input.DailyReset with
    | true ->
        volumeAgent.Post(Reset)
        None
    | false ->
        let dailyVol = volumeAgent.PostAndReply(CheckCurrentVolume)
        let maxVol = tradingStrategyAgent.PostAndReply(GetParams).MaxDailyVolume
        volumeAgent.Post(UpdateVolume(dailyVol + tradeBookedVolume))

        match dailyVol + tradeBookedVolume with
        | x when x >= maxVol -> Some { Cause = MaximalDailyTransactionVolumeReached }
        | _ -> None

// Processing a new trading strategy provided by the user
let acceptNewTradingStrategy (input: TradingParametersInputed) =
    let newStrat =
        { AcceptedStrategy =
            { TrackedCurrencies = input.TrackedCurrencies
              MinPriceSpread = input.MinPriceSpread
              MinTransactionProfit = input.MinTransactionProfit
              MaxAmountTotal = input.MaxAmountTotal
              MaxDailyVolume = input.MaxDailyVolume } }

    tradingStrategyAgent.Post(UpdateStrategy newStrat.AcceptedStrategy)
    newStrat

// Activating an accepted trading strategy, as needed
let activateAcceptedTradingStrategy (input: TradingStrategyAccepted) : TradingStrategyActivated = 
    tradingStrategyAgent.Post(Activate)
    { ActivatedStrategy = input.AcceptedStrategy}

// Resetting for the day.
//
// Because the trading strategy parameters
// dictate a daily maximal volume, this should be reset upon a new
// day, and a strategy that was deactivated due to reaching the
// max volume should be reset.
let resetForNewDay (input: NewDayBegan) =
    { DailyReset = true
      TradeBookedValue = 0.0 // Doesn't matter since no trade was booked
    }

// Reactivating upon a new day.
//
// A strategy that was deactivated due to reaching the max volume
// should be reactivated when the daily volume is reset.
let reactivateUponNewDay (input: NewDayBegan): TradingStrategyActivated option =
    match input.PrevDayDeactivated with
    | Some x ->
        match x.Cause with
        | MaximalDailyTransactionVolumeReached -> 
            tradingStrategyAgent.Post(Activate)
            Some { ActivatedStrategy = input.TradingStrategy }
        | _ -> None
    | None -> None


// ---------------------------
// REST API Endpoint Handlers
// ---------------------------

let newTradingStrategy =
// ref: https://theimowski.gitbooks.io/suave-music-store/content/en/query_parameters.html
// ref: https://www.c-sharpcorner.com/article/routing-in-suave-io-web-development-with-f-sharp/ 
    request (fun r ->
    let trackedCurrencies = match r.queryParam "trackedcurrencies" with
                            | Choice1Of2 tc -> int tc
                            | _ -> 0 
    let minPriceSpread = match r.queryParam "minpricespread" with
                            | Choice1Of2 mps -> float mps
                            | _ -> 0.0
    let minTransactionProfit = match r.queryParam "minprofit" with
                                | Choice1Of2 mp -> float mp
                                | _ -> 0.0
    let maxAmountTotal = match r.queryParam "maxamount" with
                            | Choice1Of2 ma -> float ma
                            | _ -> 0.0
    let maxDailyVol = match r.queryParam "maxdailyvol" with
                        | Choice1Of2 mdv -> float mdv
                        | _ -> 0.0
    let newStrat = {
        TrackedCurrencies = trackedCurrencies
        MinPriceSpread = minPriceSpread
        MinTransactionProfit = minTransactionProfit
        MaxAmountTotal = maxAmountTotal
        MaxDailyVolume = maxDailyVol
    }
    tradingStrategyAgent.Post(UpdateStrategy {
        TrackedCurrencies = trackedCurrencies
        MinPriceSpread = minPriceSpread
        MinTransactionProfit = minTransactionProfit
        MaxAmountTotal = maxAmountTotal
        MaxDailyVolume = maxDailyVol
    })
    OK (sprintf "New trading strategy inputed: %A" newStrat)
    )

let startTrading =
    request (fun r ->
    tradingStrategyAgent.Post(Activate)
    OK "Trading strategy activated")

let stopTrading =
    request (fun r ->
    tradingStrategyAgent.Post(Deactivate)
    OK "Trading strategy deactivated"
    )

let app =
    POST >=> choose
        [ path "/tradingstrategy" >=> newTradingStrategy
          path "/tradingstart" >=> startTrading
          path "/tradingstop" >=> stopTrading ]
