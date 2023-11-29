module DatabaseOperations

open Azure.Data.Tables
open DatabaseSchema
open DatabaseConfig

let tableClient = tableServiceClient.GetTableClient("Orders") 

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
