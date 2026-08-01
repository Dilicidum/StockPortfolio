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
