<#
.SYNOPSIS
    Azure AI Foundry Control Plane Demo - デプロイスクリプト

.DESCRIPTION
    このスクリプトは Azure AI Foundry Control Plane デモ環境をデプロイします。
    Bicep テンプレートを使用してすべての Azure リソースをプロビジョニングします。

.PARAMETER Environment
    デプロイ環境 (dev, staging, prod)

.PARAMETER Location
    Azure リージョン

.PARAMETER BaseName
    リソース名のベース

.PARAMETER SkipConfirmation
    確認プロンプトをスキップ

.EXAMPLE
    .\deploy.ps1 -Environment dev -Location eastus2

.NOTES
    必要条件:
    - Azure CLI 2.67+
    - Bicep CLI 0.32+
    - 適切な Azure サブスクリプション権限
#>

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment = 'dev',

    [Parameter()]
    [string]$Location = 'eastus2',

    [Parameter()]
    [string]$BaseName = 'fcpdemo',

    [Parameter()]
    [switch]$SkipConfirmation
)

# エラーハンドリング設定
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ===================================================================
# 関数定義
# ===================================================================

function Write-Header {
    param([string]$Message)
    Write-Host "`n$('=' * 60)" -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "$('=' * 60)`n" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARNING] $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Test-Prerequisites {
    Write-Step "前提条件をチェックしています..."
    
    # Azure CLI
    try {
        $azVersion = az version --output json | ConvertFrom-Json
        Write-Host "  Azure CLI: $($azVersion.'azure-cli')" -ForegroundColor Gray
    }
    catch {
        throw "Azure CLI がインストールされていません。https://aka.ms/installazurecliwindows"
    }

    # Bicep
    try {
        $bicepVersion = az bicep version 2>&1
        Write-Host "  Bicep: $bicepVersion" -ForegroundColor Gray
    }
    catch {
        Write-Warning "Bicep をインストールしています..."
        az bicep install
    }

    # Azure ログイン確認
    $account = az account show --output json 2>$null | ConvertFrom-Json
    if (-not $account) {
        Write-Step "Azure にログインしてください..."
        az login
        $account = az account show --output json | ConvertFrom-Json
    }
    
    Write-Host "  サブスクリプション: $($account.name)" -ForegroundColor Gray
    Write-Host "  テナント: $($account.tenantId)" -ForegroundColor Gray

    return $account
}

function Confirm-Deployment {
    param(
        [string]$Environment,
        [string]$Location,
        [string]$BaseName,
        [string]$SubscriptionName
    )

    Write-Header "デプロイ設定の確認"
    Write-Host "環境:           $Environment"
    Write-Host "リージョン:     $Location"
    Write-Host "ベース名:       $BaseName"
    Write-Host "サブスクリプション: $SubscriptionName"
    Write-Host "リソースグループ:   rg-$BaseName-$Environment"
    Write-Host ""

    if (-not $SkipConfirmation) {
        $confirmation = Read-Host "デプロイを続行しますか? (y/N)"
        if ($confirmation -ne 'y' -and $confirmation -ne 'Y') {
            Write-Host "デプロイがキャンセルされました。" -ForegroundColor Yellow
            exit 0
        }
    }
}

function Deploy-Infrastructure {
    param(
        [string]$Environment,
        [string]$Location,
        [string]$BaseName
    )

    Write-Header "インフラストラクチャのデプロイ"

    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $infraDir = Join-Path $scriptDir ".." "infra"
    $templateFile = Join-Path $infraDir "main.bicep"
    $parametersFile = Join-Path $infraDir "main.bicepparam"

    # Bicep の検証
    Write-Step "Bicep テンプレートを検証しています..."
    az bicep build --file $templateFile --stdout | Out-Null

    # What-If 分析
    Write-Step "What-If 分析を実行しています..."
    $whatIfResult = az deployment sub what-if `
        --location $Location `
        --template-file $templateFile `
        --parameters $parametersFile `
        --parameters environment=$Environment location=$Location baseName=$BaseName `
        --output table

    Write-Host $whatIfResult

    # デプロイ実行
    Write-Step "デプロイを実行しています... (これには 15-30 分かかる場合があります)"
    
    $deploymentName = "fcpdemo-$Environment-$(Get-Date -Format 'yyyyMMddHHmmss')"
    
    $result = az deployment sub create `
        --name $deploymentName `
        --location $Location `
        --template-file $templateFile `
        --parameters $parametersFile `
        --parameters environment=$Environment location=$Location baseName=$BaseName `
        --output json | ConvertFrom-Json

    if ($LASTEXITCODE -ne 0) {
        throw "デプロイが失敗しました。"
    }

    return $result
}

function Show-DeploymentOutputs {
    param($DeploymentResult)

    Write-Header "デプロイ完了 - 出力情報"
    
    $outputs = $DeploymentResult.properties.outputs

    Write-Host "リソースグループ:           $($outputs.resourceGroupName.value)"
    Write-Host "AI Foundry Account:          $($outputs.aiFoundryAccountName.value)"
    Write-Host "AI Foundry Project:          $($outputs.aiFoundryProjectName.value)"
    Write-Host "Azure OpenAI Endpoint:       $($outputs.openAIEndpoint.value)"
    Write-Host "API Management Gateway:      $($outputs.apimGatewayUrl.value)"
    Write-Host "AI Search Endpoint:          $($outputs.aiSearchEndpoint.value)"
    Write-Host ""
    Write-Host "Application Insights:"
    Write-Host "  接続文字列: $($outputs.appInsightsConnectionString.value)"
}

function Show-NextSteps {
    Write-Header "次のステップ"
    
    Write-Host "1. Azure Portal で監視を確認:"
    Write-Host "   - Azure AI Foundry Portal: https://ai.azure.com"
    Write-Host "   - Application Insights: Azure Portal > Application Insights"
    Write-Host ""
    Write-Host "2. デモアプリケーションを実行:"
    Write-Host "   cd src/FoundryControlPlane"
    Write-Host "   dotnet run"
    Write-Host ""
    Write-Host "3. AI Gateway 設定をポータルで構成:"
    Write-Host "   - API Management > Products > AI Gateway > Policies"
    Write-Host "   - レート制限、セマンティックキャッシュ、コンテンツセーフティを設定"
    Write-Host ""
}

# ===================================================================
# メイン処理
# ===================================================================

try {
    Write-Header "Azure AI Foundry Control Plane Demo - デプロイ"

    # 前提条件チェック
    $account = Test-Prerequisites

    # 確認
    Confirm-Deployment `
        -Environment $Environment `
        -Location $Location `
        -BaseName $BaseName `
        -SubscriptionName $account.name

    # デプロイ実行
    $result = Deploy-Infrastructure `
        -Environment $Environment `
        -Location $Location `
        -BaseName $BaseName

    # 結果表示
    Show-DeploymentOutputs -DeploymentResult $result

    # 次のステップ
    Show-NextSteps

    Write-Host "`nデプロイが正常に完了しました!" -ForegroundColor Green
}
catch {
    Write-Error $_.Exception.Message
    Write-Host "`nデプロイに失敗しました。エラーを確認してください。" -ForegroundColor Red
    exit 1
}
