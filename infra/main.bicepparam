// Local and manual deploys only: deploy.yml passes its parameters inline from GitHub secrets and never reads this file.
// The passwords must be the ones db/init/01-roles.sql created on this server, or the service roles will not authenticate.

using './main.bicep'

param namePrefix = 'stockp'

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

param finnhubApiKey = readEnvironmentVariable('FINNHUB_API_KEY', '')

param tags = {
  application: 'stockportfolio'
  managedBy: 'bicep'
}
