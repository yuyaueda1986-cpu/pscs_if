# test/PcscIfTest/Program.cs — テスト用コンソールの入口

| 項目 | 内容 |
|---|---|
| ソース | [`test/PcscIfTest/Program.cs`](../../test/PcscIfTest/Program.cs) |
| プロジェクト | [`test/PcscIfTest/PcscIfTest.csproj`](../../test/PcscIfTest/PcscIfTest.csproj)（SDK 形式、.NET 10） |
| 名前空間 / 型 | `PcscIfTest.Program`（internal static class） |
| 関連 | 自己テスト本体は [`SelfTest.cs`](PcscIfTest_SelfTest.md) |

---

## 1. このファイルの役割

テスト用のコマンドラインツールの入口です。第 1 引数のコマンドで処理を振り分けます。

| 種類 | コマンド | リーダ |
|---|---|---|
| 自己テスト | `selftest` | 不要（CI 向け） |
| 実機確認 | `list` / `read` / `write` / `loop` | 必要 |

## 2. プロジェクトの設定

| 設定 | 値 | 理由 |
|---|---|---|
| `TargetFramework` | `net10.0` | テスト用処理は .NET 10 でよいという前提 |
| `LangVersion` | `latest` | テストコード側は新しい構文も使える |
| `Nullable` | `disable` | ライブラリ本体（C# 5）と同じ前提で書くため |
| `ImplicitUsings` | `disable` | `using` を明示する |
| `InvariantGlobalization` | `true` | 地域設定に依存しない |
| コンパイル対象 | `..\..\src\*.cs` をリンク | ライブラリ本体を**ソースごと**取り込む |

- ライブラリを DLL として参照するのではなく、**ソースを同じアセンブリにコンパイルします。**
  そのため、テストから `internal` のメンバにもアクセスできます。
- `src/Properties/AssemblyInfo.cs` は含めません。SDK 形式は同じ属性を自動生成するため、二重定義になるからです。
- ライブラリ本体は C# 5 の範囲で書かれているので、.NET 10 でもそのままコンパイルできます。

## 3. 実行方法

```bash
dotnet run --project test/PcscIfTest -- <コマンド> [引数...]
```

ビルド済みなら、`test/PcscIfTest/bin/Debug/net10.0/PcscIfTest.exe <コマンド>` でも実行できます。

> Windows のスマートアプリコントロールが有効な環境では、ビルドした直後の DLL が
> 「アプリケーション制御ポリシーによってブロックされました」で起動できないことがありました。
> 再ビルドで解消した例があります。

---

## 4. コマンド

### 4.1 一覧

| コマンド | 引数 | 内容 | 終了コード |
|---|---|---|---|
| （なし） | — | `selftest` と同じ | 失敗件数 |
| `selftest` | — | リーダ不要の自己テスト | 失敗件数（0 = 全件 PASS） |
| `list` | — | 接続中のリーダを列挙 | 0 = 1 台以上 / 1 = 0 台 |
| `read` | `[reader] [startBlock] [count]` | Connect → Read → Disconnect | 0 = Read 成功 / 1 = 失敗 |
| `write` | `<reader> <hex> --yes [startBlock]` | Connect → Write → Disconnect（**破壊的**） | 0 = Write 成功 / 1 = 失敗 |
| `loop` | `[reader] [count]` | Read を繰り返してステータスを集計 | 0（結果にかかわらず）/ 1 = 接続失敗 |
| その他 | — | 使い方を表示 | 2 |

処理中に例外が起きた場合（引数の解析失敗など）は、`Main` が捕捉して「`PcscIfTest: メッセージ`」を
標準エラーに出し、終了コード 1 で終わります。

### 4.2 `selftest`

```csharp
case "selftest": return SelfTest.Run();
```

`SelfTest.Run()` の戻り値（失敗件数）をそのまま終了コードにします。
CI では「終了コードが 0 なら成功」と判定できます。詳細は [PcscIfTest_SelfTest.md](PcscIfTest_SelfTest.md)。

### 4.3 `list`

```bash
dotnet run --project test/PcscIfTest -- list
```

~~~
[0] <リーダ名>
~~~

`Pcsc.ListReaders()` の結果を番号付きで表示します。`read` などに渡すリーダ名はここで確認します。

### 4.4 `read`

```csharp
string reader = args.Length > 1 ? args[1] : null;
byte startBlock = args.Length > 2 ? byte.Parse(args[2]) : FelicaLiteS.FirstUserBlock;
int blockCount = args.Length > 3 ? int.Parse(args[3]) : FelicaLiteS.DefaultAccessBlocks;
```

| 引数 | 省略時 | 注意 |
|---|---|---|
| `reader` | `null`（最初のリーダ） | 空文字 `""` を渡しても最初のリーダ |
| `startBlock` | `0` | **10 進数**で指定（`13` はブロック `0x0D`） |
| `count` | `1` | 1..4 |

引数は位置で決まるため、**ブロックを指定するときはリーダ名も省略できません。**
`read 3 2` と書くと `"3"` がリーダ名として扱われます。最初のリーダでよければ `read "" 3 2` とします。

```bash
dotnet run --project test/PcscIfTest -- read
dotnet run --project test/PcscIfTest -- read "<リーダ名>" 0 4
```

~~~
[pcsc] Ok (接続しました: <リーダ名>) NNms
Connect  => Ok (接続しました: <リーダ名>) NNms
[pcsc] Ok idm=<IDm> SF=0000 len=16 (1 ブロック（16 バイト）を読み出した) NNms
Read     => Ok idm=<IDm> SF=0000 len=16 (1 ブロック（16 バイト）を読み出した) NNms
data     : 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
[pcsc] Ok (切断しました: <リーダ名>) NNms
Disconnect => Ok (切断しました: <リーダ名>) NNms
~~~

`[pcsc]` で始まる行はライブラリのログ（`LiveOptions` で `Console.WriteLine` を指定）、
`=>` の行はこのツールが結果を表示したものです。同じ内容が 2 回出るのはこのためです。

### 4.5 `write`（破壊的）

```csharp
if (args.Length < 3)
    throw new ArgumentException("usage: write \"<reader>\" <hex> --yes [startBlock]");
bool confirmed = false;
for (int i = 0; i < args.Length; i++) if (args[i] == "--yes") confirmed = true;
if (!confirmed)
    throw new ArgumentException("write はユーザ領域を上書きします。--yes を付けてください。");

string reader = args[1];
byte[] payload = Hex.Parse(args[2]);
byte startBlock = FelicaLiteS.FirstUserBlock;
if (args.Length > 4 && args[4] != "--yes") startBlock = byte.Parse(args[4]);
```

| 引数 | 必須 | 注意 |
|---|---|---|
| `reader` | 必須 | `""` なら最初のリーダ |
| `hex` | 必須 | 16 バイトの倍数。空白・ハイフン・コロン・先頭の `0x` を許容（`Hex.Parse`） |
| `--yes` | 必須 | **事故で書き込まないための確認。** 無いと例外で終了（何も書かない） |
| `startBlock` | 任意 | **5 番目の位置（`--yes` の後ろ）**に 10 進数で指定。省略時は `0` |

`write R HEX 3 --yes` のように `--yes` の前に開始ブロックを書くと、開始ブロックは無視されて `0` になります。

```bash
dotnet run --project test/PcscIfTest -- write "<リーダ名>" 000102030405060708090A0B0C0D0E0F --yes
dotnet run --project test/PcscIfTest -- write "<リーダ名>" 00000000000000000000000000000000 --yes 3
```

### 4.6 `loop`

```csharp
string reader = args.Length > 1 ? args[1] : null;
int count = args.Length > 2 ? int.Parse(args[2]) : 20;
```

1 回接続したまま `Read()` を `count` 回（既定 20 回）繰り返し、次を表示します。

- 1 回ごとの結果（`0001 Ok idm=...`）
- ステータスごとの回数
- 所要時間の中央値と最大値

```bash
dotnet run --project test/PcscIfTest -- loop "<リーダ名>" 50
```

~~~
0001 Ok idm=<IDm> SF=0000 len=16 (1 ブロック（16 バイト）を読み出した) NNms
...
---- 集計 ----
  Ok = 50
  median=NNms max=NNms
~~~

- **重ね置き検出率の測定に使います。** 1 枚置いて `Ok` だけになること、2 枚重ねて `MultipleCards` だけになることを、
  実際の設置条件ごとに確認します。
- 中央値は、並べ替えた所要時間の `Count / 2` 番目です（回数が偶数のときは上側の値）。
- **結果にかかわらず終了コードは 0 です。** 判定は集計の表示を見て行います。
- `count` に 0 を渡すと集計で例外になり、終了コード 1 で終わります（切断は `using` で行われます）。

---

## 5. 共通の処理

### 5.1 `LiveOptions`

```csharp
private static PcscOptions LiveOptions()
{
    PcscOptions o = new PcscOptions();
    o.Log = Console.WriteLine;
    return o;
}
```

実機コマンドでは、ログを標準出力に出す以外は**すべて既定値**です。実機検証の結果（README §8.1）も、この設定で取っています。

### 5.2 接続と後始末

`read` / `write` / `loop` は、どれも次の形です。

```csharp
using (Pcsc pcsc = new Pcsc(LiveOptions()))
{
    PcscResult connect = pcsc.Connect(reader);
    Console.WriteLine("Connect  => " + connect);
    if (!connect.Ok) return 1;
    // ... Read / Write ...
    Console.WriteLine("Disconnect => " + pcsc.Disconnect());
    return ...;
}
```

明示的に `Disconnect` を呼び、さらに `using` の終わりで `Dispose`（= `Disconnect`）も呼ばれます。
`Disconnect` は冪等なので、2 回呼んでも問題ありません。途中で `return` や例外があっても、`using` で必ず切断されます。
