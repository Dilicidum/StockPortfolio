@description('Name of the flexible server. Lowercase alphanumeric and hyphens, globally unique.')
@minLength(3)
@maxLength(63)
param name string

@description('Azure region.')
param location string

@description('Administrator login name.')
param administratorLogin string

@description('Administrator password.')
@secure()
param administratorLoginPassword string

@description('Application database name.')
param databaseName string

@description('Major PostgreSQL version.')
@allowed([
  '15'
  '16'
  '17'
])
param postgresVersion string = '17'

@description('Storage size in GB.')
param storageSizeGB int = 32

@description('Tags applied to the server.')
param tags object = {}

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: postgresVersion
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    authConfig: {
      passwordAuth: 'Enabled'
      // The migrator and the four service roles are plain PostgreSQL roles, which Entra auth would not reach.
      activeDirectoryAuth: 'Disabled'
    }
    storage: {
      storageSizeGB: storageSizeGB
      autoGrow: 'Disabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

// The 0.0.0.0-0.0.0.0 sentinel is not the whole internet: ARM reads it as "allow Azure services", which is how ACA Consumption connects.
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2025-08-01' = {
  parent: server
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2025-08-01' = {
  parent: server
  name: databaseName
  dependsOn: [
    allowAzureServices
  ]
}

output id string = server.id
output name string = server.name
output fullyQualifiedDomainName string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
