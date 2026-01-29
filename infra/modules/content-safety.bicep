// Content Safety Module
// =====================================================
// AVMはlistKeys()を呼ぶためdisableLocalAuth環境で失敗するため、
// 直接リソース定義で代替する
// =====================================================

@description('Content Safety resource name')
param name string

@description('Location for the resource')
param location string

@description('Tags to apply')
param tags object = {}

@description('Custom subdomain name')
param customSubDomainName string

resource contentSafety 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  tags: tags
  kind: 'ContentSafety'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: customSubDomainName
    publicNetworkAccess: 'Enabled'
    // disableLocalAuthはAzure Policyで強制されるため指定しない
  }
}

@description('Content Safety resource name')
output name string = contentSafety.name

@description('Content Safety resource ID')
output resourceId string = contentSafety.id

@description('Content Safety endpoint')
output endpoint string = contentSafety.properties.endpoint
