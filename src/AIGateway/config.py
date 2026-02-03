"""
AI Gateway 設定モジュール

環境変数または .env ファイルから設定を読み込みます。
"""

import os
from dataclasses import dataclass
from pathlib import Path

from dotenv import load_dotenv

# .env ファイルを読み込み（存在する場合）
env_path = Path(__file__).parent / ".env"
load_dotenv(env_path)


@dataclass
class AIGatewayConfig:
    """AI Gateway 接続設定"""
    
    apim_endpoint: str
    api_key: str
    default_model: str
    api_version: str
    
    @property
    def base_url_chat(self) -> str:
        """Chat Completions / Assistants API 用ベース URL"""
        return f"{self.apim_endpoint}/openai/openai"
    
    @property
    def base_url_responses(self) -> str:
        """Responses API 用ベース URL"""
        # Azure OpenAI 経由の Responses API パス
        return f"{self.apim_endpoint}/openai/openai"
    
    def get_headers(self) -> dict:
        """API リクエスト用ヘッダー"""
        return {
            "api-key": self.api_key,
            "Content-Type": "application/json"
        }


def load_config() -> AIGatewayConfig:
    """環境変数から設定を読み込み"""
    
    apim_endpoint = os.getenv("APIM_ENDPOINT")
    api_key = os.getenv("APIM_API_KEY")
    
    if not apim_endpoint:
        raise ValueError(
            "APIM_ENDPOINT が設定されていません。\n"
            ".env ファイルを作成するか、環境変数を設定してください。\n"
            "例: cp .env.example .env"
        )
    
    if not api_key:
        raise ValueError(
            "APIM_API_KEY が設定されていません。\n"
            "Azure Portal → APIM → Subscriptions でキーを確認してください。"
        )
    
    return AIGatewayConfig(
        apim_endpoint=apim_endpoint.rstrip("/"),
        api_key=api_key,
        default_model=os.getenv("DEFAULT_MODEL", "gpt-4o"),
        api_version=os.getenv("API_VERSION", "2025-03-01-preview")
    )


# シングルトンとして設定をエクスポート
def get_config() -> AIGatewayConfig:
    """設定を取得（遅延ロード）"""
    return load_config()
