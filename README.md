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

- Azure サブスクリプション（Hosted Agent には `Microsoft.FoundryComputePreview` 機能登録が必要）
- Azure CLI 2.67+ (`az`)
- Python 3.10+（Hosted Agent 登録スクリプト用）
- PowerShell 7.5+

## クイックスタート

インフラは **deploy（リソース作成）** と **configure（設定適用）** の2段階に分離されています。

### 1. サブスクリプション設定

```powershell
az login
az account set --subscription "<your-subscription-id>"
```

### 2. Soft-deleted リソースの確認・削除（必要な場合）

```powershell
# soft-deleted リソースを確認
az cognitiveservices account list-deleted --query "[?contains(id, 'japaneast')]" -o table

# もしリソースがあれば purge（なければスキップ）
az cognitiveservices account purge --name "<resource-name>" --resource-group "rg-fcpjpe-dev" --location "japaneast"
```

### 3. インフラデプロイ

```powershell
# リソース作成 (15-30分)
az deployment sub create --location japaneast --template-file infra/deploy/main.bicep --parameters infra/params/dev.deploy.bicepparam

# デプロイ完了後、リソース名を確認（リソースグループ名は infra/params/*.bicepparam の baseName から決まります）
az resource list --resource-group <resource-group-name> --query "[?type=='Microsoft.CognitiveServices/accounts'].name" -o tsv
```

### 4. Prompt Agent のデプロイ

.NET プロジェクトを使って Prompt Agent を作成します。

```powershell
# src/AgentDemos フォルダに移動
cd src/AgentDemos

# appsettings.Development.json を作成（まだない場合）
cp appsettings.Development.json.example appsettings.Development.json

# エンドポイントを編集して設定
# ProjectEndpoint: https://<ai-services-name>.japaneast.api.azureml.ms/api/v1.0/subscriptions/<subscription-id>/resourceGroups/<resource-group-name>/providers/Microsoft.MachineLearningServices/workspaces/<project-name>

# Prompt Agent を自動作成（作成後に削除しない）
dotnet run -- --auto --type prompt --no-cleanup

cd ../..
```

**作成される Agent:**

- Name: `demo-prompt-agent-<timestamp>`
- Model: `gpt-4o`
- 機能: ユーザーの質問に丁寧に答えるアシスタント

**稼働確認:**

1. [Azure AI Foundry Portal](https://ai.azure.com) にアクセス
2. プロジェクトを選択 → **Agents** に移動
3. 作成した `demo-prompt-agent-<timestamp>` をクリック
4. **Playground** タブで動作確認
   - 例: 「こんにちは」「Azure AI Foundry とは何ですか？」などを入力してレスポンスを確認

### 5. Workflow Agent のデプロイ

YAML で定義されたワークフローエージェントと、それが参照する Sub Agent を作成します。

```powershell
# src/AgentDemos フォルダに移動
cd src/AgentDemos

# Workflow Agent を自動作成（Sub Agent + Workflow Agent）
dotnet run -- --auto --type workflow --no-cleanup

cd ../..
```

**作成される Agents:**

- Sub Agent: `demo-workflow-sub-agent-<timestamp>` (Workflow から呼び出されるプロンプトエージェント)
- Workflow Agent: `demo-workflow-agent-<timestamp>` (YAML で定義されたワークフロー。Sub Agent を順次実行)

**稼働確認:**

1. [Azure AI Foundry Portal](https://ai.azure.com) にアクセス
2. プロジェクトを選択 → **Agents** に移動
3. 作成した `demo-workflow-agent-<timestamp>` をクリック
4. **Playground** タブで動作確認
   - Workflow が Sub Agent を呼び出して処理を実行することを確認
   - 例: 「テストメッセージ」を入力してワークフローの実行を確認

### 6. Hosted Agent のデプロイ

> **Important: Hosted Agent の Managed Identity について**
>
> - **開発時（公開前）**: **Project の共通 Managed Identity** を使用
>   - すべての未公開エージェントが同じ Project Identity で Azure リソースにアクセス
>   - Bicep デプロイ時に `hostedAgentRbac` モジュールで自動設定済み:
>     - `AcrPull` (Container Registry からイメージを pull)
>     - `Cognitive Services OpenAI User` (Azure OpenAI へのアクセス)
> - **公開後**: **Agent 専用の独立した Identity** が自動作成される
>   - Agent Application リソースに紐づく専用 Identity
>   - ⚠️ **RBAC 権限は引き継がれません** - セキュリティのための意図的な設計
>   - 公開後は、Agent Identity に必要な権限を再度割り当てる必要があります
>
> **公開後の RBAC 設定方法:**
>
> 1. **Portal UI で Agent を公開**
> 2. **Agent Identity の Principal ID を取得**:
>
>    ```powershell
>    $appId = az cognitiveservices application show `
>      --name <application-name> `
>      --project-name <project-name> `
>      --account-name <ai-services-name> `
>      --resource-group <rg-name> `
>      --query "identity.principalId" -o tsv
>    ```
>
> 3. **必要なロールを割り当て**:
>
>    ```powershell
>    # ACR Pull 権限
>    az role assignment create `
>      --assignee $appId `
>      --role "AcrPull" `
>      --scope <acr-resource-id>
>
>    # Azure OpenAI アクセス権限
>    az role assignment create `
>      --assignee $appId `
>      --role "Cognitive Services OpenAI User" `
>      --scope <ai-services-resource-id>
>    ```
>
> **IaC で自動化する場合**:
>
> このリポジトリには公開後の Agent Identity 用 RBAC 自動化の Bicep は含まれていません。  
> IaC で管理したい場合は、[Foundry samples](https://github.com/microsoft-foundry/foundry-samples) の `azd` テンプレートを参考にしてください。
>
> **なぜこのような設計？**  
> 開発時の権限が自動的に本番環境に引き継がれると、過剰な権限が付与されるリスクがあります。  
> Agent ごとに必要最小限の権限（Least Privilege）を明示的に設定することで、セキュリティを強化しています。
>
> 詳細: [Agent identity concepts](https://learn.microsoft.com/azure/ai-foundry/agents/concepts/agent-identity) | [Publish agents](https://learn.microsoft.com/azure/ai-foundry/agents/how-to/publish-agent)

#### 前提条件: Capability Host の作成（初回のみ）

Hosted Agent をデプロイする前に、account-level capability host を作成する必要があります（**AI Services アカウントごとに1回のみ実行**）。

##### Capability Host の制約と再作成

> **重要**: Microsoft の現在の実装では、**Capability Host は作成後に更新（PUT）できません**。  
> 設定を変更する必要がある場合は、既存の Capability Host を **DELETE してから再作成** する必要があります。
>
> 参考: [Capability hosts concepts | Microsoft Learn](https://learn.microsoft.com/ja-jp/azure/ai-foundry/agents/concepts/capability-hosts)

**設定変更が必要な場合の手順:**

```powershell
# 1. 既存の Capability Host を削除
az rest --method delete `
  --url "https://management.azure.com/subscriptions/<subscription-id>/resourceGroups/<resource-group-name>/providers/Microsoft.CognitiveServices/accounts/<ai-services-name>/capabilityHosts/accountcaphost?api-version=2025-10-01-preview"

# 2. 削除完了を確認（NotFound になれば削除完了）
az rest --method get `
  --url "https://management.azure.com/subscriptions/<subscription-id>/resourceGroups/<resource-group-name>/providers/Microsoft.CognitiveServices/accounts/<ai-services-name>/capabilityHosts/accountcaphost?api-version=2025-10-01-preview" `
  2>&1 | Select-String "NotFound"

# 3. 新しい設定で再作成（下記の作成手順を実行）
```

##### 作成手順（簡易モード）

**簡易モード** では、Microsoft 管理のリソース（Storage, Cosmos DB, AI Search）を自動的に使用します。  
開発・テスト・デモには最適で、カスタムリソースの指定は不要です。

```powershell
# Capability Host を作成（簡易モード）
az rest --method put `
  --url "https://management.azure.com/subscriptions/<subscription-id>/resourceGroups/<resource-group-name>/providers/Microsoft.CognitiveServices/accounts/<ai-services-name>/capabilityHosts/accountcaphost?api-version=2025-10-01-preview" `
  --headers "content-type=application/json" `
  --body '{
    "properties": {
      "capabilityHostKind": "Agents",
      "enablePublicHostingEnvironment": true
    }
  }'

# プロビジョニング完了を確認（Succeeded になるまで待機）
az rest --method get `
  --url "https://management.azure.com/subscriptions/<subscription-id>/resourceGroups/<resource-group-name>/providers/Microsoft.CognitiveServices/accounts/<ai-services-name>/capabilityHosts/accountcaphost?api-version=2025-10-01-preview" `
  --query "properties.provisioningState" -o tsv
```

**期待される出力**: `Succeeded`（通常 30-60秒で完了）

##### 標準セットアップ（カスタムリソース）

本番環境やエンタープライズ要件（データ主権、コンプライアンス、VNet 統合など）が必要な場合は、  
以下のように独自の Azure リソースを指定して Capability Host を作成します：

```powershell
# 標準セットアップ例
az rest --method put `
  --url "https://management.azure.com/subscriptions/<subscription-id>/resourceGroups/<resource-group-name>/providers/Microsoft.CognitiveServices/accounts/<ai-services-name>/capabilityHosts/accountcaphost?api-version=2025-10-01-preview" `
  --headers "content-type=application/json" `
  --body '{
    "properties": {
      "capabilityHostKind": "Agents",
      "enablePublicHostingEnvironment": true,
      "threadStorageConnections": ["your-cosmos-connection"],
      "storageConnections": ["your-storage-connection"],
      "vectorStoreConnections": ["your-ai-search-connection"],
      "aiServicesConnections": ["your-openai-connection"]
    }
  }'
```

**必要なリソース:**

- **Cosmos DB**: スレッドデータの永続化
- **Storage Account**: ファイル保存
- **AI Search**: ベクトル検索
- **Azure OpenAI**: モデル接続

**どちらを選ぶべきか:**

| ユースケース         | 推奨モード       | 理由                                       |
| -------------------- | ---------------- | ------------------------------------------ |
| プロトタイプ・デモ   | 簡易モード       | 迅速なセットアップ、管理不要               |
| 本番環境（一般）     | 簡易モード       | Microsoft 管理で十分な場合が多い           |
| データ主権・規制対応 | 標準セットアップ | データ保存場所を厳密に制御                 |
| VNet 統合が必要      | 標準セットアップ | Private Endpoint で完全なネットワーク分離  |
| カスタムスケーリング | 標準セットアップ | 独自のスケーリング設定やコスト最適化が可能 |

> **Note**: このプロジェクトでは**簡易モード**を使用しています。

#### デプロイ手順

```powershell
# 1. コンテナイメージをビルド & ACR にプッシュ
cd src/HostedAgent
az acr build --registry <acr-name> --image hosted-agent:v1 .
cd ../..

# 2. Hosted Agent を登録 & 公開
python scripts/register_hosted_agent.py create \
  --endpoint "https://<ai-services-name>.services.ai.azure.com/api/projects/<project-name>" \
  --image "<acr-name>.azurecr.io/hosted-agent:v1" \
  --name "demo-hosted-agent" \
  --publish \
  --subscription-id "<subscription-id>" \
  --resource-group "<resource-group-name>"
```

> **環境変数について:**
>
> `register_hosted_agent.py` スクリプトは、Hosted Agent の実行に必要な環境変数を**自動的に設定**します：
>
> - `AZURE_AI_PROJECT_ENDPOINT`: プロジェクトエンドポイント（自動抽出）
> - `AZURE_OPENAI_ENDPOINT`: Azure OpenAI エンドポイント（自動抽出）
> - `AZURE_OPENAI_DEPLOYMENT_NAME`: モデルデプロイメント名（デフォルト: `gpt-4o-mini`）
>
> モデルを変更する場合は `--model` オプションを使用：
>
> ```powershell
> python scripts/register_hosted_agent.py create \
>   --endpoint "..." \
>   --image "..." \
>   --name "demo-hosted-agent" \
>   --model "gpt-4o" \
>   --publish \
>   --subscription-id "..." \
>   --resource-group "..."
> ```

**リソース名の確認方法:**

```powershell
# AI Services 名を確認（Capability Host 作成に必要）
az resource list --resource-group <resource-group-name> --query "[?type=='Microsoft.CognitiveServices/accounts'].name" -o tsv

# Project 名を確認
az resource list --resource-group <resource-group-name> --query "[?type=='Microsoft.MachineLearningServices/workspaces'].name" -o tsv

# ACR 名を確認
az acr list --resource-group <resource-group-name> --query "[].name" -o tsv

# サブスクリプション ID を確認
az account show --query id -o tsv
```

**デプロイ成功の確認:**

```powershell
# Hosted Agent の状態を確認
python scripts/register_hosted_agent.py list \
  --endpoint "https://<ai-services-name>.services.ai.azure.com/api/projects/<project-name>"

# デプロイメント状態を確認（Running になれば成功）
az rest --method get \
  --url "https://management.azure.com/subscriptions/<subscription-id>/resourceGroups/<resource-group-name>/providers/Microsoft.CognitiveServices/accounts/<ai-services-name>/projects/<project-name>/applications/demo-hosted-agent-app/agentdeployments/demo-hosted-agent-deployment?api-version=2025-10-01-preview" \
  --query "properties.state" -o tsv
```

**期待される出力**: `Running`

#### UI での Deploy開始と稼働確認

コマンドラインでの登録後、**Portal UI でデプロイを開始して稼働確認**を行います：

1. [Azure AI Foundry Portal](https://ai.azure.com) にアクセス
2. プロジェクトを選択 → **Agents** に移動
3. **`demo-hosted-agent`** を選択
4. **Versions** タブでバージョンを確認
   - 最新バージョン（環境変数が正しく設定されたバージョン）を確認
5. **Applications** タブ → `demo-hosted-agent-app` を選択
6. **Deployments** セクションで `demo-hosted-agent-deployment` を選択
7. **Update deployment** をクリックし、最新バージョンを選択して更新
   - または、新しいデプロイメントを作成（`+ New deployment`）
8. デプロイメントの状態が **Running** になるまで待機（数分かかる場合あり）
9. **Playground** タブに移動して**動作確認:**
   - 例: 「こんにちは」「今日の天気は？」などを入力してレスポンスを確認
   - Hosted Agent が正常に応答することを確認

> **Note:**
>
> - `--publish` オプションでデプロイメントが作成されますが、既存デプロイメントの更新は Portal UI で行う必要があります
> - 初回起動には数分かかる場合があります。状態が `Starting` の場合は、`Running` になるまで待機してください
> - コンテナログに問題がある場合は、トラブルシューティングセクションを参照してください

#### トラブルシューティング

**エラー: "Failed to create Deployment: 400" または "404"**

原因: Capability Host が未作成、またはプロビジョニング中

解決策:

```powershell
# Capability Host の状態を確認
az rest --method get \
  --url "https://management.azure.com/subscriptions/<subscription-id>/resourceGroups/<resource-group-name>/providers/Microsoft.CognitiveServices/accounts/<ai-services-name>/capabilityHosts/accountcaphost?api-version=2025-10-01-preview" \
  --query "properties.provisioningState" -o tsv

# 出力が "NotFound" の場合: 上記「前提条件」セクションの手順で Capability Host を作成
# 出力が "Creating" の場合: "Succeeded" になるまで待機（30-60秒）
# 出力が "Succeeded" の場合: Hosted Agent 登録を再実行
```

**エラー: "AcrPullWithMSIFailed" または "InvalidAcrPullCredentials"**

原因: Project の Managed Identity に ACR への Pull 権限がない

解決策: Bicep で `hostedAgentRbac` モジュールが正しくデプロイされているか確認（通常は自動設定済み）

**エラー: "Agent creation failed: Failed to invoke the Azure CLI"**

原因: Azure CLI の認証トークンが期限切れ

解決策:

```powershell
az login
az account set --subscription <subscription-id>
```

**エラー: Hosted Agent がメッセージに応答しない（タイムアウト）**

原因:

1. 環境変数の設定ミス
2. コンテナ起動失敗
3. **Managed Identity の RBAC 権限不足（最も一般的）**

解決策:

```powershell
# 1. コンテナログを確認
$token = az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv
curl -N "https://<ai-services-name>.services.ai.azure.com/api/projects/<project-name>/agents/demo-hosted-agent/versions/<version-number>/containers/default:logstream?kind=console&tail=100&api-version=2025-11-15-preview" -H "Authorization: Bearer $token"

# 2. ログに "Hosted Agent starting..." が表示されない場合、環境変数を確認:
#    register_hosted_agent.py スクリプトが以下を自動設定しています:
#      - AZURE_AI_PROJECT_ENDPOINT (プロジェクトエンドポイント)
#      - AZURE_OPENAI_DEPLOYMENT_NAME (デフォルト: gpt-4o-mini)
#
#    モデル名による問題の場合:
#    - プロジェクトにデプロイされているモデル名を確認
#    - --model オプションで正しいデプロイメント名を指定して再作成

# 3. Managed Identity の RBAC 権限を確認
#    Project の Managed Identity には以下のロールが必要:
#    - Container Registry: "AcrPull" (イメージの pull 用)
#    - AI Services Account: "Cognitive Services OpenAI User" (Azure OpenAI アクセス用)
#    → 通常は hostedAgentRbac モジュールで自動設定されます

# 確認コマンド:
az role assignment list --assignee <project-principal-id> --scope <acr-resource-id> -o table
az role assignment list --assignee <project-principal-id> --scope <ai-services-resource-id> -o table

# Principal ID は次のコマンドで取得:
az cognitiveservices account show --name <ai-services-name> --resource-group <rg-name> --query "projects[0].identity.principalId" -o tsv

# 4. 新しいバージョンを作成して再デプロイ
python scripts/register_hosted_agent.py create \
  --endpoint "https://<ai-services-name>.services.ai.azure.com/api/projects/<project-name>" \
  --image "<acr-name>.azurecr.io/hosted-agent:v1" \
  --name "demo-hosted-agent" \
  --model "gpt-4o-mini"

# 5. Portal UI でデプロイメントを新しいバージョンに更新
#    Applications → demo-hosted-agent-app → Deployments → Update deployment
```

**期待されるログ出力:**

```
Hosted Agent starting...
OpenAI Endpoint: https://<ai-services-name>.cognitiveservices.azure.com/
Deployment: gpt-4o-mini
Agent: Hosted Demo Agent
Hosted Agent ready on port 8088
```

**重要: Hosted Agent の Managed Identity について**

- **開発時（Unpublished）**: Project の System-assigned Managed Identity を使用
  - すべての未公開エージェントが共有の Project Identity で認証
  - Project Identity には ACR と Azure OpenAI へのアクセス権限が必要
- **公開後（Published）**: 専用の Agent Identity が自動作成される
  - Agent Application リソースに紐づく独立した Identity
  - **公開時に RBAC 権限を手動で再割り当てする必要がある**（自動引き継ぎされない）

詳細: [Agent identity concepts | Microsoft Learn](https://learn.microsoft.com/azure/ai-foundry/agents/concepts/agent-identity)

**公開後のワークフローについて**

Agent を公開すると、Project Identity の権限は**自動的には移行されません**。これは Microsoft の意図的な設計です：

> "permissions assigned to a project identity do not transfer to an application upon publishing an agent; you must explicitly (re)assign the necessary privileges to the publishing application's identity"
>
> — [Publish and share agents | Microsoft Learn](https://learn.microsoft.com/azure/ai-foundry/agents/how-to/publish-agent)

**理由:** セキュリティ分離のため、各公開済み Agent が独立した Identity を持ち、必要最小限の権限のみを与える設計になっています。

**公開時の標準ワークフロー:**

1. Azure AI Foundry Portal で Agent を公開
2. Agent Application と専用 Agent Identity が自動作成される
3. **手動で RBAC ロールを Agent Identity に割り当てる**（Portal UI の "Assign permissions for tool authentication" ステップ）
4. Agent をデプロイして公開完了

**必要な RBAC 割り当て例:**

```bash
# Agent Identity の Principal ID を取得
AGENT_PRINCIPAL_ID=$(az cognitiveservices account show \
  --name <ai-services-name> \
  --resource-group <rg-name> \
  --query "properties.applications[?name=='<agent-app-name>'].identity.principalId" -o tsv)

# ACR Pull 権限を付与
az role assignment create \
  --assignee $AGENT_PRINCIPAL_ID \
  --role "AcrPull" \
  --scope "/subscriptions/<sub-id>/resourceGroups/<rg-name>/providers/Microsoft.ContainerRegistry/registries/<acr-name>"

# Azure OpenAI 権限を付与
az role assignment create \
  --assignee $AGENT_PRINCIPAL_ID \
  --role "Cognitive Services OpenAI User" \
  --scope "/subscriptions/<sub-id>/resourceGroups/<rg-name>/providers/Microsoft.CognitiveServices/accounts/<ai-services-name>"
```

**運用上の推奨事項:**

- 開発・検証中は Unpublished のまま使用して RBAC 管理を最小限に抑える
- 本番公開時のみ Publish して、専用の RBAC 設定を行う
- Bicep/Terraform 等の IaC で Agent Identity の RBAC を自動化することも可能

### 7. Portal UI で確認

- **Azure AI Foundry Portal**: [ai.azure.com](https://ai.azure.com) → プロジェクト → Agents
- **監視・トレーシング**: Azure AI Foundry Portal → Tracing
- **メトリクス**: Azure Portal → Application Insights

## デプロイされる Agent 一覧

このプロジェクトでは、3種類のエージェントをデプロイします：

| Agent タイプ       | Agent 名                                                                   | 実装方式            | 主な機能                                       |
| ------------------ | -------------------------------------------------------------------------- | ------------------- | ---------------------------------------------- |
| **Prompt Agent**   | `demo-prompt-agent-<timestamp>`                                            | Prompt ベース       | ユーザーの質問に丁寧に答えるアシスタント       |
| **Workflow Agent** | `demo-workflow-agent-<timestamp>`<br>`demo-workflow-sub-agent-<timestamp>` | YAML ワークフロー   | 複数の Sub Agent を順次実行するワークフロー    |
| **Hosted Agent**   | `demo-hosted-agent`                                                        | コンテナベース (C#) | カスタムコードで実装されたホスト型エージェント |

**特徴の比較:**

- **Prompt Agent**: ポータル UI で instructions とモデルを設定。最もシンプル。
- **Workflow Agent**: YAML で複雑なフローを定義。複数エージェントの連携が可能。
- **Hosted Agent**: 完全なコード制御。独自フレームワーク (Microsoft Agent Framework など) を使用可能。コンテナとして実行され、スケーラブル。

## インフラ構成

### デプロイされる Azure リソース

| リソース                     | 用途                                                       |
| ---------------------------- | ---------------------------------------------------------- |
| **Azure AI Foundry**         | エージェント管理の中核 + AI Services（モデルデプロイ含む） |
| **Azure Storage**            | ファイル・ドキュメント保存                                 |
| **Azure Container Registry** | Hosted Agent コンテナイメージ格納                          |
| **Key Vault**                | シークレット・キー管理                                     |
| **Log Analytics**            | ログ集約・分析                                             |
| **Application Insights**     | トレーシング・監視                                         |
| **Azure AI Content Safety**  | コンテンツフィルタリング                                   |

### リソースグループ構成

```text
<resource-group-name>
├── <ai-services-name>              # AI Foundry Account (CognitiveServices)
│   ├── <project-name>              # AI Foundry Project
│   └── model deployments           # gpt-4o, gpt-4o-mini
├── <storage-account-name>          # Storage Account
├── <key-vault-name>                # Key Vault
├── <acr-name>                      # Azure Container Registry
├── <log-analytics-name>            # Log Analytics Workspace
├── <app-insights-name>             # Application Insights
└── <content-safety-name>           # Content Safety
```

> **Note:** リソース名は `infra/params/*.bicepparam` で指定した `baseName` + `environment` + ランダムサフィックスで生成されます。

### RBAC 設定（Hosted Agent 用）

Hosted Agent が動作するために必要な権限は、Bicep デプロイ時に自動設定されます（`infra/modules/hostedAgentRbac.bicep`）：

| 対象リソース            | ロール                           | スコープ               | 用途                            |
| ----------------------- | -------------------------------- | ---------------------- | ------------------------------- |
| **Container Registry**  | `AcrPull`                        | ACR リソース           | コンテナイメージの pull         |
| **AI Services Account** | `Cognitive Services OpenAI User` | AI Services アカウント | Azure OpenAI モデルへのアクセス |

**重要な注意点:**

- これらの権限は **Project の Managed Identity** に付与されます
- **公開前（開発中）** のすべての Hosted Agent がこの Project Identity を共有します
- **公開後**は Agent 専用の Identity が作成されるため、同じ権限を再度割り当てる必要があります

## 設定ファイル

`src/AgentDemos/appsettings.Development.json` に以下を設定：

```json
{
  "AzureAI": {
    "FoundryEndpoint": "https://<ai-services-name>.services.ai.azure.com/api/projects/<project-name>",
    "ProjectName": "<project-name>"
  },
  "ApplicationInsights": {
    "ConnectionString": "<app-insights-connection-string>"
  }
}
```

## 使用技術

| カテゴリ                       | 技術                               | バージョン                 |
| ------------------------------ | ---------------------------------- | -------------------------- |
| **言語**                       | C# / .NET                          | 10.0 LTS                   |
| **エージェントフレームワーク** | Microsoft Agent Framework          | 1.0.0                      |
| **Azure SDK**                  | Azure.AI.Agents                    | 2.0.0-alpha.20251107.3     |
|                                | Azure.AI.AgentServer.AgentFramework | 1.0.0-beta.6 (Hosted)      |
|                                | Azure.AI.OpenAI                    | 2.5.0-beta.1               |
|                                | Azure.Identity                     | 1.17.0-1.17.1              |
| **監視**                       | OpenTelemetry                      | 1.12.0                     |
|                                | Azure.Monitor.OpenTelemetry.Exporter | 1.4.0                    |
| **コンテナ**                   | Docker                             | -                          |
| **IaC**                        | Bicep + Azure Verified Modules     | 0.40+                      |

## ⚠️ 注意事項

### Cognitive Services の Soft Delete について

Azure Cognitive Services（AI Foundry Account を含む）は、削除後 **48日間 soft-deleted 状態で保持** されます。
同じ名前・リージョンでリソースを再作成する場合、soft-deleted リソースを先に **purge（完全削除）** する必要があります。

**症状**: デプロイ時に以下のようなエラーが発生する場合があります：

```
A]Services/accounts with the same name still exists.
```

**確認方法**:

```powershell
# soft-deleted リソースを確認
az cognitiveservices account list-deleted --query "[?contains(id, '<location>')]" -o table
```

**解決方法**:

```powershell
# 各リソースを完全削除（purge）
az cognitiveservices account purge --name "<resource-name>" --resource-group "<original-rg>" --location "<location>"
```

**例（Japan East リージョン）**:

```powershell
az cognitiveservices account list-deleted --query "[?contains(id, 'japaneast')]" -o table

# 表示されたリソースを順に purge
az cognitiveservices account purge --name "aiffcpjpdevockb" --resource-group "rg-fcpjpe-dev" --location "japaneast"
az cognitiveservices account purge --name "cs-fcpjpe-dev-ockb" --resource-group "rg-fcpjpe-dev" --location "japaneast"
```

> **Note**: purge には「Cognitive Services Contributor」以上のロールが必要です。
> 元のリソースグループ名が不明な場合は、`az cognitiveservices account list-deleted -o json` で完全な情報を確認してください。

## クリーンアップ

```powershell
# リソースグループごと削除
az group delete --name rg-fcpjpe2-dev --yes --no-wait
```

## 参考リンク

- [Azure AI Foundry ドキュメント](https://learn.microsoft.com/azure/ai-foundry/)
- [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)
- [Bicep ドキュメント](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)

## ライセンス

MIT License
