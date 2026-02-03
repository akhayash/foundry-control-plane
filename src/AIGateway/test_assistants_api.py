#!/usr/bin/env python3
"""
Assistants API 動作確認スクリプト

AI Gateway 経由で Azure AI Agent Service (OpenAI Assistants API 互換) をテストします。

Note: 新規開発では Responses API が推奨されています。
      このスクリプトは既存のエージェントやツール（Code Interpreter, File Search）を
      使用する場合に有用です。
"""

import argparse
import sys
import time

import requests

from config import get_config


class AssistantsAPIClient:
    """Assistants API クライアント"""
    
    def __init__(self, base_url: str, api_key: str, api_version: str):
        self.base_url = base_url
        self.api_version = api_version
        self.headers = {
            "api-key": api_key,
            "Content-Type": "application/json"
        }
    
    def _url(self, path: str) -> str:
        return f"{self.base_url}{path}?api-version={self.api_version}"
    
    def create_assistant(self, name: str, model: str, instructions: str) -> dict:
        """Assistant を作成"""
        response = requests.post(
            self._url("/assistants"),
            headers=self.headers,
            json={
                "name": name,
                "model": model,
                "instructions": instructions
            }
        )
        response.raise_for_status()
        return response.json()
    
    def delete_assistant(self, assistant_id: str) -> dict:
        """Assistant を削除"""
        response = requests.delete(
            self._url(f"/assistants/{assistant_id}"),
            headers=self.headers
        )
        response.raise_for_status()
        return response.json()
    
    def list_assistants(self) -> dict:
        """Assistant 一覧を取得"""
        response = requests.get(
            self._url("/assistants"),
            headers=self.headers
        )
        response.raise_for_status()
        return response.json()
    
    def create_thread(self) -> dict:
        """Thread を作成"""
        response = requests.post(
            self._url("/threads"),
            headers=self.headers,
            json={}
        )
        response.raise_for_status()
        return response.json()
    
    def add_message(self, thread_id: str, content: str, role: str = "user") -> dict:
        """Thread にメッセージを追加"""
        response = requests.post(
            self._url(f"/threads/{thread_id}/messages"),
            headers=self.headers,
            json={
                "role": role,
                "content": content
            }
        )
        response.raise_for_status()
        return response.json()
    
    def create_run(self, thread_id: str, assistant_id: str) -> dict:
        """Run を作成"""
        response = requests.post(
            self._url(f"/threads/{thread_id}/runs"),
            headers=self.headers,
            json={
                "assistant_id": assistant_id
            }
        )
        response.raise_for_status()
        return response.json()
    
    def get_run(self, thread_id: str, run_id: str) -> dict:
        """Run のステータスを取得"""
        response = requests.get(
            self._url(f"/threads/{thread_id}/runs/{run_id}"),
            headers=self.headers
        )
        response.raise_for_status()
        return response.json()
    
    def wait_for_run(
        self, 
        thread_id: str, 
        run_id: str, 
        timeout: int = 60,
        poll_interval: float = 1.0
    ) -> dict:
        """Run の完了を待機"""
        terminal_states = {"completed", "failed", "cancelled", "expired"}
        start_time = time.time()
        
        while True:
            run = self.get_run(thread_id, run_id)
            status = run["status"]
            
            if status in terminal_states:
                return run
            
            if time.time() - start_time > timeout:
                raise TimeoutError(f"Run did not complete within {timeout} seconds")
            
            time.sleep(poll_interval)
    
    def get_messages(self, thread_id: str) -> dict:
        """Thread のメッセージを取得"""
        response = requests.get(
            self._url(f"/threads/{thread_id}/messages"),
            headers=self.headers
        )
        response.raise_for_status()
        return response.json()


def test_full_workflow(client: AssistantsAPIClient, model: str, cleanup: bool = True):
    """完全なワークフローをテスト"""
    
    print(f"\n{'='*60}")
    print("Assistants API ワークフローテスト")
    print(f"{'='*60}")
    
    assistant_id = None
    thread_id = None
    
    try:
        # 1. Assistant 作成
        print("\n1. Creating Assistant...")
        assistant = client.create_assistant(
            name="test-assistant",
            model=model,
            instructions="あなたは親切なアシスタントです。日本語で回答してください。"
        )
        assistant_id = assistant["id"]
        print(f"   ✅ Assistant ID: {assistant_id}")
        
        # 2. Thread 作成
        print("\n2. Creating Thread...")
        thread = client.create_thread()
        thread_id = thread["id"]
        print(f"   ✅ Thread ID: {thread_id}")
        
        # 3. Message 追加
        print("\n3. Adding Message...")
        user_message = "Azure AI Foundry の主な機能を3つ教えてください。"
        message = client.add_message(thread_id, user_message)
        print(f"   ✅ Message ID: {message['id']}")
        print(f"   User: {user_message}")
        
        # 4. Run 作成
        print("\n4. Creating Run...")
        run = client.create_run(thread_id, assistant_id)
        run_id = run["id"]
        print(f"   ✅ Run ID: {run_id}")
        print(f"   Initial Status: {run['status']}")
        
        # 5. Run 完了待機
        print("\n5. Waiting for Run to complete...")
        completed_run = client.wait_for_run(thread_id, run_id)
        print(f"   ✅ Final Status: {completed_run['status']}")
        
        if completed_run["status"] != "completed":
            print(f"   ❌ Run failed with status: {completed_run['status']}")
            return
        
        # 6. Messages 取得
        print("\n6. Retrieving Messages...")
        messages = client.get_messages(thread_id)
        
        print(f"\n{'='*60}")
        print("Conversation:")
        print("-" * 60)
        
        # メッセージを時系列順にソート
        sorted_messages = sorted(messages["data"], key=lambda m: m["created_at"])
        
        for msg in sorted_messages:
            role = msg["role"].upper()
            content = msg["content"][0]["text"]["value"] if msg["content"] else "(empty)"
            print(f"\n[{role}]")
            print(content)
        
        print(f"\n{'='*60}")
        print("✅ ワークフローテスト完了")
        print(f"{'='*60}")
        
    finally:
        if cleanup and assistant_id:
            print("\n7. Cleanup...")
            try:
                client.delete_assistant(assistant_id)
                print(f"   ✅ Assistant {assistant_id} deleted")
            except Exception as e:
                print(f"   ⚠️ Cleanup failed: {e}")


def list_assistants(client: AssistantsAPIClient):
    """既存の Assistant 一覧を表示"""
    
    print(f"\n{'='*60}")
    print("Assistants 一覧")
    print(f"{'='*60}")
    
    assistants = client.list_assistants()
    
    if not assistants.get("data"):
        print("(No assistants found)")
        return
    
    for asst in assistants["data"]:
        print(f"\n  ID: {asst['id']}")
        print(f"  Name: {asst.get('name', '(unnamed)')}")
        print(f"  Model: {asst['model']}")
        print(f"  Created: {asst['created_at']}")


def main():
    parser = argparse.ArgumentParser(
        description="Assistants API 動作確認",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python test_assistants_api.py
  python test_assistants_api.py --model gpt-4o-mini
  python test_assistants_api.py --list
  python test_assistants_api.py --no-cleanup
        """
    )
    parser.add_argument(
        "--model", "-m",
        help="使用するモデル名（デフォルト: 環境変数 DEFAULT_MODEL）"
    )
    parser.add_argument(
        "--list", "-l",
        action="store_true",
        help="既存の Assistant 一覧を表示"
    )
    parser.add_argument(
        "--no-cleanup",
        action="store_true",
        help="テスト後に Assistant を削除しない"
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
    print(f"Model: {model}")
    
    # クライアント作成
    client = AssistantsAPIClient(
        base_url=config.base_url_chat,
        api_key=config.api_key,
        api_version=config.api_version
    )
    
    try:
        if args.list:
            list_assistants(client)
        else:
            test_full_workflow(client, model, cleanup=not args.no_cleanup)
        
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
