// Hosted Agent RBAC Module
// AI Foundry Project の Managed Identity に必要な権限を付与
// - AcrPull: Container Registry からイメージを pull
// - Cognitive Services OpenAI User: OpenAI モデルへのアクセス

@description('AI Foundry Account name')
param aiServicesName string

@description('AI Foundry Project name')
param projectName string

@description('Container Registry resource ID')
param containerRegistryId string

// Built-in role definition IDs
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles
var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var cognitiveServicesOpenAIUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')

// 既存の AI Foundry Account を参照
resource aiServices 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: aiServicesName
}

// 既存の AI Foundry Project を参照 (プロジェクトは Account のサブリソース)
resource aiProject 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' existing = {
  parent: aiServices
  name: projectName
}

// 既存の Container Registry を参照
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: last(split(containerRegistryId, '/'))
}

// AcrPull ロールを Project MI に付与 (ACR scope)
// Hosted Agent が Container Registry からイメージを pull するために必要
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, aiProject.id, acrPullRoleDefinitionId)
  scope: containerRegistry
  properties: {
    principalId: aiProject.identity.principalId
    roleDefinitionId: acrPullRoleDefinitionId
    principalType: 'ServicePrincipal'
  }
}

// Cognitive Services OpenAI User ロールを Project MI に付与 (AI Services Account scope)
// Hosted Agent が Azure OpenAI モデルにアクセスするために必要
resource openAIUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiServices.id, aiProject.id, cognitiveServicesOpenAIUserRoleDefinitionId)
  scope: aiServices
  properties: {
    principalId: aiProject.identity.principalId
    roleDefinitionId: cognitiveServicesOpenAIUserRoleDefinitionId
    principalType: 'ServicePrincipal'
  }
}

@description('Project Managed Identity Principal ID')
output projectPrincipalId string = aiProject.identity.principalId

@description('AcrPull role assignment ID')
output acrPullRoleAssignmentId string = acrPullRoleAssignment.id

@description('OpenAI User role assignment ID')
output openAIUserRoleAssignmentId string = openAIUserRoleAssignment.id
