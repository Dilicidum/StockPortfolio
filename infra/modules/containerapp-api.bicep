@description('Name of the container app.')
param name string

@description('Azure region.')
param location string

@description('Resource id of the Container Apps managed environment.')
param environmentId string

@description('Resource id of the user-assigned managed identity.')
param userAssignedIdentityId string

@description('Login server of the container registry, e.g. myacr.azurecr.io.')
param acrLoginServer string

@description('Fully qualified image reference to run.')
param containerImage string

@description('Origin the SPA is served from, e.g. https://owner.github.io. Passed to ASP.NET Core, not to the ingress.')
param corsOrigin string

@description('Postgres connection string for the identity_svc role.')
@secure()
param identityConnectionString string

@description('Postgres connection string for the portfolio_svc role.')
@secure()
param portfolioConnectionString string

@description('Postgres connection string for the marketdata_svc role.')
@secure()
param marketDataConnectionString string

@description('Postgres connection string for the alerts_svc role.')
@secure()
param alertsConnectionString string

@description('StackExchange.Redis connection string for Azure Managed Redis.')
@secure()
param redisConnectionString string

@description('Finnhub API key. Leave empty to run on FakeQuoteProvider.')
@secure()
param finnhubApiKey string = ''

@description('Whether a signed-in user may bring their own provider key. Not a secret — a plain feature switch.')
param byokEnabled bool = true

@description('Minimum replicas. Must stay at 1 while a BackgroundService exists in src/.')
param minReplicas int = 1

@description('Maximum replicas. Must stay at 2: the database connection budget allows no more.')
param maxReplicas int = 2

@description('Tags applied to the container app.')
param tags object = {}

var baseSecrets = [
  {
    name: 'pg-identity'
    value: identityConnectionString
  }
  {
    name: 'pg-portfolio'
    value: portfolioConnectionString
  }
  {
    name: 'pg-marketdata'
    value: marketDataConnectionString
  }
  {
    name: 'pg-alerts'
    value: alertsConnectionString
  }
  {
    name: 'redis-connection'
    value: redisConnectionString
  }
]

// An ACA secret with an empty value is rejected, so the secret and its env var are omitted rather than set to ''.
var finnhubSecrets = empty(finnhubApiKey)
  ? []
  : [
      {
        name: 'finnhub-api-key'
        value: finnhubApiKey
      }
    ]

var baseEnv = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'ConnectionStrings__Identity'
    secretRef: 'pg-identity'
  }
  {
    name: 'ConnectionStrings__Portfolio'
    secretRef: 'pg-portfolio'
  }
  {
    name: 'ConnectionStrings__MarketData'
    secretRef: 'pg-marketdata'
  }
  {
    name: 'ConnectionStrings__Alerts'
    secretRef: 'pg-alerts'
  }
  {
    name: 'ConnectionStrings__Redis'
    secretRef: 'redis-connection'
  }
  {
    name: 'Cors__Origins__0'
    value: corsOrigin
  }
  {
    name: 'MarketData__Byok__Enabled'
    value: string(byokEnabled)
  }
]

var pollingEnv = [
  {
    name: 'MarketData__Polling__IntervalSeconds'
    value: '60'
  }
  {
    name: 'MarketData__Polling__RetentionMinutes'
    value: '75'
  }
  {
    name: 'MarketData__Polling__MaxMissedSamples'
    value: '3'
  }
  {
    name: 'MarketData__Polling__MinimumSamples'
    value: '5'
  }
  {
    name: 'Alerts__MaxWindowMinutes'
    value: '60'
  }
  {
    name: 'Alerts__CooldownMinutes'
    value: '15'
  }
  {
    name: 'Alerts__HistoryLimit'
    value: '50'
  }
]

var finnhubEnv = empty(finnhubApiKey)
  ? []
  : [
      {
        name: 'Finnhub__ApiKey'
        secretRef: 'finnhub-api-key'
      }
    ]

resource api 'Microsoft.App/containerApps@2026-01-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        // 'auto' carries the hub's WebSocket upgrade; no stickySessions block, because the browser skips negotiation.
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
        // Never add corsPolicy here: ASP.NET Core owns CORS, and two layers emit Access-Control-Allow-Origin twice, which browsers reject.
      }
      registries: [
        {
          server: acrLoginServer
          identity: userAssignedIdentityId
        }
      ]
      secrets: concat(baseSecrets, finnhubSecrets)
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat(baseEnv, pollingEnv, finnhubEnv)

          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 20
              timeoutSeconds: 3
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              timeoutSeconds: 5
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '400'
              }
            }
          }
        ]
      }
    }
  }
}

output id string = api.id
output name string = api.name
output fqdn string = api.properties.configuration.ingress.fqdn
output url string = 'https://${api.properties.configuration.ingress.fqdn}'
