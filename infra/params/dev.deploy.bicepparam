// Azure AI Foundry Control Plane - Development Environment Parameters
// ===================================================================
// deploy/main.bicep 用パラメータ

using '../deploy/main.bicep'

param environment = 'dev'
param location = 'northcentralus'
param baseName = 'fcpncus'
param tags = {
  project: 'foundry-control-plane-demo'
  environment: 'dev'
  managedBy: 'bicep-avm'
  createdBy: 'azure-ai-foundry-demo'
}
