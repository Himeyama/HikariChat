# CLAUDE.md - HikariChat

## プロジェクト概要

HikariChat は、複数の LLM プロバイダー (OpenAI, Anthropic, Google Gemini, xAI, DeepSeek, OpenRouter, Hugging Face, Ollama 等) に接続できるデスクトップチャットクライアント。

## アーキテクチャ

- **HikariChat.UI** — WinUI 3 デスクトップアプリ (C# / .NET 9 / Windows App SDK)。WebView2 で frontend を表示
- **HikariChat.Server** — バックエンドライブラリ (C# / .NET 9)。API 管理、MCP 連携、Ollama クライアント等
- **frontend** — React + TypeScript + Vite。チャット UI と設定画面。ビルド出力先は `HikariChat.UI/Assets/EditorUI/`

## 開発環境

- OS: Windows
- シェル: PowerShell (使用可能コマンド: `Get-ChildItem`, `Write-Output`, `Set-Location` 等)
- .NET 9 (TargetFramework: `net9.0-windows10.0.22621.0`)
- Node.js (frontend)

## よく使うコマンド
> アプリのビルド
```ps1
# UI をビルドし、アプリをビルド
npm run build && .\dev.ps1 build
```

> フロントエンドをビルドしてアプリを起動
```ps1
# UI をビルドし、アプリを起動
npm run build --prefix frontend && .\dev.ps1 run
```

## コミットとプッシュ

```ps1
# UI をビルドし、アプリをビルド
npm run build && .\dev.ps1 build

# ビルドが成功した場合
git commit -m <コミットメッセージ>

# コミット後にプッシュ
git push origin master
```

## プロジェクト構成

```
CApp/
├── HikariChat.sln            # ソリューションファイル
├── HikariChat.UI/            # WinUI 3 アプリ (エントリポイント)
│   ├── MainWindow.xaml.cs
│   ├── SettingsWindow.xaml.cs
│   └── Assets/EditorUI/      # frontend ビルド出力先
├── HikariChat.Server/        # バックエンドライブラリ
│   ├── SimpleApiServer.cs    # API サーバー
│   ├── ApiSettings.cs        # API 設定モデル
│   ├── ApiSettingsManager.cs # API 設定管理
│   ├── McpManager.cs         # MCP 連携
│   └── OllamaClient.cs      # Ollama クライアント
├── frontend/                 # React フロントエンド
│   ├── src/
│   │   ├── App.tsx           # メインチャット画面
│   │   ├── SettingsApp.tsx   # 設定画面
│   │   └── chatUtils.ts     # チャットユーティリティ
│   ├── index.html            # チャット画面エントリ
│   └── settings.html         # 設定画面エントリ
├── dev.ps1                   # 開発用 PowerShell スクリプト
└── installer.nsh             # NSIS インストーラー定義
```

## 注意事項

- frontend のビルド出力は `HikariChat.UI/Assets/EditorUI/` に直接書き出される (vite.config.ts で設定済み)
- Vite のマルチページ構成: `index.html` (チャット) と `settings.html` (設定)
- コミットメッセージは日本語で記述
