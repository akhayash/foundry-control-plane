#!/usr/bin/env python3
"""
Chat Completions API 動作確認スクリプト

AI Gateway 経由で Azure OpenAI の Chat Completions API をテストします。
"""

import argparse
import json
import sys

from openai import AzureOpenAI

from config import get_config


def test_simple_chat(client: AzureOpenAI, model: str, message: str) -> None:
    """シンプルなチャット完了テスト"""
    
    print(f"\n{'='*60}")
    print("Chat Completions テスト")
    print(f"{'='*60}")
    print(f"Model: {model}")
    print(f"Message: {message}")
    print("-" * 60)
    
    response = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "user", "content": message}
        ],
        max_tokens=200
    )
    
    print(f"\n✅ 成功!")
    print(f"Response Model: {response.model}")
    print(f"Response ID: {response.id}")
    print(f"\nContent:")
    print(response.choices[0].message.content)
    print(f"\nUsage:")
    print(f"  - Prompt tokens: {response.usage.prompt_tokens}")
    print(f"  - Completion tokens: {response.usage.completion_tokens}")
    print(f"  - Total tokens: {response.usage.total_tokens}")


def test_streaming(client: AzureOpenAI, model: str, message: str) -> None:
    """ストリーミングレスポンステスト"""
    
    print(f"\n{'='*60}")
    print("Streaming テスト")
    print(f"{'='*60}")
    print(f"Model: {model}")
    print(f"Message: {message}")
    print("-" * 60)
    print("\nStreaming response:")
    
    stream = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "user", "content": message}
        ],
        max_tokens=200,
        stream=True
    )
    
    full_response = ""
    for chunk in stream:
        if chunk.choices and chunk.choices[0].delta.content:
            content = chunk.choices[0].delta.content
            print(content, end="", flush=True)
            full_response += content
    
    print("\n")
    print(f"✅ ストリーミング完了 (Total chars: {len(full_response)})")


def test_multi_turn(client: AzureOpenAI, model: str) -> None:
    """マルチターン会話テスト"""
    
    print(f"\n{'='*60}")
    print("Multi-turn 会話テスト")
    print(f"{'='*60}")
    
    messages = [
        {"role": "system", "content": "あなたは親切なアシスタントです。"},
        {"role": "user", "content": "私の名前は田中太郎です。覚えておいてください。"}
    ]
    
    print(f"\n[Turn 1] User: {messages[1]['content']}")
    
    response1 = client.chat.completions.create(
        model=model,
        messages=messages,
        max_tokens=100
    )
    
    assistant_msg1 = response1.choices[0].message.content
    print(f"[Turn 1] Assistant: {assistant_msg1}")
    
    # 会話履歴に追加
    messages.append({"role": "assistant", "content": assistant_msg1})
    messages.append({"role": "user", "content": "私の名前は何でしたか？"})
    
    print(f"\n[Turn 2] User: {messages[3]['content']}")
    
    response2 = client.chat.completions.create(
        model=model,
        messages=messages,
        max_tokens=100
    )
    
    assistant_msg2 = response2.choices[0].message.content
    print(f"[Turn 2] Assistant: {assistant_msg2}")
    
    print(f"\n✅ マルチターン会話完了")


def main():
    parser = argparse.ArgumentParser(
        description="Chat Completions API 動作確認",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python test_chat_completions.py
  python test_chat_completions.py --message "Azure AI Foundry とは？"
  python test_chat_completions.py --model gpt-4o-mini --streaming
  python test_chat_completions.py --multi-turn
        """
    )
    parser.add_argument(
        "--model", "-m",
        help="使用するモデル名（デフォルト: 環境変数 DEFAULT_MODEL）"
    )
    parser.add_argument(
        "--message",
        default="Hello! What is Azure AI Foundry?",
        help="送信するメッセージ"
    )
    parser.add_argument(
        "--streaming", "-s",
        action="store_true",
        help="ストリーミングモードでテスト"
    )
    parser.add_argument(
        "--multi-turn",
        action="store_true",
        help="マルチターン会話をテスト"
    )
    parser.add_argument(
        "--all", "-a",
        action="store_true",
        help="すべてのテストを実行"
    )
    
    args = parser.parse_args()
    
    # 設定読み込み
    try:
        config = get_config()
    except ValueError as e:
        print(f"❌ エラー: {e}", file=sys.stderr)
        sys.exit(1)
    
    model = args.model or config.default_model
    
    print(f"AI Gateway Endpoint: {config.apim_endpoint}")
    print(f"API Version: {config.api_version}")
    
    # OpenAI クライアント作成（APIM 経由）
    # AI Gateway のパスは /openai/deployments/{model}/... なので
    # azure_endpoint に /openai を追加
    client = AzureOpenAI(
        api_key=config.api_key,
        api_version=config.api_version,
        azure_endpoint=f"{config.apim_endpoint}/openai"
    )
    
    try:
        if args.all:
            test_simple_chat(client, model, args.message)
            test_streaming(client, model, "短い詩を書いてください。")
            test_multi_turn(client, model)
        elif args.streaming:
            test_streaming(client, model, args.message)
        elif args.multi_turn:
            test_multi_turn(client, model)
        else:
            test_simple_chat(client, model, args.message)
        
        print(f"\n{'='*60}")
        print("✅ すべてのテストが正常に完了しました")
        print(f"{'='*60}")
        
    except Exception as e:
        print(f"\n❌ エラー: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
