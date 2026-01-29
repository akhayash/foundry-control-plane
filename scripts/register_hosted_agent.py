#!/usr/bin/env python3
"""
Hosted Agent を Azure AI Foundry に登録するスクリプト

使用方法:
    python scripts/register_hosted_agent.py \
        --endpoint "https://<account>.services.ai.azure.com/api/projects/<project>" \
        --image "acrname.azurecr.io/hosted-agent:v1" \
        --name "demo-hosted-agent"

前提条件:
    - pip install azure-ai-projects>=2.0.0b3 azure-identity
    - az login でログイン済み
"""

import argparse
import sys
from azure.identity import AzureCliCredential
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import (
    ImageBasedHostedAgentDefinition,
    ProtocolVersionRecord,
    AgentProtocol,
)


def create_hosted_agent(
    endpoint: str,
    image: str,
    name: str = "demo-hosted-agent",
    cpu: str = "1",
    memory: str = "2Gi",
    model_name: str = "gpt-4o-mini",
) -> None:
    """Hosted Agent を作成/更新"""
    print(f"Creating hosted agent: {name}")
    print(f"  Endpoint: {endpoint}")
    print(f"  Image: {image}")
    print(f"  CPU: {cpu}, Memory: {memory}")
    print()

    # Extract account name from project endpoint to build OpenAI endpoint
    # e.g., https://accountname.services.ai.azure.com/api/projects/projectname
    import re
    match = re.match(r"https://([^.]+)\.services\.ai\.azure\.com", endpoint)
    if match:
        account_name = match.group(1)
        openai_endpoint = f"https://{account_name}.cognitiveservices.azure.com/"
    else:
        openai_endpoint = endpoint  # fallback

    credential = AzureCliCredential()
    client = AIProjectClient(endpoint=endpoint, credential=credential)

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
                environment_variables={
                    "AZURE_AI_PROJECT_ENDPOINT": endpoint,
                    "AZURE_OPENAI_ENDPOINT": openai_endpoint,
                    "AZURE_OPENAI_DEPLOYMENT_NAME": model_name,
                },
            ),
        )
        print(f"✓ Agent created successfully")
        print(f"  Name: {agent.name}")
        print(f"  Version: {agent.version}")
        print()
        print("次のステップ:")
        print("  1. Azure AI Foundry Portal でプロジェクトを開く")
        print(f"  2. Agents → {name} を選択")
        print("  3. 'Start' でエージェントを起動")
        print("  4. Playground でテスト")
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
