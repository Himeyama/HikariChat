# テスト自動化ガイド

このドキュメントでは、CApp のテスト自動化方法について説明します。

## 概要

CApp は以下の方法でテスト自動化をサポートしています：

1. **HTTP API 経由でのテスト**: サーバーに対して GET/POST リクエストを送信
2. **WebView2 操作**: C# 経由でブラウザ内の JavaScript を実行
3. **サーバーの制御**: プロセスの起動・終了（kill）

## サーバー仕様

- **デフォルトポート**: `30078`
- **ベース URL**: `http://localhost:30078/`

## API エンドポイント

### チャット API

```http
POST /api/chat
Content-Type: application/json

{
  "messages": [
    { "role": "user", "content": "こんにちは" }
  ],
  "apiKey": "your-api-key",
  "apiEndpoint": "https://api.openai.com/v1/chat/completions",
  "model": "gpt-4o-mini",
  "apiType": "chat_completions",
  "endpointPreset": "openai",
  "streaming": true,
  "mcpEnabled": false
}
```

### MCP ツール実行 API

```http
POST /api/mcp/execute
Content-Type: application/json

{
  "name": "server/tool_name",
  "arguments": { "arg1": "value1" }
}
```

### テスト自動化 API

#### JavaScript 実行

WebView2 内で任意の JavaScript を実行します。

```http
POST /api/test/execute-script
Content-Type: application/json

{
  "script": "document.title"
}
```

レスポンス：
```json
{
  "result": "ひかりチャット"
}
```

#### チャット履歴取得

現在のチャット履歴を取得します。

```http
GET /api/test/chat-history
```

レスポンス：
```json
{
  "history": "{\"tab-chat-1\": {\"conversationHistory\": [...]}}"
}
```

## サーバーの制御

### サーバーの起動

アプリを起動すると、自動的にサーバーが起動します。

```powershell
# 例：dotnet run で起動
dotnet run --project CApp/CApp.csproj
```

### サーバーの停止

サーバーを終了する場合は、プロセスを kill します。

```powershell
# プロセス名で kill
Get-Process CApp | Stop-Process -Force

# または PID で kill
Stop-Process -Id <PID> -Force
```

## テスト例

### 例 1: 単純なチャット送信

```powershell
$body = @{
    messages = @(
        @{ role = "user"; content = "こんにちは" }
    )
    apiKey = "test-key"
    apiEndpoint = "https://api.openai.com/v1/chat/completions"
    model = "gpt-4o-mini"
    apiType = "chat_completions"
    endpointPreset = "openai"
    streaming = $false
    mcpEnabled = $false
} | ConvertTo-Json -Depth 10

$response = Invoke-RestMethod -Uri "http://localhost:30078/api/chat" -Method Post -Body $body -ContentType "application/json"
$response.choices[0].message.content
```

### 例 2: JavaScript 実行でページタイトル取得

```powershell
$body = @{
    script = "document.title"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "http://localhost:30078/api/test/execute-script" -Method Post -Body $body -ContentType "application/json"
$response.result
```

### 例 3: チャット履歴の取得

```powershell
$response = Invoke-RestMethod -Uri "http://localhost:30078/api/test/chat-history" -Method Get
$history = $response.history | ConvertFrom-Json
$history."tab-chat-1".conversationHistory
```

### 例 4: 要素の存在確認

```powershell
# チャット入力フィールドの存在確認
$body = @{
    script = "document.querySelector('.chat-input-area textarea') !== null"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "http://localhost:30078/api/test/execute-script" -Method Post -Body $body -ContentType "application/json"
$exists = [bool]$response.result
Write-Host "入力フィールド存在：$exists"
```

### 例 5: メッセージ数の取得

```powershell
$body = @{
    script = @"
(() => {
  const tabs = window.chrome.webview.targetEnvironment?.tabs || {};
  const activeTab = tabs['tab-chat-1'];
  if (!activeTab) return 0;
  return activeTab.conversationHistory.length;
})()
"@
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "http://localhost:30078/api/test/execute-script" -Method Post -Body $body -ContentType "application/json"
$messageCount = [int]$response.result
Write-Host "メッセージ数：$messageCount"
```

## 完全なテストスクリプト例

```powershell
# テスト自動化スクリプト例

$serverUrl = "http://localhost:30078"

# 1. アプリ起動（別プロセス）
Start-Process "dotnet" -ArgumentList "run", "--project", "CApp/CApp.csproj"
Start-Sleep -Seconds 5  # サーバー起動を待つ

try {
    # 2. サーバーが応答するか確認
    $maxRetries = 10
    $retryCount = 0
    while ($retryCount -lt $maxRetries) {
        try {
            Invoke-RestMethod -Uri "$serverUrl/" -Method Get -TimeoutSec 2 | Out-Null
            Write-Host "サーバー起動確認 OK"
            break
        } catch {
            $retryCount++
            Start-Sleep -Seconds 1
        }
    }
    
    if ($retryCount -eq $maxRetries) {
        throw "サーバーが起動しませんでした"
    }
    
    # 3. 初期状態の確認
    $body = @{ script = "document.title" } | ConvertTo-Json
    $response = Invoke-RestMethod -Uri "$serverUrl/api/test/execute-script" -Method Post -Body $body -ContentType "application/json"
    Write-Host "ページタイトル：$($response.result)"
    
    # 4. チャット入力フィールドの確認
    $body = @{ script = "document.querySelector('.chat-input-area textarea') !== null" } | ConvertTo-Json
    $response = Invoke-RestMethod -Uri "$serverUrl/api/test/execute-script" -Method Post -Body $body -ContentType "application/json"
    if ([bool]$response.result) {
        Write-Host "チャット入力フィールド：存在確認 OK"
    } else {
        Write-Host "チャット入力フィールド：存在確認 NG" -ForegroundColor Red
    }
    
    # 5. 初期メッセージ数の確認
    $body = @{ script = "const t = window.chrome.webview.targetEnvironment?.tabs || {}; return (t['tab-chat-1']?.conversationHistory || []).length;" } | ConvertTo-Json
    $response = Invoke-RestMethod -Uri "$serverUrl/api/test/execute-script" -Method Post -Body $body -ContentType "application/json"
    Write-Host "初期メッセージ数：$($response.result)"
    
    Write-Host "テスト完了" -ForegroundColor Green
    
} finally {
    # 6. アプリを閉じる
    Get-Process CApp -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host "アプリを終了しました"
}
```

## LLM によるテスト確認

LLM がテスト結果を確認する際は、以下の形式で出力します：

```json
{
  "test_name": "初期状態確認",
  "status": "pass|fail",
  "details": {
    "expected": "期待値",
    "actual": "実際の値",
    "message": "追加メッセージ"
  }
}
```

### 確認項目例

1. **アプリ起動確認**: サーバーが応答するか
2. **UI 要素確認**: 必要な要素が存在するか
3. **チャット機能**: メッセージ送信・受信ができるか
4. **ツール実行**: MCP ツールが実行できるか
5. **エラー処理**: エラー時に適切に処理されるか

## 注意事項

- テスト実行後は必ずアプリを kill して終了してください
- 並列テスト実行は避けてください（ポート競合の可能性があります）
- 実際の API キーを使用する場合は、環境変数などで管理してください
