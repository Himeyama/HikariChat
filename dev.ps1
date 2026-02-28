# set -euo pipefail の PowerShell 版
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

$csproj = ".\HikariChat.UI\HikariChat.UI.csproj"
$appName = "HikariChat"
$publisher = "ひかり"
$execFile = "HikariChat.exe"
$version = (Get-Date).ToString("yy.M.d")
$date = (Get-Date).ToString("yyyyMMdd")
$publishDir = "HikariChat.UI\publish"
$muiIcon = "HikariChat.UI\Assets\App.ico"

$startMenuPath = [Environment]::GetFolderPath("Programs")

if ($appName -eq "") { throw "AppName is not defined." }
$appPath = "$env:localappdata\$appName"
$startMenuPath = Join-Path $startMenuPath $appName

$arg = if ($Args.Count -gt 0) { $Args[0] } else { $null }

# ============================================================
# ユーティリティ関数
# ============================================================

function ShowProgress($message, $icon = "⏳") {
    Write-Host "`n$icon $message" -ForegroundColor Cyan
}

function CreateShortcut($link, $target) {
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($link)
    $Shortcut.TargetPath = $target
    $Shortcut.Save()
}

# ============================================================
# メインコマンド
# ============================================================

function Run() {
    ShowProgress "アプリを起動します..." "🚀"
    dotnet run --project $csproj
}

function OccupyPort() {
    $port = 29000
    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add("http://localhost:${port}/")
    
    ShowProgress "ポータル (ポート $port) を占有しました。Ctrl+C で解放します..." "🏰"
    $listener.Start()
    
    try {
        while ($true) { Start-Sleep -Seconds 1 }
    } finally {
        $listener.Stop()
        Write-Host "`n🔓 ポータルを解放しました。お疲れ様でした！" -ForegroundColor Yellow
    }
}

function Publish() {
    ShowProgress "ナレッジベース (プロジェクト) を解析中..." "🔍"
    
    ShowProgress "ソースコードをコンパイルし、パッケージ化しています..." "📦"
    dotnet publish $csproj -c Release -p:Version=$version -v q
    
    ShowProgress "パブリッシュ完了！実行ファイルが準備できました。" "✅"
}

function Zip() {
    Publish
    
    ShowProgress "アーカイブの作成準備をしています..." "📋"
    $zipPath = "$appName-$version.zip"
    
    if (Test-Path $zipPath) {
        ShowProgress "古いアーカイブをスキャンして削除しています..." "🧹"
        Remove-Item $zipPath
    }
    
    ShowProgress "データを高度に圧縮して ZIP に変換中..." "🤐"
    Compress-Archive -Path $publishDir -DestinationPath $zipPath
    
    ShowProgress "アーカイブが完成しました！ ($zipPath)" "🎉"
}

function Install() {
    ShowProgress "システムへのデプロイシーケンスを開始します" "🛸"
    Publish

    # アプリをコピー
    if (Test-Path $appPath) {
        ShowProgress "既存のソフトウェア（$appPath）をクリーニングして初期化中..." "🧹"
        Remove-Item -Recurse -Path $appPath -Force
    }
    
    ShowProgress "新しいソフトウェア（$publishDir）をシステム（$appPath）へ転送中..." "🚚"
    Copy-Item -Path $publishDir -Recurse -Destination $appPath

    # ショートカットの作成
    if (-not (Test-Path $startMenuPath)) {
        ShowProgress "新しいショートカットフォルダを構成中..." "📂"
        $null = New-Item -Path $startMenuPath -ItemType Directory
    }
    
    $shortcutPath = Join-Path $startMenuPath "${appName}.lnk"
    $targetPath = Join-Path $appPath $execFile
    
    ShowProgress "OS とのリンク (ショートカット) を作成しています..." "✨"
    CreateShortcut $shortcutPath $targetPath
    
    ShowProgress "インストールに成功しました。" "🎊"
}

function Uninstall() {
    ShowProgress "痕跡を残さずクリーニングを開始します..." "🧹"
    
    # ショートカットの削除
    if (Test-Path $startMenuPath) {
        ShowProgress "ショートカットを削除中..." "🗑️"
        Remove-Item -Recurse -Path $startMenuPath -Force
    }

    # アプリを削除
    if (Test-Path $appPath) {
        ShowProgress "システム上のアプリケーション本体 ($appPath) を抹消しています..." "💨"
        Remove-Item -Path $appPath -Recurse -Force
    }
    
    ShowProgress "アンインストール完了。" "👋"
}

function Pack() {
    Publish
    
    ShowProgress "最終形態 (インストーラー) の錬成を開始します..." "🔮"
    
    $nsisPath = "C:\Program Files (x86)\NSIS\makensis.exe"
    if (-not (Test-Path $nsisPath)) {
        throw "NSIS (makensis.exe) が見つかりません: $nsisPath"
    }

    ShowProgress "アセットのサイズを計測しています..." "📏"
    $publishItems = Get-ChildItem $publishDir -Force -Recurse -ErrorAction SilentlyContinue
    $size = [Math]::Round(($publishItems | Measure-Object Length -Sum).Sum / 1KB, 0, [MidpointRounding]::AwayFromZero)
    Write-Host "📊 合計サイズ: $size KB" -ForegroundColor Gray

    ShowProgress "NSIS コンパイラを呼び出し、セットアップファイルを構築中..." "🛠️"
    & $nsisPath /DVERSION="$version" /DDATE="$date" /DSIZE="$size" /DMUI_ICON="$muiIcon" /DMUI_UNICON="$muiIcon" /DPUBLISH_DIR="$publishDir" /DPRODUCT_NAME="$appName" /DEXEC_FILE="$execFile" /DPUBLISHER="$publisher" installer.nsh
    
    ShowProgress "インストーラーが完了しました！" "🎉"
}

# ============================================================
# エントリポイント
# ============================================================

Write-Host "開発アシスタント ✨`n" -ForegroundColor Gray

switch ($arg) {
    "run" { Run }
    "publish" { Publish }
    "zip" { Zip }
    "install" { Install }
    "uninstall" { Uninstall }
    "pack" { Pack }
    "occupy-port" { OccupyPort }
    Default {
        Write-Host "使用可能なコマンドはこちらです 🤖" -ForegroundColor Magenta
        Write-Host " .\dev.ps1 [run | publish | zip | install | uninstall | pack | occupy-port]" -ForegroundColor White
    }
}

Write-Host "`n✨ 全てのプロセスが終了しました！ ✨`n" -ForegroundColor Gray
