// The only module that computes its own name, because deploy.yml deploys it standalone as a bootstrap step before `docker push`.

@description('Short alphanumeric prefix. Must match the value passed to main.bicep or the bootstrap will create a second registry.')
@minLength(3)
@maxLength(11)
param namePrefix string = 'stockp'

@description('Azure region.')
param location string = resourceGroup().location

@description('Tags applied to the registry.')
param tags object = {}

// ACR names are alphanumeric only — no hyphens — 5 to 50 characters, and globally unique.
var registryName = '${namePrefix}acr${uniqueString(resourceGroup().id)}'

resource registry 'Microsoft.ContainerRegistry/registries@2025-11-01' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
    roleAssignmentMode: 'LegacyRegistryPermissions'
  }
}

output id string = registry.id
output name string = registry.name
output loginServer string = registry.properties.loginServer
