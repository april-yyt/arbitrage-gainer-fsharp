# Arbitrage Gainer Milestone III

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

### Side Effects
- **User input**: 
  - `startTrading` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L269)): The user can start trading with the `/tradingstart` REST API endpoint.
  - `stopTrading` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L274)): The user can stop trading with the `/tradingstop` REST API endpoint.
  - `newTradingStrategy` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L225)): The user can input new trading strategy parameters through the `/tradingstrategy` REST API endpoint.

### Error Handling
- Here, the error handling primarily occurs in ensuring whether the trading strategy parameters provided by the user are valid. If invalid or missing inputs are found, then an error response is returned instead of the 200 success; see [code](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs#L244) for reference. 

## Arbitrage Opportunity

The ArbitrageOpportunity is a bounded context representing a **core subdomain**. It consists of the following workflows, all of which can be found in [ArbitrageOpportunity.fs](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs):

- `subscribeToRealTimeDataFeed` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L101)): Subcribing to real-time data
  feed for top N currency pairs.

  - **Contains error handling when external service (Polygon) throws exception**

- `retrieveDataFromRealTimeFeed` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L106)): Upon receiving real-time
  data, process the data and starting trading

  - **Contains error handling when external service (WebSocket) throws exception**

- `assessRealTimeArbitrageOpportunity` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L115)): Based on user provided
  trading strategy, identify arbitrage opportunities and emit orders correspondingly

- `unsubscribeRealTimeDataFeed` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L207)): When trading strategy is
  deactivated, pause trading and unsubscribe from real-time data feed

### Side Effects

Each workflow listed above includes error handling to manage potential failures and ensure the system's robustness. The following side effect areas have been identified: 

- **Database Operations**:
   - `fetchCryptoPairsFromDB`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L164-L181))
- **API Calls to Polygon for Real Time Data Retrieval**:
   - `authenticatePolygon`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L184))
   - `connectWebSocket`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L201))
   - `subscribeData`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L213))
   - `subscribeToRealTimeDataFeed`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L228))
   - `unsubscribeRealTimeDataFeed`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L238))
   - `receiveMsgFromWSAndTrade`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/ArbitrageOpportunity.fs#L267-L292))

## Order Management

The OrderManagement a bounded context representing a **generic subdomain**. The workflows within this bounded context can all be found within [OrderManagement.fs](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs):

### Basic Functionalities

- `createOrders` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L111)): Processes each order by capturing details, initiating buy/sell orders, and recording them in the database, ultimately generating an `OrderCreationConfirmation`.

- `tradeExecution` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L123)): Manages the execution of trades and updates order statuses.

- `orderFulfillment` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L132)): Tracks and updates the fulfillment status of orders, ensuring accurate management of order lifecycles.

- `updateTransactionTotals` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L142)): Processes update about the transaction volume and amount, maintaining up-to-date records of transactions.

- `userNotification` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L162)): Notifies users about their order status when only one side of the order is filled.

- `handleOrderError` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L180)): Handles errors that occur during order processing. It involves detecting errors, performing corrective actions, and confirming error handling, thereby ensuring the robustness and reliability of the order management system.

- `databaseOperations` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L189)): Handles database interactions, crucial for maintaining persistent data and state of the trading operations.

### Side Effects and Error Handling

Each workflow listed above includes error handling to manage potential failures and ensure the system's robustness. The following side effect areas have been identified and error-handled:

- **API Calls to Exchanges**: `initiateBuySellOrderAsync`
- **Database Operations**: `recordOrderInDatabaseAsync`
  <!-- -  **Trade Execution**: `tradeExecution` -->
  <!-- -  **Order Fulfillment**: `orderFulfillment` -->
- **User Notifications**: `userNotification`, to be implemented in Milestone IV
- **Order Update Broadcast**: `pushOrderUpdate`

## Domain Services

We have classified the following functionalities in our system as domain services, each consisting of a few simple workflows.

### Crosstraded Cryptocurrencies

- `updateCrossTradedCryptos` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L93)): retrieve cross-traded cryptocurrencies
- `uploadCryptoPairsToDB` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L141)): note that this workflow will be implemented fully in the next milestone, as it represents a side effect.
- **Side Effects**:
  - `crossTradedCryptos` [link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L153): A user-accessible REST API endpoint, `/crosstradedcryptos`, was added to retrieve updated cross traded currencies
  - The workflow `uploadCryptoPairsToDB` (now found [here](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L141)) now uploads database results to an Azure Storage table.
  - The input currency lists for each exchange are loaded in from external txt files; see `validCurrencyPairsFromFile`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L85)).
- **Error Handling**:
  - Railway-oriented error handling was added in checking the new input files starting at `validCurrencyPairsFromFile` and propagating into the REST API endpoint when cross traded currencies are updated (see [here](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L156)). The properties checked are whether the files exist (`validateFileExistence` [here](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L71)) and whether the file is the proper type (`validateFileType` [here](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L77)).
  - The database operation's response is checked for errors in the [endpoint](https://github.com/yutongyaF2023/arbitragegainer/blob/main/CrossTradedCryptos.fs#L162).
### Historical Spread Calculation

- `calculateHistoricalSpreadWorkflow` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/HistoricalSpreadCalc.fs#L77)): performs the historical spread calculation — the historical value file's quotes are separated into 5ms buckets, and arbitrage opportunities are identified from these buckets.
- **Side Effects**:
  - The historical value file is now loaded in with JSON type providers; see the `HistoricalValues` [type](https://github.com/yutongyaF2023/arbitragegainer/blob/main/HistoricalSpreadCalc.fs#L51) and `loadHistoricalValuesFile` [here](https://github.com/yutongyaF2023/arbitragegainer/blob/main/HistoricalSpreadCalc.fs#L95).
  - This calculation can be invoked by the REST API endpoint `/historicalspread` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/HistoricalSpreadCalc.fs#L95)).
  - The results are persisted in a database; see `persistOpportunitiesInDB` [here](https://github.com/yutongyaF2023/arbitragegainer/blob/main/HistoricalSpreadCalc.fs#L163).
- **Error Handling**:
  - We validate that the historicalData.txt file exists [here](https://github.com/yutongyaF2023/arbitragegainer/blob/main/HistoricalSpreadCalc.fs#L175)
  - We validate that the JSON type provider has all the fields we require [here](https://github.com/yutongyaF2023/arbitragegainer/blob/main/HistoricalSpreadCalc.fs#L72)
  - We handle DB errors [here](https://github.com/yutongyaF2023/arbitragegainer/blob/main/HistoricalSpreadCalc.fs#L185)
  - See the REST API endpoint to see the errors propagate into failures.


