// Container Apps Job that applies EF Core migrations, connecting as the `migrator` role.
//
// Manual trigger: the deploy workflow starts it explicitly between `az deployment group create`
// and `az containerapp update`, and waits for it to succeed before the new image goes live.
//
// The API itself must NEVER call Database.Migrate() at startup. Two replicas racing the same
// migration corrupts __EFMigrationsHistory, and each module's history table lives in its own
// schema (HasDefaultSchema does not move it — efcore#24127), so the corruption is per-module
// and confusing.

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
        // Exactly one replica, exactly one completion. Parallel migrators race the history
        // table, which is the failure this whole job exists to avoid.
        parallelism: 1
        replicaCompletionCount: 1
      }
      replicaTimeout: replicaTimeout
      // No retries. A failed migration should fail the deploy loudly, not half-apply twice.
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
