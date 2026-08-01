// StockPortfolio — Azure infrastructure.
//
// TOPOLOGY. The API runs on Azure Container Apps. The React SPA does NOT run here: it is built
// as static assets and published to GitHub Pages. Cross-origin between the SPA and the API is
// therefore permanent and by design — it is why CORS is configured, why the SSE endpoint uses a
// single-use ticket rather than an Authorization header, and why the refresh token cannot live
// in a same-site httpOnly cookie in the Pages deployment.
//
//   GitHub Pages (SPA, static)  ──REST + SSE, cross-origin──▶  Container App (API)
//   GitHub Actions ──push image──▶ ACR ──managed-identity pull──▶ Container App + migration Job
//   Container App ──▶ Postgres Flexible B1ms · Azure Managed Redis Balanced B0
//
// Deploy with:
//   az deployment group what-if -g <rg> -f infra/main.bicep -p infra/main.bicepparam
//   az deployment group create   -g <rg> -f infra/main.bicep -p infra/main.bicepparam
//
// Always run what-if first.

targetScope = 'resourceGroup'

// ---------------------------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------------------------

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

@description('JWT signing key. Must be at least 32 bytes; the host fails fast at startup otherwise.')
@secure()
@minLength(32)
param jwtSigningKey string

@description('JWT issuer.')
param jwtIssuer string = 'stockportfolio'

@description('JWT audience.')
param jwtAudience string = 'stockportfolio-spa'

@description('Finnhub API key. Leave empty and the app runs on FakeQuoteProvider — deliberate, Finnhub killed its sandbox in 2022 so the demo must work without a key.')
@secure()
param finnhubApiKey string = ''

@description('Tags applied to every resource.')
param tags object = {
  application: 'stockportfolio'
  managedBy: 'bicep'
}

// ---------------------------------------------------------------------------------------------
// Names
// ---------------------------------------------------------------------------------------------

// uniqueString over the resource group id: stable across redeploys into the same group, distinct
// across groups and subscriptions. ACR and Postgres server names are globally unique, so this is
// not cosmetic.
var suffix = uniqueString(resourceGroup().id)

var identityName = '${namePrefix}-id-${suffix}'
var postgresName = '${namePrefix}-pg-${suffix}'
var redisName = '${namePrefix}-redis-${suffix}'
var environmentName = '${namePrefix}-env-${suffix}'
var apiAppName = '${namePrefix}-api-${suffix}'
var migrateJobName = '${namePrefix}-job-migrate-${suffix}'

// ---------------------------------------------------------------------------------------------
// Identity, registry and the AcrPull grant
// ---------------------------------------------------------------------------------------------

module uami 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    name: identityName
    location: location
    tags: tags
  }
}

// acr.bicep derives its own name from namePrefix + uniqueString(resourceGroup().id) so that the
// deploy workflow can deploy it standalone as a bootstrap step and get the same name back.
module registry 'modules/acr.bicep' = {
  name: 'acr'
  params: {
    namePrefix: namePrefix
    location: location
    tags: tags
  }
}

// Its own module purely so the container app and job can dependsOn it. See the comment at the
// top of modules/roleassignment.bicep — without this barrier the image pull races the grant.
module acrPull 'modules/roleassignment.bicep' = {
  name: 'acr-pull-grant'
  params: {
    acrName: registry.outputs.name
    principalId: uami.outputs.principalId
  }
}

// ---------------------------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------------------------

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

// Access key is read at deploy time rather than passed in. The container app module depends on
// the redis module through hostName, so the cluster exists by the time this is evaluated.
resource redisCluster 'Microsoft.Cache/redisEnterprise@2025-04-01' existing = {
  name: redisName
}

resource redisDatabase 'Microsoft.Cache/redisEnterprise/databases@2025-04-01' existing = {
  parent: redisCluster
  name: 'default'
}

// ---------------------------------------------------------------------------------------------
// Connection strings
// ---------------------------------------------------------------------------------------------

// `Maximum Pool Size=2` on EVERY string, and it is not tuning — it is a correctness constraint.
// B1ms allows 35 user connections; a different Username is a different Npgsql pool; Npgsql
// defaults to 100. 100 x 4 roles x 2 replicas = 800 requested against a budget of 35.
// 2 x 4 x 2 = 16 leaves room for the migration job and a psql session.
//
// No `SearchPath=` — two open Npgsql issues make it fail migrations with
// 42P07 relation "__EFMigrationsHistory" already exists. Each DbContext pins its own
// MigrationsHistoryTable instead.
var postgresHost = postgres.outputs.fullyQualifiedDomainName
var pgPrefix = 'Host=${postgresHost};Port=5432;Database=${postgresDatabaseName}'
var pgSuffix = 'SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=2'

var migratorConnectionString = '${pgPrefix};Username=migrator;Password=${migratorPassword};${pgSuffix}'
var identityConnectionString = '${pgPrefix};Username=identity_svc;Password=${identityPassword};${pgSuffix}'
var portfolioConnectionString = '${pgPrefix};Username=portfolio_svc;Password=${portfolioPassword};${pgSuffix}'
var marketDataConnectionString = '${pgPrefix};Username=marketdata_svc;Password=${marketDataPassword};${pgSuffix}'
var alertsConnectionString = '${pgPrefix};Username=alerts_svc;Password=${alertsPassword};${pgSuffix}'

// Trust Server Certificate=true is a deliberate simplification: Npgsql 8+ validates the chain
// under SSL Mode=Require, and while Azure's certificate is publicly trusted, the root set inside
// the aspnet base image has bitten enough people that a first deploy failing on a TLS handshake
// is not worth the purity. Tighten it (drop the flag, or ship the DigiCert root) before this
// carries anything real.

// Azure Managed Redis: TLS-only on port 10000, NOT 6379. abortConnect=false so the multiplexer
// reconnects rather than staying permanently poisoned after one startup blip.
var redisConnectionString = '${redis.outputs.hostName}:${redis.outputs.port},password=${redisDatabase.listKeys().primaryKey},ssl=True,abortConnect=False'

// ---------------------------------------------------------------------------------------------
// Compute
// ---------------------------------------------------------------------------------------------

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
    jwtSigningKey: jwtSigningKey
    jwtIssuer: jwtIssuer
    jwtAudience: jwtAudience
    finnhubApiKey: finnhubApiKey
    minReplicas: 1
    maxReplicas: 2
    tags: tags
  }
  dependsOn: [
    // Explicit: nothing in the container app references the role assignment, so without this
    // ARM starts both at once and the first image pull fails.
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

// ---------------------------------------------------------------------------------------------
// Outputs — consumed by .github/workflows/deploy.yml
// ---------------------------------------------------------------------------------------------

output acrName string = registry.outputs.name
output acrLoginServer string = registry.outputs.loginServer
output containerAppName string = api.outputs.name
output apiFqdn string = api.outputs.fqdn
output apiUrl string = api.outputs.url
output migrateJobName string = migrateJob.outputs.name
output postgresFqdn string = postgres.outputs.fullyQualifiedDomainName
output redisHostName string = redis.outputs.hostName
output managedIdentityName string = uami.outputs.name
