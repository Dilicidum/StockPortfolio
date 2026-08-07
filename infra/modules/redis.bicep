// Microsoft.Cache/redisEnterprise is Azure Managed Redis; never "fix" it to Microsoft.Cache/redis, which is the retiring service.

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
    name: 'Balanced_B0'
  }
  properties: {
    encryption: {}
    highAvailability: 'Disabled'
    minimumTlsVersion: '1.2'
  }
}

resource database 'Microsoft.Cache/redisEnterprise/databases@2025-04-01' = {
  parent: cluster
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
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

// The key is read here, where `database` is a resource being created, so listKeys() cannot be hoisted ahead of it.
@secure()
output connectionString string = '${cluster.properties.hostName}:${database.properties.port},password=${database.listKeys().primaryKey},ssl=True,abortConnect=False'
