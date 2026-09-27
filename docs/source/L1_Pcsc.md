# L1_Pcsc.cs — 公開 API 本体（`Pcsc`）

| 項目 | 内容 |
|---|---|
| ソース | [`src/L1_Pcsc.cs`](../../src/L1_Pcsc.cs) |
| 層 | 層1 公開 API |
| 含まれる型 | `Pcsc`（`public sealed class`、`IDisposable`） |
| 使う側 | 業務アプリ、[`example/Program.cs`](example_Program.md)、[`test/PcscIfTest`](PcscIfTest_Program.md) |

---

## 1. このファイルの役割

業務アプリが直接触る唯一のクラスです。4 つの API（`Connect` / `Disconnect` / `Read` / `Write`）と、
補助の `ListReaders` を提供します。

`Pcsc` 自身はバイト列を組み立てません。下の層の部品を**正しい順序で呼び、結果を `PcscResult` に
変換する**のが仕事です。

- 引数の検証（I/O の前に弾く）
- Transparent Session と RF の開閉（1 回の Read / Write を 1 つのセッションに閉じ込める）
- 重ね置きチェックの呼び出し（`DuplicateGuard.Collect`）と判定（`Gate`）
- カードコマンドの送受信と、応答の分類から `PcscStatus` への変換
- 例外を捕捉して `PcscResult` に変換する（**API は例外を投げない**。例外はコンストラクタのオプション検証だけ）
- 結果のログ出力

## 2. 依存関係

| 使うもの | 層 | 用途 |
|---|---|---|
| `PcscOptions` / `PcscResult` / `PcscStatus` | 層1 | 設定と戻り値（[L1_PcscModels.md](L1_PcscModels.md)） |
| `DuplicateGuard` / `ScanObservation` | 層1 | 重ね置きの観測（[L1_DuplicateGuard.md](L1_DuplicateGuard.md)） |
| `Felica` / `FelicaLiteS` / `FelicaResponseKind` | 層2 | IDm の文字列化、Read / Write フレームの組み立てと応答解析（[L2_FelicaCommands.md](L2_FelicaCommands.md)） |
| `TransceiveResult` | 層3 | 送受信 1 回分の結果（[L3_PcscResponse.md](L3_PcscResponse.md)） |
| `IPcscTransport` / `RfProtocol` / `Rcs300Transport` | 層4 | セッション・RF の制御と送受信。既定の実装が `Rcs300Transport`（[L4_PcscTransport.md](L4_PcscTransport.md)） |
| `Interop` | 層5 | `ListReaders` だけが直接呼ぶ（[L5_PcscNative.md](L5_PcscNative.md)） |
| `Hex` | 共通 | 読み戻しデータの比較（[Common_Hex.md](Common_Hex.md)） |

## 3. メンバ一覧

### 3.1 公開メンバ

| メンバ | 種別 | 説明 |
|---|---|---|
| `Pcsc()` | コンストラクタ | 既定オプション・実機トランスポートで生成 |
| `Pcsc(PcscOptions options)` | コンストラクタ | オプションを指定。`null` なら既定値 |
| `Pcsc(PcscOptions options, Func<IPcscTransport> transportFactory)` | コンストラクタ | トランスポートの生成方法を差し替える（テスト用） |
| `IsConnected` | プロパティ | Connect 済みなら `true` |
| `ReaderName` | プロパティ | 接続中のリーダ名。未接続なら `""` |
| `ListReaders()` | static メソッド | リーダ名の一覧。失敗しても空リスト（例外なし） |
| `Connect()` / `Connect(string readerName)` | メソッド | ① 接続 |
| `Disconnect()` | メソッド | ② 切断（冪等） |
| `Read()` / `Read(byte startBlock, int blockCount)` | メソッド | ③ 読み出し |
| `Write(byte[] data)` / `Write(byte startBlock, byte[] data)` | メソッド | ④ 書き込み |
| `Dispose()` | メソッド | `Disconnect()` を呼ぶだけ |

### 3.2 内部メンバ

| メンバ | 説明 |
|---|---|
| `_opt` | コンストラクタでコピーしたオプション（`readonly`） |
| `_transportFactory` | トランスポートを生成するデリゲート（`readonly`） |
| `_gate` | 排他用のロックオブジェクト |
| `_transport` / `_readerName` | 接続状態。`_transport != null` が「接続中」を意味する |
| `CreateDefaultTransport()` | `new Rcs300Transport()` を返す既定のファクトリ |
| `Execute(operation, action)` | Read / Write 共通のセッション制御 |
| `Gate(observation, sw)` | 重ね置き判定。続行してよければ `null` を返す |
| `DoRead(...)` / `DoWrite(...)` | Read / Write の本体 |
| `Slice(...)` | 配列の一部をコピーする |
| `TrySingleFrame(...)` | 送受信の結果から応答フレームをちょうど 1 個取り出す |
| `Log(result)` | ログを出して、結果をそのまま返す |

---

## 4. 生成（コンストラクタ）

```csharp
public Pcsc(PcscOptions options, Func<IPcscTransport> transportFactory)
{
    PcscOptions source = options == null ? new PcscOptions() : options;
    // 走査中に呼び出し側が書き換えても効かないようスナップショットする。
    _opt = source.Snapshot();
    _opt.Validate();
    _transportFactory = transportFactory == null
        ? new Func<IPcscTransport>(CreateDefaultTransport)
        : transportFactory;
}
```

- 3 つのコンストラクタは `: this(...)` で、この 1 つに集約されています。
- **オプションはコピーしてから検証します。** 生成後に呼び出し側が `PcscOptions` を書き換えても、
  この `Pcsc` には効きません。検査の途中でラウンド数が変わる、といった事故を防ぐためです。
- 範囲外のオプションは `ArgumentOutOfRangeException` になります。**`Pcsc` が例外を投げるのはここだけ**です。
- ファクトリは `Connect` のたびに呼ばれます。`Disconnect` → `Connect` とやり直すと、新しいトランスポートが作られます。
- この時点ではリーダに一切触りません。

## 5. `ListReaders`（static）

```csharp
IntPtr context = IntPtr.Zero;
int rc = Interop.SCardEstablishContext(Interop.SCARD_SCOPE_USER,
    IntPtr.Zero, IntPtr.Zero, out context);
if (rc != Interop.SCARD_S_SUCCESS) return new List<string>();
try { return Interop.ListReaders(context); }
finally { Interop.SCardReleaseContext(context); }
```

- 一時的なコンテキストを作ってリーダ名を列挙し、`finally` で必ず解放します。
- **失敗しても例外を投げず、空のリストを返します。** 「リーダが無い」と「スマートカードサービスが
  動いていない」を区別しません。原因を知りたいときは `Connect` の結果（`Message` / `Error`）を見てください。
- 接続状態を持たないので `static` です。`new Pcsc()` の前に呼べます。
- トランスポートを差し替えていても、常に実際の winscard を呼びます。

## 6. `Connect`

| # | 処理 | 失敗したとき |
|---|---|---|
| 1 | ロックを取り、計時を始める | — |
| 2 | すでに接続中か確認 | `AlreadyConnected`（既存の接続はそのまま） |
| 3 | `readerName` が `null` か空白だけなら、`ListReaders()` の先頭を使う | 1 台も無ければ `ReaderError`（`Error` は `null`） |
| 4 | `_transportFactory()` でトランスポートを生成 | `null` なら `ReaderError` |
| 5 | `transport.Open(target)` | トランスポートを `Dispose` してから `ReaderError`（`Error` に原因の例外） |
| 6 | `_transport` と `_readerName` に保存 | — |
| 7 | `Ok`（「接続しました: リーダ名」）を返す | — |

- **RF はここでは起動しません。** 実機トランスポートの `Open` が行うのは、`SCardConnect`（`SCARD_SHARE_DIRECT`）と
  Escape 制御コードの取得までです。RF はカードに触る Read / Write の間だけ ON にします。
- `Open` が途中で失敗しても、ハンドルを残さないようにトランスポートを破棄します。
  破棄の例外は握りつぶし、元の例外を `throw;` で再送出して、外側の `catch` で `ReaderError` に変換します。
- `SCARD_SHARE_DIRECT` は Windows では排他接続です。接続中は、他のアプリから同じリーダを使えません。

## 7. `Disconnect`

```csharp
string target = _readerName;
IPcscTransport transport = _transport;
_transport = null;
_readerName = null;
try
{
    transport.Dispose();
    return Log(/* Ok 「切断しました」 */);
}
catch (Exception ex)
{
    // ハンドルはすでに手放しているので、再接続は可能。
    return Log(/* ReaderError */);
}
```

- 未接続なら何もせずに `Ok` を返します（**冪等**）。`finally` の中で気軽に呼べるようにするためです。
- **先にフィールドを `null` にしてから破棄します。** 破棄で例外が起きても `Pcsc` は「未接続」になり、
  再び `Connect` できます。
- 実機トランスポートの `Dispose` は、RF やセッションが残っていれば落としてからハンドルを解放します。
  ただし `SCardDisconnect` / `SCardReleaseContext` の戻り値は確認していないため、解放に失敗しても
  `Ok` が返ります（[L4_PcscTransport.md](L4_PcscTransport.md) の「既知の制限」）。

---

## 8. `Read`

### 8.1 引数の検証（I/O の前）

```csharp
public PcscResult Read()
{
    return Read(FelicaLiteS.FirstUserBlock, FelicaLiteS.DefaultAccessBlocks);
}

public PcscResult Read(byte startBlock, int blockCount)
{
    byte[] blocks;
    try
    {
        blocks = FelicaLiteS.BlockRange(startBlock, blockCount);
        // I/O 前にコマンドを組み立てて、実機が拒否する要求をここで弾く。
        FelicaLiteS.BuildRead(new byte[8], blocks);
    }
    catch (Exception ex)
    {
        return Log(PcscResult.Failure(PcscStatus.InvalidArgument, ex.Message, 0, ex));
    }
    return Execute("read", /* DoRead を呼ぶ匿名メソッド */);
}
```

- 引数なしの `Read()` は `Read(0x00, 1)`、つまり**ユーザ領域の先頭 16 バイト**です。
- `BlockRange` が、ユーザ領域（`0x00`..`0x0D`）の範囲と桁あふれを検査します。
- **ダミーの IDm（ゼロ 8 バイト）で `BuildRead` を 1 回組み立てています。** 組み立て時の検査
  （1 コマンド 4 ブロックまで、など）を I/O の前に通すためです。これが無いと、RF を起動して
  重ね置きチェックまで済ませてから、引数の誤りに気付くことになります。
- 引数の検査は**接続状態の確認より先**です。未接続でも、引数が不正なら `InvalidArgument` になります。
- `InvalidArgument` の `ElapsedMs` は常に `0`、`Error` には原因の `ArgumentException` が入ります。

| 呼び出し | 結果 |
|---|---|
| `Read()` | ブロック `0x00` の 16 バイト |
| `Read(3, 4)` | ブロック `0x03`..`0x06` の 64 バイト |
| `Read(13, 1)` | ブロック `0x0D` の 16 バイト |
| `Read(0, 5)` | `InvalidArgument`（Read は 1 コマンド 4 ブロックまで） |
| `Read(13, 2)` | `InvalidArgument`（ユーザ領域を超える） |
| `Read(0x88, 1)` | `InvalidArgument`（システムブロック） |
| `Read(0, 0)` | `InvalidArgument` |
| `Read(2, int.MaxValue)` | `InvalidArgument`（巨大な配列を確保する前に弾く） |

### 8.2 本体 `DoRead`

`Execute` の中で、重ね置きチェックを通過したときだけ呼ばれます。
引数の `idm` は、チェックで確定した 1 枚分の IDm です。

| # | 処理 | 失敗したとき |
|---|---|---|
| 1 | `FelicaLiteS.BuildRead(idm, blocks)` で Read フレームを作る | — |
| 2 | `transport.Transceive(frame, TransceiveTimeoutUs)` で送受信 | — |
| 3 | `TrySingleFrame` で、応答フレームがちょうど 1 個か確認 | `Indeterminate`「読み出し応答を解析できない」 |
| 4 | `FelicaLiteS.ParseReadResponse` で解析 | 下表 |

| 解析結果 | `PcscStatus` | `StatusFlag1/2` | `Data` |
|---|---|---|---|
| `Ok` | `Ok` | `00 00` | 読み出した「ブロック数 × 16」バイト |
| `CardError` | `CardRejected` | カードが返した値 | 空 |
| `Malformed` | `Indeterminate` | `FF FF`（カードまで届いていない扱い） | 空 |

`CardError`（カードが明確に拒否した）と `Malformed`（応答が壊れていて、何が起きたか分からない）を
分けているのは、呼び出し側の復旧判断が違うためです。どちらの場合も `Idm` は入ります。

---

## 9. `Write`

### 9.1 引数の検証（I/O の前）

| 検査 | 失敗したとき |
|---|---|
| `data` が `null` または長さ 0 | `InvalidArgument` |
| `data.Length` が 16 の倍数でない | `InvalidArgument` |
| `BlockRange(startBlock, data.Length / 16)` | ユーザ領域の外・超過なら `InvalidArgument` |

検査を通ったら **`data` を `Clone()` します。** 重ね置きチェックの最中に呼び出し側が配列を書き換えても、
送る内容が変わらないようにするためです。

Write には、Read のような 4 ブロックの上限はありません。1 ブロックずつ送るので、
ユーザ領域全体（14 ブロック = 224 バイト）まで書けます。

### 9.2 本体 `DoWrite` — 書き込み

```csharp
// 1 コマンド 1 ブロック。まとめて送ると 2 ブロック目で拒否される（実機確認済み）。
for (int i = 0; i < blocks.Length; i++)
{
    byte[] block = new byte[FelicaLiteS.BlockSize];
    Buffer.BlockCopy(data, i * FelicaLiteS.BlockSize, block, 0, block.Length);
    byte[] frame = FelicaLiteS.BuildWrite(idm, new byte[] { blocks[i] }, block);
    // Transceive → TrySingleFrame → ParseWriteResponse
}
```

**1 コマンドで 1 ブロックだけを送ります。** 対象カードは Write 1 コマンドにつき 1 ブロックしか受け付けず、
まとめて送ると 2 ブロック目で `SF1=02 SF2=A8` を返すことを実機で確認しています
（`FelicaLiteS.MaxBlocksPerWrite = 1`）。

ブロックごとの結果:

| 状況 | `PcscStatus` | メッセージ |
|---|---|---|
| 応答フレームが 1 個でない | `Indeterminate` | 「ブロック 0xNN の書き込み応答を解析できない。書き込まれたか不明」 |
| `CardError` | `CardRejected` | 「カードがブロック 0xNN の書き込みを拒否した」 |
| `Malformed` | `Indeterminate` | 「ブロック 0xNN の書き込み応答が不正。書き込まれたか不明」 |
| `Ok` | 次のブロックへ進む | — |

2 ブロック目以降で失敗した場合は、メッセージの末尾に「。先行ブロックは書き換わっている」が付きます。
**Write は原子的ではありません。** 途中で失敗すると、カードには前半だけが書かれた状態で残ります。

### 9.3 本体 `DoWrite` — 読み戻し

`VerifyAfterWrite` が `false` なら、ここで `Ok`（「読み戻し未実施」）を返します。

既定（`true`）では、**成功応答を信用せず、同じセッション内で読み戻して比較します。**
Read は 1 コマンド 4 ブロックまでなので、書き込んだ範囲を 4 ブロックずつに分けて読みます。

```csharp
for (int offset = 0; offset < blocks.Length; offset += FelicaLiteS.MaxBlocksPerRead)
{
    int count = Math.Min(FelicaLiteS.MaxBlocksPerRead, blocks.Length - offset);
    PcscResult verify = DoRead(transport, idm, Slice(blocks, offset, count), sw);
    // verify.Ok でなければ Indeterminate
    // 送ったデータの同じ範囲と一致しなければ Indeterminate
}
```

| 書き込んだブロック数 | Write コマンド数 | 読み戻しの Read コマンド数 |
|---|---|---|
| 1 | 1 | 1 |
| 4 | 4 | 1 |
| 5 | 5 | 2（4 + 1） |
| 14（ユーザ領域全体） | 14 | 4（4 + 4 + 4 + 2） |

| 読み戻しの結果 | `PcscStatus` |
|---|---|
| Read が `Ok` 以外 | `Indeterminate`「書き込み後の読み戻しに失敗。内容を確認できない」（SF は読み戻しの値） |
| 内容が一致しない | `Indeterminate`「書き込み後の読み戻しが一致しない」 |
| すべて一致 | `Ok`「N ブロックを書き込み、読み戻しで一致を確認」 |

分割しないと、5 ブロック以上を書いたときに `BuildRead` が例外になり、
**カードは全ブロック書き換わっているのに `Indeterminate` が返る**という不具合になります。

---

## 10. `Execute` — セッション制御

Read と Write は、どちらもこのメソッドを通ります。
**重ね置きチェックと実際の読み書きを、1 つの RF セッションに閉じ込める**のが目的です。

~~~
lock (_gate)
  未接続なら NotConnected を返す
  StartTransparentSession
  TurnOffRf                      ← 切替の前に必ず RF を落とす
  SwitchProtocol(Felica)         ← フレーミング切替
  TurnOnRf
  Thread.Sleep(RfSettleDelayMs)  ← 0 なら待たない
  observation = DuplicateGuard.Collect(transport, DuplicateCheckRounds, TransceiveTimeoutUs)
  blocked = Gate(observation)
  result = blocked が null なら action(transport, observation.SingleIdm)   ← DoRead / DoWrite
           そうでなければ blocked
  TurnOffRf
  EndTransparentSession
  return Log(result)
~~~

- 検査と読み書きを別のセッションに分けると、その間にカードが入れ替わる窓ができます。1 セッションにまとめるのはそれを防ぐためです。
- `Gate` が「続行不可」を返したら、**読み書きコマンドは 1 バイトも送りません。** そのまま RF を落としてセッションを閉じます。
- `observation.SingleIdm` は IDm が 1 種類でないと例外を投げますが、`Gate` が `null` を返すのは
  1 種類のときだけなので、ここで例外になることはありません。
- 1 回の Read で、RF OFF は 2 回（切替前と終了時）、ON は 1 回呼ばれます。

### 10.1 例外が起きたとき

`try` の中のどこで例外が起きても、`catch` で次を行います。

1. `TurnOffRf()` を試す（失敗は無視）
2. `EndTransparentSession()` を試す（失敗は無視）
3. 結果を `Indeterminate` にする（`Message` は `"read: "` または `"write: "` + 例外メッセージ、`Error` に例外）

| 例外が起きた場所 | 例 | 読み書き |
|---|---|---|
| セッション開始〜RF ON | 管理コマンドの拒否、プロトコル切替の失敗 | 実行していない |
| 重ね置きチェック | トランスポートが例外を投げた | 実行していない |
| `DoRead` / `DoWrite` | リーダが閉じられていた | 途中まで実行した可能性がある |
| 終了処理（RF OFF / セッション終了） | 管理コマンドの拒否 | **完了していても `Indeterminate` に上書き** |

最後の行が重要です。**読み書きが成功していても、後始末に失敗したら成功を返しません。**
リーダが RF ON のまま、あるいはセッションが開いたままの可能性があり、次の操作の前提が崩れているためです。

なお、送受信そのものの winscard エラーは、実機トランスポートでは例外ではなく
`TransceiveResult.Unknown` として返ります。この場合は例外の経路ではなく、
`Gate` や `TrySingleFrame` の経路で `Indeterminate` になります。

## 11. `Gate` — 重ね置き判定

`DuplicateGuard.Collect` の観測結果を、次の**順序で**評価します。上から最初に当てはまったものが結果です。

| 順 | 条件 | 結果 | メッセージ |
|---|---|---|---|
| 1 | 異なる IDm が 2 種類以上 | `MultipleCards` | 「異なる対象カードの IDm を N 種類検出（重ね置き）。読み書きしない: IDm,IDm」 |
| 2 | RF 衝突を観測した | `Indeterminate` | 「RF 衝突を検出（枚数未確定）。読み書きしない」 |
| 3 | 通信異常・不正な応答 | `Indeterminate` | 「通信異常または対象外・不正応答。読み書きしない」 |
| 4 | IDm が 0 種類 | `NoCard` | 「対象カードの応答を観測しなかった（物理的な不存在の証明ではない）」 |
| 5 | 途中で無応答のラウンドがあった | `Indeterminate` | 「検査中に応答が途切れた。置き直して再実行」 |
| — | どれにも当てはまらない（IDm 1 種類・異常なし） | `null`（続行） | — |

順序の理由（詳しくは [DuplicateGuard.md](../DuplicateGuard.md) の「判定」の章）:

- 複数枚あるという事実は、その後に異常が起きても覆らないので最優先にする。
- 通信異常を「カード無し」と報告しないよう、衝突と異常を `NoCard` より先に見る。
- 全ラウンド無応答なら `NoCard`、1 枚見えたのに途中で途切れたなら `Indeterminate` にする。

`Gate` が返す結果では `PcscResult.Idm` は空です。観測した IDm は `MultipleCards` のメッセージにだけ入ります。

## 12. 補助メソッド

### 12.1 `TrySingleFrame`

```csharp
payload = new byte[0];
if (t == null || t.FlagCount != 1 || !t.Ok) return false;
if (t.Frames == null || t.Frames.Count != 1) return false;
payload = t.Frames[0];
return true;
```

Read と Write は特定の IDm 宛てのコマンドなので、正常なら応答フレームはちょうど 1 個です。
無応答・衝突・矛盾したフラグ・0 個・2 個以上は、すべて失敗として扱います。
`t.Data`（全フレームの連結）ではなく `t.Frames[0]` を使うのは、複数のフレームを連結して
1 個に見せかけないためです。

### 12.2 `Slice`

配列の一部を新しい配列にコピーします。読み戻しの分割で使います。

### 12.3 `Log`

```csharp
Action<string> log = _opt.Log;
if (log != null)
{
    // ログ失敗が結果を迂回してはならない。
    try { log("[pcsc] " + result.ToString()); }
    catch { }
}
return result;
```

- 公開 API の結果は、すべて最後にこれを通ります。`DoWrite` の中の読み戻しなど、途中の結果はログされません。
  1 回の API 呼び出しにつき 1 行です。
- **ログ処理が例外を投げても握りつぶし、結果はそのまま返します。** ログの不具合で判定が変わらないようにするためです。
- ほとんどの経路で、ロックを持ったまま呼ばれます。ログ処理に時間のかかる処理を書かないでください。

出力の例（書式は [L1_PcscModels.md](L1_PcscModels.md) の `PcscResult.ToString`）:

~~~
[pcsc] Ok (接続しました: <リーダ名>) NNms
[pcsc] Ok idm=0100000000000001 SF=0000 len=16 (1 ブロック（16 バイト）を読み出した) NNms
[pcsc] MultipleCards (異なる対象カードの IDm を 2 種類検出（重ね置き）。読み書きしない: 0100000000000001,0100000000000002) NNms
~~~

### 12.4 `Dispose`

`Disconnect()` を呼ぶだけです。`using` ブロックで使えます。

---

## 13. スレッド安全性

- `Connect` / `Disconnect` / `Execute`（Read / Write）/ `IsConnected` / `ReaderName` は、同じロック `_gate` で直列化されています。
  1 つの `Pcsc` を複数のスレッドから呼んでも、セッションが交錯することはありません。
- ただし 1 回の Read / Write は重ね置きチェックを含むため、1 枚なら 200ms 前後、カードが無いと
  既定値で約 2.5 秒かかります（README §8.1 の実測）。その間、ほかのスレッドの呼び出しは待たされます。
- `Read` / `Write` の引数検証はロックの外で行います。

## 14. ステータスの出どころ

| `PcscStatus` | 返すメソッド | 条件 |
|---|---|---|
| `Ok` | `Connect` / `Disconnect` / `DoRead` / `DoWrite` | 成功（`Disconnect` は未接続でも） |
| `InvalidArgument` | `Read(byte, int)` / `Write(byte, byte[])` | 事前検証で例外が出た |
| `NotConnected` | `Execute` | 未接続で Read / Write を呼んだ |
| `AlreadyConnected` | `Connect` | 接続中に Connect を呼んだ |
| `ReaderError` | `Connect` / `Disconnect` | リーダ無し・生成失敗・Open 失敗 / 破棄で例外 |
| `NoCard` | `Gate` | IDm が 0 種類 |
| `MultipleCards` | `Gate` | IDm が 2 種類以上 |
| `Indeterminate` | `Gate` / `Execute` / `DoRead` / `DoWrite` | 衝突・異常・途切れ / 例外 / 応答不正 / 読み戻しの失敗・不一致 |
| `CardRejected` | `DoRead` / `DoWrite` | カードがステータスフラグでエラーを返した |

## 15. C# 5 で書くための工夫

| 箇所 | 書き方 | 使っていない C# 6 以降の構文 |
|---|---|---|
| `Execute` に渡す処理 | `new Func<IPcscTransport, byte[], Stopwatch, PcscResult>(delegate(...) { ... })`（匿名メソッド） | — |
| メッセージの組み立て | `"すでに接続しています: " + _readerName` | 文字列補間 |
| 例外の引数名 | `new ArgumentException("...", "data")` | `nameof` |
| `IsConnected` / `ReaderName` | `get { lock (_gate) { return ...; } }` | 式形式メンバ |

## 16. 関連テスト

[`test/PcscIfTest/SelfTest.cs`](PcscIfTest_SelfTest.md) の次のセクションが、このファイルの動作を検証しています。

| セクション | 主な確認内容 |
|---|---|
| 6. Connect / Disconnect | 未接続時の `NotConnected`、二重接続、冪等な切断、Open 失敗時の破棄と `ReaderError` |
| 7. Read と重ね置きチェック | 先頭 16 バイト、Polling の回数、RF / セッションの手順、`MultipleCards` のときコマンドを送らないこと、各種 `Indeterminate`、引数エラー |
| 8. Write と重ね置きチェック | 1 ブロックずつの送信、読み戻しの分割（4+1、4+4+4+2）、`CardRejected` と `Indeterminate` の区別、入力配列の複製 |
| 9. オプション検証とログ | コンストラクタの範囲検査、スナップショット、ログ失敗の無害化、ログの行数 |

## 17. 関連ドキュメント

- [DuplicateGuard.md](../DuplicateGuard.md) — 重ね置きチェックの仕組み
- [protocol.md](../protocol.md) — 送受信するバイト列の詳細
