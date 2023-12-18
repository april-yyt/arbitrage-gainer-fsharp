module TradingStrategy

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Utils.Collections
open Suave.RequestErrors
open ServiceBus
open Types
open Newtonsoft.Json

open FSharp.CloudAgent
open FSharp.CloudAgent.Messaging
open FSharp.CloudAgent.Connections

// ---------------------------
// Types and Event Definitions
// ---------------------------

// Type def for error handling
type Result<'Success,'Failure> =
| Ok of 'Success
| Error of 'Failure

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

// Discriminated union type representing different events
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


type UpdateTransactionVolume = { OrderID: OrderID; Quantity: float }

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


// Agent used to store and update the current trading strategy.
let tradingStrategyAgent =
    MailboxProcessor.Start(fun inbox ->
        let rec loop currParams =
            async {
                let! msg = inbox.Receive()

                match msg with
                | UpdateStrategy newParams -> return! loop newParams
                | GetParams replyChannel ->
                    replyChannel.Reply(currParams)
                    return! loop currParams
                | _ -> return! loop currParams
            }

        loop initTradingParams // The new trading strategy should be deactivated until
                                     // explicitly activated with an Activate message.
    )

// Refactored agent for extra credit task
let activationAgent (agentId: ActorKey) =
    MailboxProcessor.Start(fun inbox ->
        let rec loop activated =
            async {
                let! msg = inbox.Receive()

                match msg with
                | Activate -> 
                    printfn "Activated by agent %A" agentId
                    return! loop true
                | Deactivate -> 
                    printfn "Deactivated by agent %A" agentId
                    return! loop false
                | _ -> return! loop activated
            }

        loop false // The new trading strategy should be deactivated until
                                     // explicitly activated with an Activate message.
    )

// For extra credit task
let connStr = "Endpoint=sb://arbitragegainer.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=RX56IkxeBgdYjM6OoHXozGRw37tsUQrGk+ASbNEYcl0="
let queueName = "agentqueue"
let cloudConn = CloudConnection.WorkerCloudConnection(ServiceBusConnection connStr, Connections.Queue queueName)
ConnectionFactory.StartListening(cloudConn, activationAgent >> BasicCloudAgent)
let distributedPost = ConnectionFactory.SendToWorkerPool cloudConn

// ----------
// Workflows
// ----------

// Helper for connecting the Order Management and Trading Strategy BCs.
let processNewTransactionVolume (updateTransactionVol: UpdateTransactionVolume) : DailyTransactionsVolumeUpdated =
    {
        TradeBookedValue = updateTransactionVol.Quantity
        DailyReset = false
    }

let rec listenForVolumeUpdate () = 
    async {
        let msg = receiveMessageAsync "strategyqueue"
        printfn "msg received from service bus: %A" msg

        match msg with
        | null | "" ->
            printfn "Received an empty message, skipping."
        | _ ->
            try
                printfn "Attempting to deserialize message: %s" msg
                let volUpdate = JsonConvert.DeserializeObject<UpdateTransactionVolume>(msg)
                printfn "Deserialized message to volume update: %A" volUpdate
                let tradeBookedVolume = volUpdate.Quantity
                let dailyVol = volumeAgent.PostAndReply(CheckCurrentVolume)
                let maxVol = tradingStrategyAgent.PostAndReply(GetParams).MaxDailyVolume
                volumeAgent.Post(UpdateVolume(dailyVol + tradeBookedVolume))

                match dailyVol + tradeBookedVolume with
                | x when x >= maxVol ->
                    printfn "Max volume reached. Sending stop trading message."
                    tradingStrategyAgent.Post(Deactivate)
                    sendMessageAsync("tradingqueue", "stop trading")
                | _ -> ()
            with
            | ex ->
                printfn "Error processing message: %s" ex.Message

        do! Async.Sleep(1000)
        return! listenForVolumeUpdate ()
    }



// Processing a new trading strategy provided by the user
let acceptNewTradingStrategy (input: TradingParametersInputed) =
    let newStrat =
        { AcceptedStrategy =
            { TrackedCurrencies = input.TrackedCurrencies
              MinPriceSpread = input.MinPriceSpread
              MinTransactionProfit = input.MinTransactionProfit
              MaxAmountTotal = input.MaxAmountTotal
              MaxDailyVolume = input.MaxDailyVolume } }

    distributedPost (UpdateStrategy newStrat.AcceptedStrategy)
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
                            | _ -> -1
    let minPriceSpread = match r.queryParam "minpricespread" with
                            | Choice1Of2 mps -> float mps
                            | _ -> -1.0
    let minTransactionProfit = match r.queryParam "minprofit" with
                                | Choice1Of2 mp -> float mp
                                | _ -> -1.0
    let maxAmountTotal = match r.queryParam "maxamount" with
                            | Choice1Of2 ma -> float ma
                            | _ -> -1.0
    let maxDailyVol = match r.queryParam "maxdailyvol" with
                        | Choice1Of2 mdv -> float mdv
                        | _ -> -1.0
    // Invalid/absent query parameters are marked as negative values
    match (trackedCurrencies < 0 ||
            minPriceSpread < 0.0 ||
            minTransactionProfit < 0.0 ||
            maxAmountTotal < 0.0 ||
            maxDailyVol < 0.0) with
            | true ->  BAD_REQUEST "Invalid parameter(s) supplied."
            | _ ->
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
    let stratJson = JsonConvert.SerializeObject(tradingStrategyAgent.PostAndReply(GetParams))
    sendMessageAsync("tradingqueue", stratJson)
    OK "Trading strategy activated")

let stopTrading =
    request (fun r ->
    tradingStrategyAgent.Post(Deactivate)
    sendMessageAsync("tradingqueue", "stop trading")
    OK "Trading strategy deactivated"
    )
