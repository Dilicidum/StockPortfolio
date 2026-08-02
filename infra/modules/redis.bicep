// Azure Managed Redis, Balanced B0.
//
// RESOURCE TYPE IS Microsoft.Cache/redisEnterprise. This is Azure Managed Redis (AMR).
// It is NOT Microsoft.Cache/redis — that is Azure Cache for Redis, which is being retired.
// Do not "fix" this to Microsoft.Cache/redis.
//
// AMR is TLS-only and listens on port 10000, not 6379. Local Redis in docker compose is
// plaintext on 6379, so the two connection strings differ in more than the hostname; the whole
// string is parameterised rather than just the host.

@description('Name of the Redis Enterprise cluster. Alphanumeric and hyphens, max 60 chars.')
@maxLength(60)
param name string

@description('Azure region.')
param location string

@description('Tags applied to the cluster.')
param tags object = {}

resource cluster 'Microsoft.Cache/redisEnterprise@2025-04-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    // Smallest Managed Redis SKU. `capacity` applies only to Enterprise/EnterpriseFlash SKUs
    // and must be omitted for Balanced.
    name: 'Balanced_B0'
  }
  properties: {
    // Empty object = Microsoft-managed key encryption. Required to be present.
    encryption: {}
    // HA off: single node, no replication. Halves the cost and this cache holds only quotes,
    // rate-limit windows and SSE tickets, all of which are rebuildable.
    highAvailability: 'Disabled'
    minimumTlsVersion: '1.2'
  }
}

resource database 'Microsoft.Cache/redisEnterprise/databases@2025-04-01' = {
  parent: cluster
  // The database must be named 'default'; AMR supports exactly one.
  name: 'default'
  properties: {
    // TLS-only.
    clientProtocol: 'Encrypted'
    // EnterpriseCluster, not OSSCluster, on purpose. OSSCluster exposes real hash slots, and
    // multi-key commands across slots (MGET over a batch of tickers, which the dashboard
    // read-through does) fail with CROSSSLOT. EnterpriseCluster presents a single logical
    // endpoint and behaves like standalone Redis to StackExchange.Redis.
    clusteringPolicy: 'EnterpriseCluster'
    evictionPolicy: 'VolatileLRU'
    port: 10000
    modules: []
  }
}

output id string = cluster.id
output name string = cluster.name
output hostName string = cluster.properties.hostName
output port int = database.properties.port
output databaseName string = database.name

// The access key is read HERE, not by main.bicep, and that placement is the whole point.
//
// main.bicep used to declare the cluster and database as `existing` and call listKeys() on them.
// An `existing` reference creates no dependency on the module that builds the resource, so ARM was
// free to evaluate the key before the cluster was queryable -- and did, failing a first deploy into
// an empty resource group with ParentResourceNotFound every time. A retry then "fixed" it, because
// by the second run the cluster existed, which is exactly the kind of bug that survives to
// production by looking like a flake.
//
// Inside this module `database` is a real resource being created, so listKeys() cannot be hoisted
// ahead of it. The ordering is structural rather than hopeful.
@secure()
output connectionString string = '${cluster.properties.hostName}:${database.properties.port},password=${database.listKeys().primaryKey},ssl=True,abortConnect=False'
