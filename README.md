# Arbitrage Gainer

**Team 6:** Sahana Rangarajan (ssrangar), April Yang (yutongya), Audrey Zhou (yutongz7)

**Miro Board link:** https://miro.com/app/board/uXjVNfeHKWc=/?share_link_id=780735329174

## Table of Contents

1. [Trading Strategy](#trading-strategy)
2. [Arbitrage Opportunity](#positive-test-cases)
3. [Order Management](#order-management)
4. [Domain Services](#domain-services)
   - 4a. [Crosstraded Cryptocurrencies](#crosstraded-cryptocurrencies)
   - 4b. [Historical Spread Calculation](#historical-spread-calculation)
5. [REST Endpoints](#rest-endpoints)

## Trading Strategy

The trading strategy is a bounded context representing a **core subdomain**. It consists of the following workflows, all of which can be found in [TradingStrategy.fs](https://github.com/yutongyaF2023/arbitragegainer/blob/main/TradingStrategy.fs):

- `listenForVolumeUpdate`: Listens to service bus queue for an update to volume from the Order Management bounded context, and processes an update to the transactions daily volume
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

The OrderManagement module is a bounded context representing a **generic subdomain**, focusing on managing and processing orders. This module interacts with various cryptocurrency exchanges and ensures that orders are executed according to emitted order details it receives. The source code can be found in [OrderManagement.fs](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs).

### Core Workflows

- `createOrderAsync` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L305)): Processes each order by capturing details, initiating buy/sell orders, and recording them in the database.
- `submitOrderAsync`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L187)): Asynchronously submits a new order to the specified exchange.
- `processOrderUpdate`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L324)): Retrieves and processes updates for an existing order. Query the status from the exchanges and perform corresponding actions based on fulfillment status.
- `createAndProcessOrders`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L402)): Main workflow to create new orders and process their updates.



### Side Effects

In each of these modules, functions are designed to manage side effects—such as making HTTP requests or database operations—and include error handling to ensure the robustness of the system. 

**Database Operations**: Interacts with Azure Table Storage for storing and retrieving order details.
  - `addOrderToDatabase`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L98)): Adds a new order to the database. 
  - `updateOrderStatus`([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L110)): Updated an existing order in the database.

**Helper Functions**

- `generateRandomID`, `generateRandomFulfillmentStatus`, `generateRandomRemainingQuantity`: Generate random IDs and statuses for testing purposes.
- `processBitfinexResponse`, `processKrakenResponse`, `processBitstampResponse`: Parse responses from different exchanges and update the order status accordingly.

**Bitfinex API Functions:**

- `submitOrder` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/api/BitfinexAPI.fs#L9)) : Sends an order to the Bitfinex exchange and handles responses or errors accordingly. It ensures that any network or API-specific errors are caught and reported back to the caller.

- `retrieveOrderTrades` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/api/BitfinexAPI.fs#L23)) : Retrieves the trades for a given order from Bitfinex. It includes error handling for failed HTTP requests and unexpected response formats.

**Kraken API Functions:**

- `submitOrder` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/api/KrakenAPI.fs#L18)) : Submits an order to the Kraken exchange, handling potential errors such as connection issues or invalid responses. It ensures the caller receives a clear error message for any issues encountered.
- `queryOrderInformation` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/api/KrakenAPI.fs#L35)) : Queries detailed information about specific orders on Kraken. It handles errors robustly, including logging and reporting back any issues with the connection or the API response.

**Bitstamp API Functions:**

- `buyMarketOrder` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/api/BitstampAPI.fs#L22)) and `sellMarketOrder` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/api/BitstampAPI.fs#L29)) : These functions initiate market orders on Bitstamp and are equipped to handle errors such as network failures or unexpected API changes.
- `orderStatus` ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/api/BitstampAPI.fs#L36)) : Checks the status of an order on Bitstamp, handling any potential errors during the request or in parsing the response.



### Error Handling
- The module includes comprehensive error handling for various operations, such as adding orders to the database and updating order statuses. 
- The error handling strategies involve catching exceptions, validating API responses, and ensuring that any failures do not disrupt the overall workflow and are logged for further analysis.

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

### REST Endpoints
- `/tradingstrategy`: This POST endpoint accepts a new trading strategy, specified as the following required query parameters:
  - `trackedcurrencies`: the number of cryptocurrencies to track (**int**)
  - `minpricespread`: minimal price spread value (**float**)
  - `minprofit`: minimal trasaction profit (**float**)
  - `maxamount`: maximal transaction amount (**float**)
  - `maxdailyvol`: maximal daily transactions volume (**float**)
- `/tradingstart`: This POST endpoint activates the trading strategy and begins the trading flow.
- `/tradingstop`: This POST endpoint deactivates the trading strategy and halts the trading flow.
- `/crosstradedcurrencies`: This GET endpoint retrieves pairs of currencies traded at all the currencies contained within the exchange files expected in the same location as the code. It also logs the output into a text file (`crossTradedCurrencies.txt`) and perpetuates the results in an Azure table.

### Azure Service Bus
- **[Service Bus Setup Code Pointer]()**
- **[tradingqueue](https://portal.azure.com/#@andrewcmu.onmicrosoft.com/resource/subscriptions/075cf1cf-2912-4a8b-8d6f-fbb9c461bc2b/resourceGroups/ArbitrageGainer/providers/Microsoft.ServiceBus/namespaces/ArbitrageGainer/queues/tradingqueue/overview)**: connecting **TradingStrategy** and **ArbitrageOpportunity** bounded contexts
   - message type:
      - "Stop trading" for stopping trading
      - stringified trading strategy parameters for starting trading with specified trading strategy
   - send message code pointer
   - receive message code pointer
- **[orderqueue](https://portal.azure.com/#@andrewcmu.onmicrosoft.com/resource/subscriptions/075cf1cf-2912-4a8b-8d6f-fbb9c461bc2b/resourceGroups/ArbitrageGainer/providers/Microsoft.ServiceBus/namespaces/ArbitrageGainer/queues/orderqueue/overview)**: connecting **ArbitrageOpportunity** and **OrderManagement** bounded contexts
   - message type:
      - stringified order details for creating orders
   - send message code pointer
   - receive message code pointer 
- **[strategyqueue](https://portal.azure.com/#@andrewcmu.onmicrosoft.com/resource/subscriptions/075cf1cf-2912-4a8b-8d6f-fbb9c461bc2b/resourceGroups/ArbitrageGainer/providers/Microsoft.ServiceBus/namespaces/ArbitrageGainer/queues/strategyqueue/explorer)**: connecting **OrderManagement** and **TradingStrategy** bounded contexts
  - message type:
    - stringified volume update details for an order
