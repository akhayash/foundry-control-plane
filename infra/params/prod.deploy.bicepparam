// Azure AI Foundry Control Plane - Production Environment Parameters
// ===================================================================
// deploy/main.bicep 用パラメータ（本番環境サンプル）

using '../deploy/main.bicep'

param environment = 'prod'
param location = 'eastus2'
param baseName = 'fcpprod'
param tags = {
  project: 'foundry-control-plane'
  environment: 'prod'
  managedBy: 'bicep-avm'
  createdBy: 'azure-ai-foundry'
}
