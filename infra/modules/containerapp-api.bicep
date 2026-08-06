// The API container app. The React SPA is NOT here — it is static on GitHub Pages, which is
// why cross-origin is permanent and why the SSE endpoint uses a single-use ticket instead of
// an Authorization header.

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

@description('JWT signing key. Must be at least 32 bytes.')
@secure()
param jwtSigningKey string

@description('JWT issuer.')
param jwtIssuer string

@description('JWT audience.')
param jwtAudience string

@description('Finnhub API key. Leave empty to run on FakeQuoteProvider.')
@secure()
param finnhubApiKey string = ''

@description('Whether a signed-in user may bring their own provider key. Not a secret — a plain feature switch.')
param byokEnabled bool = true

@description('Minimum replicas. Must stay at 1 — see comment below.')
param minReplicas int = 1

@description('Maximum replicas. Must stay at 2 — see comment below.')
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
  {
    name: 'jwt-signing-key'
    value: jwtSigningKey
  }
]

// An ACA secret with an empty value is rejected, and with no Finnhub key configured the app is
// supposed to fall back to FakeQuoteProvider and log a warning. So the secret and the env var
// are both omitted entirely rather than set to ''.
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
    name: 'Jwt__SigningKey'
    secretRef: 'jwt-signing-key'
  }
  {
    name: 'Jwt__Issuer'
    value: jwtIssuer
  }
  {
    name: 'Jwt__Audience'
    value: jwtAudience
  }
  // CORS runs in ASP.NET Core (AddCors/UseCors reading Cors:Origins), NOT at the ingress.
  // See the ingress block below for why there is exactly one layer.
  {
    name: 'Cors__Origins__0'
    value: corsOrigin
  }
  // Not a secret and not a placeholder: the real value. string() because ACA env values are strings.
  {
    name: 'MarketData__Byok__Enabled'
    value: string(byokEnabled)
  }
]

// Polling and alerting. RetentionMinutes must stay ABOVE Alerts__MaxWindowMinutes: the host refuses
// to start otherwise, because a window longer than the history kept stops alerts firing in silence.
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
        // ASP.NET Core has listened on 8080 since .NET 8. It is NOT 80. Getting this wrong
        // produces a container that starts, passes nothing, and 502s at the ingress.
        targetPort: 8080
        // 'auto' is what carries the alert hub's WebSocket upgrade. There is deliberately no
        // stickySessions block: the browser is pinned to WebSockets and skips negotiation, which
        // is the documented exemption from session affinity with a Redis backplane. Let the client
        // fall back to another transport and this file becomes wrong without anything failing here.
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]

        // DELIBERATELY NOT SET: corsPolicy.
        //
        // CORS is handled in ASP.NET Core (a "spa" policy bound to Cors:Origins, injected above
        // as Cors__Origins__0). Enabling the ingress corsPolicy as well would put two layers on
        // the same response and risk emitting Access-Control-Allow-Origin twice, which browsers
        // reject outright — the request fails with a CORS error even though both layers are
        // individually correct.
        //
        // The ASP.NET Core layer wins because it is identical under docker compose, so it is
        // exercised by local runs and integration tests instead of only in production.
        // If you ever move CORS to the ingress, delete the AddCors/UseCors calls in the same
        // commit. Never both.
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

          // EXPLICIT HTTP PROBES ARE LOAD-BEARING.
          //
          // When ingress is enabled, Container Apps injects default TCP probes and never calls
          // /health/live or /health/ready. The liveness/readiness split in the host is then
          // decorative: an app with a dead database still reports healthy to the platform, and
          // an app still warming up receives traffic.
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
          // /health/live checks NOTHING and must stay that way. Container Apps restarts a
          // container that fails liveness, so pointing liveness at a Postgres or Redis check
          // turns a dependency blip into a restart loop — a degraded app becomes a down app.
          // /health/ready is where Postgres and Redis are checked: a failing readiness probe
          // pulls the replica out of rotation without killing it.
        }
      ]
      scale: {
        // minReplicas: 1 is load-bearing. Scale-to-zero stops the background quote poller, and
        // with it price ingestion and threshold alerts — the app looks alive and silently
        // serves stale data.
        minReplicas: minReplicas
        // maxReplicas: 2 is what the Postgres connection budget allows. See modules/postgres.bicep.
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                // 400, not the default 100. A held-open alert connection may count as one in-flight
                // request for its whole life, so at 100 a few dozen connected browsers would
                // scale on USER COUNT rather than on load - and maxReplicas is 2 regardless,
                // because that is what the Postgres connection budget allows.
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
