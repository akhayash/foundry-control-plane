# AI Gateway

Azure AI Foundry の AI Gateway 機能を使用して、API Management (APIM) 経由でモデルや Agent Service にアクセスする方法と、動作検証のための Python スクリプト集です。

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
│  - SDK          │     │  - Rate Limit   │     │  └─ Responses API       │
└─────────────────┘     │  - Caching      │     └─────────────────────────┘
                        │  - Metrics      │
                        └─────────────────┘
```

## 前提条件

- Azure サブスクリプション
- Azure AI Foundry プロジェクト（デプロイ済みモデルあり）
- **API Management インスタンス**

### APIM SKU の選択

| シナリオ                   | 必要な SKU          | 備考                                                                    |
| -------------------------- | ------------------- | ----------------------------------------------------------------------- |
| **Foundry から新規作成**   | Basic v2 (自動作成) | Foundry Portal から AI Gateway を追加すると Basic v2 で自動作成されます |
| **既存 APIM を接続 (BYO)** | Standard 以上       | Bring Your Own シナリオでは Standard SKU 以上が必要です                 |

## セットアップ手順

AI Gateway のセットアップには2つの方式があります：

### 方式1: Foundry Portal から AI Gateway を追加（推奨）

1. [Microsoft Foundry Portal](https://ai.azure.com) にサインイン
2. 画面左側のナビゲーションで **Operate** → **Admin console** を選択
3. **AI Gateway** タブを開く
4. **Add AI Gateway** をクリック
5. 接続する Foundry リソースを選択
6. APIM の作成方法を選択：
   - **Create new**: 新しい APIM インスタンスを作成（Basic v2 SKU）
   - **Use existing**: 既存の APIM インスタンスを使用（Standard SKU 以上が必要）
7. Gateway 名を入力し、**Add** をクリック
8. **プロジェクトを Gateway に追加**:
   - 作成した AI Gateway の名前をクリック
   - 対象プロジェクトを選択し、**Add project to gateway** をクリック
   - **Gateway status** が **Enabled** になることを確認

### 方式2: 既存の API Management から接続（BYO）

1. **Azure Portal** → **API Management サービス** を選択
2. 対象の APIM インスタンスを選択
3. 左側メニューの **Azure AI Services** → **AI Gateway** を選択
4. **接続** をクリックして Azure AI Foundry リソースを選択

### API キーの取得

1. APIM の左側メニューで **Subscriptions** を選択
2. サブスクリプションを選択し、**Show/hide keys** でキーを表示
3. **Primary key** または **Secondary key** をコピー

---

## 検証スクリプト

このフォルダには、AI Gateway 経由で各種 API をテストする Python スクリプトが含まれています。

### 含まれるスクリプト

| スクリプト                 | 説明                    | API                       |
| -------------------------- | ----------------------- | ------------------------- |
| `test_chat_completions.py` | Chat Completions テスト | Chat Completions API      |
| `test_assistants_api.py`   | Agent Service テスト    | Assistants API            |
| `test_responses_api.py`    | 新しい統合 API テスト   | **Responses API（推奨）** |

### API の選択ガイド

| ユースケース                     | 推奨 API             |
| -------------------------------- | -------------------- |
| シンプルなチャット               | Chat Completions API |
| 新規エージェント開発             | **Responses API**    |
| 既存ツール（Code Interpreter等） | Assistants API       |
| バックグラウンド処理             | Responses API        |

## クイックスタート

### 1. 依存パッケージのインストール

```bash
cd src/AIGateway
pip install -r requirements.txt
```

### 2. 設定ファイルの作成

```bash
cp .env.example .env
```

`.env` ファイルを編集して値を設定：

```env
# API Management エンドポイント
APIM_ENDPOINT=https://your-apim-name.azure-api.net

# APIM サブスクリプションキー（Azure Portal → APIM → Subscriptions）
APIM_API_KEY=your-subscription-key

# デフォルトモデル
DEFAULT_MODEL=gpt-4o
```

### 3. テスト実行

```bash
# Responses API テスト（推奨）
python test_responses_api.py

# Chat Completions テスト
python test_chat_completions.py

# Assistants API テスト
python test_assistants_api.py
```

## 使用方法

### Chat Completions API

```bash
# 基本テスト
python test_chat_completions.py

# メッセージを指定
python test_chat_completions.py --message "Azure AI Foundry とは？"

# ストリーミングモード
python test_chat_completions.py --streaming

# すべてのテスト
python test_chat_completions.py --all
```

### Responses API（推奨）

2025年3月に導入された新しい統合 API です。

```bash
# 基本テスト
python test_responses_api.py

# マルチターン会話
python test_responses_api.py --multi-turn

# バックグラウンドタスク
python test_responses_api.py --background

# すべてのテスト
python test_responses_api.py --all
```

### Assistants API

```bash
# フルワークフローテスト
python test_assistants_api.py

# 既存の Assistant 一覧
python test_assistants_api.py --list

# テスト後に Assistant を残す
python test_assistants_api.py --no-cleanup
```

---

## PowerShell / curl での動作確認

スクリプトなしで直接 API を呼び出す場合の例です。

### エンドポイント構成

```
https://<apim-name>.azure-api.net/openai/openai/deployments/<model-name>/<operation>?api-version=<version>
```

### Chat Completions（PowerShell）

```powershell
$apimEndpoint = "https://<apim-name>.azure-api.net"
$apiKey = "<your-subscription-key>"
$modelName = "gpt-4o"
$apiVersion = "2025-03-01-preview"

$chatEndpoint = "$apimEndpoint/openai/openai/deployments/$modelName/chat/completions?api-version=$apiVersion"

$body = @{
    messages = @(@{ role = "user"; content = "Hello!" })
    max_tokens = 100
} | ConvertTo-Json -Depth 5

$response = Invoke-RestMethod -Uri $chatEndpoint -Method Post `
    -Headers @{ "api-key" = $apiKey } `
    -ContentType "application/json" `
    -Body $body

Write-Host $response.choices[0].message.content
```

### Chat Completions（curl）

```bash
curl -X POST "https://<apim-name>.azure-api.net/openai/openai/deployments/gpt-4o/chat/completions?api-version=2025-03-01-preview" \
  -H "api-key: <your-subscription-key>" \
  -H "Content-Type: application/json" \
  -d '{"messages": [{"role": "user", "content": "Hello!"}], "max_tokens": 50}'
```

### Responses API（PowerShell）

```powershell
$responsesEndpoint = "$apimEndpoint/openai/openai/responses?api-version=$apiVersion"

$body = @{
    model = "gpt-4o"
    input = "Azure AI Foundry とは何ですか？"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri $responsesEndpoint -Method Post `
    -Headers @{ "api-key" = $apiKey } `
    -ContentType "application/json" `
    -Body $body

$response.output | ForEach-Object {
    if ($_.type -eq "message") {
        $_.content | ForEach-Object {
            if ($_.type -eq "output_text") { Write-Host $_.text }
        }
    }
}
```

---

## トラブルシューティング

### 401 Unauthorized

API キーが正しくありません。`.env` の `APIM_API_KEY` を確認してください。

### 404 Not Found

- モデルデプロイメント名を確認
- APIM の Base URL suffix を確認

### 429 Too Many Requests

- `retry-after` ヘッダーの値だけ待機
- APIM でレート制限ポリシーを調整

### ModuleNotFoundError

```bash
pip install -r requirements.txt
```

---

## 参考リンク

- [Configure AI Gateway in your Foundry resources](https://learn.microsoft.com/azure/ai-foundry/configuration/enable-ai-api-management-gateway-portal)
- [Azure AI Gateway Overview](https://learn.microsoft.com/azure/api-management/genai-gateway-capabilities)
- [Azure OpenAI Responses API](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/responses)
- [Azure OpenAI REST API Reference](https://learn.microsoft.com/azure/ai-services/openai/reference)
