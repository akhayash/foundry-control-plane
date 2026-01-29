// AI Foundry Project - Application Insights Connection
// プロジェクトに Application Insights を接続してトレースを有効化

@description('AI Foundry Account name')
param aiServicesName string

@description('AI Foundry Project name')
param projectName string

@description('Application Insights resource ID')
param applicationInsightsResourceId string

// Application Insights 接続を作成
resource appInsightsConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-04-01-preview' = {
  name: '${aiServicesName}/${projectName}/application-insights'
  properties: {
    category: 'ApplicationInsights'
    target: applicationInsightsResourceId
    authType: 'AAD'
    isSharedToAll: true
    metadata: {
      ApiType: 'Azure'
    }
  }
}

@description('Connection name')
output connectionName string = appInsightsConnection.name
