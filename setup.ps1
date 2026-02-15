# 引数を取得
param (
    [string]$inputArgument,
    [string]$name
)

# ヘルプメッセージを表示する関数
function Show-Help {
    Write-Host "使い方: スクリプト名 [オプション]"
    Write-Host ""
    Write-Host "オプション:"
    Write-Host "  --name      名前を設定します"
    Write-Host "  --help      ヘルプを表示します"
    Write-Host "  --version   バージョン情報を表示します"
}

function Update-FileContent {
    param (
        [string]$filePath,
        [string]$oldValue,
        [string]$newValue
    )
    $content = Get-Content -Path $filePath -Raw
    $content = $content -replace $oldValue, $newValue
    $content = $content.TrimEnd()
    Set-Content -Path $filePath -Value $content -NoNewline
    Write-Host "$filePath 内の参照を更新しました"
}

function Rename-App {
    param (
        [string]$name
    )
    $oldName = ""
    # README.mdファイルを読み込む
    $content = Get-Content -Path 'README.md'

    # 最初の行を取得
    $firstLine = $content[0]

    # 正規表現で文字列を抽出
    if ($firstLine -match '^#\s*(.+)$') {
        $oldName = $matches[1]
    }

    # 削除
    if (Test-Path -Path .\$oldName\bin) {
        Remove-Item -Path .\$oldName\bin -Recurse -Force
    }
    if (Test-Path -Path .\$oldName\obj) {
        Remove-Item -Path .\$oldName\obj -Recurse -Force
    }
    if (Test-Path -Path .\$oldName\publish) {
        Remove-Item -Path .\$oldName\publish -Recurse -Force
    }

    # Write-Host "アプリケーションの名前を '$newName' に変更しました"
    if (Test-Path -Path $name) {
        Write-Host "指定された名前 '$name' は既に存在します。別の名前を指定してください"
        return
    }

    if (Test-Path -Path $oldName) {
        Move-Item -Path $oldName -Destination $name
        Write-Host "アプリケーションの名前を '$name' に変更しました"
    } else {
        Write-Host "アプリケーション '$oldName' が見つかりません"
        return
    }

    if (Test-Path -Path "$name\$oldName.csproj") {
        Move-Item -Path "$name\$oldName.csproj" -Destination "$name\$name.csproj"
        Write-Host "プロジェクトファイルの名前を '$name.csproj' に変更しました"
    } else {
        Write-Host "プロジェクトファイル '$name\$name.csproj' が見つかりません"
        return
    }

    Update-FileContent -filePath "README.md" -oldValue $oldName -newValue $name
    Update-FileContent -filePath "README.en.md" -oldValue $oldName -newValue $name
    Update-FileContent -filePath "dev.ps1" -oldValue $oldName -newValue $name
    Update-FileContent -filePath "$name\App.xaml" -oldValue $oldName -newValue $name
    Update-FileContent -filePath "$name\App.xaml.cs" -oldValue $oldName -newValue $name
    Update-FileContent -filePath "$name\MainWindow.xaml" -oldValue $oldName -newValue $name
    Update-FileContent -filePath "$name\MainWindow.xaml.cs" -oldValue $oldName -newValue $name
    Update-FileContent -filePath "$name\Package.appxmanifest" -oldValue $oldName -newValue $name
    Update-FileContent -filePath "$name\$name.csproj" -oldValue $oldName -newValue $name
}

# 引数が '--help' であればヘルプを表示
if ($inputArgument -eq '--help') {
    Show-Help
} elseif ($inputArgument -eq '--version') {
    Write-Host "バージョン 1.0.0"
} elseif ($inputArgument -eq '--name') {
    if ($name -cmatch '^[A-Z][a-zA-Z0-9]*$') {
        Rename-App $name
    } else {
        Write-Host "不正な名前の形式です。名前は大文字で始まり、英数字のみを含む必要があります"
    }
}else {
    Write-Host "無効な引数です。'--help' を入力して使用法を確認してください。"
}