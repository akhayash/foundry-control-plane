#!/usr/bin/env python3
"""
Azure AI Foundry でトレースを確認するサンプルスクリプト

前提条件:
    pip install azure-ai-projects azure-identity azure-monitor-opentelemetry opentelemetry-sdk azure-core-tracing-opentelemetry openai

使用方法:
    python scripts/test_agent_with_tracing.py

環境変数:
    PROJECT_ENDPOINT: AI Foundry Project エンドポイント (任意)
"""

import os
from azure.identity import AzureCliCredential
from azure.ai.projects import AIProjectClient
from azure.core.settings import settings
from openai import AzureOpenAI

# ========================================
# 1. OpenTelemetry トレース設定
# ========================================

# コンテンツ記録を有効化（prompts/completions の内容を記録）
os.environ["AZURE_TRACING_GEN_AI_CONTENT_RECORDING_ENABLED"] = "true"

# azure-core のトレース実装を OpenTelemetry に設定
settings.tracing_implementation = "opentelemetry"

# ========================================
# 2. Application Insights 接続
# ========================================

credential = AzureCliCredential()
endpoint = os.environ.get(
    "PROJECT_ENDPOINT",
    "https://aiffcpncdevpevn.services.ai.azure.com/api/projects/aifpfcpndevpevn"
)

client = AIProjectClient(endpoint=endpoint, credential=credential)

# Application Insights 接続文字列を取得してトレースを設定
from azure.monitor.opentelemetry import configure_azure_monitor

connection_string = client.telemetry.get_application_insights_connection_string()
print(f"✓ Application Insights connected")

# Azure Monitor にトレースを送信
configure_azure_monitor(connection_string=connection_string)
print("✓ Tracing enabled")

# ========================================
# 3. Azure OpenAI Chat Completions
# ========================================

print("\n" + "="*50)
print("Testing Chat Completions with Tracing...")
print("="*50)

# Azure OpenAI クライアント作成
openai_endpoint = "https://aiffcpncdevpevn.cognitiveservices.azure.com/"
token = credential.get_token("https://cognitiveservices.azure.com/.default")

openai_client = AzureOpenAI(
    azure_endpoint=openai_endpoint,
    api_key=token.token,
    api_version="2024-08-01-preview"
)

# ========================================
# 4. Chat Completion リクエスト
# ========================================

user_message = "Azure AI Foundry のトレーシング機能について教えてください。簡潔に3点で回答してください。"
print(f"\n📝 User: {user_message}")

response = openai_client.chat.completions.create(
    model="gpt-4o-mini",
    messages=[
        {"role": "system", "content": "あなたはAzureの専門家です。日本語で回答してください。"},
        {"role": "user", "content": user_message}
    ],
    temperature=0.7,
    max_tokens=500
)

print(f"\n🤖 Assistant: {response.choices[0].message.content}")

# ========================================
# 5. トレース情報の確認
# ========================================

print("\n" + "="*50)
print("トレース確認方法:")
print("="*50)
print("1. Azure AI Foundry Portal → Tracing タブ")
print("2. Application Insights → Transaction search")
print("3. 数分後にトレースが表示されます")
print()
print("トレースに含まれる情報:")
print("  - リクエスト/レスポンス時間")
print("  - トークン使用量")
print("  - モデル名")
print("  - プロンプト/コンプリーション内容 (AZURE_TRACING_GEN_AI_CONTENT_RECORDING_ENABLED=true時)")
print()
print("✓ スクリプト完了")
