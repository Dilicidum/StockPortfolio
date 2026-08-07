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
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }

  // requestIdleTimeout is left unset: 4 minutes is both the default and the floor on Consumption, so raising it needs a Dedicated profile.
}

output id string = env.id
output name string = env.name
output defaultDomain string = env.properties.defaultDomain
output staticIp string = env.properties.staticIp
