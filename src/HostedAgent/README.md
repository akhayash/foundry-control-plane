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

### 1. ビルド

```powershell
cd src/HostedAgent
docker build -t hosted-agent:v1 .
```

### 2. ACR にプッシュ

```powershell
# ACR名を確認
$acrName = "acrfcpncusdevpevn"  # 環境に合わせて変更

# ログイン
az acr login --name $acrName

# タグ付け & プッシュ
docker tag hosted-agent:v1 "$acrName.azurecr.io/hosted-agent:v1"
docker push "$acrName.azurecr.io/hosted-agent:v1"
```

### 3. Foundry に登録

```python
from azure.identity import AzureCliCredential
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import (
    ImageBasedHostedAgentDefinition,
    ProtocolVersionRecord,
    AgentProtocol
)

credential = AzureCliCredential()
endpoint = "https://<account>.services.ai.azure.com/api/projects/<project>"
client = AIProjectClient(endpoint=endpoint, credential=credential)

agent = client.agents.create_version(
    agent_name="demo-hosted-agent",
    definition=ImageBasedHostedAgentDefinition(
        container_protocol_versions=[
            ProtocolVersionRecord(protocol=AgentProtocol.RESPONSES, version="v1")
        ],
        image="<acr>.azurecr.io/hosted-agent:v1",
        cpu="1",
        memory="2Gi",
        environment_variables={
            "AZURE_OPENAI_ENDPOINT": "https://<account>.cognitiveservices.azure.com/",
            "AZURE_AI_PROJECT_ENDPOINT": endpoint,
            "MODEL_NAME": "gpt-4o-mini"
        }
    )
)
print(f"Version: {agent.version}")
```

### 4. スクリプトで一括デプロイ

```powershell
./scripts/deploy-hosted-agent.ps1 -ResourceGroup "rg-fcpncus-dev" -ImageTag "v1"
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
