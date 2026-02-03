#!/usr/bin/env python3
"""
Responses API 動作確認スクリプト

AI Gateway 経由で Azure OpenAI の Responses API をテストします。

Responses API は 2025年3月に導入された新しい統合 API で、
Chat Completions と Assistants API の機能を統合しています。
新規開発ではこの API が推奨されています。
"""

import argparse
import sys
import time

import requests

from config import get_config


class ResponsesAPIClient:
    """Responses API クライアント"""
    
    def __init__(self, base_url: str, api_key: str, api_version: str = "2025-03-01-preview"):
        self.base_url = base_url
        self.api_version = api_version
        self.headers = {
            "api-key": api_key,
            "Content-Type": "application/json"
        }
    
    def _url(self, path: str) -> str:
        """API URL を構築（api-version パラメータ付き）"""
        return f"{self.base_url}{path}?api-version={self.api_version}"
    
    def create_response(
        self, 
        model: str, 
        input_text: str,
        previous_response_id: str = None,
        background: bool = False,
        store: bool = True
    ) -> dict:
        """レスポンスを生成"""
        
        body = {
            "model": model,
            "input": input_text
        }
        
        if previous_response_id:
            body["previous_response_id"] = previous_response_id
        
        if background:
            body["background"] = True
            body["store"] = True  # background requires store=true
        elif store is not None:
            body["store"] = store
        
        response = requests.post(
            self._url("/responses"),
            headers=self.headers,
            json=body
        )
        response.raise_for_status()
        return response.json()
    
    def get_response(self, response_id: str) -> dict:
        """レスポンスのステータスを取得"""
        response = requests.get(
            self._url(f"/responses/{response_id}"),
            headers=self.headers
        )
        response.raise_for_status()
        return response.json()
    
    def wait_for_response(
        self, 
        response_id: str, 
        timeout: int = 120,
        poll_interval: float = 2.0
    ) -> dict:
        """バックグラウンドレスポンスの完了を待機"""
        terminal_states = {"completed", "failed", "cancelled", "expired"}
        start_time = time.time()
        
        while True:
            resp = self.get_response(response_id)
            status = resp.get("status", "unknown")
            
            if status in terminal_states:
                return resp
            
            if time.time() - start_time > timeout:
                raise TimeoutError(f"Response did not complete within {timeout} seconds")
            
            print(f"   Status: {status}...", flush=True)
            time.sleep(poll_interval)
    
    def cancel_response(self, response_id: str) -> dict:
        """バックグラウンドレスポンスをキャンセル"""
        response = requests.post(
            self._url(f"/responses/{response_id}/cancel"),
            headers=self.headers
        )
        response.raise_for_status()
        return response.json()


def extract_text_output(response: dict) -> str:
    """レスポンスからテキスト出力を抽出"""
    texts = []
    
    for output in response.get("output", []):
        if output.get("type") == "message":
            for content in output.get("content", []):
                if content.get("type") == "output_text":
                    texts.append(content.get("text", ""))
    
    return "\n".join(texts) if texts else "(no text output)"


def test_simple_response(client: ResponsesAPIClient, model: str, message: str):
    """シンプルなレスポンス生成テスト"""
    
    print(f"\n{'='*60}")
    print("Responses API - 基本テスト")
    print(f"{'='*60}")
    print(f"Model: {model}")
    print(f"Input: {message}")
    print("-" * 60)
    
    response = client.create_response(model, message)
    
    print(f"\n✅ 成功!")
    print(f"Response ID: {response.get('id')}")
    print(f"Model: {response.get('model')}")
    print(f"Status: {response.get('status')}")
    
    text = extract_text_output(response)
    print(f"\nOutput:")
    print(text)
    
    # Usage 情報があれば表示
    usage = response.get("usage", {})
    if usage:
        print(f"\nUsage:")
        print(f"  - Input tokens: {usage.get('input_tokens', 'N/A')}")
        print(f"  - Output tokens: {usage.get('output_tokens', 'N/A')}")
        print(f"  - Total tokens: {usage.get('total_tokens', 'N/A')}")


def test_multi_turn(client: ResponsesAPIClient, model: str):
    """マルチターン会話テスト（previous_response_id 使用）"""
    
    print(f"\n{'='*60}")
    print("Responses API - マルチターン会話テスト")
    print(f"{'='*60}")
    
    # Turn 1
    print("\n[Turn 1]")
    print("User: 私の名前は田中太郎です。覚えておいてください。")
    
    response1 = client.create_response(
        model, 
        "私の名前は田中太郎です。覚えておいてください。"
    )
    
    text1 = extract_text_output(response1)
    print(f"Assistant: {text1}")
    
    response1_id = response1.get("id")
    print(f"(Response ID: {response1_id})")
    
    # Turn 2 - 前のレスポンスを参照
    print("\n[Turn 2]")
    print("User: 私の名前は何でしたか？")
    
    response2 = client.create_response(
        model,
        "私の名前は何でしたか？",
        previous_response_id=response1_id
    )
    
    text2 = extract_text_output(response2)
    print(f"Assistant: {text2}")
    
    print(f"\n{'='*60}")
    print("✅ マルチターン会話テスト完了")
    print(f"{'='*60}")


def test_background_task(client: ResponsesAPIClient, model: str):
    """バックグラウンドタスクテスト"""
    
    print(f"\n{'='*60}")
    print("Responses API - バックグラウンドタスクテスト")
    print(f"{'='*60}")
    
    print("\nStarting background task...")
    
    response = client.create_response(
        model,
        "「人工知能」について、短い説明文を書いてください。",
        background=True
    )
    
    response_id = response.get("id")
    initial_status = response.get("status")
    
    print(f"Response ID: {response_id}")
    print(f"Initial Status: {initial_status}")
    
    if initial_status in {"completed", "failed"}:
        print(f"\nTask already finished with status: {initial_status}")
    else:
        print("\nPolling for completion...")
        completed = client.wait_for_response(response_id)
        
        final_status = completed.get("status")
        print(f"\n✅ Final Status: {final_status}")
        
        if final_status == "completed":
            text = extract_text_output(completed)
            print(f"\nOutput:")
            print(text)
    
    print(f"\n{'='*60}")
    print("✅ バックグラウンドタスクテスト完了")
    print(f"{'='*60}")


def main():
    parser = argparse.ArgumentParser(
        description="Responses API 動作確認",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python test_responses_api.py
  python test_responses_api.py --message "Azure AI Foundry とは？"
  python test_responses_api.py --model gpt-4o-mini
  python test_responses_api.py --multi-turn
  python test_responses_api.py --background
  python test_responses_api.py --all
        """
    )
    parser.add_argument(
        "--model", "-m",
        help="使用するモデル名（デフォルト: 環境変数 DEFAULT_MODEL）"
    )
    parser.add_argument(
        "--message",
        default="Azure AI Foundry の主な機能を簡潔に説明してください。",
        help="送信するメッセージ"
    )
    parser.add_argument(
        "--multi-turn",
        action="store_true",
        help="マルチターン会話をテスト"
    )
    parser.add_argument(
        "--background",
        action="store_true",
        help="バックグラウンドタスクをテスト"
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
    print(f"Model: {model}")
    print(f"API Version: {config.api_version}")
    
    # クライアント作成
    client = ResponsesAPIClient(
        base_url=config.base_url_responses,
        api_key=config.api_key,
        api_version=config.api_version
    )
    
    try:
        if args.all:
            test_simple_response(client, model, args.message)
            test_multi_turn(client, model)
            test_background_task(client, model)
        elif args.multi_turn:
            test_multi_turn(client, model)
        elif args.background:
            test_background_task(client, model)
        else:
            test_simple_response(client, model, args.message)
        
        print(f"\n{'='*60}")
        print("✅ すべてのテストが正常に完了しました")
        print(f"{'='*60}")
        
    except requests.exceptions.HTTPError as e:
        print(f"\n❌ HTTP エラー: {e}", file=sys.stderr)
        if e.response is not None:
            print(f"   Response: {e.response.text}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"\n❌ エラー: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
