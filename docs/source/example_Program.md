# example/Program.cs — 使い方サンプル

| 項目 | 内容 |
|---|---|
| ソース | [`example/Program.cs`](../../example/Program.cs) |
| プロジェクト | [`example/Example.csproj`](../../example/Example.csproj)（従来形式、.NET Framework 4.5、`src/PcscIf.csproj` を参照） |
| 名前空間 / 型 | `PcscIfExample.Program`（internal static class） |
| 実行ファイル | `Example.exe` |

---

## 1. このファイルの役割

ライブラリの**最小の使い方**を示すサンプルです。C 言語の `main()` に相当します。

**PC に接続されたカードリーダは 1 台だけ**という前提で、見つかった最初の 1 台を使います。

| C 言語での感覚 | このサンプル | ライブラリの API |
|---|---|---|
| リーダの検索 | 手順 1 | `Pcsc.ListReaders()` |
| open | 手順 2 | `pcsc.Connect(reader)` |
| read | 手順 3 | `pcsc.Read()` |
| write | 手順 4（`--write` のときだけ） | `pcsc.Write(data)` |
| close | 手順 5 | `pcsc.Disconnect()` |

## 2. 実行方法

| コマンド | 動作 |
|---|---|
| `Example` | リーダ検索 → open → read → close |
| `Example --write` | 上に加えて、先頭 16 バイトに `00 01 02 .. 0F` を書き込む（**カードを上書きします**） |

引数なしでは書き込みません。誤って実行しても、カードの内容は変わりません。

Visual Studio 2013 では、`PcscIf.sln` を開いて `Example` をスタートアップ プロジェクトに設定すれば実行できます。
コマンドラインでは次のようにビルドします。

```bash
"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" example\Example.csproj /p:Configuration=Release
```

## 3. 終了コード

| コード | 意味 |
|---|---|
| `0` | すべて成功 |
| `1` | リーダが見つからない / 接続失敗 / 読み出し失敗 / 書き込み失敗 |

失敗の内容は標準エラー出力に出ます。

---

## 4. コードの解説

### 4.1 引数の確認

```csharp
bool doWrite = args.Length > 0 && args[0] == "--write";
```

最初の引数が `--write` のときだけ書き込みます。それ以外の引数は無視します。

### 4.2 手順 1: リーダを検索する

```csharp
List<string> readers = Pcsc.ListReaders();
if (readers.Count == 0)
{
    Console.Error.WriteLine("リーダが見つかりません。");
    return 1;
}
string reader = readers[0];
Console.WriteLine("リーダ : " + reader);
```

- `Pcsc.ListReaders()` は **static メソッド**です。インスタンス（`new Pcsc()`）を作る前に呼べます。
  リーダの一覧は接続状態に関係しない情報なので、インスタンスを持つ必要がないためです。
- 失敗しても例外は投げず、空のリストが返ります。
- 1 台前提なので `readers[0]` を使います。複数台つながっている環境では、意図しないリーダを使う可能性があります。

### 4.3 手順 2: open

```csharp
Pcsc pcsc = new Pcsc();
try
{
    PcscResult open = pcsc.Connect(reader);
    if (!open.Ok)
    {
        Console.Error.WriteLine("接続失敗 : " + open.Message);
        return 1;
    }
    ...
}
finally
{
    pcsc.Disconnect();
}
```

- `new Pcsc()` は、接続状態（リーダのハンドル）を持つ入れ物を作るだけで、リーダには触りません。
  **`new` した時点ではまだ何も開いていない**点が、C の `fopen` のような関数とは違います。
- 実際にリーダを開くのは `Connect` です。`Connect()`（引数なし）でも最初のリーダに接続しますが、
  サンプルでは手順 1 で選んだ名前を明示的に渡しています。
- **`try` / `finally` で、どこで `return` しても必ず `Disconnect` が呼ばれます。**
  `try` の中の `return 1;` でも、関数を抜ける前に `finally` が実行されます。
- `Pcsc` は `IDisposable` なので `using (Pcsc pcsc = new Pcsc()) { ... }` とも書けます。
  サンプルでは close の位置が見えるように `finally` で明示しています。
- 結果の判定は `open.Ok` だけで十分です。`Ok` 以外はすべて失敗です。

### 4.4 手順 3: read

```csharp
PcscResult read = pcsc.Read();
if (!read.Ok)
{
    // MultipleCards ならカードが 2 枚重なっている。
    Console.Error.WriteLine("読み出し失敗 : " + read.Status + " - " + read.Message);
    return 1;
}
Console.WriteLine("IDm    : " + read.Idm);
Console.WriteLine("読み出し: " + Hex.ToString(read.Data));
```

- `Read()`（引数なし）は、**ユーザ領域の先頭 16 バイト（1 ブロック）**を読みます。
- `Read` の中で**重ね置きチェックが自動的に行われます。** カードが 2 枚重なっていると
  `read.Status` が `MultipleCards` になり、読み出しコマンドはカードに送られません。
- 失敗時は `Status`（種類）と `Message`（説明）を両方出しています。業務アプリでは `Status` で分岐し、
  `Message` はログに残す、という使い分けを想定しています。
- `read.Data` は 16 バイトの `byte[]`、`read.Idm` は 16 桁の 16 進文字列です。

### 4.5 手順 4: write

```csharp
if (doWrite)
{
    byte[] data = new byte[FelicaLiteS.DefaultAccessLength];
    for (int i = 0; i < data.Length; i++) data[i] = (byte)i;

    PcscResult write = pcsc.Write(data);
    if (!write.Ok)
    {
        Console.Error.WriteLine("書き込み失敗 : " + write.Status + " - " + write.Message);
        return 1;
    }
    Console.WriteLine("書き込み: " + Hex.ToString(data));
}
```

- 書き込むデータの長さは `FelicaLiteS.DefaultAccessLength`（16）です。数値を直接書かず定数を使うことで、
  「原則 1 ブロック = 16 バイト」という仕様との対応が分かるようにしています。
- `Write(data)` は先頭ブロックに書き込みます。Read と同じく重ね置きチェックが自動的に行われます。
- 既定では、書き込み後に同じセッションで読み戻して一致を確認します。`write.Ok` が `true` なら、読み戻しまで一致しています。

### 4.6 手順 5: close

`finally` の `pcsc.Disconnect()` です。戻り値は確認していません。
`Disconnect` は未接続でも `Ok` を返す（冪等）ので、`Connect` に失敗した後に呼んでも問題ありません。

---

## 5. 出力例

~~~
リーダ : <リーダ名>
IDm    : <IDm>
読み出し: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
書き込み: 00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F
~~~

カードが 2 枚重なっている場合:

~~~
リーダ : <リーダ名>
読み出し失敗 : MultipleCards - 異なる対象カードの IDm を 2 種類検出（重ね置き）。読み書きしない: <IDm>,<IDm>
~~~

## 6. 業務アプリに組み込むときのヒント

| やりたいこと | 方法 |
|---|---|
| ログを出したい | `PcscOptions options = new PcscOptions(); options.Log = Console.WriteLine;` を `new Pcsc(options)` に渡す |
| 先頭以外のブロックを読みたい | `pcsc.Read(startBlock, blockCount)`（`blockCount` は 1..4） |
| 先頭以外のブロックに書きたい | `pcsc.Write(startBlock, data)`（`data` は 16 バイトの倍数） |
| 接続エラーに強くしたい | `ReaderError` を受け取ったら少し待って再試行する（README §8.1 の推奨） |
| 何度も読み書きする | `Connect` は 1 回だけ行い、`Read` / `Write` を繰り返す。終わったら `Disconnect` |

## 7. C# 5 で書くための工夫

| 箇所 | 書き方 | 使っていない C# 6 以降の構文 |
|---|---|---|
| 出力の組み立て | `"リーダ : " + reader` | 文字列補間 |
| 型の指定 | `List<string> readers = ...`、`PcscResult read = ...` | —（`var` は C# 3 だが、型を明示して読みやすくしている） |
