// Container Apps managed environment, Consumption workload profile.

@description('Name of the managed environment.')
param name string

@description('Azure region.')
param location string

@description('Tags applied to the environment.')
param tags object = {}

resource env 'Microsoft.App/managedEnvironments@2026-01-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    // NOT SET: properties.appLogsConfiguration.
    //
    // No Log Analytics workspace. Its ingestion is the least predictable line on the bill for a
    // demo, and the app already emits structured logs to stdout, readable with
    // `az containerapp logs show`. Add 'log-analytics' plus a logAnalyticsConfiguration block if
    // you want retained, queryable logs.
    //
    // The block is OMITTED rather than set to destination: 'none'. The literal string is rejected
    // at preflight with "App Logs destination 'none' not supported. Supported values:
    // 'log-analytics', 'azure-monitor' or none" -- where that trailing "or none" means the
    // property absent, not the word. The error reads like a contradiction and costs an hour if
    // taken at face value.
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }

  // NOT SET: properties.ingressConfiguration.requestIdleTimeout.
  // It defaults to 4 minutes and 4 is also the FLOOR on Consumption — raising it requires a
  // Dedicated D4+ profile with two nodes, which costs more than the rest of this stack.
  // That is why the SSE alert feed must emit a named `ping` event every 20 seconds:
  // an idle stream is killed at 4 minutes otherwise.
}

output id string = env.id
output name string = env.name
output defaultDomain string = env.properties.defaultDomain
output staticIp string = env.properties.staticIp
