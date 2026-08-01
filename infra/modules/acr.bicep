// Azure Container Registry, Basic SKU. Images are pulled with the user-assigned managed
// identity, never with the admin user.
//
// THIS MODULE COMPUTES ITS OWN NAME. Every other module takes a fully resolved name from
// main.bicep; this one does not, because it has to be deployable on its own:
//
//   az deployment group create -g <rg> -f infra/modules/acr.bicep -p namePrefix=stockp
//
// .github/workflows/deploy.yml runs exactly that as a bootstrap step. You cannot `docker push`
// to a registry that does not exist, and the full deployment references images that only exist
// after the push — so the registry has to be created first. Since the name embeds
// uniqueString(resourceGroup().id), which only ARM can evaluate, the workflow cannot compute it
// and must read it back from this deployment's outputs.

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
    // Managed-identity pull only. The admin account is a shared static password and nothing here
    // needs it.
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
    // Pinned deliberately. Microsoft has stated that 'AbacRepositoryPermissions' will become the
    // default for new registries, and the legacy roles (AcrPull / AcrPush / AcrDelete) are NOT
    // honoured on an ABAC-enabled registry. Pinning to Legacy keeps the AcrPull assignment in
    // modules/roleassignment.bicep valid instead of silently ineffective. If this is ever flipped
    // to ABAC, that assignment must become 'Container Registry Repository Reader' plus
    // 'Container Registry Repository Catalog Lister', in the same commit.
    roleAssignmentMode: 'LegacyRegistryPermissions'
  }
}

output id string = registry.id
output name string = registry.name
output loginServer string = registry.properties.loginServer
