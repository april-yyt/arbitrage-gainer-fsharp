# Arbitrage Gainer Milestone II
**Team 6:** Sahana Rangarajan (ssrangar), April Yang (yutongya), Audrey Zhou (yutongz7)

**Miro Board link:** https://miro.com/app/board/uXjVNfeHKWc=/?share_link_id=780735329174
## Table of Contents
1. [Trading Strategy](#trading-strategy)
2. [Arbitrage Opportunity](#positive-test-cases)
3. [Order Management](#order-management)
4. [Domain Services](#domain-services)
    - 4a. [Crosstraded Cryptocurrencies](#crosstraded-cryptocurrencies)
    - 4b. [Historical Spread Calculation](#historical-spread-calculation)

## Trading Strategy
The trading strategy is a bounded context representing a **core subdomain**. It consists of the following workflows, all of which can be found in [TradingStrategy.fs](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs):
- `updateTransactionsVolume` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L163)): processing an update to the transactions daily volume
- `updateTransactionsAmount` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L180)): processing an update to the transactions total amount
- `acceptNewTradingStrategy` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L191)): process a new trading strategy provided by the user
- `activateAcceptedTradingTrategy` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L204)): activate an accepted trading strategy for use in trading
- `resetForNewDay` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L214)): the daily volume should be reset so as to accurately track whether the maximal daily volume is reached
- `reactivateUponNewDay` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L223)): a strategy that was deactivated due to reaching the maximal daily volume should be reactivated when the daily volume is reset

Note that there are also two "helper workflows", `processNewTransactionVolume` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L150)) and `processNewTransactionAmount` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L156)). These functions' purpose is to link this bounded context to the order management bounded context by translating the events between both of them. This will be improved for the next milestone when we fully integrate the bounded contexts.

## Arbitrage Opportunity
The ArbitrageOpportunity is a bounded context representing a **core subdomain**. It consists of the following workflows, all of which can be found in [ArbitrageOpportunity.fs](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs):

- `subscribeToRealTimeDataFeed` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L101)): Subcribing to real-time data
feed for top N currency pairs.

- `retrieveDataFromRealTimeFeed` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L106)): Upon receiving real-time
data, process the data and starting trading

- `assessRealTimeArbitrageOpportunity` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L115)): Based on user provided
trading strategy, identify arbitrage opportunities and emit orders correspondingly

- `unsubscribeRealTimeDataFeed` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L207)): When trading strategy is 
deactivated, pause trading and unsubscribe from real-time data feed

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
- `updateCrossTradedCryptos` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L93)): retrieve cross-traded cryptocurrencies
- `uploadCryptoPairsToDB` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L113)): note that this workflow will be implemented fully in the next milestone, as it represents a side effect.

### Historical Spread Calculation
- `calculateHistoricalSpreadWorkflow` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/HistoricalSpreadCalc.fs#L77)): performs the historical spread calculation — the historical value file's quotes are separated into 5ms buckets, and arbitrage opportunities are identified from these buckets. Loading the historical data file (third-party integration) and persisting the resulting arbitrage opportunities in a database (side-effect) will both be implemented in the next milestone(s).
