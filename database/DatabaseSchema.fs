module DatabaseSchema

open Azure
open Azure.Data.Tables

type OrderEntity() =
    inherit TableEntity()
    member val OrderID: string = null with get, set
    member val Currency: string = null with get, set
    member val Price: double = 0.0 with get, set
    member val OrderType: string = null with get, set
    member val Quantity: int = 0 with get, set
    member val Exchange: string = null with get, set
    member val Status: string = null with get, set
