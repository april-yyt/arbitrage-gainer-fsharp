# Extra Credit Task 2: Distributed Agents

## Implementation 1: FSharp Cloud Agent

In order to adapt our current MailboxProcessors to a distributed cloud environment, we made use of FSharp.CloudAgent because this solution allowed us to keep our MailboxProcessors intact.

### Steps
1. Install and import necessary modules:
    ```
    open FSharp.CloudAgent
    open FSharp.CloudAgent.Messaging
    open FSharp.CloudAgent.Connections
    ```

2. Wrap existing agent inside an initialization function:
    ```
    let distributedAgent (agentId: ActorKey) =
    MailboxProcessor.Start(fun inbox ->
        ...
    )
    ```
    Note that this is the only change required to the existing agent code.

3. Create a new ServiceBus connection (we used our existing ServiceBus). Be sure to create the new queue referred to here; this is the queue where messages will be sent to and read by the agents:
    ```
    let connStr = "Endpoint=sb://arbitragegainer.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=RX56IkxeBgdYjM6OoHXozGRw37tsUQrGk+ASbNEYcl0="
    
    let queueName = "agentqueue"
    
    let cloudConn = CloudConnection.WorkerCloudConnection(ServiceBusConnection connStr, Connections.Queue queueName)
    ```

4. Listen on the connection, and add code for CloudAgent to create a new agent when a message that needs processing is received (this is how the CloudAgents work). 
    ```
    ConnectionFactory.StartListening(cloudConn, distributedAgent >> BasicCloudAgent)
    ```
    The second part of the arguments to StartListening (`activationAgent >> BasicCloudAgent`) is how the new agents are created and piped to a new BasicCloudAgent. 

5. Reimplement message sending to be compatible with this new distributed agent:
    ```
    let distributedPost = ConnectionFactory.SendToWorkerPool cloudConn
    ```
    Essentially, this replaces the MailboxProcessor agents' `post`, and instead sends the message it takes as an input to the WorkerPool through the cloud connection. Once the message is received in the queue, a new agent will be created to process it.

6. Replace calls to `post` with `distributedPost`. 

    Example before:
    ```
    match dailyVol + tradeBookedVolume with
        | x when x >= maxVol -> // Halt trading when max volume has been reached
            tradingStrategyAgent.Post(Deactivate)
    ```
    Example after:
    ```
    match dailyVol + tradeBookedVolume with
        | x when x >= maxVol -> // Halt trading when max volume has been reached
            distributedPost Deactivate
    ```

### Implementations within our Project
1. Order Management
The order management logic is maintained within the `orderCloudAgent` function, where the agents process orders based on received messages. This function represents the core logic of the order management system, adapted for distributed processing.


### Issues
- The build ended up failing due to compatibility issues of FSharp.CloudAgent, System.ServiceModel, and .NET 6.0, which meant we could not test our solution.

### Code locations
- Order management logic: [link to orderCloudAgent](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L544)
- CloudAgent configuration and setup: [link to CloudAgent setup](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L562)



## Implementation 2: Akka.NET 

### Introduction to Akka.NET
Akka.NET is a powerful toolkit and runtime for building highly concurrent, distributed, and fault-tolerant event-driven applications on .NET & Mono. This framework offers a way to write simplified big data and distributed cloud applications, by providing a model where multiple concurrent processes can coexist and interact in a non-blocking manner. Akka.NET is especially beneficial for developing systems that require scalability both vertically (on single machines) and horizontally (across multiple machines).

### Implementation Logic
In our project, we have refactored our state management mechanism to adapt to a distributed environment by employing Akka.NET. This approach enabled us to effectively deploy our system across multiple machines, leveraging the capabilities of Akka.NET to manage concurrent processes and state in a distributed context.

### Key Components
- **OrderActor** ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L576)): This actor class, inheriting from `ReceiveActor`, is responsible for processing orders emitted within the system. It handles each order based on its ID, with particular attention to cases where the ID is unknown.
- **Actor System Configuration** ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L600-L615)): The system is configured to operate remotely with a specified hostname and port, making it suitable for deployment in a distributed environment.
- **Order Processing Workflow**: The `receiveAndProcessOrdersAkka` asynchronous workflow ([link](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L618)) listens for messages from the `orderqueue`, deserializes them, and processes them using the `OrderActor`.


### Steps

1. **Install and Import Necessary Modules**:

    ```fsharp
    open Akka.FSharp
    open Akka.Actor
    open Akka.Cluster
    open Akka.Remote
    open Akka.Configuration
    ```

2. **Initialize Actor**:
   Define an actor class, inheriting from `ReceiveActor`. This actor will be responsible for processing the orders.

    ```fsharp
    type OrderActor() = 
        inherit ReceiveActor()

        let runOrderManagement (ordersEmitted: OrderEmitted) =
            // Logic for the order management bounded context

        do
            base.Receive<OrderMessage>(fun message ->
                match message with
                | ProcessOrders ordersEmitted ->
                    runOrderManagement ordersEmitted
                | Stop ->
                    printfn "Stopping order processing actor."
            )
    ```

3. **Configure Actor System**:
   Create a configuration for the actor system specifying the hostname and port, suitable for a distributed environment.

    ```fsharp
    let config = ConfigurationFactory.ParseString("""
        akka {  
            actor {
                provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            }
            remote {
                dot-netty.tcp {
                    port = 8085
                    hostname = "localhost"
                }
            }
        }
        """)

    let system = ActorSystem.Create("OrderSystem", config)
    let orderActorRef = system.ActorOf<OrderActor>("orderActor")
    ```

4. **Implement Order Processing Workflow**:
   Create an asynchronous workflow that listens for messages from `orderqueue`, deserializes them, and processes them using the `OrderActor`.

   Before:
    ```fsharp
    type OrderMessage = 
    | ProcessOrders of OrderEmitted
    | Stop

    let orderAgentBasic = MailboxProcessor<OrderMessage>.Start(fun inbox ->
        let rec messageLoop () = async {
            let! msg = inbox.Receive()
            match msg with
            | ProcessOrders ordersEmitted ->
                runOrderManagement ordersEmitted
                return! messageLoop()
            | Stop ->
                printfn "Stopping order processing agent."
        }
        messageLoop()
    )

    let rec receiveAndProcessOrdersBasic () =
        async {
            printfn "Waiting for message from 'orderqueue'..."
            let! receivedMessageJson = async { return receiveMessageAsync "orderqueue" }
            printfn "Received message: %s" receivedMessageJson
            if not (String.IsNullOrEmpty receivedMessageJson) then
                try
                    let ordersEmitted = JsonConvert.DeserializeObject<OrderEmitted>(receivedMessageJson)
                    orderAgentBasic.Post(ProcessOrders ordersEmitted)
                with
                | ex ->
                    printfn "An exception occurred: %s" ex.Message
        }
    ```

    After:
    ```fsharp
    let rec receiveAndProcessOrdersAkka () =
    async {
        printfn "Waiting for message from 'orderqueue'..."
        let! receivedMessageJson = async { return receiveMessageAsync "orderqueue" }
        printfn "Received message: %s" receivedMessageJson
        if not (String.IsNullOrEmpty receivedMessageJson) then
            try
                let ordersEmitted = JsonConvert.DeserializeObject<OrderEmitted>(receivedMessageJson)
                orderActorRef <! ProcessOrders ordersEmitted 
            with
            | ex ->
                printfn "An exception occurred: %s" ex.Message
    }
    ```

5. **Replace Basic Mailbox Processor with Akka.NET Actor**:
   Update the existing mailbox processor logic to use the Akka.NET actor system.

    Before:
    ```fsharp
    // Using Akka.NET...
    receiveAndProcessOrdersBasic () |> Async.Start
    ```

    After:
    ```fsharp
    // Using Akka.NET...
    receiveAndProcessOrdersAkka () |> Async.Start
    ```

6. **Run the System**:
   Compile and run the project. The Akka.NET actor system will start and begin processing messages as they arrive.


### Conclusion
By employing Akka.NET, we have successfully refactored our state management to work effectively in a distributed environment. This setup allows the system to scale and manage state across multiple machines, leveraging the capabilities of Akka.NET for concurrent processing. Key highlights include:
- Refactoring state management to be handled by the `OrderActor`, which is inherently suitable for distributed and concurrent scenarios.
- Easy scalability and deployability across multiple machines, thanks to Akka.NET's remote deployment capabilities.
- Efficiency and responsiveness resulting from the asynchronous and non-blocking nature of Akka.NET actors.
- Simplification of the complexities traditionally associated with distributed systems development.


### Code Locations
- OrderActor definition: [link to OrderActor](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L576)
- Actor System Configuration: [link to configuration](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L600-L615)
- Order Processing Workflow: [link to workflow](https://github.com/yutongyaF2023/arbitragegainer/blob/main/OrderManagement.fs#L618)
