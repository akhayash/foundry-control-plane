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

このプロジェクトでは **AzureCliCredential** を使用して Azure に認証します。

```bash
# 事前にAzure CLIでログイン
az login
```

> ⚠️ **重要**: `DefaultAzureCredential` は使用しません。ローカル開発では常に `AzureCliCredential` を使用してください。

### リージョン制約

> ⚠️ **Hosted Agent (Preview)** は現在 **North Central US** リージョンのみで利用可能です。
> 2026年以降、順次リージョンが拡大される予定です。
>
> そのため、このデモでは `northcentralus` にデプロイすることを推奨します。
> 他のリージョンにデプロイした場合、Hosted Agent 機能は使用できません。

## プロジェクト構成

```text
foundry-control-plane/
├── README.md                          # このファイル
├── infra/                             # Bicepインフラ定義
│   ├── main.bicep                     # メインテンプレート (AVM使用)
│   └── main.bicepparam                # パラメータファイル
├── scripts/
│   ├── deploy.ps1                     # デプロイスクリプト
│   ├── deploy-hosted-agent.ps1        # Hosted Agentデプロイスクリプト
│   └── cleanup.ps1                    # クリーンアップスクリプト
└── src/
    ├── FoundryControlPlane/           # C# CLIアプリケーション
    │   ├── FoundryControlPlane.csproj
    │   ├── Program.cs
    │   ├── Agents/
    │   │   ├── AgentServiceStrategy.cs    # Prompt Agent
    │   │   └── WorkflowAgentStrategy.cs   # Workflow Agent
    │   ├── Runners/
    │   │   ├── AgentServiceRunner.cs
    │   │   ├── WorkflowRunner.cs
    │   │   └── HostedAgentRunner.cs       # Hosted Agent実行
    │   └── Telemetry/
    │       └── TelemetryService.cs        # テレメトリ
    └── HostedAgent/                   # Hosted Agentコンテナ
        ├── HostedAgent.csproj
        ├── Program.cs                 # Hosting Adapterエントリポイント
        ├── Dockerfile                 # マルチステージビルド
        ├── agent.yaml                 # azd ai agent 定義
        └── appsettings.json
```

### Azure Verified Modules (AVM) 使用

インフラストラクチャは [Azure Verified Modules](https://azure.github.io/Azure-Verified-Modules/) の最新パターンを使用しています：

| モジュール          | AVM パス                                 | バージョン |
| ------------------- | ---------------------------------------- | ---------- |
| **AI Foundry**      | `avm/ptn/ai-ml/ai-foundry`               | 0.6.0      |
| **Storage Account** | `avm/res/storage/storage-account`        | 0.31.0     |
| **Key Vault**       | `avm/res/key-vault/vault`                | 0.13.3     |
| **App Insights**    | `avm/res/insights/component`             | 0.7.1      |
| **Log Analytics**   | `avm/res/operational-insights/workspace` | 0.15.0     |
| **API Management**  | `avm/res/api-management/service`         | 0.14.0     |
| **Redis**           | `avm/res/cache/redis`                    | 0.16.4     |
| **Content Safety**  | `avm/res/cognitive-services/account`     | 0.14.1     |

> **Note**: AVM AI Foundry パターンモジュールは最新の `Microsoft.CognitiveServices/accounts` + `/projects` アーキテクチャを使用しています（旧 ML Workspace Hub/Project ではありません）。

## クイックスタート

### 1. インフラのデプロイ

```powershell
# Azureにログイン
az login

# デプロイスクリプトを実行
./scripts/deploy.ps1 -ResourceGroupName "rg-foundry-demo" -Location "eastus2"
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
