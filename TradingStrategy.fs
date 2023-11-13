module TradingStrategy

open OrderManagement

// ---------------------------
// Types and Event Definitions
// ---------------------------

// Message type used for the volume update agent
type VolumeMessage =
    | UpdateVolume of float
    | CheckCurrentVolume of AsyncReplyChannel<float>
    | Reset

// Message type used for the amount update agent
type AmountMessage =
    | UpdateAmount of float
    | CheckCurrentAmount of AsyncReplyChannel<float>

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

// Agent used to maintain and update the current running total transaction amount.
let amountAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop currAmount =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateAmount newAmount -> return! loop (currAmount + newAmount)
                | CheckCurrentAmount replyChannel ->
                    replyChannel.Reply(currAmount)
                    return! loop currAmount
            }

        loop 0.0)


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
            }

        loop initTradingParams false // The new trading strategy should be deactivated until
                                     // explicitly activated with an Activate message.
    )

// ----------
// Workflows
// ----------

// Helper for connecting the Order Management and Trading Strategy BCs. This will be finessed for Milestone 3.
let processNewTransactionVolume (updateTransactionVol: UpdateTransactionVolume) : TotalTransactionsAmountUpdated =
    {
        TradeBookedValue = updateTransactionVol.TransactionVolume
    }

// Helper for connecting the Order Management and Trading Strategy BCs. This will be finessed for Milestone 3.
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

// Processing an update to the transactions total amount
let updateTransactionsAmount (input: TotalTransactionsAmountUpdated) : TradingStrategyDeactivated option =
    let amount = amountAgent.PostAndReply(CheckCurrentAmount)
    let tradeVal = input.TradeBookedValue
    let maxAmt = tradingStrategyAgent.PostAndReply(GetParams).MaxAmountTotal
    amountAgent.Post(UpdateAmount(amount + tradeVal))

    match amount + tradeVal with
    | x when x >= maxAmt -> Some { Cause = MaximalTotalTransactionAmountReached }
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
