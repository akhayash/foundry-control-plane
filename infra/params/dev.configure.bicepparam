// Azure AI Foundry Control Plane - Development Environment Parameters
// ===================================================================
// configure/main.bicep 用パラメータ

using '../configure/main.bicep'

param environment = 'dev'
param baseName = 'fcpncus'
param aiServicesNameOverride = 'aiffcpncdevpn3s'
param projectNameOverride = 'aifpfcpndevpn3s'
param enableAppInsights = false
param appInsightsApiKey = ''
