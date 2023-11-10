module TradingStrategy

type TradingStrategyParameters = {
    TrackedCurrencies: int
    MinPriceSpread: float
    MinTransactionProfit: float
    MaxAmountTotal: float // crypto quantity * price, buying and
                          // selling, per transaction
    MaxDailyVolume: float // quantity of cryptocurrency
}

type Event =
    | DailyTransactionsVolumeUpdated
    | TotalTransactionsAmountUpdated
    | TradingStrategyDeactivated
    | TradingParametersInputed
    | TradingStrategyAccepted
    | TradingStrategyActivated
    | NewDayBegan
    | TradingContinuedWithUpdatedVolume // Tentative new return types
    | TradingContinuedWithUpdatedAmount

type DailyTransactionsVolumeUpdated = {
    // TODO: find out if the user input parameters can be saved as globals
    // (shared state lecture on Monday?)
    MaxDailyVolume: float
    DailyTransactionsVolume: float
    TradeBookedValue: float
    DailyReset: bool
}

type TradingContinuedWithUpdatedVolume = {
    CurrentVolume: int
}

type TradingContinuedWithUpdatedAmount = {
    CurrentAmount: float
}

// TODO: ask if it's ok if I have more info in the actual code than I did in the psuedocode
type TotalTransactionsAmountUpdated = {
    MaxAmountTotal: float
    TransactionsAmount: float
    TradeBookedValue: float
}

type Cause =
    | MaximalDailyTransactionVolumeReached
    | MaximalTotalTransactionAmountReached
    | UserInvocation

type TradingStrategyDeactivated = {
    Cause: Cause
}

type TradingParametersInputed = {
    TrackedCurrencies: int
    MinPriceSpread: float
    MinTransactionProfit: float
    MaxAmountTotal: float
    MaxDailyVolume: float
}

type UpdateProcessed =
    | TradingStrategyDeactivated of TradingStrategyDeactivated
    | TradingContinuedWithUpdatedAmount of TradingContinuedWithUpdatedAmount
    | TradingContinuedWithUpdatedVolume of TradingContinuedWithUpdatedVolume

type TradingStrategyAccepted = {
    AcceptedStrategy: TradingStrategyParameters
}

type NewDayBegan = {
    PrevDayDeactivated: TradingStrategyDeactivated option
    TradingStrategy: TradingStrategyParameters
}

type TradingStrategyActivated = {
    // TODO: define fields once the related function is clearer
    Field: bool
}

// Processing an update to the transactions daily volume
// TODO: fix volume update calculation
let updateTransactionsVolume (input: DailyTransactionsVolumeUpdated) =
    let dailyVol = input.DailyTransactionsVolume
    let tradeBooked = (input.TradeBookedValue > 0)
    let maxVol = input.MaxDailyVolume
    match input.DailyReset with
    // Syntax ref: https://stackoverflow.com/questions/29801418/f-can-i-return-a-discriminated-union-from-a-function
    | true -> TradingContinuedWithUpdatedVolume { CurrentVolume = 0 }
    | false ->
        match dailyVol + 1 with
        | x when x >= maxVol -> TradingStrategyDeactivated { Cause = MaximalDailyTransactionVolumeReached }
        | _ -> TradingContinuedWithUpdatedVolume { CurrentVolume = dailyVol + 1 }

// Processing an update to the transactions total amount
let updateTransactionsAmount (input: TotalTransactionsAmountUpdated) =
    let amount = input.TransactionsAmount
    let tradeVal = input.TradeBookedValue
    let maxAmt = input.MaxAmountTotal
    match amount + tradeVal with
    | x when x >= maxAmt -> TradingStrategyDeactivated { Cause = MaximalTotalTransactionAmountReached }
    | _ -> TradingContinuedWithUpdatedAmount {CurrentAmount = amount + tradeVal}

// Processing a new trading strategy provided by the user
let acceptNewTradingStrategy (input: TradingParametersInputed) =
     { AcceptedStrategy = {
        TrackedCurrencies = input.TrackedCurrencies
        MinPriceSpread = input.MinPriceSpread
        MinTransactionProfit = input.MinTransactionProfit
        MaxAmountTotal = input.MaxAmountTotal
        MaxDailyVolume = input.MaxDailyVolume }
    }

// Activating an accepted trading strategy, as needed
let activateAcceptedTradingStrategy (input: TradingStrategyAccepted) =
    { Field = true }
    // TODO after shared state lecture: this could involve replacing a global trading parameters variable, maybe.

// Resetting for the day.
//
// Because the trading strategy parameters
// dictate a daily maximal volume, this should be reset upon a new
// day, and a strategy that was deactivated due to reaching the
// max volume should be reset.
let resetForNewDay (input: NewDayBegan) =
    {
        DailyReset = true
        DailyTransactionsVolume = 0 // This value doesn't matter since it is getting reset
        MaxDailyVolume = input.TradingStrategy.MaxDailyVolume
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
        | MaximalDailyTransactionVolumeReached -> Some { Field = true }
        | _ -> None
    | None -> None
