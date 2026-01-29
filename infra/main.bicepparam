// Azure AI Foundry Control Plane Demo - Parameters
// ================================================
// 環境別パラメータファイル

using './main.bicep'

// Development Environment
param environment = 'dev'
param location = 'northcentralus'
param baseName = 'fcpncus'
param tags = {
  project: 'foundry-control-plane-demo'
  environment: 'dev'
  managedBy: 'bicep'
  createdBy: 'azure-ai-foundry-demo'
}
