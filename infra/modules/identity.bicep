// One user-assigned managed identity for everything: the API container app, the
// migration job, and the ACR pull. A system-assigned identity cannot be referenced
// from another resource in the same single-pass deployment, which is why this is
// user-assigned and created first.

@description('Name of the user-assigned managed identity.')
param name string

@description('Azure region.')
param location string

@description('Tags applied to the identity.')
param tags object = {}

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: name
  location: location
  tags: tags
}

output id string = uami.id
output name string = uami.name
output principalId string = uami.properties.principalId
output clientId string = uami.properties.clientId
