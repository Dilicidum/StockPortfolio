// AcrPull for the user-assigned managed identity, scoped to the registry.
//
// This lives in its own module ON PURPOSE.
//
// Inside a single deployment, ARM has no way to know that the container app's image pull
// depends on this role assignment: nothing in the containerApps resource references the
// roleAssignments resource, so ARM schedules them in parallel. The container app then loses
// the race, fails to pull, and the deployment reports an image-pull failure that disappears
// on a retry. Splitting it into a module gives the container app module something concrete
// to `dependsOn`, and module completion is a real ordering barrier.
//
// Do not inline this back into main.bicep.

@description('Name of the container registry to scope the assignment to.')
param acrName string

@description('Principal (object) id of the identity that should be able to pull.')
param principalId string

// AcrPull. Verified against learn.microsoft.com/azure/role-based-access-control/built-in-roles#containers
var acrPullRoleDefinitionId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource registry 'Microsoft.ContainerRegistry/registries@2025-11-01' existing = {
  name: acrName
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  // Deterministic name: re-running the deployment updates rather than conflicts.
  name: guid(registry.id, principalId, acrPullRoleDefinitionId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleDefinitionId)
    principalId: principalId
    // Required. Without it ARM may fail the assignment while the freshly created managed
    // identity has not yet replicated into Microsoft Entra.
    principalType: 'ServicePrincipal'
  }
}

output roleAssignmentId string = acrPull.id
