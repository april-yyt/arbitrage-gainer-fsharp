module TradingStrategy

type TradingStrategyParameters = {
    TrackedCurrencies: int
    MinPriceSpread: float
    MinTransactionProfit: float
    MaxAmountTotal: float
    MaxDailyVolume: float
}

type Event =
    | DailyTransactionsVolumeUpdated
    | TotalTransactionsAmountUpdated
    | TradingStrategyDeactivated
    | TradingParametersInputed
    | TradingStrategyAccepted
    | TradingStrategyActivated
    | NewDayBegan
    | None

type DailyTransactionsVolumeUpdated = {
    TradeBookedValue: float
    DailyTransactionVolumeMax: float
}

type Cause =
    | MaximalDailyTransactionVolumeReached
    | MaximalTotalTransactionAmountReached
    | UserInvocation

type TradingStrategyDeactivated = {
    Cause: Cause
}
// Processing an update to the transactions daily volume
let updateTransactionsVolume DailyTransactionsVolumeUpdated TradingStrategyDeactivated option =
    None

// Processing an update to the transactions total amount
let updateTransactionsAmount TotalTransactionsAmountUpdated TradingStrategyDeactivated option =
    None

// Processing a new trading strategy provided by the user
let acceptNewTradingStrategy TradingParametersInputed TradingStrategyAccepted =
    TradingStrategyAccepted

// Activating an accepted trading strategy, as needed
let activateAcceptedTradingStrategy TradingStrategyAccepted TradingStrategyActivated option =
    None

// Resetting for the day.
//
// Because the trading strategy parameters
// dictate a daily maximal volume, this should be reset upon a new
// day, and a strategy that was deactivated due to reaching the
// max volume should be reactivated.
let resetForNewDay NewDayBegan DailyTransactionsVolumeUpdated option =
    None

// Reactivating upon a new day.
// A strategy that was deactivated due to reaching the max volume
// should be reactivated when the daily volume is reset.
let reactivateUponNewDay NewDayBegan TradingStrategyActivated option =
    None
