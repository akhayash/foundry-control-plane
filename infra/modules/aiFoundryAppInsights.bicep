// AI Foundry Project - Application Insights Connection
// プロジェクトに Application Insights を接続してトレースを有効化

@description('AI Foundry Account name')
param aiServicesName string

@description('AI Foundry Project name')
param projectName string

@description('Application Insights resource ID')
param applicationInsightsResourceId string

@description('Application Insights API key for connection')
@secure()
param appInsightsApiKey string

// Application Insights 接続を作成
resource appInsightsConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-04-01-preview' = {
  name: '${aiServicesName}/${projectName}/application-insights'
  properties: {
    category: 'AppInsights'
    target: applicationInsightsResourceId
    authType: 'ApiKey'
    isSharedToAll: false
    useWorkspaceManagedIdentity: false
    credentials: {
      key: appInsightsApiKey
    }
    metadata: {
      ResourceId: applicationInsightsResourceId
      displayName: last(split(applicationInsightsResourceId, '/'))
    }
  }
}

@description('Connection name')
output connectionName string = appInsightsConnection.name
