// Everything the live demo runs on, in one resource group. Deploy (or preview with what-if) with:
//   az deployment group create -g rg-teetime-demo -f infra/main.bicep -p infra/main.bicepparam
// The GitHub OIDC identity (an Entra app registration) is outside this file: see infra/README.md.

@description('Region of the app, its job and the database.')
param location string = 'swedencentral'

@description('Region of Log Analytics and Application Insights.')
param monitorLocation string = 'northeurope'

@description('Image the app and the migration job run. CI deploys a new one by commit.')
param image string

@description('Entra user who administers the SQL server (sign-in name).')
param sqlAdminLogin string

@description('Object id of that Entra user.')
param sqlAdminObjectId string

@description('Who gets the alerts by email.')
param alertEmail string

var sqlServerName = 'sql-teetime-swedencentral-89a75f'
var databaseName = 'TeeTimeBooking'
var sqlHost = '${sqlServerName}${environment().suffixes.sqlServerHostname}'
var connection = 'Server=tcp:${sqlHost},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=60'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-teetime-demo'
  location: monitorLocation
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.1') } // keeps ingestion inside the free amount
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-teetime-demo'
  location: monitorLocation
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource sql 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true // no SQL logins, no passwords
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: tenant().tenantId
      principalType: 'User'
    }
  }
}

// 0.0.0.0 is Azure's convention for "Azure services": Container Apps have no fixed outbound address.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sql
  name: databaseName
  location: location
  sku: { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause' // out of free compute: pause until next month, never bill
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

// Booking tickets are signed with a key that never leaves this vault. RBAC, not access policies: who may do what is
// a role assignment like any other. Purge protection: a deleted key stays recoverable for the retention period.
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-teetime-${uniqueString(resourceGroup().id)}' // vault names are global, 24 characters at most
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

// EC P-256 for ES256. Rotated every 90 days; old versions stay enabled, so tickets they signed still verify.
// Created once: deploying again neither replaces the key nor adds a version.
resource ticketKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'ticket-signing'
  properties: {
    kty: 'EC'
    curveName: 'P-256'
    keyOps: [ 'sign', 'verify' ]
    rotationPolicy: {
      attributes: { expiryTime: 'P1Y' }
      lifetimeActions: [
        { trigger: { timeAfterCreate: 'P90D' }, action: { type: 'rotate' } }
        { trigger: { timeBeforeExpiry: 'P30D' }, action: { type: 'notify' } }
      ]
    }
  }
}

// Every read, signature and denied request, in the same workspace as the app's logs (table AzureDiagnostics).
resource vaultAudit 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'audit-to-log-analytics'
  scope: vault
  properties: {
    workspaceId: logs.id
    logs: [ { categoryGroup: 'audit', enabled: true } ]
  }
}

// The migration job's identity: the only one that may change the schema.
resource migrator 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-teetime-migrator'
  location: location
}

resource env 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-teetime-demo'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [ { name: 'Consumption', workloadProfileType: 'Consumption' } ]
    zoneRedundant: false
    peerAuthentication: { mtls: { enabled: false } }
    peerTrafficConfiguration: { encryption: { enabled: false } }
  }
}

resource app 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'teetime-booking'
  location: location
  identity: { type: 'SystemAssigned' } // the app's database user: read and write, no schema rights
  properties: {
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        exposedPort: 0
        traffic: [ { latestRevision: true, weight: 100 } ]
      }
      maxInactiveRevisions: 100
      secrets: [ { name: 'appinsights-connection', value: appInsights.properties.ConnectionString } ]
    }
    template: {
      containers: [
        {
          name: 'teetime-booking'
          image: image
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ConnectionStrings__TeeTimes', value: connection }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection' }
            { name: 'OTEL_SERVICE_NAME', value: 'TeeTimeBooking.Api' }
            { name: 'Database__MigrateOnStartup', value: 'false' } // the migration job does it
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' } // client IP for rate limits
            { name: 'TicketSigning__VaultUri', value: vault.properties.vaultUri }
            { name: 'TicketSigning__KeyName', value: ticketKey.name }
          ]
          probes: [
            // The process only: a database outage must not restart containers.
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 30, timeoutSeconds: 5, failureThreshold: 3 }
            // No traffic until the database answers.
            { type: 'Readiness', httpGet: { path: '/health', port: 8080 }, periodSeconds: 15, timeoutSeconds: 10, failureThreshold: 3, successThreshold: 1 }
            // Migrations are not run here any more, but a paused database still takes up to a minute to wake.
            { type: 'Startup', httpGet: { path: '/health/live', port: 8080 }, initialDelaySeconds: 5, periodSeconds: 15, timeoutSeconds: 5, failureThreshold: 10 }
          ]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 1, cooldownPeriod: 300, pollingInterval: 30 } // scale to zero; one replica caps the cost
    }
  }
}

// The app may read the ticket key's public parts and sign with it, on this key only. Nothing else: the built-in
// "Key Vault Crypto User" would also let it update, back up, encrypt or decrypt with the key, and so disable its
// versions. Nobody can read the private key; the migration job gets nothing here.
resource ticketSigner 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(resourceGroup().id, 'ticket-signer')
  properties: {
    roleName: 'Ticket Signer (${resourceGroup().name})' // role names are unique per tenant
    description: 'Read a Key Vault key and its public part, and sign with it. No other key operation.'
    type: 'CustomRole'
    permissions: [
      {
        actions: []
        notActions: []
        dataActions: [
          'Microsoft.KeyVault/vaults/keys/read'
          'Microsoft.KeyVault/vaults/keys/sign/action'
        ]
        notDataActions: []
      }
    ]
    assignableScopes: [ resourceGroup().id ]
  }
}

resource appSignsTickets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(ticketKey.id, app.id, ticketSigner.id)
  scope: ticketKey
  properties: {
    roleDefinitionId: ticketSigner.id
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Run once per deployment, before the app gets the new image: `az containerapp job start`.
resource migrate 'Microsoft.App/jobs@2025-01-01' = {
  name: 'job-teetime-migrate'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${migrator.id}': {} }
  }
  properties: {
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0 // a failed migration fails the deployment; it is not retried blindly
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: image
          args: [ '--migrate' ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            // A user-assigned identity is named by its client id in the connection string.
            { name: 'ConnectionStrings__TeeTimes', value: '${connection};User Id=${migrator.properties.clientId}' }
          ]
        }
      ]
    }
  }
}

// Scale to zero is what keeps the app inside the free grant: a replica that never stops (a bot or an uptime
// pinger keeping it warm) uses the month's free vCPU-seconds in about eight days. The metric reads 0 while the
// app is scaled to zero, so a minimum above 0 over six hours means it has not stopped once in that time.
resource owner 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-teetime-owner'
  location: 'global'
  properties: {
    groupShortName: 'teetime'
    enabled: true
    emailReceivers: [ { name: 'owner', emailAddress: alertEmail, useCommonAlertSchema: true } ]
  }
}

resource alwaysOn 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-teetime-not-scaling-to-zero'
  location: 'global'
  properties: {
    description: 'The app has had a replica running for six hours without scaling to zero: something keeps it warm, and it will soon cost money.'
    severity: 2
    enabled: true
    scopes: [ app.id ]
    evaluationFrequency: 'PT1H'
    windowSize: 'PT6H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'replica-never-scaled-to-zero'
          metricNamespace: 'Microsoft.App/containerApps'
          metricName: 'Replicas'
          timeAggregation: 'Minimum'
          operator: 'GreaterThan'
          threshold: 0
        }
      ]
    }
    autoMitigate: true // resolves itself once the app scales to zero again
    actions: [ { actionGroupId: owner.id } ]
  }
}

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output migratorPrincipalName string = migrator.name
output ticketVaultName string = vault.name
