// Manual trigger: deploy.yml starts it between the infrastructure deploy and the image release, and waits for it.

@description('Name of the container apps job.')
param name string

@description('Azure region.')
param location string

@description('Resource id of the Container Apps managed environment.')
param environmentId string

@description('Resource id of the user-assigned managed identity.')
param userAssignedIdentityId string

@description('Login server of the container registry, e.g. myacr.azurecr.io.')
param acrLoginServer string

@description('Fully qualified migrator image reference to run.')
param containerImage string

@description('Postgres connection string for the migrator role.')
@secure()
param migratorConnectionString string

@description('Seconds a replica may run before it is killed.')
param replicaTimeout int = 600

@description('Tags applied to the job.')
param tags object = {}

resource job 'Microsoft.App/jobs@2026-01-01' = {
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
      triggerType: 'Manual'
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      replicaTimeout: replicaTimeout
      // No retries: a failed migration must fail the deploy loudly, not half-apply twice.
      replicaRetryLimit: 0
      registries: [
        {
          server: acrLoginServer
          identity: userAssignedIdentityId
        }
      ]
      secrets: [
        {
          name: 'pg-migrator'
          value: migratorConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__Migrator'
              secretRef: 'pg-migrator'
            }
          ]
        }
      ]
    }
  }
}

output id string = job.id
output name string = job.name
