# Common_Hex.cs — 16 進文字列との相互変換

| 項目 | 内容 |
|---|---|
| ソース | [`src/Common_Hex.cs`](../../src/Common_Hex.cs) |
| 層 | 共通（特定の層に属さない） |
| 含まれる型 | `Hex`（public static class） |
| 使う側 | ほぼすべての層、`example/Program.cs`、`test/PcscIfTest` |

---

## 1. このファイルの役割

バイト列と 16 進文字列を相互に変換する小さな補助です。
ログ出力・IDm の文字列化・テストの入力解析など、層をまたいで使うので `Common_` にしています。

| 使っている場所 | 使っているメソッド | 用途 |
|---|---|---|
| `Felica.IdmToString`（層2） | `ToString(data, 8, false)` | IDm を `"0100000000000001"` の形にする |
| `TransceiveResult.ToString`（層3） | `ToString(data)` | 診断表示の生バイト列 |
| `Rcs300Transport.ValidateManagementResponse`（層4） | `ToString(data)` | 例外メッセージに応答を載せる |
| `Pcsc.DoWrite`（層1） | `SequenceEqual` | 書き込み後の読み戻しの比較 |
| `example/Program.cs` | `ToString(data)` | 読み書きしたデータの表示 |
| `test/PcscIfTest/Program.cs` | `Parse`、`ToString` | `write` コマンドの 16 進引数、結果の表示 |

---

## 2. メソッド

| メソッド | 公開 | 説明 |
|---|---|---|
| `ToString(byte[] data)` | public | 全体を空白区切りの 16 進大文字にする |
| `ToString(byte[] data, int length, bool separated)` | public | 先頭 `length` バイトを、空白区切りの有無を選んで 16 進大文字にする |
| `Parse(string text)` | public | 16 進文字列をバイト列にする |
| `SequenceEqual(byte[] a, byte[] b)` | internal | 2 つのバイト列が同じ内容か |

### 2.1 `ToString`

```csharp
public static string ToString(byte[] data, int length, bool separated)
{
    if (data == null) return "(null)";
    if (length < 0 || length > data.Length) length = data.Length;
    StringBuilder sb = new StringBuilder(length * 3);
    for (int i = 0; i < length; i++)
    {
        if (separated && i > 0) sb.Append(' ');
        sb.Append(data[i].ToString("X2"));
    }
    return sb.ToString();
}
```

| 呼び出し | 結果 |
|---|---|
| `Hex.ToString(new byte[] { 0x01, 0xAB })` | `"01 AB"` |
| `Hex.ToString(new byte[] { 0x01, 0xAB }, 2, false)` | `"01AB"` |
| `Hex.ToString(new byte[] { 0x01, 0xAB }, 1, true)` | `"01"` |
| `Hex.ToString(new byte[] { 0x01, 0xAB }, 99, true)` | `"01 AB"`（範囲外の長さは全体に丸める） |
| `Hex.ToString(new byte[0])` | `""` |
| `Hex.ToString(null)` | `"(null)"` |

- **例外を投げません。** ログや例外メッセージの組み立てに使うので、そこで新たな例外が起きないようにしています。
- 静的クラスのメソッド名が `ToString` ですが、`object.ToString()` の上書きではありません。
  `Hex.ToString(bytes)` と型名付きで呼びます。

### 2.2 `Parse`

```csharp
public static byte[] Parse(string text)
{
    if (text == null) throw new ArgumentNullException("text");
    string s = text.Replace(" ", "").Replace("-", "").Replace(":", "");
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
    if (s.Length % 2 != 0) throw new ArgumentException(...);
    // 2 文字ずつ byte.TryParse(..., NumberStyles.HexNumber, CultureInfo.InvariantCulture, ...)
}
```

1. 空白・ハイフン・コロンを取り除く。
2. 先頭の `0x`（大文字小文字を問わない）を 1 つだけ取り除く。
3. 桁数が奇数なら例外。
4. 2 文字ずつ 16 進として解釈する。解釈できなければ例外。

| 入力 | 結果 |
|---|---|
| `"01AB"` | `{ 0x01, 0xAB }` |
| `"01 ab"` | `{ 0x01, 0xAB }`（小文字も可） |
| `"01-AB"` / `"01:AB"` | `{ 0x01, 0xAB }` |
| `"0x01AB"` / `"0X01AB"` | `{ 0x01, 0xAB }` |
| `""` | 長さ 0 の配列 |
| `"ABC"` | `ArgumentException`（奇数桁） |
| `"ZZ"` | `ArgumentException`（16 進でない） |
| `"0x01 0x02"` | `ArgumentException`（`0x` を取り除くのは先頭の 1 つだけ） |
| `null` | `ArgumentNullException` |

`CultureInfo.InvariantCulture` を指定しているので、OS の地域設定によって結果が変わりません。

### 2.3 `SequenceEqual`（internal）

```csharp
internal static bool SequenceEqual(byte[] a, byte[] b)
{
    if (a == null || b == null || a.Length != b.Length) return false;
    for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
    return true;
}
```

- どちらかが `null` なら `false` です（**両方 `null` でも `false`**）。比較できないものを「一致」とは言わない、という安全側の判断です。
- LINQ の `Enumerable.SequenceEqual` を使わずに書いているのは、`null` の扱いをこのように明示するためと、
  配列の比較に余計な割り当てを発生させないためです。

---

## 3. C# 5 で書くための工夫

| 箇所 | 書き方 | 使っていない C# 6 以降の構文 |
|---|---|---|
| 例外の引数名 | `"text"` | `nameof(text)` |
| `TryParse` の出力 | `byte value;` を宣言してから `out value` | `out var` |
| 名前空間の指定 | `System.Globalization.NumberStyles.HexNumber` と完全修飾 | —（`using` を増やさないため） |

## 4. 関連テスト

直接テストするセクションはありませんが、次の経路で使われています。

- 「3. 対象カードの Polling 応答の抽出」の「IDm 文字列は 16 桁の大文字」（`IdmToString` 経由の `ToString`）
- 「8. Write と重ね置きチェック」の読み戻し比較（`SequenceEqual`）
- 失敗した既知ベクタの `expected` / `actual` の表示（`ToString`）
