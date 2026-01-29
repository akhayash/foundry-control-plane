# deploy-hosted-agent.ps1
# ========================================
# Hosted Agent を Azure AI Foundry にデプロイ
# 
# 使用方法:
#   ./scripts/deploy-hosted-agent.ps1 -ResourceGroup "rg-fcpdemo-dev"
#
# 前提条件:
#   - Azure CLI がインストールされていること
#   - Docker Desktop が起動していること
#   - az login でログイン済みであること

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$ImageTag = "v1",

    [Parameter(Mandatory = $false)]
    [switch]$SkipBuild,

    [Parameter(Mandatory = $false)]
    [switch]$LocalTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Hosted Agent Deployment Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ===== 1. リソース情報取得 =====
Write-Host "[1/6] リソース情報を取得中..." -ForegroundColor Yellow

$resources = az resource list --resource-group $ResourceGroup --query "[?type=='Microsoft.ContainerRegistry/registries']" | ConvertFrom-Json
if ($resources.Count -eq 0) {
    throw "Container Registry が見つかりません。先に infra をデプロイしてください。"
}
$acrName = $resources[0].name
$acrLoginServer = az acr show --name $acrName --query loginServer -o tsv

# AI Foundry Account を取得
$aiAccounts = az resource list --resource-group $ResourceGroup --query "[?type=='Microsoft.CognitiveServices/accounts']" | ConvertFrom-Json
$accountName = ($aiAccounts | Where-Object { $_.name -like "aif*" })[0].name

# プロジェクトエンドポイントを取得
$aiProjects = az resource list --resource-group $ResourceGroup --query "[?type=='Microsoft.CognitiveServices/accounts/projects']" | ConvertFrom-Json
if ($aiProjects.Count -eq 0) {
    # プロジェクトがサブリソースの場合
    $projectEndpoint = "https://$accountName.services.ai.azure.com/api/projects/$accountName"
} else {
    $projectName = $aiProjects[0].name
    $projectEndpoint = "https://$accountName.services.ai.azure.com/api/projects/$projectName"
}

Write-Host "  ACR: $acrLoginServer" -ForegroundColor Gray
Write-Host "  Project Endpoint: $projectEndpoint" -ForegroundColor Gray
Write-Host ""

# ===== 2. ローカルテストモード =====
if ($LocalTest) {
    Write-Host "[LOCAL TEST] ローカルでエージェントを起動します..." -ForegroundColor Magenta
    
    Push-Location "$PSScriptRoot/../src/HostedAgent"
    try {
        $env:AZURE_AI_PROJECT_ENDPOINT = $projectEndpoint
        $env:MODEL_NAME = "gpt-4o"
        dotnet run
    }
    finally {
        Pop-Location
    }
    return
}

# ===== 3. Docker ビルド =====
if (-not $SkipBuild) {
    Write-Host "[2/6] Docker イメージをビルド中..." -ForegroundColor Yellow
    
    Push-Location "$PSScriptRoot/../src/HostedAgent"
    try {
        docker build -t "hosted-agent:$ImageTag" .
        if ($LASTEXITCODE -ne 0) { throw "Docker build failed" }
    }
    finally {
        Pop-Location
    }
    Write-Host "  ビルド完了" -ForegroundColor Green
} else {
    Write-Host "[2/6] ビルドをスキップ" -ForegroundColor Gray
}
Write-Host ""

# ===== 4. ACR にプッシュ =====
Write-Host "[3/6] ACR にログイン中..." -ForegroundColor Yellow
az acr login --name $acrName
if ($LASTEXITCODE -ne 0) { throw "ACR login failed" }

Write-Host "[4/6] イメージをタグ付け & プッシュ中..." -ForegroundColor Yellow
$fullImageName = "$acrLoginServer/hosted-agent:$ImageTag"
docker tag "hosted-agent:$ImageTag" $fullImageName
docker push $fullImageName
if ($LASTEXITCODE -ne 0) { throw "Docker push failed" }
Write-Host "  プッシュ完了: $fullImageName" -ForegroundColor Green
Write-Host ""

# ===== 5. Capability Host 作成 (初回のみ) =====
Write-Host "[5/6] Capability Host を確認/作成中..." -ForegroundColor Yellow
$subscriptionId = az account show --query id -o tsv
$accountName = ($aiAccounts | Where-Object { $_.name -like "aif*" })[0].name

$capHostUrl = "https://management.azure.com/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.CognitiveServices/accounts/$accountName/capabilityHosts/accountcaphost?api-version=2025-10-01-preview"

$capHostBody = @{
    properties = @{
        capabilityHostKind = "Agents"
        enablePublicHostingEnvironment = $true
    }
} | ConvertTo-Json -Compress

try {
    az rest --method put --url $capHostUrl --headers "content-type=application/json" --body $capHostBody 2>$null
    Write-Host "  Capability Host 作成/更新完了" -ForegroundColor Green
} catch {
    Write-Host "  Capability Host は既に存在または作成中" -ForegroundColor Gray
}
Write-Host ""

# ===== 6. Hosted Agent 登録 =====
Write-Host "[6/6] Hosted Agent を Foundry に登録中..." -ForegroundColor Yellow

# Python スクリプトを使用（azure-ai-projects>=2.0.0b3 が必要）
$registerScript = Join-Path $PSScriptRoot "register_hosted_agent.py"

if (Test-Path $registerScript) {
    python $registerScript create `
        --endpoint $projectEndpoint `
        --image $fullImageName `
        --name "demo-hosted-agent" `
        --model "gpt-4o-mini"
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Green
        Write-Host "デプロイ完了！" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "エージェント登録に失敗しました" -ForegroundColor Red
        Write-Host "pip install azure-ai-projects>=2.0.0b3 azure-identity を確認してください" -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "register_hosted_agent.py が見つかりません" -ForegroundColor Red
    Write-Host "手動で実行してください:" -ForegroundColor Yellow
    Write-Host "  python scripts/register_hosted_agent.py create --endpoint `"$projectEndpoint`" --image `"$fullImageName`"" -ForegroundColor Cyan
}
