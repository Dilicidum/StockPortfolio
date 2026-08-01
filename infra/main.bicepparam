// Parameter file for local / manual deployments and `az deployment group what-if`.
//
// Every secret is read from an environment variable at build time, so nothing sensitive is ever
// committed. Set them before running:
//
//   $env:PAGES_ORIGIN        = 'https://<owner>.github.io'
//   $env:API_IMAGE           = '<acr>.azurecr.io/stockportfolio-api:local'
//   $env:MIGRATOR_IMAGE      = '<acr>.azurecr.io/stockportfolio-migrator:local'
//   $env:PG_ADMIN_PASSWORD   = '...'
//   $env:MIGRATOR_PW         = '...'
//   $env:IDENTITY_PW         = '...'
//   $env:PORTFOLIO_PW        = '...'
//   $env:MARKETDATA_PW       = '...'
//   $env:ALERTS_PW           = '...'
//   $env:JWT_SIGNING_KEY     = '...'   # at least 32 bytes
//   $env:FINNHUB_API_KEY     = ''      # optional; empty means FakeQuoteProvider
//
//   az deployment group what-if -g <rg> -f infra/main.bicep -p infra/main.bicepparam
//
// The same passwords must be the ones db/init/01-roles.sql created on this server, or the
// service roles will not authenticate.
//
// .github/workflows/deploy.yml does NOT use this file — it passes parameters inline from GitHub
// secrets, so that the values come from one place and the workflow never depends on env-var
// resolution semantics inside a .bicepparam.

using './main.bicep'

param namePrefix = 'stockp'

// Scheme + host only. A trailing slash or a path never matches a browser Origin header.
param pagesOrigin = readEnvironmentVariable('PAGES_ORIGIN', 'https://localhost:5173')

param apiImage = readEnvironmentVariable('API_IMAGE')
param migratorImage = readEnvironmentVariable('MIGRATOR_IMAGE')

param postgresAdminLogin = 'pgadmin'
param postgresAdminPassword = readEnvironmentVariable('PG_ADMIN_PASSWORD')
param postgresDatabaseName = 'stockportfolio'
param postgresVersion = '17'

param migratorPassword = readEnvironmentVariable('MIGRATOR_PW')
param identityPassword = readEnvironmentVariable('IDENTITY_PW')
param portfolioPassword = readEnvironmentVariable('PORTFOLIO_PW')
param marketDataPassword = readEnvironmentVariable('MARKETDATA_PW')
param alertsPassword = readEnvironmentVariable('ALERTS_PW')

param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY')
param jwtIssuer = 'stockportfolio'
param jwtAudience = 'stockportfolio-spa'

// Empty is a supported, tested configuration: the app falls back to FakeQuoteProvider and logs a
// warning. Finnhub shut down its sandbox in 2022, so the demo has to work without a key.
param finnhubApiKey = readEnvironmentVariable('FINNHUB_API_KEY', '')

param tags = {
  application: 'stockportfolio'
  managedBy: 'bicep'
}
