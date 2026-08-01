// Azure Database for PostgreSQL Flexible Server, Burstable B1ms.
//
// Public access plus the AllowAllAzureServicesAndResourcesWithinAzureIps rule. Container Apps
// on the Consumption workload profile has no VNet of its own, so there is no subnet to
// allow-list; the 0.0.0.0 sentinel rule is the documented way to let Azure-internal callers in.
//
// CONNECTION BUDGET. B1ms allows 35 user connections. A different Username is a different
// Npgsql pool, so the app opens four pools (identity_svc, portfolio_svc, marketdata_svc,
// alerts_svc). Every connection string therefore carries `Maximum Pool Size=2`:
// 2 replicas x 4 roles x 2 = 16, leaving headroom for the migration job and psql. Npgsql's
// default of 100 would ask for 800. PgBouncer is not available on Burstable, so there is no
// escape hatch below this.

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
      // The migration job and the four service roles are plain PostgreSQL roles created by
      // db/init/01-roles.sql, not Entra principals. Entra auth would not reach them.
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
      // Burstable does not support HA, and this is a demo topology.
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

// The 0.0.0.0-0.0.0.0 sentinel is not "the whole internet": ARM interprets it as
// "allow Azure services and resources within Azure IPs". ACA Consumption egresses from
// Azure-owned addresses, so this is what lets the API and the migration job connect.
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
  // Schemas and roles are created by the migration job, not here.
  dependsOn: [
    allowAzureServices
  ]
}

output id string = server.id
output name string = server.name
output fullyQualifiedDomainName string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
