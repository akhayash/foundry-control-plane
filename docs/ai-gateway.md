# AI Gateway セットアップガイド

Azure AI Foundry の AI Gateway 機能を使用して、API Management (APIM) 経由でモデルや Agent Service にアクセスする方法を説明します。

## 概要

AI Gateway は Azure API Management と Azure AI Foundry を統合し、以下の機能を提供します：

- **レート制限・スロットリング**: トークン・リクエスト単位での制御
- **セマンティックキャッシュ**: 類似クエリの応答キャッシュによるコスト削減
- **コンテンツセーフティ**: Azure AI Content Safety との統合
- **トークン消費メトリクス**: Application Insights でのモニタリング
- **ロードバランシング**: 複数のバックエンドへの負荷分散

### アーキテクチャ

```text
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────────────┐
│   Client App    │────▶│  API Management │────▶│  Azure AI Foundry       │
│                 │     │  (AI Gateway)   │     │  ├─ Chat Completions    │
│  - REST API     │     │                 │     │  ├─ Assistants API      │
│  - SDK          │     │  - Rate Limit   │     │  └─ Embeddings          │
└─────────────────┘     │  - Caching      │     └─────────────────────────┘
                        │  - Metrics      │
                        └─────────────────┘
```

## 前提条件

- Azure サブスクリプション
- Azure AI Foundry プロジェクト（デプロイ済みモデルあり）
- **API Management インスタンス（Standard SKU 以上）**
  - Basic SKU は AI Gateway 機能をサポートしていません

> **Note**: Bicep で APIM をデプロイする場合は、`infra/params/*.bicepparam` で `deployApim = true` を設定してください（デフォルトは `false`）。

## セットアップ手順

### 1. AI Gateway を API Management に接続

1. **Azure Portal** にアクセス
2. 左メニューから **API Management サービス** を選択
3. 対象の APIM インスタンスを選択
4. 左側メニューの **Azure AI Services** → **AI Gateway** を選択
5. **接続** をクリックして Azure AI Foundry リソースを選択

### 2. Foundry API を APIM にインポート

1. APIM の左側メニューで **APIs** を選択
2. **+ Add API** をクリック
3. **Azure AI Foundry** を選択（「Create from Azure resource」セクション）
4. 以下の設定を入力：

| 項目 | 設定値 | 説明 |
|------|--------|------|
| **Azure AI Foundry エンドポイント** | 接続済みのエンドポイントを選択 | 手順1で接続したリソース |
| **Display name** | `Foundry AI Models` | APIの表示名（任意） |
| **Name** | `foundry-ai-models` | API識別子（自動生成） |
| **Base URL suffix** | `openai` | `/openai/...` のパスでアクセス |
| **OpenAI or Azure AI** | お好みで選択 | クライアントの互換性設定 |

5. **Create** をクリック

> **クライアント互換性の選択について:**
>
> - **Azure OpenAI API format**: Azure SDK (`Azure.AI.OpenAI`) を使用する場合に最適
> - **Azure AI Inference API format**: Azure AI SDK の統一インターフェース向け
>
> どちらを選んでも、REST API での直接呼び出しは同様に動作します。
> デモや検証では **Azure OpenAI API format** が一般的です。

### 3. API キーの取得

1. APIM の左側メニューで **Subscriptions** を選択
2. デフォルトの **Built-in all-access subscription** または作成したサブスクリプションを選択
3. **Show/hide keys** でキーを表示
4. **Primary key** または **Secondary key** をコピー

## 動作確認

### エンドポイント構成

AI Gateway 経由の API エンドポイントは以下の形式になります：

```
https://<apim-name>.azure-api.net/<base-suffix>/openai/deployments/<model-name>/<operation>?api-version=<version>
```

**例:**
- APIM名: `aiffcpncdevzqum-gw`
- Base suffix: `openai`
- モデル: `gpt-4o`
- API Version: `2025-03-01-preview`

→ `https://aiffcpncdevzqum-gw.azure-api.net/openai/openai/deployments/gpt-4o/chat/completions?api-version=2025-03-01-preview`

### モデルの動作確認（Chat Completions）

PowerShell を使用して、gpt-4o モデルへの Chat Completions リクエストを送信します。

```powershell
# 変数設定
$apimEndpoint = "https://<apim-name>.azure-api.net"
$apiKey = "<your-subscription-key>"
$modelName = "gpt-4o"
$apiVersion = "2025-03-01-preview"

# エンドポイント構築
$chatEndpoint = "$apimEndpoint/openai/openai/deployments/$modelName/chat/completions?api-version=$apiVersion"

# リクエストボディ
$body = @{
    messages = @(
        @{ role = "user"; content = "Hello! What is Azure AI Foundry?" }
    )
    max_tokens = 100
} | ConvertTo-Json -Depth 5

# API 呼び出し
$response = Invoke-RestMethod -Uri $chatEndpoint -Method Post `
    -Headers @{ "api-key" = $apiKey } `
    -ContentType "application/json" `
    -Body $body

# 結果表示
Write-Host "Model: $($response.model)"
Write-Host "Response: $($response.choices[0].message.content)"
Write-Host "Tokens used: $($response.usage.total_tokens)"
```

**期待される出力例:**

```
Model: gpt-4o-2024-11-20
Response: Azure AI Foundry is Microsoft's comprehensive platform for building and deploying AI applications...
Tokens used: 87
```

### curl での確認

```bash
curl -X POST "https://<apim-name>.azure-api.net/openai/openai/deployments/gpt-4o/chat/completions?api-version=2025-03-01-preview" \
  -H "api-key: <your-subscription-key>" \
  -H "Content-Type: application/json" \
  -d '{
    "messages": [{"role": "user", "content": "Hello!"}],
    "max_tokens": 50
  }'
```

## Agent Service（Assistants API）の動作確認

Azure AI Agent Service（OpenAI Assistants API 互換）を AI Gateway 経由でテストします。
Assistants API は以下のワークフローで動作します：

1. **Assistant 作成** - エージェントの定義
2. **Thread 作成** - 会話セッションの開始
3. **Message 追加** - ユーザーメッセージの送信
4. **Run 作成** - エージェントの実行
5. **Run 完了待機** - ポーリングで完了を確認
6. **Messages 取得** - エージェントの応答を取得

### 完全なテストスクリプト（PowerShell）

```powershell
# ===========================================
# Agent Service 動作確認スクリプト
# ===========================================

# 変数設定
$apimEndpoint = "https://<apim-name>.azure-api.net"
$apiKey = "<your-subscription-key>"
$apiVersion = "2025-03-01-preview"
$basePath = "$apimEndpoint/openai/openai"

# ヘッダー設定
$headers = @{
    "api-key" = $apiKey
    "Content-Type" = "application/json"
}

# -----------------------------------------
# 1. Assistant 作成
# -----------------------------------------
Write-Host "1. Creating Assistant..." -ForegroundColor Cyan

$assistantBody = @{
    name = "demo-agent-service"
    model = "gpt-4o"
    instructions = "You are a helpful assistant."
} | ConvertTo-Json

$assistant = Invoke-RestMethod `
    -Uri "$basePath/assistants?api-version=$apiVersion" `
    -Method Post `
    -Headers $headers `
    -Body $assistantBody

$assistantId = $assistant.id
Write-Host "   Assistant ID: $assistantId" -ForegroundColor Green

# -----------------------------------------
# 2. Thread 作成
# -----------------------------------------
Write-Host "2. Creating Thread..." -ForegroundColor Cyan

$thread = Invoke-RestMethod `
    -Uri "$basePath/threads?api-version=$apiVersion" `
    -Method Post `
    -Headers $headers `
    -Body "{}"

$threadId = $thread.id
Write-Host "   Thread ID: $threadId" -ForegroundColor Green

# -----------------------------------------
# 3. Message 追加
# -----------------------------------------
Write-Host "3. Adding Message..." -ForegroundColor Cyan

$messageBody = @{
    role = "user"
    content = "Hello! What can you help me with?"
} | ConvertTo-Json

$message = Invoke-RestMethod `
    -Uri "$basePath/threads/$threadId/messages?api-version=$apiVersion" `
    -Method Post `
    -Headers $headers `
    -Body $messageBody

Write-Host "   Message ID: $($message.id)" -ForegroundColor Green

# -----------------------------------------
# 4. Run 作成
# -----------------------------------------
Write-Host "4. Creating Run..." -ForegroundColor Cyan

$runBody = @{
    assistant_id = $assistantId
} | ConvertTo-Json

$run = Invoke-RestMethod `
    -Uri "$basePath/threads/$threadId/runs?api-version=$apiVersion" `
    -Method Post `
    -Headers $headers `
    -Body $runBody

$runId = $run.id
Write-Host "   Run ID: $runId" -ForegroundColor Green
Write-Host "   Initial Status: $($run.status)" -ForegroundColor Yellow

# -----------------------------------------
# 5. Run 完了待機（ポーリング）
# -----------------------------------------
Write-Host "5. Waiting for Run to complete..." -ForegroundColor Cyan

$maxAttempts = 30
$attempt = 0
$status = $run.status

while ($status -notin @("completed", "failed", "cancelled", "expired") -and $attempt -lt $maxAttempts) {
    Start-Sleep -Seconds 1
    $attempt++
    
    $runStatus = Invoke-RestMethod `
        -Uri "$basePath/threads/$threadId/runs/$runId`?api-version=$apiVersion" `
        -Method Get `
        -Headers $headers
    
    $status = $runStatus.status
    Write-Host "   Attempt $attempt : Status = $status" -ForegroundColor Gray
}

if ($status -eq "completed") {
    Write-Host "   Run completed successfully!" -ForegroundColor Green
} else {
    Write-Host "   Run ended with status: $status" -ForegroundColor Red
    exit 1
}

# -----------------------------------------
# 6. Messages 取得
# -----------------------------------------
Write-Host "6. Retrieving Messages..." -ForegroundColor Cyan

$messages = Invoke-RestMethod `
    -Uri "$basePath/threads/$threadId/messages?api-version=$apiVersion" `
    -Method Get `
    -Headers $headers

Write-Host "`n=== Conversation ===" -ForegroundColor Magenta
foreach ($msg in $messages.data | Sort-Object created_at) {
    $role = $msg.role.ToUpper()
    $content = $msg.content[0].text.value
    Write-Host "[$role] $content"
}

# -----------------------------------------
# クリーンアップ（オプション）
# -----------------------------------------
Write-Host "`n7. Cleanup (Optional)..." -ForegroundColor Cyan

# Assistant 削除
# Invoke-RestMethod `
#     -Uri "$basePath/assistants/$assistantId`?api-version=$apiVersion" `
#     -Method Delete `
#     -Headers $headers

Write-Host "   Skipped (uncomment to delete resources)" -ForegroundColor Yellow

Write-Host "`n=== Test Complete ===" -ForegroundColor Green
```

**期待される出力例:**

```
1. Creating Assistant...
   Assistant ID: asst_Ylm1H3lRHbmP1OtWYErF0BjA
2. Creating Thread...
   Thread ID: thread_cBAU6i40mOCb3PABqzmjIZZI
3. Adding Message...
   Message ID: msg_Po8kxnqXzJD4SPfRsfC5vFdb
4. Creating Run...
   Run ID: run_OnkuavWhEJoNc1l6Rw9OsgGp
   Initial Status: queued
5. Waiting for Run to complete...
   Attempt 1 : Status = in_progress
   Attempt 2 : Status = completed
   Run completed successfully!
6. Retrieving Messages...

=== Conversation ===
[USER] Hello! What can you help me with?
[ASSISTANT] Hi there! I can help with answering questions, providing information, brainstorming ideas, writing assistance, coding help, and much more. What would you like assistance with?

7. Cleanup (Optional)...
   Skipped (uncomment to delete resources)

=== Test Complete ===
```

## オプション機能の設定

### トークン消費メトリクスの有効化

API Gateway でトークン使用量を Application Insights で追跡するには、追加設定が必要です：

1. **Application Insights** が APIM にリンクされていることを確認
2. **カスタムメトリクス** が有効になっていることを確認（App Insights → 使用量と見積もりコスト → カスタムメトリクス）
3. **診断エンティティ** で `metrics: true` を設定

> **Note**: この設定は REST API または ARM テンプレートで行う必要があります。詳細は [Azure AI Gateway documentation](https://learn.microsoft.com/azure/api-management/azure-ai-gateway-overview) を参照してください。

### セマンティックキャッシュの設定

類似クエリへの応答をキャッシュすることで、コストとレイテンシを削減できます：

1. **Azure Managed Redis** インスタンスを作成
2. APIM の **AI Gateway** → **Caching** でキャッシュを有効化
3. キャッシュポリシーを設定（TTL、類似度しきい値など）

### コンテンツセーフティポリシー

Azure AI Content Safety を使用して、入出力の安全性を検証：

1. **Azure AI Content Safety** リソースを作成
2. APIM の **AI Gateway** → **Content Safety** で有効化
3. カテゴリ別のしきい値を設定

## トラブルシューティング

### 401 Unauthorized

**原因**: API キーが無効または期限切れ

**解決策**:
```powershell
# APIM サブスクリプションキーを再確認
# Azure Portal → APIM → Subscriptions → Show keys
```

### 404 Not Found

**原因**: 
- モデルデプロイメント名が間違っている
- Base URL suffix が正しくない

**解決策**:
```powershell
# デプロイ済みモデルを確認
az cognitiveservices account deployment list `
  --name <ai-services-name> `
  --resource-group <rg-name> `
  -o table
```

### 429 Too Many Requests

**原因**: レート制限に到達

**解決策**:
- `retry-after` ヘッダーの値だけ待機
- APIM でレート制限ポリシーを調整
- 複数のバックエンドでロードバランシングを設定

### 500 Internal Server Error

**原因**: バックエンドサービスの問題

**解決策**:
```powershell
# APIM の診断ログを確認
# Azure Portal → APIM → Monitoring → Logs

# AI Services の正常性を確認
az cognitiveservices account show `
  --name <ai-services-name> `
  --resource-group <rg-name> `
  --query "properties.provisioningState"
```

## 参考リンク

- [Azure AI Gateway Overview](https://learn.microsoft.com/azure/api-management/azure-ai-gateway-overview)
- [Azure OpenAI REST API Reference](https://learn.microsoft.com/azure/ai-services/openai/reference)
- [Assistants API Reference](https://learn.microsoft.com/azure/ai-services/openai/assistants-reference)
- [API Management Best Practices](https://learn.microsoft.com/azure/api-management/api-management-howto-best-practices)
