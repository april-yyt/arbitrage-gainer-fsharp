open Azure
open Azure.Data.Tables
open Azure.Identity

// --------------------------
// DB Configuration Constants
// --------------------------

let storageConnString = "DefaultEndpointsProtocol=https;AccountName=18656team6;AccountKey=qJTSPfoWo5/Qjn9qFcogdO5FWeIYs9+r+JAp+6maOe/8duiWSQQL46120SrZTMusJFi1WtKenx+e+AStHjqkTA==;EndpointSuffix=core.windows.net" // This field will later use the connection string from the Azure console.
let tableClient = TableServiceClient storageConnString
let table = tableClient.GetTableClient "Orders"

// --------------------------
// DB Schema
// --------------------------

type OrderEntity() =
    inherit TableEntity()
    member val OrderID: string = null with get, set
    member val Currency: string = null with get, set
    member val Price: double = 0.0 with get, set
    member val OrderType: string = null with get, set
    member val Quantity: int = 0 with get, set
    member val Exchange: string = null with get, set
    member val Status: string = null with get, set

// --------------------------
// DB Operations
// --------------------------

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
