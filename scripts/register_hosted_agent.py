#!/usr/bin/env python3
"""
Hosted Agent を Azure AI Foundry に登録するスクリプト

使用方法:
    python scripts/register_hosted_agent.py create \
        --endpoint "https://<account>.services.ai.azure.com/api/projects/<project>" \
        --image "acrname.azurecr.io/hosted-agent:v1" \
        --name "demo-hosted-agent"

    # 作成と同時にPublish（ポータルに表示）:
    python scripts/register_hosted_agent.py create \
        --endpoint "https://<account>.services.ai.azure.com/api/projects/<project>" \
        --image "acrname.azurecr.io/hosted-agent:v1" \
        --name "demo-hosted-agent" \
        --publish \
        --subscription-id <sub-id> \
        --resource-group <rg-name>

前提条件:
    - pip install azure-ai-projects>=2.0.0b3 azure-identity requests
    - az login でログイン済み
"""

import argparse
import re
import sys
import time
import requests
from azure.identity import AzureCliCredential
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import (
    ImageBasedHostedAgentDefinition,
    ProtocolVersionRecord,
    AgentProtocol,
)


def publish_agent(
    credential: AzureCliCredential,
    subscription_id: str,
    resource_group: str,
    account_name: str,
    project_name: str,
    agent_name: str,
    agent_version: str,
    app_name: str | None = None,
    deployment_type: str = "Hosted",
) -> bool:
    """
    エージェントをPublish（Agent ApplicationとDeploymentを作成）
    
    Args:
        credential: Azure認証情報
        subscription_id: AzureサブスクリプションID
        resource_group: リソースグループ名  
        account_name: AI Servicesアカウント名
        project_name: プロジェクト名
        agent_name: エージェント名
        agent_version: エージェントバージョン
        app_name: アプリケーション名（省略時はエージェント名を使用）
        deployment_type: "Hosted" または "Managed"
    """
    if app_name is None:
        app_name = f"{agent_name}-app"
    
    deployment_name = f"{agent_name}-deployment"
    api_version = "2025-10-01-preview"
    
    # ARM用トークンを取得
    token = credential.get_token("https://management.azure.com/.default").token
    headers = {
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json",
    }
    
    base_url = f"https://management.azure.com/subscriptions/{subscription_id}/resourceGroups/{resource_group}/providers/Microsoft.CognitiveServices/accounts/{account_name}/projects/{project_name}"
    
    # 1. Agent Applicationを作成
    print(f"Creating Agent Application: {app_name}")
    app_url = f"{base_url}/applications/{app_name}?api-version={api_version}"
    app_payload = {
        "properties": {
            "displayName": app_name,
            "agents": [{"agentName": agent_name}],
        }
    }
    
    resp = requests.put(app_url, headers=headers, json=app_payload)
    if resp.status_code not in [200, 201, 202]:
        print(f"✗ Failed to create Agent Application: {resp.status_code}")
        print(f"  Response: {resp.text}")
        return False
    print(f"  ✓ Agent Application created/updated")
    
    # 2. Deploymentを作成
    print(f"Creating Deployment: {deployment_name}")
    deploy_url = f"{base_url}/applications/{app_name}/agentdeployments/{deployment_name}?api-version={api_version}"
    
    deploy_payload = {
        "properties": {
            "displayName": deployment_name,
            "deploymentType": deployment_type,
            "protocols": [
                {"protocol": "responses", "version": "1.0"}
            ],
            "agents": [
                {"agentName": agent_name, "agentVersion": agent_version}
            ],
        }
    }
    
    # Hostedの場合はreplica設定を追加
    if deployment_type == "Hosted":
        deploy_payload["properties"]["minReplicas"] = 1
        deploy_payload["properties"]["maxReplicas"] = 1
    
    resp = requests.put(deploy_url, headers=headers, json=deploy_payload)
    if resp.status_code not in [200, 201, 202]:
        print(f"✗ Failed to create Deployment: {resp.status_code}")
        print(f"  Response: {resp.text}")
        return False
    print(f"  ✓ Deployment created/updated")
    
    # 3. デプロイメント状態を確認（オプション）
    print("Waiting for deployment to start...")
    for _ in range(6):  # 最大30秒待機
        time.sleep(5)
        resp = requests.get(deploy_url, headers=headers)
        if resp.status_code == 200:
            data = resp.json()
            state = data.get("properties", {}).get("state", "Unknown")
            prov_state = data.get("properties", {}).get("provisioningState", "Unknown")
            print(f"  State: {state}, ProvisioningState: {prov_state}")
            if state == "Running":
                break
    
    print()
    print(f"✓ Agent published successfully!")
    print(f"  Application: {app_name}")
    print(f"  Deployment: {deployment_name}")
    print(f"  Endpoint: https://{account_name}.services.ai.azure.com/api/projects/{project_name}/applications/{app_name}/protocols/openai")
    return True


def create_hosted_agent(
    endpoint: str,
    image: str,
    name: str = "demo-hosted-agent",
    cpu: str = "1",
    memory: str = "2Gi",
    model_name: str = "gpt-4o-mini",
    publish: bool = False,
    subscription_id: str | None = None,
    resource_group: str | None = None,
) -> None:
    """Hosted Agent を作成/更新"""
    print(f"Creating hosted agent: {name}")
    print(f"  Endpoint: {endpoint}")
    print(f"  Image: {image}")
    print(f"  CPU: {cpu}, Memory: {memory}")
    if publish:
        print(f"  Publish: Yes (will appear in Portal)")
    print()

    # Extract account name from project endpoint to build OpenAI endpoint
    # e.g., https://accountname.services.ai.azure.com/api/projects/projectname
    import re
    match = re.match(r"https://([^.]+)\.services\.ai\.azure\.com", endpoint)
    if match:
        account_name = match.group(1)
        # Azure AI Foundryの場合、cognitiveservices エンドポイントを使用
        openai_endpoint = f"https://{account_name}.cognitiveservices.azure.com/"
    else:
        openai_endpoint = endpoint  # fallback

    credential = AzureCliCredential()
    client = AIProjectClient(endpoint=endpoint, credential=credential)

    # Application Insights 接続文字列を取得（トレース用）- タイムアウト回避のためスキップ可能
    app_insights_conn_str = None
    print(f"  ⚠ Application Insights lookup skipped (can be configured later)")

    # 環境変数を構築
    # Note: Azure AI Foundry では AZURE_AI_PROJECT_ENDPOINT が推奨
    env_vars = {
        "AZURE_AI_PROJECT_ENDPOINT": openai_endpoint,  # Program.cs で使用
        "AZURE_OPENAI_ENDPOINT": openai_endpoint,
        "AZURE_OPENAI_DEPLOYMENT_NAME": model_name,
    }
    if app_insights_conn_str:
        env_vars["APPLICATIONINSIGHTS_CONNECTION_STRING"] = app_insights_conn_str
    
    print(f"  Environment variables:")
    print(f"    AZURE_AI_PROJECT_ENDPOINT: {openai_endpoint}")
    print(f"    AZURE_OPENAI_DEPLOYMENT_NAME: {model_name}")

    try:
        agent = client.agents.create_version(
            agent_name=name,
            definition=ImageBasedHostedAgentDefinition(
                container_protocol_versions=[
                    ProtocolVersionRecord(protocol=AgentProtocol.RESPONSES, version="v1")
                ],
                cpu=cpu,
                memory=memory,
                image=image,
                environment_variables=env_vars,
            ),
        )
        print(f"✓ Agent created successfully")
        print(f"  Name: {agent.name}")
        print(f"  Version: {agent.version}")
        print()
        
        # Publish処理
        if publish:
            if not subscription_id or not resource_group:
                print("✗ --publish requires --subscription-id and --resource-group", file=sys.stderr)
                sys.exit(1)
            
            # エンドポイントからプロジェクト名を抽出
            project_match = re.search(r"/projects/([^/]+)", endpoint)
            project_name = project_match.group(1) if project_match else None
            
            if not project_name:
                print("✗ Could not extract project name from endpoint", file=sys.stderr)
                sys.exit(1)
            
            print()
            print("Publishing agent...")
            success = publish_agent(
                credential=credential,
                subscription_id=subscription_id,
                resource_group=resource_group,
                account_name=account_name,
                project_name=project_name,
                agent_name=agent.name,
                agent_version=str(agent.version),
                deployment_type="Hosted",
            )
            if not success:
                sys.exit(1)
        else:
            print("次のステップ:")
            print("  1. Azure AI Foundry Portal でプロジェクトを開く")
            print(f"  2. Agents → {name} を選択")
            print("  3. 'Start' でエージェントを起動")
            print("  4. Playground でテスト")
            print()
            print("  または --publish オプションで自動公開")
    except Exception as e:
        print(f"✗ Agent creation failed: {e}", file=sys.stderr)
        sys.exit(1)


def list_agents(endpoint: str) -> None:
    """登録済みエージェント一覧を表示"""
    credential = AzureCliCredential()
    client = AIProjectClient(endpoint=endpoint, credential=credential)

    agents = list(client.agents.list())
    print(f"Found {len(agents)} agent(s):")
    for agent in agents:
        print(f"  - {agent.name} (id: {agent.id})")


def delete_agent(endpoint: str, name: str) -> None:
    """エージェントを削除"""
    credential = AzureCliCredential()
    client = AIProjectClient(endpoint=endpoint, credential=credential)

    try:
        client.agents.delete(agent_name=name)
        print(f"✓ Agent '{name}' deleted")
    except Exception as e:
        print(f"✗ Delete failed: {e}", file=sys.stderr)
        sys.exit(1)


def main():
    parser = argparse.ArgumentParser(
        description="Hosted Agent を Azure AI Foundry に登録"
    )
    subparsers = parser.add_subparsers(dest="command", help="コマンド")

    # create コマンド
    create_parser = subparsers.add_parser("create", help="エージェントを作成")
    create_parser.add_argument(
        "--endpoint", required=True, help="AI Foundry Project endpoint"
    )
    create_parser.add_argument(
        "--image", required=True, help="コンテナイメージ (例: acr.azurecr.io/agent:v1)"
    )
    create_parser.add_argument(
        "--name", default="demo-hosted-agent", help="エージェント名"
    )
    create_parser.add_argument("--cpu", default="1", help="CPU (default: 1)")
    create_parser.add_argument("--memory", default="2Gi", help="メモリ (default: 2Gi)")
    create_parser.add_argument(
        "--model", default="gpt-4o-mini", help="モデル名 (default: gpt-4o-mini)"
    )
    create_parser.add_argument(
        "--publish", action="store_true", help="作成後にPublish（ポータルに表示）"
    )
    create_parser.add_argument(
        "--subscription-id", help="AzureサブスクリプションID（--publish時に必要）"
    )
    create_parser.add_argument(
        "--resource-group", help="リソースグループ名（--publish時に必要）"
    )

    # list コマンド
    list_parser = subparsers.add_parser("list", help="エージェント一覧")
    list_parser.add_argument(
        "--endpoint", required=True, help="AI Foundry Project endpoint"
    )

    # delete コマンド
    delete_parser = subparsers.add_parser("delete", help="エージェントを削除")
    delete_parser.add_argument(
        "--endpoint", required=True, help="AI Foundry Project endpoint"
    )
    delete_parser.add_argument("--name", required=True, help="エージェント名")

    args = parser.parse_args()

    if args.command == "create":
        create_hosted_agent(
            endpoint=args.endpoint,
            image=args.image,
            name=args.name,
            cpu=args.cpu,
            memory=args.memory,
            model_name=args.model,
            publish=args.publish,
            subscription_id=getattr(args, 'subscription_id', None),
            resource_group=getattr(args, 'resource_group', None),
        )
    elif args.command == "list":
        list_agents(endpoint=args.endpoint)
    elif args.command == "delete":
        delete_agent(endpoint=args.endpoint, name=args.name)
    else:
        parser.print_help()
        sys.exit(1)


if __name__ == "__main__":
    main()
