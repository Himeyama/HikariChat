# C# コーディング規約

## 1. 型の明示

`var` は使用しない。すべての変数は明示的な型で宣言する。

```csharp
// ✅ 正しい
string name = "Alice";
int count = 42;

// ❌ 誤り
var name = "Alice";
var count = 42;
```

ループ変数も例外ではない。

```csharp
// ✅ 正しい
foreach (string item in items)
{
    Console.WriteLine(item);
}

// ❌ 誤り
foreach (var item in items)
{
    Console.WriteLine(item);
}
```

---

## 2. コレクションの初期化

### コレクションリテラルを優先する

コレクションの初期化にはコレクションリテラル (`[]`) を優先して使用する。

```csharp
// ✅ 正しい
List<string> names = ["Alice", "Bob", "Carol"];
int[] scores = [10, 20, 30];
string[] empty = [];

// ❌ 避けること
List<string> names = new List<string> { "Alice", "Bob", "Carol" };
int[] scores = new int[] { 10, 20, 30 };
string[] empty = Array.Empty<string>();
```

空のコレクションもコレクションリテラルで統一する。

```csharp
// ✅ 正しい
List<Order> orders = [];
Dictionary<string, int> counts = [];
```

### 型名が既に定義されている場合は new() を使用する

変数の左辺に型名が明示されている場合、右辺のコンストラクタ呼び出しは `new()` と記述する。

```csharp
// ✅ 正しい
CreateUserRequest request = new();
OrderService service = new(repository, logger);
StringBuilder builder = new();

// ❌ 避けること
CreateUserRequest request = new CreateUserRequest();
OrderService service = new OrderService(repository, logger);
```

コレクションにコレクションリテラルが使えない場合（初期容量の指定など）も同様に `new()` を使用する。

```csharp
// ✅ 正しい（初期容量指定）
List<string> names = new(capacity: 100);
Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
```

---

## 3. 命名規則

### クラス・構造体・インターフェース・列挙型

PascalCase を使用する。

```csharp
public class UserAccount { }
public interface IUserRepository { }
public enum OrderStatus { Pending, Shipped, Delivered }
```

### メソッド・プロパティ

PascalCase を使用する。

```csharp
public string GetFullName() { }
public int TotalCount { get; set; }
```

### フィールド・ローカル変数・引数

camelCase を使用する。プライベートフィールドはアンダースコアプレフィックス (`_`) を付ける。

```csharp
private int _retryCount;
private readonly string _connectionString;

public void ProcessOrder(string orderId, int quantity)
{
    int itemCount = quantity;
}
```

### 定数

PascalCase を使用する。

```csharp
public const int MaxRetryCount = 3;
private const string DefaultSchema = "dbo";
```

---

## 4. コードレイアウト

### インデント

スペース4つを使用する（タブ文字は使用しない）。

### 波括弧

常に独立した行に記述する（Allman スタイル）。

```csharp
// ✅ 正しい
if (isValid)
{
    Execute();
}

// ❌ 誤り
if (isValid) {
    Execute();
}
```

1行でも波括弧を省略しない。

```csharp
// ✅ 正しい
if (isValid)
{
    return;
}

// ❌ 誤り
if (isValid)
    return;
```

### 1ファイル1クラス

原則として1つのファイルに1つのクラスを定義する。ファイル名はクラス名と一致させる。

---

## 5. クラス設計

### メンバーの宣言順序

以下の順序でメンバーを宣言する。

1. 定数
2. 静的フィールド
3. インスタンスフィールド
4. コンストラクタ
5. プロパティ
6. パブリックメソッド
7. プロテクテッド・インターナルメソッド
8. プライベートメソッド

### アクセス修飾子

アクセス修飾子は必ず明示する。省略しない。

```csharp
// ✅ 正しい
private int _count;
public string Name { get; set; }

// ❌ 誤り
int _count;
string Name { get; set; }
```

---

## 6. メソッド

### 単一責任

1つのメソッドは1つの責務のみを持つ。行数の目安は50行以内。

### 引数

引数の数は4つ以内を目安とする。それ以上になる場合はパラメータオブジェクトへのまとめを検討する。

```csharp
// ✅ 正しい
public void CreateUser(CreateUserRequest request) { }

// 改善の余地あり
public void CreateUser(string firstName, string lastName, string email, string role, bool isActive) { }
```

### 戻り値

戻り値は明示的な型で宣言する。

```csharp
// ✅ 正しい
public List<Order> GetOrders(int userId)
{
    List<Order> orders = _repository.FindByUserId(userId);
    return orders;
}
```

---

## 7. null の扱い

### null 許容型

null を許容する場合は null 許容参照型 (`?`) を明示する。

```csharp
public string? MiddleName { get; set; }

public User? FindById(int id)
{
    // 見つからない場合は null を返す
}
```

### null チェック

`is null` / `is not null` を使用する。

```csharp
// ✅ 正しい
if (user is null)
{
    throw new ArgumentNullException(nameof(user));
}

// ❌ 避けること
if (user == null)
```

---

## 8. 例外処理

### 例外のキャッチ

`Exception` を直接キャッチするのは避け、具体的な例外型を指定する。

```csharp
// ✅ 正しい
try
{
    int result = int.Parse(input);
}
catch (FormatException ex)
{
    _logger.LogError(ex, "無効な入力値: {Input}", input);
    throw;
}

// ❌ 避けること
try { }
catch (Exception ex) { }  // 握りつぶし禁止
```

### 再スロー

例外を再スローする場合は `throw;` を使用し、スタックトレースを保持する。

```csharp
// ✅ 正しい
throw;

// ❌ 誤り（スタックトレースが失われる）
throw ex;
```

---

## 9. コメント・ドキュメント

### XMLドキュメントコメント

公開メンバーには XMLドキュメントコメントを記述する。

```csharp
/// <summary>
/// 指定されたユーザーIDに紐づく注文一覧を取得する。
/// </summary>
/// <param name="userId">ユーザーID。</param>
/// <returns>注文のリスト。存在しない場合は空のリストを返す。</returns>
public List<Order> GetOrdersByUserId(int userId)
{
    // 実装
}
```

### インラインコメント

コードの「何をしているか」ではなく「なぜそうするか」を説明する。

```csharp
// ✅ 良いコメント
// APIの仕様上、1秒以内に再試行すると429エラーになるため待機する
await Task.Delay(1000);

// ❌ 不要なコメント
// iをインクリメントする
i++;
```

---

## 10. 非同期処理

### 命名

非同期メソッドには `Async` サフィックスを付ける。

```csharp
public async Task<List<User>> GetUsersAsync() { }
public async Task SaveAsync(User user) { }
```

### `async void` の禁止

イベントハンドラを除き、`async void` は使用しない。必ず `async Task` を返す。

```csharp
// ✅ 正しい
public async Task ProcessAsync()
{
    await DoWorkAsync();
}

// ❌ 誤り
public async void ProcessAsync()
{
    await DoWorkAsync();
}
```

### `.Result` / `.Wait()` の禁止

デッドロックを引き起こす可能性があるため、非同期メソッドを同期的にブロックしない。

```csharp
// ✅ 正しい
List<User> users = await GetUsersAsync();

// ❌ 誤り
List<User> users = GetUsersAsync().Result;
```

---

## 11. LINQ

LINQのメソッドチェーンは複数行に分割して可読性を高める。

```csharp
// ✅ 正しい
List<string> activeUserNames = users
    .Where(u => u.IsActive)
    .OrderBy(u => u.LastName)
    .Select(u => u.FullName)
    .ToList();

// ❌ 避けること
List<string> activeUserNames = users.Where(u => u.IsActive).OrderBy(u => u.LastName).Select(u => u.FullName).ToList();
```

---

## 12. using ディレクティブ

`using` は名前空間の先頭にまとめて記述する。ファイルスコープの名前空間を使用する。

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyApp.Domain.Models;

namespace MyApp.Application.Services;

public class OrderService
{
    // 実装
}
```

---

## 13. コードレビューチェックリスト

| 項目 | 確認内容 |
|------|---------|
| 型の明示 | `var` を使用していないか |
| コレクション | コレクションリテラル `[]` を優先しているか |
| コンストラクタ | 左辺に型名がある場合 `new()` を使用しているか |
| 命名 | 規則に従っているか |
| 例外処理 | 握りつぶしがないか |
| 非同期 | `async void` や `.Result` を使っていないか |
| null安全 | null チェックが適切か |
| コメント | 公開メンバーにドキュメントコメントがあるか |
