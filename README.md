# Arbitrage Gainer Milestone II
**Team 6:** Sahana Rangarajan (ssrangar), April Yang (yutongya), Audrey Zhou (yutongz7)
## Table of Contents
1. [Trading Strategy](#trading-strategy)
2. [Arbitrage Opportunity](#positive-test-cases)
3. [Order Management](#order-management)
4. [Domain Services](#domain-services)
    - 4a. [Crosstraded Cryptocurrencies](#crosstraded-cryptocurrencies)
    - 4b. [Historical Spread Calculation](#historical-spread-calculation)

## Trading Strategy
The trading strategy is a bounded context representing a **core subdomain**. It consists of the following workflows, all of which can be found in [TradingStrategy.fs](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs):
- `updateTransactionsVolume` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/f5fc2be54374fe9191aa2a324e5f96c405b08004/TradingStrategy.fs#L128)): processing an update to the transactions daily volume
- `updateTransactionsAmount` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L146)): processing an update to the transactions total amount
- `acceptNewTradingStrategy` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/f5fc2be54374fe9191aa2a324e5f96c405b08004/TradingStrategy.fs#L157)): process a new trading strategy provided by the user
- `activateAcceptedTradingTrategy` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L171)): activate an accepted trading strategy for use in trading
- `resetForNewDay` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L179)): the daily volume should be reset so as to accurately track whether the maximal daily volume is reached
- `reactivateUponNewDay` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L189)): a strategy that was deactivated due to reaching the maximal daily volume should be reactivated when the daily volume is reset

## Arbitrage Opportunity
The trading strategy is a bounded context representing a **core subdomain**. It consists of the following workflows, all of which can be found in

## Order Management
The OrderManagement a bounded context representing a **generic subdomain**. The workflows within this bounded context can all be found within [OrderManagement.fs](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs):

- `createOrders` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L111)): Processes each order by capturing details, initiating buy/sell orders, and recording them in the database, ultimately generating an `OrderCreationConfirmation`.

- `tradeExecution` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L123)): Manages the execution of trades and updates order statuses.

- `orderFulfillment` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L132)): Tracks and updates the fulfillment status of orders, ensuring accurate management of order lifecycles.

- `updateTransactionTotals` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L142)): Processes update about the transaction volume and amount, maintaining up-to-date records of transactions.

- `userNotification` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L162)): Notifies users about their order status when only one side of the order is filled.

- `handleOrderError` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L180)): Handles errors that occur during order processing. It involves detecting errors, performing corrective actions, and confirming error handling, thereby ensuring the robustness and reliability of the order management system.

- `databaseOperations` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L189)): Handles database interactions, crucial for maintaining persistent data and state of the trading operations.



## Domain Services
We have classified the following functionalities in our system as domain services, each consisting of a few simple workflows. 
### Crosstraded Cryptocurrencies
- `updateCrossTradedCryptos` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L67)): retrieve cross-traded cryptocurrencies
- `uploadCryptoPairsToDB` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L85)): note that this workflow will be implemented fully in the next milestone, as it represents a side effect.

### Historical Spread Calculation
TODO: fill in once this is fully implemented/finalized
