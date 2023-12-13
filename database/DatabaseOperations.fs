open Azure
open Azure.Data.Tables
open Azure.Identity

type OrderEntity() =
    inherit TableEntity()
    member val OrderID: string = null with get, set
    member val Currency: string = null with get, set
    member val Price: double = 0.0 with get, set
    member val OrderType: string = null with get, set
    member val Quantity: int = 0 with get, set
    member val Exchange: string = null with get, set
    member val Status: string = null with get, set

let namespace = "ArbitrageGainer.servicebus.windows.net"
let tableServiceClient = TableServiceClient(namespace, DefaultAzureCredential())

let tableName = "Orders"
let tableClient = tableServiceClient.GetTableClient(tableName) 

// Function to create table if it does not exist
let createTableIfNotExists () =
    if not (tableServiceClient.QueryTables(tableName).Any()) then
        tableServiceClient.CreateTable(tableName)
    else
        printfn "Table already exists"

createTableIfNotExists() 

let addOrderToDatabase (order: OrderEntity) : bool =
    let response = tableClient.AddEntity order
    response.IsSucceeded

let getOrderFromDatabase (partitionKey: string, rowKey: string) : OrderEntity option =
    let response = tableClient.GetEntity<OrderEntity>(partitionKey, rowKey)
    if response.IsSucceeded then Some(response.Value) else None

let updateOrderInDatabase (order: OrderEntity) : bool =
    let response = tableClient.UpdateEntity(order, ETag.All, TableUpdateMode.Replace)
    response.IsSucceeded

let deleteOrderFromDatabase (partitionKey: string, rowKey: string) : bool =
    let response = tableClient.DeleteEntity(partitionKey, rowKey)
    response.IsSucceeded
