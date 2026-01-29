# Copilot Instructions for foundry-control-plane

このファイルはGitHub CopilotおよびAIアシスタントが本リポジトリで作業する際に遵守すべきルールを定義します。

## 必須ルール

### 1. Microsoft Agent Framework を使用すること（Semantic Kernel 禁止）

**重要**: このプロジェクトでは **Microsoft Agent Framework (AgentFramework)** の最新版を使用してください。

- ❌ **禁止**: Semantic Kernel (`Microsoft.SemanticKernel.*`) の使用
- ✅ **必須**: Microsoft Agent Framework (`Azure.AI.AgentServer.*`, `Microsoft.Agents.Sdk`) の使用

理由:

- Azure AI Foundry との統合に最適化されている
- Hosted Agent パターンをネイティブサポート
- 最新のAIエージェント開発パターンに準拠

推奨パッケージ:

```xml
<PackageReference Include="Azure.AI.AgentServer.AgentFramework" Version="1.0.0-beta.6" />
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.0.0-preview.251125.1" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.0.1-preview.1.25571.5" />
<PackageReference Include="Azure.AI.OpenAI" Version="2.5.0-beta.1" />
```

### 2. 認証は AzureCliCredential を使用すること

**重要**: 開発時の認証には **AzureCliCredential** を使用してください。

- ❌ **禁止**: DefaultAzureCredential（ローカル開発で問題が発生する場合あり）
- ✅ **必須**: AzureCliCredential

事前準備:

```bash
az login
```

コード例:

```csharp
using Azure.Identity;

var credential = new AzureCliCredential();
```

理由:

- DefaultAzureCredential は複数の認証方法をフォールバックするため、意図しない認証が行われる可能性がある
- AzureCliCredential は明示的で開発者が `az login` 済みであることを保証できる

### 3. 機密情報の取り扱い

- `appsettings.json` には機密情報（エンドポイント、接続文字列、キーなど）を含めない
- 開発用設定は `appsettings.Development.json` に記載（`.gitignore` で除外済み）
- テンプレートは `appsettings.Development.json.example` を参照

### 4. Azure AI Foundry 設定

- Hosted Agent は現在 **North Central US** リージョンでのみ利用可能（プレビュー制限）
- モデルデプロイメント時はリージョンごとのモデル可用性を確認すること

## コード規約

### C# / .NET

- .NET 10 以上を対象
- nullable reference types を有効化
- async/await パターンを適切に使用
- Azure.Identity の `AzureCliCredential` を認証に使用（開発時）

### Bicep / IaC

- AVM (Azure Verified Modules) を優先的に使用
- パラメータファイルは `.bicepparam` 形式を使用
- リソース名にはユニークサフィックスを含める

## 参考リンク

- [Azure AI Foundry Documentation](https://learn.microsoft.com/azure/ai-services/agents/)
- [Microsoft Agent Framework](https://github.com/microsoft/agents)
