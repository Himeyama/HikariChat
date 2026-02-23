# プロジェクト構造

## 概要

このプロジェクトは、C# WPF をバックエンドとし、WebView2 を使用して UI を構築したデスクトップアプリケーションです。

## ディレクトリ構成

```
HikariChat/
├── HikariChat/                          # C# WPF アプリケーション本体
│   ├── Assets/
│   │   ├── EditorUI/              # frontend/ からビルドされた WebView2 用フロントエンド UI
│   │   ├── index.html         # メイン UI 画面
│   │   ├── settings.html      # 設定画面
│   │   ├── vite.svg
│   │   ├── assets/            # ビルドされたJS/CSSなどのアセット
│   │   └── App.ico                # アプリケーションアイコン
│   ├── Properties/                # アプリケーション設定ファイル
│   ├── Strings/                   # リソース文字列
│   ├── ApiSettings.cs             # API 設定クラス
│   ├── SimpleApiServer.cs         # 簡易 API サーバー実装
│   ├── App.xaml / App.xaml.cs     # アプリケーションエントリーポイント
│   ├── MainWindow.xaml / .xaml.cs # メインウィンドウ (WebView2 ホスト)
│   ├── SettingsWindow.xaml / .xaml.cs  # 設定ウィンドウ
│   └── HikariChat.csproj                # プロジェクトファイル
├── frontend/                      # フロントエンドのソースコードとビルド設定
├── docs/                          # ドキュメント
├── dev.ps1                        # 開発用 PowerShell スクリプト
├── setup.ps1                      # セットアップ用 PowerShell スクリプト
├── installer.nsh                  # インストーラー設定 (NSIS)
└── README.md / README.en.md       # README (日本語/英語)
```

## 技術スタック

| 層 | 技術 |
|---|---|
| バックエンド | C# / WPF (.NET) |
| UI ホスティング | WebView2 |
| フロントエンド | HTML5 / CSS3 / JavaScript (Vanilla) |

## 主要コンポーネント

### HikariChat/HikariChat/MainWindow.xaml
WebView2 コントロールを含むメインウィンドウ。`Assets/EditorUI/index.html` を読み込みます。

### HikariChat/HikariChat/SimpleApiServer.cs
フロントエンドとの通信用の簡易 HTTP サーバーを実装しています。

### HikariChat/HikariChat/ApiSettings.cs
API エンドポイントや設定を管理します。

### HikariChat/Assets/EditorUI/
`frontend/` ディレクトリからビルドされ、WebView2 で表示されるフロントエンド UI ファイル群。

## 開発フロー

1. **セットアップ**: `setup.ps1` を実行して依存関係をインストール
2. **開発**: `dev.ps1` を使用して開発サーバーを起動
3. **ビルド**: Visual Studio または `dotnet build` でビルド
