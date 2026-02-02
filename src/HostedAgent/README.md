# Hosted Agent

Azure AI Foundry Hosted Agent のサンプル実装です。

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          Azure AI Foundry                                   │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                      Agent Service                                    │  │
│  │                                                                       │  │
│  │   ┌─────────────────┐      ┌─────────────────────────────────────┐   │  │
│  │   │  Hosted Agent   │      │       Container Runtime             │   │  │
│  │   │  (登録情報)     │─────▶│  ┌─────────────────────────────────┐│   │  │
│  │   │                 │      │  │    HostedAgent Container       ││   │  │
│  │   │ - Name          │      │  │  ┌───────────┐ ┌─────────────┐ ││   │  │
│  │   │ - Image         │      │  │  │ Adapter   │ │   Agent     │ ││   │  │
│  │   │ - Protocol      │      │  │  │ (8088)    │ │   Logic     │ ││   │  │
│  │   │ - Env Vars      │      │  │  │           │ │             │ ││   │  │
│  │   └─────────────────┘      │  │  │ Responses │ │HostedDemo   │ ││   │  │
│  │                            │  │  │ API       │ │Agent.cs     │ ││   │  │
│  │                            │  │  └─────┬─────┘ └──────┬──────┘ ││   │  │
│  │                            │  │        │              │        ││   │  │
│  │                            │  └────────┼──────────────┼────────┘│   │  │
│  │                            └───────────┼──────────────┼─────────┘   │  │
│  │                                        │              │             │  │
│  └────────────────────────────────────────┼──────────────┼─────────────┘  │
│                                           │              │                │
│  ┌────────────────────────────────────────▼──────────────▼─────────────┐  │
│  │                    Azure OpenAI                                     │  │
│  │                    (gpt-4o-mini)                                    │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │                    Application Insights (トレーシング)              │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                     Azure Container Registry                                │
│                                                                             │
│   hosted-agent:latest  ◄─── Docker Push                                    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### コンポーネント構成

| コンポーネント                 | 役割                                                        |
| ------------------------------ | ----------------------------------------------------------- |
| **Program.cs (Adapter)**       | 構成・認証・ホスティング。8088ポートでResponses APIをホスト |
| **HostedDemoAgent.cs (Agent)** | ビジネスロジック。差し替え可能なエージェント実装            |
| **Dockerfile**                 | コンテナイメージのビルド定義                                |
| **agent.yaml**                 | エージェントのメタデータ定義                                |

### Hosted Agent vs Agent Service

| 観点         | Agent Service               | Hosted Agent     |
| ------------ | --------------------------- | ---------------- |
| ホスティング | Foundryが完全管理           | 自分のコンテナ   |
| コード       | 宣言的（YAML + ツール定義） | 完全なC#/Python  |
| カスタマイズ | ツール・ナレッジ追加のみ    | 何でもできる     |
| 外部API      | Foundry経由                 | 直接呼び出し可能 |
| 状態管理     | Foundry側                   | 自分で実装       |
| モデル       | Foundry接続のみ             | 複数プロバイダ可 |

## 前提条件

- Azure CLI がインストールされていること
- Docker Desktop が起動していること
- `az login` でログイン済みであること
- Python 3.10+ (エージェント登録用)

```bash
# Python依存関係
pip install azure-ai-projects>=2.0.0b3 azure-identity
```

## デプロイ手順

### 1. ACR でビルド & プッシュ

Docker Desktop 不要。ACR 上でリモートビルドします。

```powershell
# ACR名を設定（環境に合わせて変更）
$acrName = "acrfcpncusdevzqum"

# ビルド & プッシュ（1コマンドで完了）
cd src/HostedAgent
az acr build --registry $acrName --image hosted-agent:v1 .
```

### 2. Foundry に登録

```powershell
# 基本的な登録
python scripts/register_hosted_agent.py create `
    --endpoint "https://<account>.services.ai.azure.com/api/projects/<project>" `
    --image "<acr>.azurecr.io/hosted-agent:v1" `
    --name "demo-hosted-agent"

# 自動Publish（ポータルに即表示）したい場合:
python scripts/register_hosted_agent.py create `
    --endpoint "https://<account>.services.ai.azure.com/api/projects/<project>" `
    --image "<acr>.azurecr.io/hosted-agent:v1" `
    --name "demo-hosted-agent" `
    --publish `
    --subscription-id <subscription-id> `
    --resource-group <resource-group>
```

登録後:

1. Azure AI Foundry Portal でプロジェクトを開く
2. Agents → `demo-hosted-agent` を選択
3. 'Start' でエージェントを起動
4. Playground でテスト

### 3. エージェント一覧・削除

```powershell
# 一覧表示
python scripts/register_hosted_agent.py list `
    --endpoint "https://<account>.services.ai.azure.com/api/projects/<project>"

# 削除
python scripts/register_hosted_agent.py delete `
    --endpoint "https://<account>.services.ai.azure.com/api/projects/<project>" `
    --name "demo-hosted-agent"
```

## ローカル開発

### 環境変数を設定して実行

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://<account>.cognitiveservices.azure.com/"
$env:AZURE_OPENAI_DEPLOYMENT_NAME = "gpt-4o-mini"

dotnet run
```

### Docker でローカル実行

```powershell
docker run --rm -p 8088:8088 `
  -e "AZURE_OPENAI_ENDPOINT=https://<account>.cognitiveservices.azure.com/" `
  -e "MODEL_NAME=gpt-4o-mini" `
  hosted-agent:v1
```

## エージェントのカスタマイズ

### 別のエージェントに差し替える

`Program.cs` でエージェントを差し替え可能:

```csharp
// 現在: HostedDemoAgent
var agentBuilder = new HostedDemoAgent(chatClient);

// 例1: カスタムエージェント
var agentBuilder = new MyCustomAgent(chatClient);

// 例2: マルチモデルエージェント
var agentBuilder = new MultiModelAgent(primaryClient, fallbackClient);

// 例3: オーケストレーター
var agentBuilder = new OrchestratorAgent(subAgents);

var agent = agentBuilder.Build();
await agent.RunAIAgentAsync(telemetrySourceName: "Agents");
```

### 新しいエージェントを作成

`Agents/` フォルダに新しいクラスを追加:

```csharp
// Agents/MyCustomAgent.cs
public class MyCustomAgent
{
    private readonly IChatClient _chatClient;

    public MyCustomAgent(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public ChatClientAgent Build()
    {
        return (ChatClientAgent)new ChatClientAgent(_chatClient,
            name: "MyCustomAgent",
            instructions: "カスタムの指示...")
            .AsBuilder()
            .UseOpenTelemetry(sourceName: "Agents", configure: cfg => cfg.EnableSensitiveData = true)
            .Build();
    }
}
```

## 必要なRBAC

Bicepで自動設定されますが、手動設定する場合:

| ロール                         | スコープ            | 目的               |
| ------------------------------ | ------------------- | ------------------ |
| AcrPull                        | Container Registry  | イメージのプル     |
| Cognitive Services OpenAI User | AI Services Account | OpenAI APIアクセス |

## トラブルシューティング

### コンテナが起動しない

```bash
# ローカルでテスト
docker run --rm -p 8088:8088 \
  -e "AZURE_OPENAI_ENDPOINT=..." \
  hosted-agent:v1

# ログを確認
docker logs <container-id>
```

### 認証エラー

- Managed Identity が有効か確認
- RBAC設定を確認
- `AZURE_AI_PROJECT_ENDPOINT` が正しいか確認

### トレースが表示されない

- Application Insights がプロジェクトに接続されているか確認
- Azure AI Foundry ポータルでトレース設定を確認

### Hosted Agent が Start/Deploy で 404/400 になる（Preview 機能無効時）

- 症状: Portal の Start で `Capability Host not found ...@AML` (404) や、`capabilityHosts` 作成が 400 `The definition field is required` などで失敗する。
- 原因: サブスクリプション/リージョンで Hosted Agent 機能フラグが未有効、または capability host 未作成。
- 切り分けコマンド（有効化前は空のはず）:

  ```pwsh
  $sub="<sub>"; $rg="<rg>"; $acct="<account>"; $api="2025-10-01-preview"
  az rest --method get --url "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.CognitiveServices/accounts/$acct/capabilityHosts?api-version=$api"
  ```

- 有効化済みなら、以下で capability host を作成（更新不可のため新規名で作成する）。有効化されていない環境では 4xx になる。

  ```pwsh
  $url="https://management.azure.com/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<account>/capabilityHosts/<name>?api-version=2025-10-01-preview"
  az rest --method put --url $url --headers "Content-Type=application/json" --body '{"properties":{"capabilityHostKind":"Agents","enablePublicHostingEnvironment":true}}'
  ```

- 上記が 4xx で弾かれる場合: Hosted Agent は一般開放ではなく、機能有効化が必要。サポート/CSA に「Hosted Agent capability を有効化してほしい」旨を依頼し、portal での 404 request_id や `az rest` の correlation ID を添えて申請する。

## 関連ファイル

```
src/HostedAgent/
├── Program.cs                    # Adapter部分
├── Agents/
│   └── HostedDemoAgent.cs        # Agent部分
├── Dockerfile                    # コンテナ定義
├── agent.yaml                    # エージェントメタデータ
├── appsettings.json              # 構成ファイル
└── appsettings.Development.json.example

infra/modules/
├── hostedAgentRbac.bicep         # RBAC設定
└── aiFoundryAppInsights.bicep    # App Insights接続

scripts/
├── deploy-hosted-agent.ps1       # デプロイスクリプト
└── register_hosted_agent.py      # 登録スクリプト
```
