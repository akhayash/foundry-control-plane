<#
.SYNOPSIS
    Azure AI Foundry Control Plane Demo - クリーンアップスクリプト

.DESCRIPTION
    このスクリプトはデモ環境のすべての Azure リソースを削除します。
    リソースグループ全体を削除するため、すべてのリソースが完全に削除されます。

.PARAMETER Environment
    クリーンアップする環境 (dev, staging, prod)

.PARAMETER BaseName
    リソース名のベース

.PARAMETER Force
    確認プロンプトをスキップして強制削除

.EXAMPLE
    .\cleanup.ps1 -Environment dev

.EXAMPLE
    .\cleanup.ps1 -Environment dev -Force

.NOTES
    警告: このスクリプトは取り消しできない破壊的な操作を実行します。
    実行前にすべてのデータをバックアップしてください。
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment,

    [Parameter()]
    [string]$BaseName = 'fcpdemo',

    [Parameter()]
    [switch]$Force
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

function Test-ResourceGroup {
    param([string]$ResourceGroupName)

    $exists = az group exists --name $ResourceGroupName --output tsv
    return $exists -eq 'true'
}

function Get-ResourceGroupResources {
    param([string]$ResourceGroupName)

    $resources = az resource list `
        --resource-group $ResourceGroupName `
        --output json | ConvertFrom-Json

    return $resources
}

function Confirm-Cleanup {
    param(
        [string]$Environment,
        [string]$ResourceGroupName,
        $Resources
    )

    Write-Header "クリーンアップ対象の確認"
    
    Write-Host "環境:             $Environment" -ForegroundColor Yellow
    Write-Host "リソースグループ: $ResourceGroupName" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "削除されるリソース ($($Resources.Count) 個):" -ForegroundColor Yellow
    
    foreach ($resource in $Resources) {
        Write-Host "  - $($resource.name) ($($resource.type))" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "警告: この操作は取り消しできません!" -ForegroundColor Red
    Write-Host "すべてのデータが完全に削除されます。" -ForegroundColor Red
    Write-Host ""

    if (-not $Force) {
        $confirmation = Read-Host "本当に削除しますか? 確認のため 'DELETE' と入力してください"
        if ($confirmation -ne 'DELETE') {
            Write-Host "クリーンアップがキャンセルされました。" -ForegroundColor Yellow
            exit 0
        }
    }
}

function Remove-SoftDeleteProtection {
    param([string]$ResourceGroupName)

    Write-Step "Soft Delete 保護されたリソースを確認しています..."

    # Key Vault の Soft Delete 無効化
    $keyVaults = az keyvault list `
        --resource-group $ResourceGroupName `
        --output json 2>$null | ConvertFrom-Json

    foreach ($kv in $keyVaults) {
        Write-Host "  Key Vault: $($kv.name) の Soft Delete を処理中..." -ForegroundColor Gray
        try {
            az keyvault purge --name $kv.name --no-wait 2>$null
        }
        catch {
            # 既に削除されている場合は無視
        }
    }
}

function Remove-ResourceGroup {
    param([string]$ResourceGroupName)

    Write-Step "リソースグループ '$ResourceGroupName' を削除しています..."
    
    az group delete `
        --name $ResourceGroupName `
        --yes `
        --no-wait

    Write-Host "  削除が開始されました。完了までに数分かかる場合があります。" -ForegroundColor Gray
}

function Wait-ForDeletion {
    param([string]$ResourceGroupName)

    Write-Step "削除完了を待機しています..."

    $maxWaitMinutes = 30
    $waitInterval = 30
    $elapsed = 0

    while ($elapsed -lt ($maxWaitMinutes * 60)) {
        $exists = Test-ResourceGroup -ResourceGroupName $ResourceGroupName
        
        if (-not $exists) {
            Write-Host "  リソースグループが削除されました。" -ForegroundColor Green
            return $true
        }

        Write-Host "  削除中... ($([math]::Floor($elapsed / 60)) 分経過)" -ForegroundColor Gray
        Start-Sleep -Seconds $waitInterval
        $elapsed += $waitInterval
    }

    Write-Warning "削除がタイムアウトしました。バックグラウンドで処理が継続されています。"
    return $false
}

function Cleanup-DeletedResources {
    Write-Step "削除されたリソースのパージを確認しています..."

    # Purge protection が無効な Key Vault を完全削除
    $deletedVaults = az keyvault list-deleted --output json 2>$null | ConvertFrom-Json
    
    foreach ($vault in $deletedVaults) {
        if ($vault.name -like "*$BaseName*") {
            Write-Host "  削除された Key Vault '$($vault.name)' をパージしています..." -ForegroundColor Gray
            try {
                az keyvault purge --name $vault.name --location $vault.properties.location 2>$null
            }
            catch {
                Write-Warning "Key Vault のパージに失敗しました: $($vault.name)"
            }
        }
    }

    # 削除された Cognitive Services をパージ
    try {
        $deletedAccounts = az cognitiveservices account list-deleted --output json 2>$null | ConvertFrom-Json
        
        foreach ($account in $deletedAccounts) {
            if ($account.name -like "*$BaseName*") {
                Write-Host "  削除された Cognitive Services '$($account.name)' をパージしています..." -ForegroundColor Gray
                az cognitiveservices account purge `
                    --name $account.name `
                    --resource-group $account.resourceGroup `
                    --location $account.location 2>$null
            }
        }
    }
    catch {
        # 削除されたリソースがない場合は無視
    }
}

# ===================================================================
# メイン処理
# ===================================================================

try {
    Write-Header "Azure AI Foundry Control Plane Demo - クリーンアップ"

    $resourceGroupName = "rg-$BaseName-$Environment"

    # Azure ログイン確認
    $account = az account show --output json 2>$null | ConvertFrom-Json
    if (-not $account) {
        Write-Step "Azure にログインしてください..."
        az login
    }

    # リソースグループの存在確認
    $exists = Test-ResourceGroup -ResourceGroupName $resourceGroupName
    
    if (-not $exists) {
        Write-Host "リソースグループ '$resourceGroupName' は存在しません。" -ForegroundColor Yellow
        Cleanup-DeletedResources
        exit 0
    }

    # リソース一覧取得
    $resources = Get-ResourceGroupResources -ResourceGroupName $resourceGroupName

    # 確認
    Confirm-Cleanup `
        -Environment $Environment `
        -ResourceGroupName $resourceGroupName `
        -Resources $resources

    # 削除実行
    Remove-ResourceGroup -ResourceGroupName $resourceGroupName

    # 完了待機
    $deleted = Wait-ForDeletion -ResourceGroupName $resourceGroupName

    # 削除されたリソースのパージ
    Cleanup-DeletedResources

    Write-Header "クリーンアップ完了"
    
    if ($deleted) {
        Write-Host "すべてのリソースが正常に削除されました。" -ForegroundColor Green
    }
    else {
        Write-Host "削除がバックグラウンドで継続されています。" -ForegroundColor Yellow
        Write-Host "Azure Portal でステータスを確認してください。" -ForegroundColor Yellow
    }
}
catch {
    Write-Error $_.Exception.Message
    Write-Host "`nクリーンアップに失敗しました。" -ForegroundColor Red
    exit 1
}
