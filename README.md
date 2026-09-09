# pcsc_if

特定の IC カードのユーザデータ領域を、CCID Escape 経由で読み書きするための
PC/SC インタフェース API です。

[pcsc_dup_guard](../../pcsc_dup_guard) を参考に、業務側から使いやすい 4 つの API に整理し、
**C# 処理を 1 ファイル（[src/PcscIf.cs](src/PcscIf.cs)）に集約**しています。

- 本体 : **.NET Framework 4.5 / C# 5.0 / Visual Studio 2013** でビルドできること
- テスト : .NET 10 コンソールアプリ（リーダ不要の自己テスト + 実機確認）

## 1. 構成

| パス | 内容 | ターゲット |
|---|---|---|
| `src/PcscIf.cs` | **C# 処理はこの 1 ファイルに集約** | .NET Framework 4.5 / C# 5.0 |
| `src/PcscIf.csproj` | 従来形式（非 SDK）プロジェクト。VS2013 でそのまま開ける | v4.5 / ToolsVersion 12.0 |
| `PcscIf.sln` | VS2013 用ソリューション（本体 + サンプル） | Format Version 12.00 |
| `example/Program.cs` | 使い方サンプル（C の `main()` 相当） | .NET Framework 4.5 |
| `example/Example.csproj` | サンプルのプロジェクト。`src/PcscIf.csproj` を参照 | v4.5 / ToolsVersion 12.0 |
| `test/PcscIfTest/` | テスト用処理。`src/PcscIf.cs` をリンク参照 | .NET 10 |
| [`protocol.md`](protocol.md) | PC/SC API に渡すパラメータとバイト列の詳細 | — |
| [`DuplicateGuard.md`](DuplicateGuard.md) | 重ね置きチェックの仕組みと判定ロジック | — |

`src/PcscIf.cs` は次の C# 6 以降の構文を意図的に使っていません。
文字列補間 / `nameof` / 式形式メンバ / 自動プロパティ初期化子 / get のみの自動プロパティ /
`?.` / `out var` / `using` 宣言 / 数値区切り / タプル / `init` / `Array.Empty`
（`Array.Empty` は .NET Framework 4.6 以降のため 4.5 では使えません）。

## 2. API

用意しているのは 4 つです。すべて `PcscResult` を返し、例外を投げません
（引数エラーも `PcscStatus.InvalidArgument` として返ります）。

~~~csharp
using PcscIf;

var options = new PcscOptions();
options.Log = Console.WriteLine;

using (var pcsc = new Pcsc(options))
{
    // ① 接続（リーダ名を省略すると最初に見つかったリーダ）
    PcscResult connect = pcsc.Connect();
    if (!connect.Ok) { /* connect.Status を確認 */ }

    // ③ 読み出し（先頭 16 バイト = S_PAD0 の 1 ブロック）
    PcscResult read = pcsc.Read();
    if (read.Ok) { /* read.Data は 16 バイト */ }

    // ④ 書き込み（先頭 16 バイト）
    PcscResult write = pcsc.Write(payload);
    if (!write.Ok) { /* write.Status と StatusFlag1/2 を確認 */ }

    // ② 切断
    pcsc.Disconnect();
}
~~~

| API | シグネチャ | 説明 |
|---|---|---|
| ① Connect | `Connect()` / `Connect(string readerName)` | リーダハンドルを取得する。RF はまだ起動しない |
| ② Disconnect | `Disconnect()` | 切断する。未接続でも `Ok`（冪等） |
| ③ Read | `Read()` / `Read(byte startBlock, int blockCount)` | **引数なしは先頭 16 バイト（1 ブロック）** |
| ④ Write | `Write(byte[] data)` / `Write(byte startBlock, byte[] data)` | **原則 16 バイト（1 ブロック）** |

**読み書きは原則 1 ブロック = 先頭 16 バイト（S_PAD0）です**
（`FelicaLiteS.DefaultAccessLength` = 16, `FelicaLiteS.DefaultAccessBlocks` = 1）。
複数ブロックを扱う場合だけ、範囲指定のオーバーロードを明示的に呼んでください。
`Write` に 16 バイトの倍数を渡せば複数ブロックにも書けますが、その場合は原子的ではありません（§4）。

補助として `Pcsc.ListReaders()`（静的）でリーダ名を列挙できます。

### PcscStatus

| 値 | 意味 | 業務処理 |
|---|---|---|
| `Ok` | 読み書きが成功し、応答も検証できた | 続行可 |
| `MultipleCards` | **異なる IDm を 2 種類以上観測（重ね置き）。読み書きコマンドを送出していない** | 中断 |
| `NoCard` | カードの応答を観測しなかった（物理的な不存在の証明ではない） | 中断・再検査 |
| `Indeterminate` | 通信異常 / RF 衝突 / 応答途切れ / 読み戻し不一致 / セッション終了失敗 | 中断 |
| `CardRejected` | カードがステータスフラグでエラーを返した（`StatusFlag1/2` を確認） | 中断 |
| `NotConnected` | `Connect()` していない | — |
| `AlreadyConnected` | すでに接続済み | — |
| `ReaderError` | リーダが見つからない / winscard 呼び出し失敗 | — |
| `InvalidArgument` | 引数不正。I/O は行っていない | — |

`Ok` 以外はすべて失敗として扱ってください。未知の値も `Ok` プロパティは `false` になります。

### PcscOptions

| 項目 | 既定 | 範囲 | 説明 |
|---|---|---|---|
| `DuplicateCheckRounds` | 8 | 2..64 | 重ね置きチェックの Polling ラウンド数 |
| `TransceiveTimeoutUs` | 300000 | 100000..1000000 | Transceive タイムアウト（μs） |
| `RfSettleDelayMs` | 20 | 0..1000 | RF 起動待ち |
| `VerifyAfterWrite` | true | — | 書き込み後に読み戻して一致を確認 |
| `Log` | null | — | 診断ログ。例外を投げても判定は迂回されない |

オプションはコンストラクタでスナップショットされます。生成後に書き換えても効きません。
範囲外は `ArgumentOutOfRangeException` になります。

**`DuplicateCheckRounds` を減らさないでください。** 対象リーダは 1 回の Transceive で
1 枚分の応答しか返さないため、複数ラウンドの IDm 和集合が唯一の検出手段です。
1 にすると重ね置き検出は原理的に成立しません。

## 3. 重ね置きチェック（CardDuplicateGuard 相当）

`Read` / `Write` は毎回、**実アクセスと同一の RF セッション内**で次を実行します。

~~~
StartTransparentSession
  → RF OFF → フレーミング切替 → RF ON → RF 起動待ち
  → Polling を DuplicateCheckRounds 回（IDm の和集合を取る）
  → 判定
       IDm 2 種類以上          → MultipleCards（コマンドを送出せず中断）
       RF 衝突                 → Indeterminate
       通信異常・対象外応答     → Indeterminate
       IDm 0 種類              → NoCard
       途中で応答が途切れた     → Indeterminate
       IDm 1 種類              → 読み書きを実行
  → RF OFF → EndTransparentSession
~~~

判定と読み書きを 1 セッションにまとめているのは、
「検査してから別セッションで接続する」間にカードが入れ替わる窓を作らないためです。
RF OFF / セッション終了に失敗した場合は、成功していた読み書きも `Indeterminate` に落とします。

### できないこと（設計上の限界。運用手順で補うこと）

- **RF 応答がない 2 枚目は排除できない。** RF 結合や重なり方によっては応答しないカードが残ります。
  ラウンド数を増やしても全数検知や見逃し 0% にはなりません。
- **`NoCard` は物理的な不存在の証明ではない。** 「明示的な無応答を観測した」以上の意味はありません。
- **セッション終了後にカードが追加・移動される可能性は残る。**
- **`Ok` は「物理的に 1 枚だけ」の保証ではない。** 観測できた IDm が 1 種類だった、という意味です。

## 4. アクセス範囲と安全側の制約

- 対象は**ユーザブロック S_PAD0..S_PAD13（0x00..0x0D、16 バイト × 14 ブロック）のみ**です。
  システムブロック（MC / CK / CKV / ID など）は指定できません。
  MC を誤って書くとカードが復旧不能になるためです。
- Read / Write Without Encryption のみ。MAC 付きアクセス・相互認証・鍵運用は実装していません。
  平文アクセスなので、改ざん検知や機密保持が要る用途には使えません。
- **Write は原子的ではありません。** カードの Write Without Encryption は
  1 コマンドあたり 1 ブロックしか受け付けない（実機で SF1=02 SF2=A8 を確認）ため、
  32 バイトの書き込みは 2 コマンドに分かれます。1 ブロック目成功・2 ブロック目失敗なら、
  カードは中途半端な状態で残ります（結果の `Message` にその旨が入ります）。
- **Read は 1 コマンドあたり最大 4 ブロック（64 バイト）です。**
  実機で 5 ブロック以上はカード側が SF1=`FF` SF2=`A2`（ブロック数エラー）で拒否することを確認しました
  （`FelicaLiteS.MaxBlocksPerRead`）。ライブラリは I/O を行う前に `InvalidArgument` で弾きます。
  Read は 4 ブロック / Write は 1 ブロックと上限が異なる点に注意してください。

## 5. ビルド

### 本体（.NET Framework 4.5 / VS2013）

`PcscIf.sln` を Visual Studio 2013 で開いてビルドします。
NuGet パッケージへの依存はありません（参照は `System` と `System.Core` のみ）。

コマンドラインの場合:

```bash
"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" src\PcscIf.csproj /p:Configuration=Release
```

### テスト用処理（.NET 10）

```bash
dotnet build test/PcscIfTest/PcscIfTest.csproj
```

## 6. サンプル（example/）

[example/Program.cs](example/Program.cs) が最小の使い方サンプルです。
`PcscIf.sln` に含まれているので、VS2013 でスタートアップ プロジェクトに設定すれば
そのまま実行できます。

~~~
リーダ検索（1 台前提。最初に見つかったものを使う）
  → Connect（open）
  → Read（先頭 16 バイト）
  → Write（先頭 16 バイト。--write 指定時のみ）
  → Disconnect（close）
~~~

| 実行 | 動作 |
|---|---|
| `Example` | リーダ検索 → open → read → close |
| `Example --write` | 上に加えて 16 バイトの write も行う（**カードを上書きします**） |

引数なしでは書き込みません。誤って実行してもカードの内容は変わりません。

`Read` / `Write` の中で重ね置きチェックが自動的に走ります。
カードが 2 枚重なっていると `PcscStatus.MultipleCards` が返り、コマンドは送出されません。

## 7. テスト用処理の使い方

```bash
dotnet run --project test/PcscIfTest -- selftest
```

| コマンド | リーダ | 内容 |
|---|---|---|
| `selftest` | 不要 | 既知ベクタ・応答パーサ・模擬セッションでの全 API 検証。終了コード = 失敗数 |
| `list` | 必要 | 接続中のリーダを列挙 |
| `read [reader] [startBlock] [count]` | 必要 | Connect → Read → Disconnect |
| `write <reader> <hex> --yes [startBlock]` | 必要 | Connect → Write → Disconnect（**破壊的**。`--yes` 必須） |
| `loop [reader] [count]` | 必要 | Read を繰り返し、ステータスの集計と所要時間を出す |

`selftest` はリーダ不要なので CI に組み込めます。172 件のチェックを実行します。

実機での重ね置き確認は `loop` を使ってください。
2 枚重ねた状態で `MultipleCards` が出ること、1 枚のときに `Ok` が出ることを、
実際の設置条件（枚数・位置・密着度）ごとに試行回数を決めて確認します。

## 8. 検証状況

| 項目 | 状況 |
|---|---|
| `src/PcscIf.cs` が .NET Framework 4.5 / C# 5.0 で警告 0・エラー 0 でコンパイルされる | 確認済み |
| `example/Program.cs` が .NET Framework 4.5 / C# 5.0 でコンパイルされる | 確認済み |
| リーダ不要の自己テスト 172 件 | 全件 PASS |
| 実機での Connect / Disconnect / Read / Write | **確認済み**（下記） |
| 実機での重ね置き検出 | **確認済み**（下記） |
| 実機での `example` の実行（リーダ検索 → open → read → write → close） | **確認済み**（下記） |
| Visual Studio 2013 の IDE でのビルド | **未実施**（検証環境に VS2013 が無いため） |

### 8.1 実機検証の結果

検証環境: Windows 11 / 対象リーダ 1 台 /
オプションはすべて既定値（`DuplicateCheckRounds=8`, `TransceiveTimeoutUs=300000`）。

**重ね置き検出**

| 条件 | 試行 | 結果 | median / max |
|---|---|---|---|
| カード無し | 1 | `NoCard` 1 | 2464ms |
| 1 枚 | 50 | `Ok` 50 / **誤検出 0** | 166 / 197ms |
| 2 枚重ね（中心・密着） | 50 | `MultipleCards` 50 / **見逃し 0** | 56 / 673ms |
| 2 枚重ね + `Write` 要求 | 21 | `MultipleCards` 21 / **すり抜け 0** | — |

レビュー指摘の修正後に、同条件で回帰を実施した結果:

| 条件 | 試行 | 結果 | median / max |
|---|---|---|---|
| 2 枚重ね + `Read` | 50 | `MultipleCards` 50 / **見逃し 0** | 62 / 711ms |
| 2 枚重ね + `Write`（1 ブロック） | 10 | `MultipleCards` 10 / **すり抜け 0** | — |
| 2 枚重ね + `Write`（5 ブロック） | 5 | `MultipleCards` 5 / **すり抜け 0** | — |
| 2 枚重ね + `example` / `example --write` | 各 1 | 中断・終了コード 1 | — |

2 枚重ね時は両方の IDm を観測できました。1 枚のときより速いのは、
異なる IDm を 2 種類観測した時点でラウンドを打ち切る設計どおりです。
ブロックされた `Write` の後に両カードを個別に載せてユーザ領域 14 ブロック全体を読み出し、
**どちらも 1 バイトも書き換わっていない**ことを確認しました。

**Read / Write**

- `Read()`（先頭 16 バイト = 1 ブロック）: `Ok` / `SF=0000`。
- `Write()`（先頭 16 バイト = 1 ブロック）: `Ok` / `SF=0000`。1 コマンドで完了。
  別セッションの `Read(0, 2)` で **S_PAD0 だけが書き換わり、隣の S_PAD1 は無傷**であることを
  確認したうえで、ゼロを書き戻して復元しました。
- 32 バイト（2 ブロック）の `Write` も確認済み（既定が 32 バイトだった時点で実施）。
  1 ブロックずつ 2 コマンドに分割する実装が実機で動作します。
- **80 バイト（5 ブロック）の `Write`**: `Ok` / `SF=0000`。
  Read は 4 ブロックまでなので読み戻しが 4 + 1 に分割される経路。
  別セッションで `Read(0,4)` = `40..7F`、`Read(4,1)` = `80..8F` を確認し、
  範囲外の S_PAD5 が無傷であることも確認したうえで、ゼロを書き戻して復元しました。
  4 回の試行すべてで `Ok`（所要 203〜240ms）。
- 1 コマンドあたりの Read 上限は **4 ブロック**（5 ブロック以上は `SF1=FF SF2=A2`）。
  この上限はライブラリ側の `FelicaLiteS.MaxBlocksPerRead` に反映済みで、
  現在は I/O 前に `InvalidArgument` で弾きます（実機で 0ms・リーダ非接触を確認）。

**`example` の実機実行**

```
リーダ : <リーダ名>
IDm    : <IDm>
読み出し: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
書き込み: 00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F
```

リーダ検索 → open → read → write → close が `example --write` 1 回の実行で
すべて成功しました（実行後にゼロへ復元済み）。

**未解明の事象（要注意）**

検証中、テスト用 CLI をプロセスとして連続起動している最中に、
**アプリの出力が 1 行も出ずに終了コード 1 で終わる事象が 2 回**ありました。
どちらも出力を `grep` で絞っていたため診断情報を取り逃がしています。
同条件で 20 回連続実行しても再現せず、原因を確定できていません。

考えられるのは (a) `dotnet run` の起動側の一時的な失敗、
(b) `SCardConnect` の一時的な失敗（ライブラリは `ReaderError` を返して中断する）のいずれかです。
どちらの場合もカードは書き換わっておらず（事後に全ブロックを確認済み）、
ライブラリの不具合を示す証拠はありません。

**運用上は、`ReaderError` を受け取ったら再試行する作りにしておくことを推奨します。**

**未確認・注意**

- 検証に使ったリーダは 1 機種のみです。他機種での動作は未確認です。
  Escape / Transparent Session プロファイルはそのまま動作しました。
- 重ね方は「中心・密着」1 条件のみです。ずらし置き・浮き・3 枚以上は未測定です。
  §3 のとおり **RF 応答を返さない 2 枚目は原理的に検出できない**ため、
  実運用の設置条件ごとに `loop` で試行回数を決めて測定してください。
