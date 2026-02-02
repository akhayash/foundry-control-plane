# Azure AI Foundry Control Plane デモ

Azure AI Foundryの Control Plane を使用して、各種エージェントのライフサイクル管理を実演するデモプロジェクトです。
監視・トレーシングおよびAI Gateway機能はAzure Portal上で確認します。

## 概要

このデモでは、Azure AI Foundryで管理可能な4種類のエージェントとControl Plane機能を包括的にカバーします。

### 対象エージェント

| エージェント種類               | 説明                                                                 |
| ------------------------------ | -------------------------------------------------------------------- |
| **Azure AI Agent Service**     | Foundryのマネージドエージェントサービス（OpenAI Assistants API互換） |
| **Foundry Hosted Agent**       | Foundryプラットフォーム上でホスティングされるエージェント            |
| **カスタムエージェント**       | Microsoft Agent Frameworkで構築した独自エージェント                  |
| **ワークフロー型エージェント** | GroupChatパターンによるマルチエージェント連携                        |

### アーキテクチャ

```text
┌─────────────────────────────────────────────────────────────────┐
│                  スクリプト / CLI (エージェント作成)              │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌───────────────────────────────────────────────────────────────┐
│                Azure AI Foundry Control Plane                 │
│ ┌───────────────┐ ┌───────────────┐ ┌───────────────────────┐ │
│ │   Projects    │ │  Connections  │ │     Deployments       │ │
│ │   • Agents    │ │  • OpenAI     │ │  • gpt-4o              │ │
│ │   • Indexes   │ │  • Storage    │ │  • gpt-4o-mini         │ │
│ │   • Evals     │ │  • Redis      │ │  • text-embedding-3   │ │
│ └───────────────┘ └───────────────┘ └───────────────────────┘ │
└───────────────────────────────────────────────────────────────┘
                                │
        ┌───────────────────────┴───────────────────────┐
        ▼                                               ▼
┌─────────────────┐                           ┌─────────────────┐
│   Monitoring    │                           │   AI Gateway    │
│ (Portal UI確認) │                           │ (Portal UI確認) │
│ ───────────────│                           │ ───────────────│
│ • Tracing       │                           │ • RateLimit     │
│ • Metrics       │                           │ • SemanticCache │
│                 │                           │ • ContentSafety │
└─────────────────┘                           └─────────────────┘
        │                                               │
        ▼                                               ▼
┌─────────────────┐                           ┌─────────────────┐
│ App Insights    │                           │ API Management  │
│ Azure Monitor   │                           │ Managed Redis   │
└─────────────────┘                           │ Content Safety  │
                                              └─────────────────┘
```

## 前提条件

- Azure サブスクリプション
- Azure CLI 2.67+ (`az`)
- .NET 10.0 SDK（LTS）
- PowerShell 7.5+

### 認証設定

※ App Insights 接続には **App Insights の API キー** が必要です。安全に自動化する手順は以下を参照してください。

### App Insights 接続（自動化手順、推奨）

1. App Insights の API キーを作成します（必要な権限は `Application Insights コンポーネント → API Access` の操作権限）。

   ```powershell
   az monitor app-insights api-key create \
      --api-key configure-conn-key \
      --resource-group rg-fcpncus-dev \
      --app appi-fcpncus-dev-pn3s \
      --read-properties ReadTelemetry \
      -o json
   ```

2. 取得したキーを Key Vault に格納（Key Vault に `Set` 権限が必要）。

   ```powershell
   az keyvault secret set --vault-name <kvName> --name appinsights-conn-key --value "<API_KEY>"
   ```

3. Key Vault シークレット参照を含む JSON 形式のパラメータファイル（例: `infra/params/dev.configure.kv.params.json`）を作成してデプロイします。Key Vault の `Get` 権限がデプロイを実行する主体に必要です。

   params ファイルの例:

   ```json
   {
     "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
     "contentVersion": "1.0.0.0",
     "parameters": {
       "environment": { "value": "dev" },
       "baseName": { "value": "fcpncus" },
       "aiServicesNameOverride": { "value": "aiffcpncdevpn3s" },
       "projectNameOverride": { "value": "aifpfcpndevpn3s" },
       "appInsightsApiKey": {
         "reference": {
           "keyVault": {
             "id": "/subscriptions/<sub>/resourceGroups/rg-fcpncus-dev/providers/Microsoft.KeyVault/vaults/<kvName>",
             "secretName": "appinsights-conn-key"
           }
         }
       }
     }
   }
   ```

4. デプロイを実行（実行主体は Key Vault の `get` 権限が必要）。

   ```powershell
   az deployment group create \
      --resource-group rg-fcpncus-dev \
      --template-file infra/configure/main.bicep \
      --parameters infra/params/dev.configure.kv.params.json \
      --name configure-$(Get-Date -Format 'yyyyMMddHHmm')
   ```

### 必要な権限（まとめ）

- デプロイ実行主体（Azure CLI 実行ユーザー / Service Principal / Managed Identity）に必要な権限:
  - `Key Vault` : `get`（Key Vault シークレット参照用）
  - `Cognitive Services` : `Microsoft.CognitiveServices/accounts/projects/connections/*` の作成権限（Contributor など）
  - `Authorization` : ロール割当を行う場合は `Microsoft.Authorization/roleAssignments/*` 実行可能な権限（例: Owner/Contributor または特定のロール割当権限）
  - `Container Registry` : ACR の参照（ロール割当で acrPull を作成する場合）

- API キーの作成を CLI で行う場合、App Insights リソースに対する「API Access」操作権限が必要です。
  | モジュール | AVM パス | バージョン |
  | ------------------- | ---------------------------------------- | ---------- |
  | **AI Foundry** | `avm/ptn/ai-ml/ai-foundry` | 0.6.0 |
  | **Storage Account** | `avm/res/storage/storage-account` | 0.31.0 |
  | **Key Vault** | `avm/res/key-vault/vault` | 0.13.3 |
  | **App Insights** | `avm/res/insights/component` | 0.7.1 |
  | **Log Analytics** | `avm/res/operational-insights/workspace` | 0.15.0 |
  | **API Management** | `avm/res/api-management/service` | 0.14.0 |
  | **Redis** | `avm/res/cache/redis` | 0.16.4 |
  | **Content Safety** | `avm/res/cognitive-services/account` | 0.14.1 |

> **Note**: AVM AI Foundry パターンモジュールは最新の `Microsoft.CognitiveServices/accounts` + `/projects` アーキテクチャを使用しています（旧 ML Workspace Hub/Project ではありません）。

## クイックスタート

### 1. インフラのデプロイ

インフラは **deploy（リソース作成）** と **configure（設定適用）** に分離されています。

```powershell
# Azureにログイン
az login

# Step 1: リソース作成 (AVM使用、15-30分)
az deployment sub create \
  --location northcentralus \
  --template-file infra/deploy/main.bicep \
  --parameters infra/params/dev.deploy.bicepparam \
  --name deploy-$(Get-Date -Format 'yyyyMMddHHmm')

# Step 2: 設定適用 (カスタム、1-2分)
az deployment group create \
  --resource-group rg-fcpncus-dev \
  --template-file infra/configure/main.bicep \
  --parameters infra/params/dev.configure.bicepparam \
  --name configure-$(Get-Date -Format 'yyyyMMddHHmm')
```

**設定変更のみの場合（日常運用）:**

```powershell
# RBAC追加やApp Insights接続など設定変更時は configure だけ実行
az deployment group create \
  --resource-group rg-fcpncus-dev \
  --template-file infra/configure/main.bicep \
  --parameters infra/params/dev.configure.bicepparam
```

### 2. アプリケーションのビルド

```powershell
cd src/FoundryControlPlane
dotnet build
```

### 3. デモの実行

```powershell
# エージェント一覧を表示
dotnet run -- agent list

# Agent Serviceでエージェントを作成
dotnet run -- agent create --type agent-service --name "DemoAssistant"

# GroupChatワークフローを実行
dotnet run -- workflow run --type groupchat
```

### 4. Portal UIで確認

- **監視・トレーシング**: Azure AI Foundry Portal → Tracing
- **メトリクス**: Azure Portal → Application Insights
- **AI Gateway**: Azure Portal → API Management

## デモシナリオ

### シナリオ1: エージェントライフサイクル管理

4種類のエージェントのCRUD操作を実演します。

```powershell
# 1. Azure AI Agent Service
dotnet run -- agent create --type agent-service --name "MathTutor" --instructions "数学の問題を解決します"
dotnet run -- agent get --id <agent-id>
dotnet run -- agent update --id <agent-id> --instructions "高校数学の問題を解決します"
dotnet run -- agent delete --id <agent-id>

# 2. Hosted Agent
dotnet run -- agent create --type hosted --name "CodeReviewer"
# ローカルテスト
dotnet run -- --type hosted  # メニューから "1. ローカルテスト" 選択
# Dockerビルド & ACRプッシュ
./scripts/deploy-hosted-agent.ps1 -ResourceGroup "rg-foundry-demo"

# 3. カスタムエージェント（Microsoft Agent Framework）
dotnet run -- agent create --type custom --name "CustomAssistant"

# 4. ワークフロー型エージェント
dotnet run -- agent create --type workflow --name "ContentPipeline"
```

### シナリオ2: GroupChatワークフロー

Writer、Reviewer、Editor の3つのエージェントがGroupChatパターンで連携し、コンテンツを作成します。

```powershell
dotnet run -- workflow run --type groupchat --topic "AIエージェントの未来について"
```

**実行フロー:**

1. **Writer**: 初稿を作成
2. **Reviewer**: 初稿をレビューし、フィードバックを提供
3. **Editor**: レビューを踏まえて最終稿を作成
4. **GroupChatManager**: 次の話者を決定（最大10ターン）

### シナリオ3: 監視・トレーシング（Portal UI）

Application Insightsと連携してエージェントの実行を監視します。

**Azure AI Foundry Portal での確認手順:**

1. [Azure AI Foundry Portal](https://ai.azure.com) にアクセス
2. プロジェクトを選択
3. 左メニューから **Tracing** を選択
4. 以下の情報を確認:
   - Trace ID / 実行タイムライン
   - 各操作の入出力データ
   - レイテンシ・トークン使用量
   - エラー詳細（発生時）

**Azure Portal (Application Insights) での確認:**

1. Azure Portal → Application Insights リソース
2. **Live Metrics** でリアルタイム監視
3. **Transaction search** でトレース検索
4. **Metrics** でトークン使用量・リクエスト数を可視化

### シナリオ4: AI Gateway機能（Portal UI）

API Managementを介したAI Gateway機能をPortal UIで確認・設定します。

**Azure Portal (API Management) での確認手順:**

1. **トークンレート制限**
   - API Management → APIs → ポリシー
   - `llm-token-limit` ポリシーで分あたりトークン数を設定

2. **セマンティックキャッシング**
   - API Management → Caches → Azure Managed Redis
   - キャッシュヒット率・レスポンス時間を確認

3. **コンテンツセーフティ**
   - API Management → APIs → ポリシー
   - `llm-content-safety` ポリシーでフィルタリング設定
   - Azure AI Content Safety でブロック履歴を確認

## インフラ構成

### デプロイされるAzureリソース

| リソース                     | 用途                                                       |
| ---------------------------- | ---------------------------------------------------------- |
| **Azure AI Foundry**         | エージェント管理の中核 + AI Services（モデルデプロイ含む） |
| **Azure Storage**            | ファイル・ドキュメント保存                                 |
| **Azure Cosmos DB**          | スレッド・メッセージデータ保存                             |
| **Azure Container Registry** | Hosted Agent コンテナイメージ格納                          |
| **Application Insights**     | トレーシング・監視                                         |
| **API Management**           | AI Gateway（レート制限、キャッシング）                     |
| **Azure Managed Redis**      | セマンティックキャッシング                                 |
| **Azure AI Content Safety**  | コンテンツフィルタリング                                   |

### リソースグループ構成

```text
rg-foundry-demo
├── aif*                            # AI Foundry Account (CognitiveServices)
│   └── aifp*                       # AI Foundry Project
│       └── model deployments       # gpt-4o, gpt-4o-mini, text-embedding-3
├── st*                             # Storage Account
├── kv*                             # Key Vault
├── acr*                            # Azure Container Registry (Hosted Agent用)
├── log-*                           # Log Analytics Workspace
├── appi-*                          # Application Insights
├── apim-*                          # API Management
├── redis-*                         # Azure Cache for Redis
└── cs-*                            # Content Safety
```

## 環境変数

```bash
# 必須
AZURE_FOUNDRY_ENDPOINT=https://<resource>.services.ai.azure.com/api/projects/<project>
AZURE_SUBSCRIPTION_ID=<subscription-id>
AZURE_RESOURCE_GROUP=rg-foundry-demo

# オプション（デフォルト値あり）
AZURE_OPENAI_DEPLOYMENT=gpt-4o
AZURE_APIM_GATEWAY_URL=https://<apim>.azure-api.net
```

## 使用技術

| カテゴリ                       | 技術                           | バージョン       |
| ------------------------------ | ------------------------------ | ---------------- |
| **言語**                       | C# / .NET                      | 10.0 LTS         |
| **エージェントフレームワーク** | Microsoft Agent Framework      | 1.0.0            |
| **Azure SDK**                  | Azure.AI.Projects              | 1.2.0-beta.5     |
|                                | Azure.AI.Agents.Persistent     | 1.0.0            |
|                                | Azure.AI.AgentServer.Core      | preview (Hosted) |
|                                | Azure.Identity                 | 1.17.1           |
| **監視**                       | OpenTelemetry                  | 1.12.0           |
| **コンテナ**                   | Docker                         | -                |
| **IaC**                        | Bicep + Azure Verified Modules | 0.40+            |

## クリーンアップ

```powershell
# リソースグループごと削除
./scripts/cleanup.ps1 -ResourceGroupName "rg-foundry-demo"
```

## 参考リンク

- [Azure AI Foundry ドキュメント](https://learn.microsoft.com/azure/ai-foundry/)
- [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)
- [AI Gateway Reference Architecture](https://learn.microsoft.com/ai/playbook/technology-guidance/generative-ai/dev-starters/genai-gateway/)
- [Bicep ドキュメント](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)

## ライセンス

MIT License
