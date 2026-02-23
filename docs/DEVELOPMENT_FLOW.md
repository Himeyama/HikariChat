# 開発フロー

## ブランチ戦略

- 機能開発は必ず **feature ブランチ** から行ってください
- `main` ブランチには直接コミットしないでください

## コミットとプッシュ

- **こまめに commit してください**
  - 小さな単位で機能を実装し、都度コミットしましょう
  - 1 つのコミットは単一の目的に絞ってください

- **こまめに push してください**
  - 作業中のコードは頻繁にリモートにプッシュしましょう
  - バックアップとチームメンバーとの共有のためです

## テスト実行
以下の方法で GUI アプリを停止します。

```ps1
Stop-Process -Name HikariChat
```

停止しているかは次のように確認します。

```ps1
Get-Process -Name HikariChat
```

開始は次のように行います。終了と開始は 1 ステップずつ行います。

```ps1
.\dev run
```

## 基本的なワークフロー

```powershell
# 1. feature ブランチを作成
git checkout -b feature/your-feature-name

# 2. 開発を行う（こまめに commit）
git add .
git commit -m "feat: 実装内容の説明"

# 3. こまめに push
git push origin feature/your-feature-name
```

## コミットメッセージの例

```
feat: 新機能の追加
fix: バグ修正
docs: ドキュメント更新
refactor: リファクタリング
```
