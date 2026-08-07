// The API only. The SPA is static on GitHub Pages, so cross-origin between the two is permanent and by design.

targetScope = 'resourceGroup'

@description('Short alphanumeric prefix for every resource name. Lowercase.')
@minLength(3)
@maxLength(11)
param namePrefix string = 'stockp'

@description('Azure region. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Origin the SPA is served from, e.g. https://octocat.github.io. Scheme + host only, no path — a CORS origin with a path is never matched.')
param pagesOrigin string

@description('Fully qualified API image reference, e.g. myacr.azurecr.io/stockportfolio-api:abc1234.')
param apiImage string

@description('Fully qualified migrator image reference, e.g. myacr.azurecr.io/stockportfolio-migrator:abc1234.')
param migratorImage string

@description('PostgreSQL administrator login. Cannot be changed after creation.')
param postgresAdminLogin string = 'pgadmin'

@description('PostgreSQL administrator password.')
@secure()
param postgresAdminPassword string

@description('Application database name.')
param postgresDatabaseName string = 'stockportfolio'

@description('Major PostgreSQL version.')
@allowed([
  '15'
  '16'
  '17'
])
param postgresVersion string = '17'

@description('Password for the migrator role created by db/init/01-roles.sql.')
@secure()
param migratorPassword string

@description('Password for the identity_svc role.')
@secure()
param identityPassword string

@description('Password for the portfolio_svc role.')
@secure()
param portfolioPassword string

@description('Password for the marketdata_svc role.')
@secure()
param marketDataPassword string

@description('Password for the alerts_svc role.')
@secure()
param alertsPassword string

@description('Finnhub API key. Leave empty and the app runs on FakeQuoteProvider — deliberate, Finnhub killed its sandbox in 2022 so the demo must work without a key.')
@secure()
param finnhubApiKey string = ''

@description('Whether a signed-in user may bring their own provider key. Not a secret — a plain feature switch.')
param byokEnabled bool = true

@description('Tags applied to every resource.')
param tags object = {
  application: 'stockportfolio'
  managedBy: 'bicep'
}

// Stable across redeploys into the same group, distinct across groups: ACR and Postgres names are globally unique.
var suffix = uniqueString(resourceGroup().id)

var identityName = '${namePrefix}-id-${suffix}'
var postgresName = '${namePrefix}-pg-${suffix}'
var redisName = '${namePrefix}-redis-${suffix}'
var environmentName = '${namePrefix}-env-${suffix}'
var apiAppName = '${namePrefix}-api-${suffix}'
var migrateJobName = '${namePrefix}-job-migrate-${suffix}'

module uami 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    name: identityName
    location: location
    tags: tags
  }
}

module registry 'modules/acr.bicep' = {
  name: 'acr'
  params: {
    namePrefix: namePrefix
    location: location
    tags: tags
  }
}

module acrPull 'modules/roleassignment.bicep' = {
  name: 'acr-pull-grant'
  params: {
    acrName: registry.outputs.name
    principalId: uami.outputs.principalId
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    name: postgresName
    location: location
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    databaseName: postgresDatabaseName
    postgresVersion: postgresVersion
    tags: tags
  }
}

module redis 'modules/redis.bicep' = {
  name: 'redis'
  params: {
    name: redisName
    location: location
    tags: tags
  }
}

var postgresHost = postgres.outputs.fullyQualifiedDomainName
var pgPrefix = 'Host=${postgresHost};Port=5432;Database=${postgresDatabaseName}'
var pgSuffix = 'SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=2'

var migratorConnectionString = '${pgPrefix};Username=migrator;Password=${migratorPassword};${pgSuffix}'
var identityConnectionString = '${pgPrefix};Username=identity_svc;Password=${identityPassword};${pgSuffix}'
var portfolioConnectionString = '${pgPrefix};Username=portfolio_svc;Password=${portfolioPassword};${pgSuffix}'
var marketDataConnectionString = '${pgPrefix};Username=marketdata_svc;Password=${marketDataPassword};${pgSuffix}'
var alertsConnectionString = '${pgPrefix};Username=alerts_svc;Password=${alertsPassword};${pgSuffix}'

// Trust Server Certificate=true is a deliberate simplification; tighten it before this carries anything real.

var redisConnectionString = redis.outputs.connectionString

module containerAppEnv 'modules/containerapp-env.bicep' = {
  name: 'containerapp-env'
  params: {
    name: environmentName
    location: location
    tags: tags
  }
}

module api 'modules/containerapp-api.bicep' = {
  name: 'containerapp-api'
  params: {
    name: apiAppName
    location: location
    environmentId: containerAppEnv.outputs.id
    userAssignedIdentityId: uami.outputs.id
    acrLoginServer: registry.outputs.loginServer
    containerImage: apiImage
    corsOrigin: pagesOrigin
    identityConnectionString: identityConnectionString
    portfolioConnectionString: portfolioConnectionString
    marketDataConnectionString: marketDataConnectionString
    alertsConnectionString: alertsConnectionString
    redisConnectionString: redisConnectionString
    finnhubApiKey: finnhubApiKey
    byokEnabled: byokEnabled
    minReplicas: 1
    maxReplicas: 2
    tags: tags
  }
  dependsOn: [
    acrPull
  ]
}

module migrateJob 'modules/job-migrate.bicep' = {
  name: 'job-migrate'
  params: {
    name: migrateJobName
    location: location
    environmentId: containerAppEnv.outputs.id
    userAssignedIdentityId: uami.outputs.id
    acrLoginServer: registry.outputs.loginServer
    containerImage: migratorImage
    migratorConnectionString: migratorConnectionString
    tags: tags
  }
  dependsOn: [
    acrPull
  ]
}

// Consumed by .github/workflows/deploy.yml.
output acrName string = registry.outputs.name
output acrLoginServer string = registry.outputs.loginServer
output containerAppName string = api.outputs.name
output apiFqdn string = api.outputs.fqdn
output apiUrl string = api.outputs.url
output migrateJobName string = migrateJob.outputs.name
output postgresFqdn string = postgres.outputs.fullyQualifiedDomainName
output redisHostName string = redis.outputs.hostName
output managedIdentityName string = uami.outputs.name
