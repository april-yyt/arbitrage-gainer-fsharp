module DatabaseConfig

let connectionString = "AzureStorageConnectionString" 

let tableServiceClient = Azure.Data.Tables.TableServiceClient(connectionString)
