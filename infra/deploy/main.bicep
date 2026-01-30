// Azure AI Foundry Control Plane - Infrastructure Deployment
// ===================================================================
// リソース作成用テンプレート（AVM中心）
// - 初回デプロイまたはリソース追加/削除時に実行
// - configure/ との分離により設定変更は高速に実行可能
// ===================================================================

metadata description = 'Azure AI Foundry Control Plane - Infrastructure Deployment using Azure Verified Modules'
targetScope = 'subscription'

// ===================================================================
// Parameters
// ===================================================================

@description('Deployment environment (dev, staging, prod)')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('Primary Azure region')
param location string = 'eastus2'

@description('Base name for resources')
@minLength(3)
@maxLength(15)
param baseName string = 'fcpdemo'

@description('Tags to apply to all resources')
param tags object = {
  project: 'foundry-control-plane-demo'
  environment: environment
  managedBy: 'bicep-avm'
}

// ===================================================================
// Variables
// ===================================================================

var resourceGroupName = 'rg-${baseName}-${environment}'
var uniqueSuffix = uniqueString(subscription().subscriptionId, resourceGroupName)
var uniqueShort = take(uniqueSuffix, 4)
var hostedAgentTags = union(tags, {
  'azd-service-name': 'hosted-agent'
})

// ===================================================================
// Resource Group
// ===================================================================

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// ===================================================================
// AVM Modules - Infrastructure Dependencies
// ===================================================================

// Log Analytics Workspace (AVM res/operational-insights/workspace)
module logAnalytics 'br/public:avm/res/operational-insights/workspace:0.15.0' = {
  scope: resourceGroup
  name: 'logAnalytics-${uniqueSuffix}'
  params: {
    name: 'log-${baseName}-${environment}-${uniqueShort}'
    location: location
    tags: tags
    skuName: 'PerGB2018'
    dataRetention: 30
  }
}

// Application Insights (AVM res/insights/component)
module appInsights 'br/public:avm/res/insights/component:0.7.1' = {
  scope: resourceGroup
  name: 'appInsights-${uniqueSuffix}'
  params: {
    name: 'appi-${baseName}-${environment}-${uniqueShort}'
    location: location
    tags: tags
    workspaceResourceId: logAnalytics.outputs.resourceId
    applicationType: 'web'
  }
}

// Storage Account (AVM res/storage/storage-account)
module storage 'br/public:avm/res/storage/storage-account:0.31.0' = {
  scope: resourceGroup
  name: 'storage-${uniqueSuffix}'
  params: {
    name: toLower('st${take(baseName, 8)}${take(environment, 3)}${uniqueShort}')
    location: location
    tags: tags
    skuName: 'Standard_LRS'
    kind: 'StorageV2'
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    blobServices: {
      containers: [
        { name: 'agents' }
        { name: 'data' }
      ]
    }
  }
}

// Key Vault (AVM res/key-vault/vault)
module keyVault 'br/public:avm/res/key-vault/vault:0.13.3' = {
  scope: resourceGroup
  name: 'keyVault-${uniqueSuffix}'
  params: {
    name: 'kv${take(baseName, 8)}${take(environment, 3)}${uniqueShort}'
    location: location
    tags: tags
    sku: 'standard'
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ===================================================================
// AVM Modules - AI Foundry (CognitiveServices-based)
// ===================================================================

// AI Foundry Account & Project (AVM ptn/ai-ml/ai-foundry)
module aiFoundry 'br/public:avm/ptn/ai-ml/ai-foundry:0.6.0' = {
  scope: resourceGroup
  name: 'aiFoundry-${uniqueSuffix}'
  params: {
    baseName: take('${baseName}${environment}', 12)
    location: location
    tags: hostedAgentTags
    includeAssociatedResources: false
    keyVaultConfiguration: {
      existingResourceId: keyVault.outputs.resourceId
    }
    storageAccountConfiguration: {
      existingResourceId: storage.outputs.resourceId
    }
    aiFoundryConfiguration: {
      accountName: 'aif${take(baseName, 5)}${take(environment, 3)}${uniqueShort}'
      sku: 'S0'
      disableLocalAuth: false
      allowProjectManagement: true
      project: {
        name: 'aifp${take(baseName, 4)}${take(environment, 3)}${uniqueShort}'
        displayName: 'AI Foundry Demo Project'
        desc: 'Azure AI Foundry Project for Control Plane Demo'
      }
    }
    aiModelDeployments: [
      {
        name: 'gpt-4o'
        model: {
          format: 'OpenAI'
          name: 'gpt-4o'
          version: '2024-11-20'
        }
        sku: {
          name: 'Standard'
          capacity: 10
        }
      }
      {
        name: 'gpt-4o-mini'
        model: {
          format: 'OpenAI'
          name: 'gpt-4o-mini'
          version: '2024-07-18'
        }
        sku: {
          name: 'Standard'
          capacity: 10
        }
      }
    ]
  }
}

// ===================================================================
// AVM Modules - Supporting Services
// ===================================================================

// Content Safety (Local module)
module contentSafety '../modules/content-safety.bicep' = {
  scope: resourceGroup
  name: 'contentSafety-${uniqueSuffix}'
  params: {
    name: 'cs-${baseName}-${environment}-${uniqueShort}'
    location: location
    tags: tags
    customSubDomainName: 'cs-${baseName}-${environment}-${uniqueShort}'
  }
}

// Azure Container Registry (AVM res/container-registry/registry)
module containerRegistry 'br/public:avm/res/container-registry/registry:0.9.0' = {
  scope: resourceGroup
  name: 'acr-${uniqueSuffix}'
  params: {
    name: 'acr${take(baseName, 8)}${take(environment, 3)}${uniqueShort}'
    location: location
    tags: tags
    acrSku: 'Basic'
    acrAdminUserEnabled: true
    publicNetworkAccess: 'Enabled'
  }
}

// Azure Cache for Redis (AVM res/cache/redis)
module redis 'br/public:avm/res/cache/redis:0.16.4' = {
  scope: resourceGroup
  name: 'redis-${uniqueSuffix}'
  params: {
    name: 'redis-${baseName}-${environment}-${uniqueShort}'
    location: location
    tags: tags
    skuName: 'Basic'
    capacity: 0
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// API Management (AVM res/api-management/service)
module apim 'br/public:avm/res/api-management/service:0.14.0' = {
  scope: resourceGroup
  name: 'apim-${uniqueSuffix}'
  params: {
    name: 'apim-${baseName}-${environment}-${uniqueShort}'
    location: location
    tags: tags
    publisherEmail: 'admin@contoso.com'
    publisherName: 'AI Gateway Demo'
    sku: 'BasicV2'
    skuCapacity: 1
  }
}

// ===================================================================
// Outputs (for configure/ to reference)
// ===================================================================

@description('Resource group name')
output resourceGroupName string = resourceGroup.name

@description('AI Foundry Account name (AI Services)')
output aiFoundryAccountName string = aiFoundry.outputs.aiServicesName

@description('AI Foundry Project name')
output aiFoundryProjectName string = aiFoundry.outputs.aiProjectName

@description('Application Insights resource ID')
output appInsightsResourceId string = appInsights.outputs.resourceId

@description('Application Insights connection string')
output appInsightsConnectionString string = appInsights.outputs.connectionString

@description('Container Registry resource ID')
output containerRegistryResourceId string = containerRegistry.outputs.resourceId

@description('Container Registry login server')
output containerRegistryLoginServer string = containerRegistry.outputs.loginServer

@description('API Management name')
output apimName string = apim.outputs.name

@description('Key Vault name')
output keyVaultName string = keyVault.outputs.name

@description('Storage Account name')
output storageAccountName string = storage.outputs.name

@description('Redis name')
output redisName string = redis.outputs.name

@description('Content Safety name')
output contentSafetyName string = contentSafety.outputs.name
