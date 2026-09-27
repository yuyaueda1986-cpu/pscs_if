# test/PcscIfTest/SelfTest.cs — リーダ不要の自己テスト

| 項目 | 内容 |
|---|---|
| ソース | [`test/PcscIfTest/SelfTest.cs`](../../test/PcscIfTest/SelfTest.cs) |
| プロジェクト | `test/PcscIfTest/PcscIfTest.csproj`（.NET 10） |
| 名前空間 / 型 | `PcscIfTest.SelfTest`（internal static class） |
| 実行 | `dotnet run --project test/PcscIfTest -- selftest`（[PcscIfTest_Program.md](PcscIfTest_Program.md)） |
| 件数 | 279 件（13 セクション） |
| テスト 1 件ごとの仕様 | [../UnitTest.md](../UnitTest.md) |

---

## 1. このファイルの役割

**リーダもカードも無しで**ライブラリ全体を検証する自己テストです。CI に組み込めます。

外部のテストフレームワーク（xUnit など）は使っていません。小さな `Check` 関数と、
`IPcscTransport` を差し替える模擬トランスポート `FakeTransport` だけで構成しています。

検証の柱は 5 つです。

| 柱 | 方法 |
|---|---|
| 既知ベクタ | 組み立てたバイト列が、期待するバイト列と 1 バイトも違わないことを確認する |
| 応答パーサの異常系 | 壊れた応答・途中で切れた応答・矛盾した応答を、成功と誤認しないことを確認する |
| 模擬セッション | `FakeTransport` で 2 枚重ね・無応答・書き込み失敗などを再現し、`Pcsc` の判定を確認する |
| 並行呼び出し | 同じ `Pcsc` を複数スレッドから呼び、セッションが重ならないことを確認する |
| リーダ不要な単体検査 | 観測ループ `DuplicateGuard`、16 進変換 `Hex`、実機トランスポート `Rcs300Transport` のうち winscard を呼ぶ前に決まる振る舞い |

最後の 1 件（13-11）だけは OS の PC/SC 層（winscard.dll）を呼びます。存在しないリーダ名に接続するので、
リーダの有無や winscard.dll の有無にかかわらず必ず `ReaderError` になります。

## 2. 出力と終了コード

```csharp
Console.WriteLine("  [" + (pass ? "OK" : "NG") + "] " + name);
```

~~~
== 1. コマンド組み立ての既知ベクタ ==
  [OK] Polling は 88B4 / RC=01 / 16 スロット
  [OK] Polling TSN=1
  ...

SelfTest: ALL PASS (279 checks)
~~~

- 失敗があると最後の行が `SelfTest: N FAILED / 279 checks` になります。
- 既知ベクタ（`Expect`）が失敗した場合は、期待値と実際の値を 16 進で表示します。
- `Run()` は**失敗件数**を返し、`Program` がそれを終了コードにします。

---

## 3. セクション構成

各セクションは「元からあるテストのメソッド」と「追加分のメソッド」の 2 つを順に呼びます
（例: セクション 7 は `ReadTests()` の後に `ReadFailurePaths()`）。
追加分はセクションの末尾に並ぶので、既存テストの ID（出力順の連番）は変わりません。

| # | セクション | 件数 | メソッド | 対象のソース | 主な確認内容 |
|---|---|---|---|---|---|
| 1 | コマンド組み立ての既知ベクタ | 13 | `CommandVectors` / `CommandLengthLimits` | [L2](L2_FelicaCommands.md)、[L4](L4_PcscTransport.md) | Polling / プロトコル切替 / Transceive 封筒 / Read / Write のバイト列、不正引数の拒否、フレーム長の上限 |
| 2 | PC/SC 応答パーサ（fail-closed） | 45 | `ResponseParsing` / `ResponseParsingDetails` | [L3](L3_PcscResponse.md)、[L4](L4_PcscTransport.md) | SW / C0 エラー、RF エラー各ビット、`6401` だけが無応答、途中切れ、未知タグ、`81` / `82` の長さ表現、DO96 の 1 バイト形式と予約ビット、管理コマンドの検証 |
| 3 | 対象カードの Polling 応答の抽出 | 8 | `PollingExtraction` | [L2](L2_FelicaCommands.md) | 1 枚・連結 2 枚、末尾のゴミ、途中切れ、IC 種別違い、システムコード違い、IDm の文字列化 |
| 4 | ユーザ領域の境界チェック | 17 | `BlockBoundaries` | [L2](L2_FelicaCommands.md) | システムブロックの拒否、`0x0D` まで許可、重複、Read の 4 ブロック上限、`BlockRange` の桁あふれ |
| 5 | 読み書き応答の解析 | 22 | `AccessResponseParsing` | [L2](L2_FelicaCommands.md) | 正常・別 IDm・SF 異常・ブロック数不一致・途中切れ、`Ok` / `CardError` / `Malformed` の区別 |
| 6 | Connect / Disconnect | 23 | `ConnectLifecycle` / `ConnectFailurePaths` | [L1_Pcsc](L1_Pcsc.md) | 未接続時の拒否、二重接続、冪等な切断、Open 失敗時の破棄、ファクトリ null、Dispose 失敗、既定コンストラクタ |
| 7 | Read と重ね置きチェック | 37 | `ReadTests` / `ReadFailurePaths` | [L1_Pcsc](L1_Pcsc.md)、[L1_DuplicateGuard](L1_DuplicateGuard.md) | 先頭 16 バイト、RF / セッションの手順、重ね置きの各パターン、各種 `Indeterminate`、引数エラー、Read 応答の形式異常、例外時の後始末 |
| 8 | Write と重ね置きチェック | 45 | `WriteTests` / `WriteFailurePaths` | [L1_Pcsc](L1_Pcsc.md) | 1 ブロックずつの送信、読み戻しの分割、`CardRejected` と `Indeterminate` の区別、入力配列の複製、2 ブロック目以降の失敗、読み戻しの失敗 |
| 9 | オプション検証とログ | 24 | `OptionTests` / `OptionBoundariesAndResult` | [L1_PcscModels](L1_PcscModels.md)、[L1_Pcsc](L1_Pcsc.md) | 範囲の内側・外側の境界、既定値、スナップショット、RF 待ち、ログ失敗の無害化、`PcscResult.ToString` の書式 |
| 10 | 重ね置きの観測（DuplicateGuard / ScanObservation） | 13 | `ObservationTests` | [L1_DuplicateGuard](L1_DuplicateGuard.md) | 打ち切り条件（2 種類・衝突・異常）、無応答では続行、`IdList` の順序、`SingleIdm` の例外 |
| 11 | 並行呼び出し | 5 | `ConcurrencyTests` | [L1_Pcsc](L1_Pcsc.md) | 複数スレッドからの Read / Write でセッションが重ならない、取りこぼしが無い |
| 12 | 16 進変換（Hex） | 16 | `HexTests` | [Common_Hex](Common_Hex.md) | 書式、区切り文字と `0x` の受理、不正入力の拒否、往復一致、`SequenceEqual` の null |
| 13 | 実機トランスポートのリーダ不要な検査 | 11 | `OfflineTransportTests` | [L4](L4_PcscTransport.md)、[L5](L5_PcscNative.md) | 引数検証、未オープン時の例外、`Dispose` の安全性、制御コード、`PcscException`、存在しないリーダ名 |
| | **合計** | **279** | | | |

### 3.1 注目してほしいテスト

| テスト名 | なぜ重要か |
|---|---|
| あらゆる途中切れは成功にならない | 正常な応答を 0 バイト目から全長まで 1 バイトずつ切り詰めて、どの長さでも `Ok` にならないことを確認する |
| C0 6401 だけが「応答なし」 / C0 6400 などは空スロットではない | 通信異常を「カード無し」と誤判定しないことの確認 |
| 重ね置き検出時は Read / Write コマンドを送出しない | 模擬トランスポートの `Reads` / `Writes` カウンタが 0 であることまで見る |
| 途切れた DO を次の DO で補修しない | 1 枚分のフレームを 2 個の DO に分割して、つなぎ合わせないことを確認する |
| セッション終了の失敗は成功した読み出しも無効にする | 後始末の失敗を `Indeterminate` に落とすことの確認 |
| 成功応答でも読み戻しが違えば Indeterminate | カードが成功と応答したのに書かれていない、という状況の検出 |
| 5 ブロックは 5 コマンド・読み戻し 2 回（4+1） / 14 ブロックは…4 回（4+4+4+2） | 読み戻しの分割の確認 |
| 1 チャンク目 / 2 チャンク目のブロックが書けていなければ Indeterminate | 分割した読み戻しが、2 チャンク目まで実際に比較されていることの確認 |
| blockCount の桁あふれは引数エラー（巨大確保に到達しない） | `ThrowsArgument` で「引数エラーとして弾かれた」ことまで確認する（下記 5.3） |
| 処理中に呼び出し側が配列を書き換えても送出内容は変わらない | 下記 5.4 |
| 2 ブロック目の拒否時、1 ブロック目は書き換わり 2 ブロック目は元のまま | Write が原子的でないことを、メモリの状態とメッセージの両方で確認する |
| 複数スレッドから同時に Read してもセッションは重ならない | `Pcsc` 内部の `lock` が外れると失敗する（下記 5.5） |
| DO96 の上位 4 ビットが立っていたら衝突ビットがあっても不正 / Write 応答のフレームが 2 個なら Indeterminate（読み戻しに頼らず判定） | 別の検査に助けられて合格してしまわないよう、条件を工夫している（下記 5.5） |

---

## 4. `FakeTransport` — 模擬トランスポート

`IPcscTransport` を実装した、テスト用のリーダ兼カードです。

### 4.1 カードのメモリ

```csharp
private readonly byte[] _memory = new byte[FelicaLiteS.UserBlockCount * FelicaLiteS.BlockSize];
```

ユーザ領域 14 ブロック × 16 バイト = 224 バイトを持ちます。初期値はすべて 0 です。

| メソッド | 内容 |
|---|---|
| `Store(offset, data)` | テストの前に、メモリの `offset` バイト目から `data` を書いておく |
| `Read(offset, length)` | テストの後に、メモリの内容を直接取り出して確認する |

`Read` はカードへのコマンドではなく、テストが「本当に書かれたか」を確認するための覗き窓です。

### 4.2 振る舞いを変える設定

生成時にオブジェクト初期化子で指定します（例: `new FakeTransport(1) { SecondCard = 2 }`）。

| 設定 | 型 | 効果 |
|---|---|---|
| コンストラクタの `id` | `byte` | このカードの IDm の末尾バイト（IDm = `01 00 00 00 00 00 00 id`） |
| `SecondCard` | `byte` | 0 以外なら、Polling の応答に 2 枚目（この ID）のフレームを並べる |
| `NoCard` | `bool` | Polling に「応答なし」（`C0 6401`）を返す |
| `RejectRead` | `bool` | Read に `SF1=FF SF2=A1` を返す |
| `RejectWrite` | `bool` | Write に `SF1=FF SF2=A2` を返す |
| `RejectWriteForBlock` | `int` | このブロックの Write だけ `SF1=FF SF2=A2` で拒否し、メモリには書かない（-1 で無効） |
| `SilentlyDropWrite` | `bool` | Write に成功応答を返すが、メモリには書かない |
| `DropWriteForBlock` | `int` | このブロックの Write だけ、成功応答を返しつつ書かない（-1 で無効） |
| `ReadResponseIdm` | `byte` | 0 以外なら、Read の応答をこの ID の IDm で返す（別カードの応答の模擬） |
| `WrongReadResponseCode` | `bool` | Read の応答コードを `09`（Write 応答）にする |
| `ReadResponseOverride` | `TransceiveResult` | null 以外なら、Read コマンドにこの結果をそのまま返す |
| `WriteResponseOverride` | `TransceiveResult` | null 以外なら、Write コマンドにこの結果をそのまま返す（メモリには書かない） |
| `TruncateWriteResponse` | `bool` | Write の応答を 11 バイトに切り詰める |
| `TruncateWriteForBlock` | `int` | このブロックの Write だけ、メモリに書いたうえで応答を 11 バイトに切り詰める（-1 で無効） |
| `FailOpen` | `bool` | `Open` で `PcscException`（`0x8010000C`）を投げる |
| `FailSwitch` | `bool` | `SwitchProtocol` で例外を投げる |
| `FailEnd` | `bool` | `EndTransparentSession` で例外を投げる |
| `FailDispose` | `bool` | `Dispose` で例外を投げる（`Disposed` は立てたうえで投げる） |
| `MutateOnPoll` | `byte[]` | 最初の Polling のときに、この配列の先頭を `0xFF` に書き換える |
| `YieldInSession` | `bool` | セッション開始と `Transceive` でスレッドを譲り、並行呼び出しの重なりを起こしやすくする |

### 4.3 呼び出しの記録

| カウンタ | 増えるタイミング |
|---|---|
| `Polls` | Polling フレームを受け取った |
| `Reads` | Read コマンドを受け取った |
| `Writes` | Write コマンドを受け取った |
| `StartCalls` / `EndCalls` | セッション開始 / 終了 |
| `OnCalls` / `OffCalls` | RF ON / OFF |
| `SwitchCalls` | プロトコル切替 |
| `Disposed` | `Dispose` が呼ばれた |
| `Overlap` | セッションが開いている間に、別のセッション開始が呼ばれた（並行呼び出しの検出） |

これらで「コマンドを送らなかったこと」「手順が正しいこと」を確認します。
たとえば 1 回の Read では `Start=1, Switch=1, On=1, Off=2, End=1` になるはずです。
例外で中断した場合は、`Pcsc` が後始末として RF OFF とセッション終了をもう一度試みるので、回数が増えます。

セッション数の記録（`StartCalls` / `EndCalls` / `Overlap`）は内部の `lock` で守っているので、
並行呼び出しのテストでも正しく数えられます。

### 4.4 `Transceive` の振る舞い

```csharp
public TransceiveResult Transceive(byte[] frame, int timeoutUs)
{
    if (SeqEqual(frame, Felica.BuildPolling())) { /* Polling */ }
    if (frame[1] == FelicaLiteS.CommandRead)    { /* Read */ }
    if (frame[1] == FelicaLiteS.CommandWrite)   { /* Write */ }
    throw new InvalidOperationException("unexpected command");
}
```

**Polling** — 次の優先順で応答を決めます。

1. `MutateOnPoll` があれば、その配列の先頭を `0xFF` にする（1 回だけ）
2. `Script(...)` で登録した応答が残っていれば、先頭から 1 つ返す
3. `Script` を使い切っていれば、最後に登録した応答を返し続ける
4. `NoCard` なら「応答なし」
5. `SecondCard` があれば 2 枚分のフレーム、無ければ 1 枚分

**Read** — 次の優先順です。

1. `ReadResponseOverride` があればそれを返す
2. `RejectRead` なら `SF1=FF SF2=A1`
3. フレームの `[13]` をブロック数、`[15 + 2i]` をブロック番号として、メモリから読み出した応答を返す（`ReadResponseIdm` / `WrongReadResponseCode` を反映）

**Write** — 次の優先順です。

1. `WriteResponseOverride` があればそれを返す
2. `RejectWrite` なら `SF1=FF SF2=A2`
3. フレームの `[13]`（ブロック数）が 1 以外なら、実機の対象カードと同じく `SF1=02 SF2=A8` で拒否する
4. `[15]` のブロック番号が `RejectWriteForBlock` なら `SF1=FF SF2=A2`
5. `SilentlyDropWrite` でも `DropWriteForBlock` でもなければ、`[16..31]` の 16 バイトをメモリに書き込む
6. 成功応答を返す（`TruncateWriteResponse` か `TruncateWriteForBlock` に該当すれば 11 バイトに切り詰める）

**実機の癖（Write は 1 コマンド 1 ブロック）を模擬側でも再現**しているので、ライブラリが誤ってまとめて送ると、
テストが失敗します。

### 4.5 `Script` — Polling の応答を固定する

```csharp
FakeTransport swapped = new FakeTransport(1);
swapped.Script(Ok(1), Ok(2));   // 1 ラウンド目は ID 1、2 ラウンド目は ID 2
```

ラウンドごとに応答を変えたいときに使います。登録した数よりラウンドが多い場合は、最後の応答を繰り返します。

| 例 | 模擬している状況 |
|---|---|
| `Script(Ok(1), Ok(2))` | ラウンドごとに別のカードが応答する |
| `Script(Parse(Response(Card(1), Card(2))))` | 1 回の応答に 2 枚分が含まれる |
| `Script(Ok(1), Empty())` | 1 枚見えた後、応答が途切れる |
| `Script(Parse(Collision()))` | RF 衝突 |
| `Script(new TransceiveResult[] { null })` | トランスポートが null を返す（`params` に null を 1 個渡すには配列で包む） |

### 4.6 `Factory` と `Wrap`

| メソッド | 内容 |
|---|---|
| `Factory()` | 自分自身を返すデリゲート。`new Pcsc(options, fake.Factory())` に渡す |
| `Wrap()` | 模擬用オプションで `Pcsc` を作り、接続まで済ませて返す |

`Factory()` は毎回同じインスタンスを返すので、テスト後に同じ `FakeTransport` のカウンタやメモリを確認できます。

---

## 5. ヘルパ

### 5.1 `Pcsc` とオプション

| ヘルパ | 内容 |
|---|---|
| `Options(rounds, timeoutUs, settleMs)` | 指定した値の `PcscOptions` |
| `FakeOptions()` | `Options(2, 300000, 0)`。ラウンド数を最小の 2、RF 待ちを 0 にしてテストを速くする |
| `New(fake)` | 模擬用オプションと `fake` で `Pcsc` を作る（未接続） |
| `Connected(fake)` | `New` して `Connect("fake reader")` まで済ませる |

模擬のラウンド数は 2 なので、1 枚のときの `Polls` は 2 になります。

### 5.2 バイト列の組み立て

| ヘルパ | 作るもの |
|---|---|
| `Card(id)` | 対象カードの Polling 応答 20 バイト（LEN=20, CMD=01, IDm=`01..id`, PMm[1]=F1, RD=88B4） |
| `Response(frames...)` | `C0 03 00 90 00` `92 01 00` `96 02 00 00` + `97` × フレーム数 + `90 00` |
| `Parse(bytes)` | `TransceiveResult.Parse(bytes)` の短縮 |
| `Ok(id)` | 1 枚分の正常な Polling 結果 |
| `Empty()` | 「応答なし」（`C0 03 02 64 01 90 00`） |
| `Collision()` | 1 枚分の応答の `96` を衝突ビットにしたもの |
| `Idm(id)` | `01 00 00 00 00 00 00 id` |
| `Payload(start, length)` | `start, start+1, ...` と増えていくバイト列 |
| `ReadFrame(idm, data)` | Read 応答（SF=00 00、ブロック数、データ） |
| `StatusFrame(code, idm, sf1, sf2)` | ステータスだけの 12 バイトの応答 |
| `Corrupt(frame, index, value)` | 1 バイトだけ書き換えたコピー |
| `Take` / `Skip` / `Concat` | 先頭 N バイト / N バイト目以降 / 連結 |

`Response` の中での位置は、`[3..4]` が `C0` の SW、`[7]` が `92` の値、`[10]` が `96` の 1 バイト目です。
テストはこの位置を直接書き換えて異常な応答を作っています（例: `collision[10] = 2`）。

### 5.3 判定

| ヘルパ | 内容 |
|---|---|
| `Check(name, pass)` | 結果を表示して件数を数える |
| `Expect(name, actual, expected)` | バイト列の一致を確認し、不一致なら両方を表示 |
| `Throws(action)` | 何かしらの例外が起きれば `true` |
| `ThrowsArgument(action)` | **`ArgumentException`（とその派生）が起きたときだけ** `true` |
| `Throws<T>(action)` | **指定した型 `T`（とその派生）の例外が起きたときだけ** `true` |
| `SeqEqual(a, b)` | バイト列の比較 |
| `Section(title)` | セクションの見出しを表示 |

`Throws` と `ThrowsArgument` を分けているのには理由があります。
`BlockRange(2, int.MaxValue)` の範囲検査が桁あふれで壊れていると、判定をすり抜けて巨大な配列を確保しに行き、
`OutOfMemoryException` になります。`Throws` だとこれも「例外が出た = 拒否できた」と数えてしまい、
不具合を見逃します。`ThrowsArgument` なら、引数エラーとして弾かれた場合だけを合格にできます。
`Throws<T>` は同じ考え方を他の例外型（`ArgumentNullException`、`InvalidOperationException`、`NotSupportedException` など）に広げたものです。

### 5.4 `CallerMutationIsIgnored`

```csharp
byte[] caller = Payload(0x20, FelicaLiteS.DefaultAccessLength);
FakeTransport fake = new FakeTransport(1);
fake.MutateOnPoll = caller;
PcscResult result = Connected(fake).Write(caller);
return result.Ok && caller[0] == 0xFF && fake.Read(0, 16)[0] == 0x20;
```

`Pcsc.Write` が入力配列を複製していることの確認です。

- **Write の完了後に**配列を書き換えるだけでは、複製していなくても合格してしまい、テストになりません。
- そこで `MutateOnPoll` を使い、**重ね置きチェックの Polling の最中**（Write コマンドを組み立てる前）に書き換えます。
- 複製していなければ、カードに `0xFF` が書かれてテストが失敗します。
- `caller[0] == 0xFF` も確認しているのは、書き換えが実際に起きたこと（テストが空振りしていないこと）を保証するためです。

### 5.5 テストが空振りしないための工夫

ライブラリを意図的に壊して（変異させて）テストが失敗するかを確かめた結果、次の 2 件は当初の書き方では
**壊しても合格してしまう**ことが分かり、条件を直しています。

| テスト | 当初の問題 | 対策 |
|---|---|---|
| DO96 の上位 4 ビットが立っていたら衝突ビットがあっても不正 | 上位ビット `0x10` だけを立てると、予約ビットの検査が無くても `RfErrors != 0` で `Unknown` になり、検査の有無を区別できなかった | 衝突ビットと同時に `0x12` を立て、「衝突」ではなく「不正」になることを確認する |
| Write 応答のフレームが 2 個なら Indeterminate（読み戻しに頼らず判定） | フレーム数の検査が無くても、読み戻しの不一致で `Indeterminate` になっていた | `VerifyAfterWrite = false` にして、フレーム数の検査だけで判定させる |

並行呼び出しのテストは、`Pcsc.Execute` の `lock` を外すと 5 件中 4 件が失敗することを確認しています。

---

## 6. テストを追加するとき

1. 対象に合うセクションのメソッドに追記する。既存 ID を変えたくない場合は、セクションの追加分のメソッド（`ReadFailurePaths` など）の末尾に書く。
2. 模擬の状況は `FakeTransport` の設定か `Script` で作る。足りなければ設定項目を追加する。
3. 結果の確認は `Check("何を確認しているか", 条件)` で書く。テスト名は、失敗したときに何が壊れたか分かる文にする。
4. 「コマンドを送らなかった」ことを確認するときは、`Reads` / `Writes` / `Polls` のカウンタも条件に含める。
5. 例外で拒否することを確認するときは、原因を区別したいなら `ThrowsArgument` か `Throws<T>` を使う。
6. 別の検査に助けられて合格していないか（対象の処理を消しても失敗するか）を考える（5.5）。
7. 件数が変わったら、README の件数（§1.3、§7、§8）、このページの表、[../UnitTest.md](../UnitTest.md) を更新する。

`SelfTest.cs` は .NET 10 のプロジェクトでコンパイルされますが、ライブラリ本体と同じく C# 5 の範囲で書かれています
（匿名メソッド `delegate { ... }`、文字列の連結など）。.NET Framework 付属の C# 5 コンパイラ（csc 4.8）で
`src/*.cs` と一緒にコンパイルできることを確認しています。
