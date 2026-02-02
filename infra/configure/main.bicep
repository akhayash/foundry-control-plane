// Azure AI Foundry Control Plane - Configuration
// ===================================================================
// 既存リソースへの設定適用用テンプレート（カスタムモジュール中心）
// - RBAC設定、接続設定、追加構成など
// - deploy/ でリソース作成後、または設定変更時に実行
// - 高速（1-2分）で適用可能
// ===================================================================

metadata description = 'Azure AI Foundry Control Plane - Configuration for existing resources'
targetScope = 'resourceGroup'

// ===================================================================
// Parameters
// ===================================================================

@description('Deployment environment (dev, staging, prod)')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('Base name for resources')
@minLength(3)
@maxLength(15)
param baseName string = 'fcpdemo'

// ===================================================================
// Variables (must match deploy/main.bicep naming)
// ===================================================================

var uniqueSuffix = uniqueString(subscription().subscriptionId, resourceGroup().name)
var uniqueShort = take(uniqueSuffix, 4)

// Resource names (match deploy/main.bicep)
@description('Override existing AI Foundry Account name (leave empty to use generated name)')
param aiServicesNameOverride string = ''

@description('Override existing AI Foundry Project name (leave empty to use generated name)')
param projectNameOverride string = ''

@description('Enable Application Insights connection creation')
param enableAppInsights bool = true

@description('Application Insights API key (secure). If empty, AppInsights connection will be skipped')
@secure()
param appInsightsApiKey string = ''

// Resource names (match deploy/main.bicep)
var aiServicesName = (aiServicesNameOverride != '' ? aiServicesNameOverride : 'aif${take(baseName, 5)}${take(environment, 3)}${uniqueShort}')
var projectName = (projectNameOverride != '' ? projectNameOverride : 'aifp${take(baseName, 4)}${take(environment, 3)}${uniqueShort}')
var containerRegistryName = 'acr${take(baseName, 8)}${take(environment, 3)}${uniqueShort}'
var appInsightsName = 'appi-${baseName}-${environment}-${uniqueShort}'

// ===================================================================
// Existing Resources References
// ===================================================================

resource aiServices 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: aiServicesName
}

resource aiProject 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: projectName
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

// ===================================================================
// Configuration Modules
// ===================================================================

// Hosted Agent RBAC
// Project MI に ACR Pull と OpenAI User 権限を付与
module hostedAgentRbac '../modules/hostedAgentRbac.bicep' = {
  name: 'hostedAgentRbac-${uniqueSuffix}'
  params: {
    aiServicesName: aiServicesName
    projectName: projectName
    containerRegistryId: containerRegistry.id
  }
}

// AI Foundry Project に Application Insights を接続（トレーシング用）
module aiFoundryAppInsights '../modules/aiFoundryAppInsights.bicep' = if (appInsightsApiKey != '') {
  name: 'aiFoundryAppInsights-${uniqueSuffix}'
  params: {
    aiServicesName: aiServicesName
    projectName: projectName
    applicationInsightsResourceId: appInsights.id
    appInsightsApiKey: appInsightsApiKey
  }
}

// Application Insights connection module intentionally omitted for this run

// ===================================================================
// Outputs
// ===================================================================

@description('AI Foundry Project Managed Identity Principal ID')
output aiProjectPrincipalId string = hostedAgentRbac.outputs.projectPrincipalId

@description('AcrPull role assignment ID')
output acrPullRoleAssignmentId string = hostedAgentRbac.outputs.acrPullRoleAssignmentId

@description('OpenAI User role assignment ID')
output openAIUserRoleAssignmentId string = hostedAgentRbac.outputs.openAIUserRoleAssignmentId

// Application Insights connection output intentionally omitted
