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

```
┌─────────────────────────────────────────────────────────────────┐
│                  スクリプト / CLI (エージェント作成)              │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌───────────────────────────────────────────────────────────────┐
│                Azure AI Foundry Control Plane                 │
│ ┌───────────────┐ ┌───────────────┐ ┌───────────────────────┐ │
│ │   Projects    │ │  Connections  │ │     Deployments       │ │
│ │   • Agents    │ │  • OpenAI     │ │  • gpt-5.2            │ │
│ │   • Indexes   │ │  • AI Search  │ │  • gpt-5-mini         │ │
│ │   • Evals     │ │  • Storage    │ │  • text-embedding-3   │ │
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

## プロジェクト構成

```
foundry-control-plane/
├── README.md                          # このファイル
├── infra/                             # Bicepインフラ定義
│   ├── main.bicep                     # メインテンプレート
│   ├── main.bicepparam                # パラメータファイル
│   └── modules/
│       ├── foundry.bicep              # AI Foundryリソース
│       ├── apim.bicep                 # API Management (AI Gateway)
│       ├── redis.bicep                # Azure Managed Redis
│       ├── app-insights.bicep         # Application Insights
│       └── content-safety.bicep       # Content Safety
├── scripts/
│   ├── deploy.ps1                     # デプロイスクリプト
│   └── cleanup.ps1                    # クリーンアップスクリプト
└── src/
    └── FoundryControlPlane/           # C# CLIアプリケーション
        ├── FoundryControlPlane.csproj
        ├── Program.cs
        ├── Agents/
        │   ├── AgentServiceManager.cs
        │   ├── HostedAgentManager.cs
        │   ├── CustomAgentManager.cs
        │   └── WorkflowAgentManager.cs
        ├── Workflows/
        │   └── GroupChatOrchestrator.cs
        └── Services/
            └── FoundryClientFactory.cs
```

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

| リソース                    | 用途                                                     |
| --------------------------- | -------------------------------------------------------- |
| **Azure AI Foundry**        | エージェント管理の中核                                   |
| **Azure OpenAI Service**    | LLMモデル（gpt-5.2, gpt-5-mini, text-embedding-3-large） |
| **Azure AI Search**         | RAG用ベクトル検索                                        |
| **Azure Storage**           | ファイル・ドキュメント保存                               |
| **Azure Cosmos DB**         | スレッド・メッセージデータ保存                           |
| **Application Insights**    | トレーシング・監視                                       |
| **API Management**          | AI Gateway（レート制限、キャッシング）                   |
| **Azure Managed Redis**     | セマンティックキャッシング                               |
| **Azure AI Content Safety** | コンテンツフィルタリング                                 |

### リソースグループ構成

```
rg-foundry-demo
├── foundry-demo                    # AI Foundry リソース
│   └── foundry-demo-project        # Foundry プロジェクト
├── aoai-demo                       # Azure OpenAI
├── search-demo                     # AI Search
├── storage-demo                    # Storage Account
├── cosmos-demo                     # Cosmos DB
├── appi-demo                       # Application Insights
├── apim-demo                       # API Management
├── redis-demo                      # Managed Redis
└── content-safety-demo             # Content Safety
```

## 環境変数

```bash
# 必須
AZURE_FOUNDRY_ENDPOINT=https://<resource>.services.ai.azure.com/api/projects/<project>
AZURE_SUBSCRIPTION_ID=<subscription-id>
AZURE_RESOURCE_GROUP=rg-foundry-demo

# オプション（デフォルト値あり）
AZURE_OPENAI_DEPLOYMENT=gpt-5.2
AZURE_APIM_GATEWAY_URL=https://<apim>.azure-api.net
```

## 使用技術

| カテゴリ                       | 技術                       | バージョン |
| ------------------------------ | -------------------------- | ---------- |
| **言語**                       | C# / .NET                  | 10.0 LTS   |
| **エージェントフレームワーク** | Microsoft Agent Framework  | 1.0.0      |
| **Azure SDK**                  | Azure.AI.Projects          | 2.0.0      |
|                                | Azure.AI.Agents.Persistent | 1.0.0      |
|                                | Azure.Identity             | 1.14.0     |
| **監視**                       | OpenTelemetry              | 1.10.0     |
| **IaC**                        | Bicep                      | 0.32+      |

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
