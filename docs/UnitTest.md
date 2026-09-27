# pcsc_if ユニットテスト仕様書（SelfTest 全 279 件）

| 項目 | 内容 |
|---|---|
| テストソース | [`test/PcscIfTest/SelfTest.cs`](../test/PcscIfTest/SelfTest.cs) |
| テストプロジェクト | [`test/PcscIfTest/PcscIfTest.csproj`](../test/PcscIfTest/PcscIfTest.csproj)（.NET 10、`src/*.cs` をリンク参照） |
| 対象ソース | `src/L1_Pcsc.cs` / `src/L1_PcscModels.cs` / `src/L1_DuplicateGuard.cs` / `src/L2_FelicaCommands.cs` / `src/L3_PcscResponse.cs` / `src/L4_PcscTransport.cs` / `src/L5_PcscNative.cs` / `src/Common_Hex.cs` |
| 件数 | **279 件**（13 セクション） |
| 最終実行結果 | `SelfTest: ALL PASS (279 checks)`、終了コード 0（2026-09-16）。C# 5 コンパイラ（csc 4.8）でビルドした実行ファイル（.NET Framework 4.8 ランタイム）で実行した。.NET 10 のビルドは同日、端末のアプリケーション制御ポリシーにより起動できなかった（テスト条件を 2 件修正する前の同じ 279 件は .NET 10 で全件合格している） |
| C# 5 互換 | `src/*.cs` と `test/PcscIfTest/*.cs` を .NET Framework 付属の C# 5 コンパイラ（csc 4.8）でコンパイルできることを確認済み |
| 関連ドキュメント | [source/PcscIfTest_SelfTest.md](source/PcscIfTest_SelfTest.md)（テストの仕組み・`FakeTransport` の解説）、[protocol.md](protocol.md)（バイト列）、[DuplicateGuard.md](DuplicateGuard.md)（重ね置き判定） |

このドキュメントは、自己テストの**チェック 1 件ごと**に「何を入力し、何を期待し、なぜその確認が必要か」を記載した一覧です。
テストの仕組み（`FakeTransport` の実装やヘルパの設計理由）は [source/PcscIfTest_SelfTest.md](source/PcscIfTest_SelfTest.md) を参照してください。

---

## 目次

- [0. 共通事項](#0-共通事項)
  - [0.1 実行方法と終了コード](#01-実行方法と終了コード)
  - [0.2 テスト ID と各項目の見方](#02-テスト-id-と各項目の見方)
  - [0.3 判定ヘルパ](#03-判定ヘルパ)
  - [0.4 テストデータ（バイト列）の早見表](#04-テストデータバイト列の早見表)
  - [0.5 模擬トランスポート FakeTransport の早見表](#05-模擬トランスポート-faketransport-の早見表)
- [1. コマンド組み立ての既知ベクタ（13 件）](#1-コマンド組み立ての既知ベクタ13-件)
- [2. PC/SC 応答パーサ（fail-closed）（45 件）](#2-pcsc-応答パーサfail-closed45-件)
- [3. 対象カードの Polling 応答の抽出（8 件）](#3-対象カードの-polling-応答の抽出8-件)
- [4. ユーザ領域の境界チェック（17 件）](#4-ユーザ領域の境界チェック17-件)
- [5. 読み書き応答の解析（22 件）](#5-読み書き応答の解析22-件)
- [6. Connect / Disconnect（23 件）](#6-connect--disconnect23-件)
- [7. Read と重ね置きチェック（37 件）](#7-read-と重ね置きチェック37-件)
- [8. Write と重ね置きチェック（45 件）](#8-write-と重ね置きチェック45-件)
- [9. オプション検証とログ（24 件）](#9-オプション検証とログ24-件)
- [10. 重ね置きの観測（DuplicateGuard / ScanObservation）（13 件）](#10-重ね置きの観測duplicateguard--scanobservation13-件)
- [11. 並行呼び出し（5 件）](#11-並行呼び出し5-件)
- [12. 16 進変換（Hex）（16 件）](#12-16-進変換hex16-件)
- [13. 実機トランスポートのリーダ不要な検査（11 件）](#13-実機トランスポートのリーダ不要な検査11-件)
- [14. 集計とトレーサビリティ](#14-集計とトレーサビリティ)
- [15. 現在のテストで確認していないこと](#15-現在のテストで確認していないこと)
- [16. 実機テストの結果（2026-09-16）](#16-実機テストの結果2026-09-16)

---

## 0. 共通事項

### 0.1 実行方法と終了コード

```bash
dotnet run --project test/PcscIfTest -- selftest
```

- リーダ・カードは不要です（`IPcscTransport` を模擬実装に差し替えて検証します）。
  ただし最後の 1 件（13-11）だけは OS の PC/SC 層（winscard.dll）を呼びます。存在しないリーダ名に接続するので、
  リーダや winscard.dll の有無にかかわらず必ず失敗し、結果は変わりません。
- 引数を省略しても `selftest` として動きます。
- 各チェックは `  [OK] テスト名` または `  [NG] テスト名` の 1 行で出力されます。
- 最後に `SelfTest: ALL PASS (279 checks)`、失敗があれば `SelfTest: N FAILED / 279 checks` を出力します。
- 各セクションは「元からあるテスト」の後に「追加分のテスト」が続きます。追加分はセクション末尾に並ぶので、
  元からあるテストの ID は変わっていません。
- **終了コード = 失敗件数**です。CI では 0 以外を失敗として扱ってください。
- 外部テストフレームワーク（xUnit / NUnit 等）は使っていません。

### 0.2 テスト ID と各項目の見方

本書では `セクション番号-連番`（例: `2-15`）でテストを識別します。連番は**出力される順番**と一致します。
テスト名は `SelfTest.cs` の `Check` / `Expect` に渡している文字列そのものです（出力と完全一致）。

各テストは次の表で記載します。

| 欄 | 意味 |
|---|---|
| ソース | `SelfTest.cs` の行番号 |
| 対象 | 検証対象のメソッド・プロパティ |
| 判定 | 使用している判定ヘルパ（0.3） |
| 前提・入力 | テストで与える状態と入力値 |
| 期待結果 | 合格条件（コード上の条件式を日本語化したもの） |
| 意図 | なぜこの確認が必要か。どんな不具合があると失敗するか |

### 0.3 判定ヘルパ

| ヘルパ | 合格条件 | 備考 |
|---|---|---|
| `Check(name, pass)` | `pass == true` | 全チェックの基本形 |
| `Expect(name, actual, expected)` | 2 つのバイト列が長さ・内容とも完全一致 | 不一致時は `expected:` / `actual:` を 16 進で表示 |
| `Throws(action)` | 何らかの例外が発生 | 例外の型は問わない |
| `ThrowsArgument(action)` | `ArgumentException`（派生の `ArgumentOutOfRangeException` を含む）が発生 | `OutOfMemoryException` 等は**不合格** |
| `Throws<T>(action)` | 型 `T`（とその派生）の例外が発生 | 別の型の例外は**不合格** |
| `SeqEqual(a, b)` | バイト列の一致 | null はどちらか一方でも不一致 |

### 0.4 テストデータ（バイト列）の早見表

#### `Idm(id)` — 8 バイト

```
01 00 00 00 00 00 00 id
```

`Idm(1)` の文字列表現は `0100000000000001` です。

#### `Card(id)` — 対象カードの Polling 応答フレーム（20 バイト）

| オフセット | 値 | 意味 |
|---|---|---|
| `[0]` | `14`（20） | LEN |
| `[1]` | `01` | 応答コード（Polling 応答） |
| `[2..9]` | `01 00 00 00 00 00 00 id` | IDm |
| `[10]` | `00` | PMm[0] |
| `[11]` | `F1` | PMm[1] = IC 種別（対象カード） |
| `[12..17]` | `00` | PMm 残り |
| `[18..19]` | `88 B4` | リクエストデータ = システムコード |

#### `Response(frames...)` — リーダ応答 TLV

```
C0 03 00 90 00      汎用ステータス（9000 = 成功）
92 01 00            受信ビット数の端数（0 = なし）
96 02 00 00         RF エラー（0 = なし）
97 LL <frame>       受信データ（フレーム数だけ繰り返す）
90 00               SW
```

テストで直接書き換えているオフセット（`Response(Card(1))`、全長 36 バイトの場合）:

| オフセット | 内容 | 書き換え例 |
|---|---|---|
| `[2]` | `C0` の値の 1 バイト目 | `2`（応答なしの形） |
| `[3..4]` | `C0` の SW | `6A 81`、`64 01` |
| `[7]` | `92` の値（端数ビット数） | `7` |
| `[10]` | `96` の値の 1 バイト目（RF エラービット） | `1`=CRC、`2`=衝突、`4`=パリティ、`8`=フレーミング |
| `[12..13]` | `97 14` | — |
| `[14..33]` | `Card(1)` の 20 バイト | — |
| `[34..35]` | SW `90 00` | `[34]=6F` |

#### その他のヘルパ

| ヘルパ | 内容 |
|---|---|
| `Parse(bytes)` | `TransceiveResult.Parse(bytes)` |
| `Ok(id)` | `Parse(Response(Card(id)))`：1 枚分の正常な Polling 結果 |
| `Empty()` | `Parse(C0 03 02 64 01 90 00)`：「ICC 応答なし」 |
| `Collision()` | `Response(Card(1))` の `[10]` を `2`（衝突ビット）にしたバイト列 |
| `Payload(start, n)` | `start, start+1, ...` と 1 ずつ増える n バイト（`byte` なので 0xFF の次は 0x00） |
| `ReadFrame(idm, data)` | `LEN 07 IDm(8) 00 00 ブロック数 data`（LEN = 13 + data 長） |
| `StatusFrame(code, idm, sf1, sf2)` | `0C code IDm(8) sf1 sf2`（12 バイト） |
| `Corrupt(frame, i, v)` | `frame` を複製し `[i]` だけ `v` にしたもの |
| `Take(a, n)` / `Skip(a, n)` / `Concat(a, b)` | 先頭 n バイト / n バイト目以降 / 連結 |
| `Options(rounds, timeoutUs, settleMs)` | 指定値の `PcscOptions` |
| `FakeOptions()` | `Options(2, 300000, 0)`（ラウンド 2、RF 待ち 0 でテストを高速化） |
| `New(fake)` | `new Pcsc(FakeOptions(), fake.Factory())`（未接続） |
| `Connected(fake)` | `New(fake)` の後 `Connect("fake reader")` 済み |

### 0.5 模擬トランスポート `FakeTransport` の早見表

`FakeTransport(id)` は IDm = `Idm(id)` の対象カード 1 枚と、それを載せたリーダを模擬します。
ユーザ領域 14 ブロック × 16 バイト = 224 バイトのメモリを持ち、初期値はすべて `00` です。

| 設定 | 効果 |
|---|---|
| `SecondCard = n` | Polling 応答に `Card(n)` のフレームも並べる（2 枚重ね） |
| `NoCard` | Polling に `Empty()`（C0 6401）を返す |
| `RejectRead` | Read に `StatusFrame(07, IDm, FF, A1)` を返す |
| `RejectWrite` | Write に `StatusFrame(09, IDm, FF, A2)` を返す |
| `SilentlyDropWrite` | Write に成功応答を返すが、メモリには書かない |
| `DropWriteForBlock = b` | ブロック b の Write だけ、成功応答を返しつつ書かない |
| `ReadResponseIdm = n` | Read 応答の IDm を `Idm(n)` にする |
| `WrongReadResponseCode` | Read 応答の応答コードを `09` にする |
| `TruncateWriteResponse` | Write 応答を先頭 11 バイトに切り詰める（メモリへの書き込みは行う） |
| `TruncateWriteForBlock = b` | ブロック b の Write だけ、メモリに書いたうえで応答を 11 バイトに切り詰める |
| `RejectWriteForBlock = b` | ブロック b の Write だけ `SF1=FF SF2=A2` で拒否する（メモリには書かない） |
| `ReadResponseOverride = r` | Read コマンドに `TransceiveResult` の `r` をそのまま返す |
| `WriteResponseOverride = r` | Write コマンドに `r` をそのまま返す（メモリには書かない） |
| `FailOpen` | `Open` で `PcscException("SCardConnect", 0x8010000C)` を投げる |
| `FailSwitch` | `SwitchProtocol` で `NotSupportedException` を投げる |
| `FailEnd` | `EndTransparentSession` で `InvalidOperationException` を投げる |
| `FailDispose` | `Dispose` で `InvalidOperationException` を投げる（`Disposed` は立てる） |
| `MutateOnPoll = arr` | 最初の Polling 受信時に `arr[0] = 0xFF` にする |
| `YieldInSession` | セッション開始と `Transceive` でスレッドを譲り、並行呼び出しの重なりを起こしやすくする |
| `Script(r1, r2, ...)` | Polling の応答を順に固定。使い切ったら最後の応答を返し続ける（`NoCard` / `SecondCard` より優先） |

| カウンタ | 増えるタイミング |
|---|---|
| `Polls` / `Reads` / `Writes` | Polling / Read / Write フレームを受信 |
| `StartCalls` / `EndCalls` | `StartTransparentSession` / `EndTransparentSession` |
| `OffCalls` / `OnCalls` | `TurnOffRf` / `TurnOnRf` |
| `SwitchCalls` | `SwitchProtocol` |
| `Disposed` | `Dispose` が呼ばれた |
| `Overlap` | セッションが開いている間に、別のセッション開始が呼ばれた（並行呼び出しの検出） |

`FakeTransport.Read(offset, length)` はカードコマンドではなく、**テストがメモリを直接覗くための関数**です。
Write はブロック数 1 以外のコマンドを `SF1=02 SF2=A8` で拒否し、実機の対象カードの制約を再現します。

---

## 1. コマンド組み立ての既知ベクタ（13 件）

メソッド `CommandVectors()`（[SelfTest.cs:85](../test/PcscIfTest/SelfTest.cs#L85)）。
カードやリーダへ送るバイト列を**1 バイト単位で**固定し、組み立て処理の退行を検出します。
バイト列の意味は [protocol.md](protocol.md) §2・§4 を参照してください。

#### 1-01 Polling は 88B4 / RC=01 / 16 スロット

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:87](../test/PcscIfTest/SelfTest.cs#L87) |
| 対象 | `Felica.BuildPolling()`（引数なし） |
| 判定 | `Expect` |
| 前提・入力 | なし |
| 期待結果 | `06 00 88 B4 01 0F` と完全一致 |
| 意図 | 重ね置きチェックで送る Polling の既定値を固定する。LEN=6、コマンド `00`、システムコード `88B4`（ビッグエンディアン）、リクエストコード `01`（システムコード要求）、タイムスロット `0F`（16 スロット）。システムコードのバイト順やスロット数が変わると、対象カードを観測できなくなる／FakeTransport が Polling と認識しなくなるため失敗する |

#### 1-02 Polling TSN=1

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:90](../test/PcscIfTest/SelfTest.cs#L90) |
| 対象 | `Felica.BuildPolling(ushort systemCode, byte requestCode, byte timeSlot)` |
| 判定 | `Expect` |
| 前提・入力 | `systemCode=0xFFFF`、`requestCode=0x01`、`timeSlot=0` |
| 期待結果 | `06 00 FF FF 01 00` と完全一致 |
| 意図 | 引数版が値をそのままの位置に配置することの確認。`timeSlot=0` は「1 スロット」（TSN=1）を意味する。ワイルドカードのシステムコード `FFFF` でも上位・下位バイトの順序が正しいことを見る |

#### 1-03 不正なタイムスロットは拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:93](../test/PcscIfTest/SelfTest.cs#L93) |
| 対象 | `Felica.BuildPolling(0xFFFF, 1, 5)` |
| 判定 | `Throws` |
| 前提・入力 | `timeSlot=5` |
| 期待結果 | 例外が発生する（実装は `ArgumentOutOfRangeException`） |
| 意図 | タイムスロットは `0 / 1 / 3 / 7 / 15`（1/2/4/8/16 スロット）だけが有効。それ以外を送るとカードの挙動が未定義になるため、組み立て時点で拒否することを確認する |

#### 1-04 不正なリクエストコードは拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:95](../test/PcscIfTest/SelfTest.cs#L95) |
| 対象 | `Felica.BuildPolling(0xFFFF, 3, 15)` |
| 判定 | `Throws` |
| 前提・入力 | `requestCode=3` |
| 期待結果 | 例外が発生する（`requestCode > 2` で `ArgumentOutOfRangeException`） |
| 意図 | リクエストコードは `00`（要求なし）/`01`（システムコード）/`02`（通信性能）のみ。範囲外を送らないことを確認する |

#### 1-05 プロトコル切替はフレーミング指定（アクティベーションではない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:98](../test/PcscIfTest/SelfTest.cs#L98) |
| 対象 | `Rcs300Transport.BuildFelicaSwitchApdu()` |
| 判定 | `Expect` |
| 前提・入力 | なし |
| 期待結果 | `FF 50 00 02 04 8F 02 03 00 00` と完全一致 |
| 意図 | Transparent Session の Switch Protocol APDU を固定する。`FF 50 00 02` = Switch Protocol、Lc=`04`、DO `8F` 長さ `02`、値 `03`（対象カードのプロトコル）`00`（**フレーミング層の指定**）、Le=`00`。末尾から 2 バイト目が `00` 以外（アクティベーション指定など）になると、実機でカードの活性化手順が変わり Polling の前提が崩れるため、値を固定して退行を防ぐ |

#### 1-06 Transceive APDU（Polling, 100ms）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:102](../test/PcscIfTest/SelfTest.cs#L102) |
| 対象 | `Rcs300Transport.BuildTransceiveApdu(byte[] frame, int timeoutUs)` |
| 判定 | `Expect` |
| 前提・入力 | `frame = 06 00 FF FF 01 00`（6 バイト）、`timeoutUs = 100000` |
| 期待結果 | 次の 27 バイトと完全一致<br>`FF 50 00 01 00 00 11` / `5F 46 04 A0 86 01 00` / `95 82 00 06` / `06 00 FF FF 01 00` / `00 00 00` |
| 意図 | カードフレームを包む `FF 50` 封筒の形式を固定する。<br>・`FF 50 00 01` = Transceive、`00 00 11` = 拡張 Lc（データ長 17 = 11 + フレーム長 6）<br>・`5F 46 04` + タイムアウト 100000us（`0x000186A0`）の**リトルエンディアン** `A0 86 01 00`<br>・`95 82 00 06` = 送信データ DO、長さ 6 を**ビッグエンディアン** 2 バイト<br>・末尾 `00 00 00` = 実機で観測した終端<br>エンディアンの取り違え、`doLen` の計算誤り、終端の欠落のいずれも検出できる |

#### 1-07 空フレームの Transceive は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:111](../test/PcscIfTest/SelfTest.cs#L111) |
| 対象 | `Rcs300Transport.BuildTransceiveApdu(new byte[0], 100000)` |
| 判定 | `Throws` |
| 前提・入力 | 長さ 0 のフレーム |
| 期待結果 | 例外が発生する（`ArgumentException`） |
| 意図 | 送信データの無い Transceive をリーダへ送らないことを確認する |

#### 1-08 タイムアウト 0 の Transceive は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:113](../test/PcscIfTest/SelfTest.cs#L113) |
| 対象 | `Rcs300Transport.BuildTransceiveApdu(new byte[] { 6 }, 0)` |
| 判定 | `Throws` |
| 前提・入力 | `timeoutUs = 0` |
| 期待結果 | 例外が発生する（`ArgumentOutOfRangeException`） |
| 意図 | タイムアウト 0 はリーダ側で「無制限」や即時失敗など機種依存の解釈になり得るため、組み立て時点で拒否することを確認する |

#### 1-09 Read コマンド（S_PAD0+S_PAD1, サービス 000B）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:117](../test/PcscIfTest/SelfTest.cs#L117) |
| 対象 | `FelicaLiteS.BuildRead(idm, FelicaLiteS.BlockRange(0, 2))` |
| 判定 | `Expect` |
| 前提・入力 | `idm = Idm(1)`、ブロック `00, 01` |
| 期待結果 | `12 06 01 00 00 00 00 00 00 01 01 0B 00 02 80 00 80 01`（18 バイト）と完全一致 |
| 意図 | Read Without Encryption の形式を固定する。LEN=`12`(18)、コマンド `06`、IDm 8 バイト、サービス数 `01`、サービスコード `000B` の**リトルエンディアン** `0B 00`、ブロック数 `02`、ブロックリスト要素 `80 00` / `80 01`（2 バイト形式、アクセスモード 0、サービス順 0）。サービスコードのバイト順誤りや、ブロックリストを 3 バイト形式にしてしまう誤りを検出する |

#### 1-10 Write コマンド（S_PAD0, サービス 0009）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:121](../test/PcscIfTest/SelfTest.cs#L121) |
| 対象 | `FelicaLiteS.BuildWrite(idm, FelicaLiteS.BlockRange(0, 1), Payload(0x10, 16))` |
| 判定 | `Expect` |
| 前提・入力 | `idm = Idm(1)`、ブロック `00`、データ `10 11 ... 1F` |
| 期待結果 | `20 08 01 00 00 00 00 00 00 01 01 09 00 01 80 00` + `10 11 ... 1F`（計 32 バイト）と完全一致 |
| 意図 | Write Without Encryption の形式を固定する。LEN=`20`(32 = ヘッダ 16 + データ 16)、コマンド `08`、サービスコード `0009`（読み書き可・認証不要）を `09 00`、ブロック数 `01`、ブロックリスト `80 00`、その後ろにブロックデータ。Read 用サービス `000B`（読み取り専用）を誤って使うとカードが拒否するため、その取り違えを検出する |

#### 1-11 既定アクセスは 16 バイト = 1 ブロック

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:125](../test/PcscIfTest/SelfTest.cs#L125) |
| 対象 | 定数 `FelicaLiteS.DefaultAccessLength` / `DefaultAccessBlocks` / `BlockSize` |
| 判定 | `Check` |
| 前提・入力 | なし |
| 期待結果 | `DefaultAccessLength == 16` かつ `DefaultAccessBlocks == 1` かつ `BlockSize == 16` |
| 意図 | 引数なしの `Read()` / `Write(data)` は「先頭 16 バイト（S_PAD0 の 1 ブロック）」という API 仕様を定数レベルで固定する。過去に既定が 32 バイトだった経緯があり、既定値が意図せず変わることを防ぐ |

### 1 の追加分: フレーム長の上限

メソッド `CommandLengthLimits()`（[SelfTest.cs:722](../test/PcscIfTest/SelfTest.cs#L722)）。
Transceive 封筒の送信データ長は 2 バイト（最大 `FFFF`）で、`doLen = 11 + フレーム長` です。
したがって送れるカードフレームは最大 65524 バイトです。

#### 1-12 Transceive は最大長 65524 バイトのフレームを許可（データ長 FFFF）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:726](../test/PcscIfTest/SelfTest.cs#L726) |
| 対象 | `Rcs300Transport.BuildTransceiveApdu(byte[] frame, int timeoutUs)` |
| 判定 | `Check` |
| 前提・入力 | 長さ 65524 のフレーム（中身はすべて `00`）、`timeoutUs = 100000` |
| 期待結果 | APDU 全長が 65545（ヘッダ 18 + フレーム 65524 + 終端 3）、`[5..6]`（拡張 Lc の下位 2 バイト）が `FF FF`、`[16..17]`（`95 82` の後の送信データ長）が `FF F4` |
| 意図 | 上限ちょうどのフレームを拒否せず、2 つの長さフィールドをビッグエンディアンで正しく埋めることを確認する。上限判定の `>` と `>=` の取り違えや、長さのバイト順の誤りを検出する |

#### 1-13 65525 バイトのフレームは拒否（データ長が 2 バイトに収まらない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:730](../test/PcscIfTest/SelfTest.cs#L730) |
| 対象 | `Rcs300Transport.BuildTransceiveApdu` |
| 判定 | `ThrowsArgument` |
| 前提・入力 | 長さ 65525 のフレーム |
| 期待結果 | `ArgumentException` が発生する |
| 意図 | `doLen` が 65536 になり、2 バイトの長さフィールドに収まらない。上位ビットが切り捨てられた不正な APDU を組み立てず、引数エラーで拒否することを確認する（1-12 と対の境界値） |

---

## 2. PC/SC 応答パーサ（fail-closed）（45 件）

メソッド `ResponseParsing()`（[SelfTest.cs:134](../test/PcscIfTest/SelfTest.cs#L134)）。
対象は `TransceiveResult.Parse` / `PcscResponse.Parse` / `Rcs300Transport.ValidateManagementResponse`（[L3_PcscResponse.cs](../src/L3_PcscResponse.cs)、[L4_PcscTransport.cs](../src/L4_PcscTransport.cs)）です。

`TransceiveResult` は `Ok` / `NoResponse` / `Collision` / `Unknown` の**ちょうど 1 つ**が立ちます。
このセクションでは「壊れた応答・曖昧な応答を `Ok`（成功）や `NoResponse`（カード無し）と誤認しない」ことを確認します。
特に記載がない限り、基準の応答は `good = Response(Card(1))`（36 バイト、[0.4](#04-テストデータバイト列の早見表)）です。

#### 2-01 正常な C0/92/96/97 応答

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:137](../test/PcscIfTest/SelfTest.cs#L137) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good`：`C0 03 00 90 00 92 01 00 96 02 00 00 97 14 <Card(1)> 90 00` |
| 期待結果 | `Ok == true` |
| 意図 | 以降の異常系テストの基準。正常な応答（SW=9000、C0=9000、端数ビット 0、RF エラー 0、DO97 が 1 個）が成功と判定されることを確認する。これが失敗すると異常系の「成功にならない」が無意味になる |

#### 2-02 エラー SW は成功にならない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:139](../test/PcscIfTest/SelfTest.cs#L139) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good` の末尾 SW を `6F 00` に変更（`[34] = 0x6F`） |
| 期待結果 | `Unknown == true` |
| 意図 | DO97 に正しいカードフレームが入っていても、最外側の SW が `9000` でなければ成功にしないことを確認する（`transportSuccess` の条件） |

#### 2-03 C0 のエラーは成功にならない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:143](../test/PcscIfTest/SelfTest.cs#L143) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good` の C0 の SW を `6A 81` に変更（`[3]=0x6A`、`[4]=0x81`）。最外側 SW は `9000` のまま |
| 期待結果 | `Unknown == true` |
| 意図 | 外側 SW が成功でも、汎用ステータス DO `C0` がエラーならコマンドは失敗である。データが付いていても成功にせず、`6401` 以外なので「応答なし」にもしないことを確認する |

#### 2-04 DO96 の衝突はデータがあっても衝突扱い

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:147](../test/PcscIfTest/SelfTest.cs#L147) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good` の `[10] = 2`（DO96 の衝突ビット） |
| 期待結果 | `Collision == true` かつ `Ok == false` |
| 意図 | RF 衝突は「複数枚が同時に応答した可能性」を示す。DO97 に 1 枚分のフレームが取れていても、それを 1 枚と信じると重ね置きを見逃すため、衝突として扱い成功にしないことを確認する |

#### 2-05 DO96 の CRC 異常はデータを無効化

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:151](../test/PcscIfTest/SelfTest.cs#L151) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good` の `[10] = 1`（CRC 異常ビット） |
| 期待結果 | `Unknown == true` |
| 意図 | CRC 異常の受信データは中身が壊れている可能性がある。`RfErrors != 0` なら DO97 を採用しないことを確認する |

#### 2-06 DO96 のパリティ異常はデータを無効化

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:153](../test/PcscIfTest/SelfTest.cs#L153) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good` の `[10] = 4`（パリティ異常ビット） |
| 期待結果 | `Unknown == true` |
| 意図 | 2-05 と同じ。パリティ異常でもデータを無効化する |

#### 2-07 DO96 のフレーミング異常はデータを無効化

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:155](../test/PcscIfTest/SelfTest.cs#L155) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good` の `[10] = 8`（フレーミング異常ビット） |
| 期待結果 | `Unknown == true` |
| 意図 | 2-05 と同じ。フレーミング異常でもデータを無効化する。2-04〜2-07 で DO96 の下位 4 ビットすべてを網羅している |

#### 2-08 端数ビットのある応答は不正

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:157](../test/PcscIfTest/SelfTest.cs#L157) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good` の `[7] = 7`（DO92 = 最終バイトの有効ビット数 7） |
| 期待結果 | `Unknown == true` |
| 意図 | バイト境界に揃わない受信は対象カードの正常応答ではない。値 7 はパーサとしては許容範囲（0..7）なので TLV 解析は通るが、`PartialByte` により成功にしないことを確認する |

#### 2-09 C0 6401 だけが「応答なし」

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:160](../test/PcscIfTest/SelfTest.cs#L160) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 02 64 01 90 00`（実機で観測した「ICC 応答なし」） |
| 期待結果 | `NoResponse == true` |
| 意図 | 「カードが応答しなかった」と判定してよい唯一の形を固定する。外側 SW=9000、C0=`6401`、RF エラー無し、端数ビット無し、DO97 無し、のすべてを満たすときだけ `NoResponse` になる |

#### 2-10 C0 6400 は空スロットではない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:162](../test/PcscIfTest/SelfTest.cs#L162)（ループ 1 回目） |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 02 64 00 90 00` |
| 期待結果 | `Unknown == true` |
| 意図 | `6400`（実行エラー）を「応答なし」と誤認すると、通信異常を「カード無し」として扱ってしまう。`6401` と 1 ビット違いの値で境界を確認する |

#### 2-11 C0 6700 は空スロットではない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:162](../test/PcscIfTest/SelfTest.cs#L162)（ループ 2 回目） |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 02 67 00 90 00` |
| 期待結果 | `Unknown == true` |
| 意図 | `6700`（長さ誤り）はコマンド側の異常。「応答なし」にしないことを確認する |

#### 2-12 C0 6A80 は空スロットではない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:162](../test/PcscIfTest/SelfTest.cs#L162)（ループ 3 回目） |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 02 6A 80 90 00` |
| 期待結果 | `Unknown == true` |
| 意図 | `6A80`（データ不正）を「応答なし」にしないことを確認する |

#### 2-13 C0 6A81 は空スロットではない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:162](../test/PcscIfTest/SelfTest.cs#L162)（ループ 4 回目） |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 02 6A 81 90 00` |
| 期待結果 | `Unknown == true` |
| 意図 | `6A81`（機能非対応）を「応答なし」にしないことを確認する |

#### 2-14 C0 6F00 は空スロットではない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:162](../test/PcscIfTest/SelfTest.cs#L162)（ループ 5 回目） |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 02 6F 00 90 00` |
| 期待結果 | `Unknown == true` |
| 意図 | `6F00`（原因不明のエラー）を「応答なし」にしないことを確認する。2-10〜2-14 で「`6401` 以外はすべて Unknown」を代表値で確認している |

#### 2-15 「応答なし」とデータの同居は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:167](../test/PcscIfTest/SelfTest.cs#L167) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good` の C0 を `02 64 01` に変更（`[2]=2`、`[3]=0x64`、`[4]=0x01`）。DO97（Card(1)）は残す |
| 期待結果 | `Unknown == true` |
| 意図 | 「応答なし」と言いながら受信データがある矛盾した応答。C0 が `9000` でないので成功にもならず、DO97 があるので「応答なし」にもならない。どちらにも倒さず `Unknown` にすることを確認する |

#### 2-16 裸の 9000 は空スロットではない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:170](../test/PcscIfTest/SelfTest.cs#L170) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `90 00`（TLV 無し、SW だけ） |
| 期待結果 | `Unknown == true` |
| 意図 | SW は成功でも、C0 `6401` という明示的な「応答なし」が無い。受信データも無いので成功でもない。「データが無い＝カードが無い」と短絡しないことを確認する |

#### 2-17 DO92 の不正値は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:171](../test/PcscIfTest/SelfTest.cs#L171) |
| 対象 | `PcscResponse.Parse` → `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `92 01 64 90 00`（端数ビット数 = 0x64 = 100） |
| 期待結果 | `Unknown == true` |
| 意図 | 端数ビット数は 0..7 しかあり得ない。範囲外の値を含む応答は TLV 解析自体を失敗（`Valid=false`）にすることを確認する |

#### 2-18 SW 欠落は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:172](../test/PcscIfTest/SelfTest.cs#L172) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `Take(good, 34)`（末尾の `90 00` を削除） |
| 期待結果 | `Unknown == true` |
| 意図 | SW が欠けると、パーサはカードフレームの末尾 `88 B4` を SW とみなし、DO97 の長さ 20 に対して残りが 18 バイトしか無くなる。SW 不一致・TLV 途中切れのどちらでも成功にしないことを確認する |

#### 2-19 あらゆる途中切れは成功にならない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:173](../test/PcscIfTest/SelfTest.cs#L173) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check`（36 通りをまとめて 1 件） |
| 前提・入力 | `n = 0, 1, ..., 35` のそれぞれで `Take(good, n)` |
| 期待結果 | すべての `n` で `Ok == false` |
| 意図 | 通信の途中切れは任意の位置で起こり得る。0 バイトから全長 − 1 バイトまで 1 バイトずつ切り詰め、どの長さでも成功と誤認しないことを網羅的に確認する。1 つでも `Ok` になると不合格 |

#### 2-20 末尾の壊れた TLV は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:177](../test/PcscIfTest/SelfTest.cs#L177) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `Take(good, 34)` + `97 03 01 90 00`（正常な TLV 群の後ろに、長さ 3 と宣言して 1 バイトしか無い DO97） |
| 期待結果 | `Unknown == true` |
| 意図 | 前半に正常な DO97 があっても、後続の TLV が壊れていれば応答全体を失敗にする。「読めたところまで採用する」実装を検出する |

#### 2-21 不定長 BER は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:179](../test/PcscIfTest/SelfTest.cs#L179) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `97 80 90 00`（長さバイト `80` = BER の不定長形式） |
| 期待結果 | `Unknown == true` |
| 意図 | 長さは短形式（`00..7F`）、`81 xx`、`82 xx xx` だけを受け付ける。不定長やその他の長形式を無視・誤解釈せず失敗にすることを確認する |

#### 2-22 C0 の重複は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:180](../test/PcscIfTest/SelfTest.cs#L180) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 00 90 00 C0 03 00 90 00 90 00`（C0 が 2 個） |
| 期待結果 | `Unknown == true` |
| 意図 | 汎用ステータスが複数あると、どちらが正しいか決められない（後勝ちにすると失敗を成功で上書きできてしまう）。重複自体を不正とすることを確認する |

#### 2-23 未知タグは fail-closed

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:182](../test/PcscIfTest/SelfTest.cs#L182) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `AA 00 90 00`（未知タグ `AA`、長さ 0） |
| 期待結果 | `Unknown == true` |
| 意図 | 解釈できないタグを読み飛ばすと、そこに含まれるエラー情報を見落とす可能性がある。未知タグは失敗にすることを確認する |

#### 2-24 複数の DO97 はすべて保持

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:183](../test/PcscIfTest/SelfTest.cs#L183) |
| 対象 | `TransceiveResult.Parse` の `Frames` |
| 判定 | `Check` |
| 前提・入力 | `Response(Card(1), Card(2))`（DO97 が 2 個） |
| 期待結果 | `Frames.Count == 2` |
| 意図 | 1 応答に複数のカードフレームが含まれる場合、最後の 1 個だけを保持すると 2 枚目を見落として重ね置きを検出できない。すべて保持することを確認する |

#### 2-25 管理コマンドは検証済みの成功だけ受理

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:186](../test/PcscIfTest/SelfTest.cs#L186) |
| 対象 | `PcscResponse.Parse(...).CommandSucceeded` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 00 90 00 90 00` |
| 期待結果 | `CommandSucceeded == true` |
| 意図 | セッション開始・RF ON/OFF などの管理コマンドの正常応答を成功と判定する基準。条件は SW=9000、C0 が無いか 9000、RF エラー 0、端数ビット無し、DO97 無し |

#### 2-26 管理コマンドは C0 の失敗を拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:188](../test/PcscIfTest/SelfTest.cs#L188) |
| 対象 | `Rcs300Transport.ValidateManagementResponse(genericError, "test")` |
| 判定 | `Throws` |
| 前提・入力 | 2-03 の `genericError`（C0=`6A81`、外側 SW=9000） |
| 期待結果 | 例外が発生する（`InvalidOperationException`） |
| 意図 | 管理コマンドが拒否されたのに処理を続けると、RF やセッションが想定と違う状態で Polling することになる。外側 SW だけで成功と判断しないことを確認する |

#### 2-27 管理コマンドはエラー SW を拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:190](../test/PcscIfTest/SelfTest.cs#L190) |
| 対象 | `Rcs300Transport.ValidateManagementResponse(new byte[] { 0x6F, 0 }, "test")` |
| 判定 | `Throws` |
| 前提・入力 | `6F 00` |
| 期待結果 | 例外が発生する |
| 意図 | 外側 SW がエラーの管理コマンド応答を拒否することを確認する |

#### 2-28 管理コマンドは空応答を拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:192](../test/PcscIfTest/SelfTest.cs#L192) |
| 対象 | `Rcs300Transport.ValidateManagementResponse(new byte[0], "test")` |
| 判定 | `Throws` |
| 前提・入力 | 長さ 0 の応答 |
| 期待結果 | 例外が発生する |
| 意図 | 応答が返ってこなかった（2 バイト未満）場合に「エラーが無かった」とみなさないことを確認する |

### 2 の追加分: TLV の長さ表現と各 DO の細部

メソッド `ResponseParsingDetails()`（[SelfTest.cs:738](../test/PcscIfTest/SelfTest.cs#L738)）。
`PcscResponse.Parse` が受け付ける長さ表現は、短形式（`00..7F`）、`81 xx`、`82 xx xx` の 3 種類だけです。
各 DO の値の形も厳密に検査し、規定外なら応答全体を不正（`Valid = false` → `Unknown`）にします。
以下、`card = Card(1)`、`good = Response(card)`、`sw = 90 00`、`c0Ok = C0 03 00 90 00` です。

#### 2-29 長さ 81 形式の DO97 を受理

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:746](../test/PcscIfTest/SelfTest.cs#L746) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `97 81 14 <card> 90 00`（長さを `81 14` = 20 で表現。C0 / 92 / 96 は無し） |
| 期待結果 | `Ok == true`、`Frames.Count == 1`、`Frames[0]` が `card` と一致 |
| 意図 | 長いデータで使われる BER の長形式 `81 xx` を正しく解釈し、長さバイトの直後からデータを切り出すことを確認する。C0 が無い応答でも、SW が `9000` でデータがあれば成功になる |

#### 2-30 長さ 82 形式の DO97 を受理

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:749](../test/PcscIfTest/SelfTest.cs#L749) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `97 82 00 14 <card> 90 00`（長さを `82 00 14` = 20 で表現） |
| 期待結果 | `Ok == true`、`Frames.Count == 1`、`Frames[0]` が `card` と一致 |
| 意図 | 2 バイト長形式を**ビッグエンディアン**で解釈することを確認する。バイト順を誤ると長さが 5120 になり、途中切れとして失敗する |

#### 2-31 長さバイトが欠けた 81 形式は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:751](../test/PcscIfTest/SelfTest.cs#L751) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `97 81 90 00`（`81` の後に長さバイトが無く、SW だけが残っている） |
| 期待結果 | `Unknown == true` |
| 意図 | 長さバイトを読む前に残りの長さを確認し、SW 部分を長さとして読み込まないことを確認する |

#### 2-32 長さバイトが欠けた 82 形式は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:753](../test/PcscIfTest/SelfTest.cs#L753) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `97 82 00 90 00`（`82` の後に長さが 1 バイトしか無い） |
| 期待結果 | `Unknown == true` |
| 意図 | 2-31 の 2 バイト長版。長さが途中で切れた TLV を失敗にすることを確認する |

#### 2-33 81 形式でも宣言長が残りより長ければ拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:755](../test/PcscIfTest/SelfTest.cs#L755) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `97 81 15 <card> 90 00`（長さ 21 と宣言、実データは 20 バイト） |
| 期待結果 | `Unknown == true` |
| 意図 | 長形式でも、宣言長と実データ長の照合（途中切れの検出）が働くことを確認する。照合が短形式だけに実装されていると、SW の 1 バイト目をデータとして取り込んでしまう |

#### 2-34 長さ 83 以上の長形式は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:757](../test/PcscIfTest/SelfTest.cs#L757) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `97 83 00 00 14 <card> 90 00`（3 バイト長形式） |
| 期待結果 | `Unknown == true` |
| 意図 | リーダ応答で使われない 3 バイト以上の長形式を、推測で解釈せず失敗にすることを確認する（2-21 の不定長 `80` と合わせて、`81` / `82` 以外の長形式をすべて拒否） |

#### 2-35 DO96 の 1 バイト形式（エラーなし）を受理

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:761](../test/PcscIfTest/SelfTest.cs#L761) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 00 90 00` `96 01 00` `97 14 <card>` `90 00` |
| 期待結果 | `Ok == true` |
| 意図 | RF エラー DO は規定上 2 バイトだが、仕様の例には 1 バイトのゼロ（エラー無し）もある。この形の正常応答を不正と誤判定しないことを確認する |

#### 2-36 DO96 の 1 バイト形式でエラー値は不正（衝突にもしない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:763](../test/PcscIfTest/SelfTest.cs#L763) |
| 対象 | `TransceiveResult.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 03 00 90 00` `96 01 02` `97 14 <card>` `90 00`（1 バイト形式に衝突ビット相当の値） |
| 期待結果 | `Unknown == true` かつ `Collision == false` |
| 意図 | 1 バイト形式で許されるのはゼロだけ。規定外の形の値を「衝突」と推測して解釈せず、応答自体を不正とすることを確認する |

#### 2-37 DO96 の上位 4 ビットが立っていたら衝突ビットがあっても不正

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:769](../test/PcscIfTest/SelfTest.cs#L769) |
| 対象 | `PcscResponse.Parse` の予約ビット検査 |
| 判定 | `Check` |
| 前提・入力 | `good` の `[10]`（DO96 の 1 バイト目）を `0x12`（予約ビット `0x10` + 衝突ビット `0x02`）に変更 |
| 期待結果 | `Unknown == true` かつ `Collision == false` |
| 意図 | DO96 の上位 4 ビットは予約で、立っていれば応答が壊れている。予約ビットの検査が無いと、この応答は衝突ビットによって `Collision` になる。<br>**上位ビットだけ（`0x10`）を立てるテストでは、予約ビットの検査を消しても `RfErrors != 0` で `Unknown` になり合格してしまう**ことを、ライブラリを意図的に壊して確認した。そのため衝突ビットと同時に立て、「衝突」ではなく「不正」になることで検査の存在を確かめている |

#### 2-38 DO96 の長さ 3 は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:771](../test/PcscIfTest/SelfTest.cs#L771) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `96 03 00 00 00 90 00` |
| 期待結果 | `Unknown == true` |
| 意図 | DO96 は 2 バイト（または 1 バイトのゼロ）だけ。それ以外の長さを不正にすることを確認する |

#### 2-39 C0 が 9000 なのに先頭バイトが 0 以外なら拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:774](../test/PcscIfTest/SelfTest.cs#L774) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `good` の `[2]`（C0 の値の 1 バイト目）を `01` に変更。C0 の SW は `9000` のまま |
| 期待結果 | `Unknown == true` |
| 意図 | 成功（`9000`）と言いながら 1 バイト目にエラー情報がある、矛盾した汎用ステータスを成功として扱わないことを確認する。なお「応答なし」（`02 64 01`）のように SW が `9000` 以外なら 1 バイト目は問わない |

#### 2-40 C0 の長さ 3 以外は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:775](../test/PcscIfTest/SelfTest.cs#L775) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `C0 02 90 00 90 00`（C0 の長さが 2） |
| 期待結果 | `Unknown == true` |
| 意図 | 汎用ステータスは 3 バイト固定。長さの違う C0 から SW を推測で読み取らないことを確認する |

#### 2-41 長さ 0 の DO97 は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:776](../test/PcscIfTest/SelfTest.cs#L776) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `97 00 90 00` |
| 期待結果 | `Unknown == true` |
| 意図 | 空の受信データ DO を「データを受信した」として数えないことを確認する。受理すると、フレーム数 1・中身 0 バイトの `Ok` という紛らわしい結果になる |

#### 2-42 DO92 の長さ 1 以外は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:777](../test/PcscIfTest/SelfTest.cs#L777) |
| 対象 | `PcscResponse.Parse` |
| 判定 | `Check` |
| 前提・入力 | `92 02 00 00 90 00` |
| 期待結果 | `Unknown == true` |
| 意図 | 端数ビット数の DO は 1 バイト固定。値が 0 でも、長さが違えば不正にすることを確認する（2-17 は値の範囲、こちらは長さの検査） |

#### 2-43 管理コマンドは受信データ付きの応答を拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:779](../test/PcscIfTest/SelfTest.cs#L779) |
| 対象 | `Rcs300Transport.ValidateManagementResponse(good, "test")` |
| 判定 | `Throws` |
| 前提・入力 | `good`（SW・C0 とも `9000` だが、DO97 にカードフレームが入っている） |
| 期待結果 | 例外が発生する |
| 意図 | セッション開始・RF ON/OFF などの管理コマンドの応答に受信データは付かない。付いていれば別コマンドの応答を取り違えている可能性があるので、成功とみなさないことを確認する |

#### 2-44 管理コマンドは RF エラー付きの応答を拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:781](../test/PcscIfTest/SelfTest.cs#L781) |
| 対象 | `Rcs300Transport.ValidateManagementResponse` |
| 判定 | `Throws` |
| 前提・入力 | `C0 03 00 90 00 96 02 02 00 90 00`（SW・C0 は成功、DO96 に衝突ビット） |
| 期待結果 | 例外が発生する |
| 意図 | SW と C0 が成功でも、RF エラーの報告がある管理コマンド応答は成功と言い切れない。`CommandSucceeded` の `RfErrors == 0` 条件を確認する |

#### 2-45 管理コマンドは端数ビット付きの応答を拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:785](../test/PcscIfTest/SelfTest.cs#L785) |
| 対象 | `Rcs300Transport.ValidateManagementResponse` |
| 判定 | `Throws` |
| 前提・入力 | `C0 03 00 90 00 92 01 01 90 00`（端数ビット数 1） |
| 期待結果 | 例外が発生する |
| 意図 | `CommandSucceeded` の `!PartialByte` 条件を確認する。2-26〜2-28・2-43〜2-45 で、成功と判定する条件（SW / C0 / RF エラー / 端数ビット / 受信データ / 形式）をすべて個別に崩している |

---

## 3. 対象カードの Polling 応答の抽出（8 件）

メソッド `PollingExtraction()`（[SelfTest.cs:200](../test/PcscIfTest/SelfTest.cs#L200)）。
対象は `Felica.TryExtractLiteSIdm` / `Felica.IdmToString`（[L2_FelicaCommands.cs](../src/L2_FelicaCommands.cs)）です。
`TryExtractLiteSIdm` は DO97 の中身を 20 バイト単位で区切り、**すべて**が対象カードの完全な Polling 応答のときだけ成功します。
1 か所でも崩れていれば再同期せず全体を失敗にし、IDm リストも空にします。

#### 3-01 正常な対象カードのフレームを解析

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:203](../test/PcscIfTest/SelfTest.cs#L203) |
| 対象 | `Felica.TryExtractLiteSIdm(byte[], out List<byte[]>)` |
| 判定 | `Check` |
| 前提・入力 | `Card(1)`（20 バイト） |
| 期待結果 | 戻り値 `true` かつ `ids.Count == 1` |
| 意図 | LEN=20、応答コード 01、PMm[1]=F1、システムコード 88B4 のフレームから IDm を 1 件取り出せることを確認する基準 |

#### 3-02 連結された 2 フレームを解析

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:205](../test/PcscIfTest/SelfTest.cs#L205) |
| 対象 | `Felica.TryExtractLiteSIdm` |
| 判定 | `Check` |
| 前提・入力 | `Concat(Card(1), Card(2))`（40 バイト） |
| 期待結果 | 戻り値 `true` かつ `ids.Count == 2` |
| 意図 | 1 つの DO97 に 2 枚分のフレームが連結されて届いた場合でも、先頭だけでなく 2 枚目の IDm も取り出すことを確認する。重ね置き検出の前提 |

#### 3-03 末尾のゴミは拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:207](../test/PcscIfTest/SelfTest.cs#L207) |
| 対象 | `Felica.TryExtractLiteSIdm` |
| 判定 | `Check` |
| 前提・入力 | `Card(1)` + `00`（21 バイト） |
| 期待結果 | 戻り値 `false` |
| 意図 | 正常なフレームの後ろに 1 バイトでも余りがあれば、応答の区切りが信用できない。先頭の 1 枚分だけ採用して成功にしないことを確認する |

#### 3-04 途中切れは拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:209](../test/PcscIfTest/SelfTest.cs#L209) |
| 対象 | `Felica.TryExtractLiteSIdm` |
| 判定 | `Check` |
| 前提・入力 | `Take(Card(1), 19)`（システムコードの 2 バイト目が欠落） |
| 期待結果 | 戻り値 `false` |
| 意図 | IDm 部分（`[2..9]`）は揃っていても、フレームが 20 バイトに満たなければ採用しないことを確認する |

#### 3-05 対象カード以外の IC 種別は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:212](../test/PcscIfTest/SelfTest.cs#L212) |
| 対象 | `Felica.TryExtractLiteSIdm` |
| 判定 | `Check` |
| 前提・入力 | `Card(1)` の `[11]`（PMm[1] = IC 種別）を `F0` に変更 |
| 期待結果 | 戻り値 `false` |
| 意図 | 同じシステムコードに応答する別種のチップを対象カードとして読み書きしないことを確認する（チップ種別の確認であり、認証ではない） |

#### 3-06 システムコード違いは拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:214](../test/PcscIfTest/SelfTest.cs#L214) |
| 対象 | `Felica.TryExtractLiteSIdm` |
| 判定 | `Check` |
| 前提・入力 | `Card(1)` の `[18..19]` を `12 FC` に変更 |
| 期待結果 | 戻り値 `false` |
| 意図 | Polling 応答末尾のシステムコードが `88B4` でなければ対象カードではない。IC 種別が正しくてもシステムコードで弾くことを確認する |

#### 3-07 IDm 文字列は 16 桁の大文字

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:217](../test/PcscIfTest/SelfTest.cs#L217) |
| 対象 | `Felica.IdmToString(byte[])` |
| 判定 | `Check` |
| 前提・入力 | `Idm(0xAB)` = `01 00 00 00 00 00 00 AB` |
| 期待結果 | `"01000000000000AB"` と一致 |
| 意図 | IDm の文字列表現は区切り無し・16 桁・大文字。`PcscResult.Idm` と、重ね置きチェックで IDm を区別する辞書のキーに使うため、表記揺れ（小文字・区切り文字）があると同一カードを別カードと数える恐れがある |

#### 3-08 8 バイト以外の IDm は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:219](../test/PcscIfTest/SelfTest.cs#L219) |
| 対象 | `Felica.IdmToString(new byte[7])` |
| 判定 | `Throws` |
| 前提・入力 | 7 バイトの配列 |
| 期待結果 | 例外が発生する（`ArgumentException`） |
| 意図 | 長さの違う IDm を黙って文字列化すると、別のカードと同じ文字列になる可能性がある。長さ誤りを例外で拒否することを確認する |

---

## 4. ユーザ領域の境界チェック（17 件）

メソッド `BlockBoundaries()`（[SelfTest.cs:227](../test/PcscIfTest/SelfTest.cs#L227)）。
対象は `FelicaLiteS.BuildRead` / `BuildWrite` / `BlockRange`（[L2_FelicaCommands.cs](../src/L2_FelicaCommands.cs)）です。
アクセス可能なのはユーザブロック **S_PAD0..S_PAD13（`0x00..0x0D`、14 ブロック）だけ**です。
システムブロック（MC `0x88` など）を誤って書くとカードが復旧不能になるため、組み立て段階で確実に拒否することを確認します。
特に記載がない限り `idm = Idm(1)` です。

#### 4-01 システムブロック MC(0x88) は読み出し不可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:230](../test/PcscIfTest/SelfTest.cs#L230) |
| 対象 | `FelicaLiteS.BuildRead(idm, new byte[] { 0x88 })` |
| 判定 | `Throws` |
| 前提・入力 | ブロック `0x88`（MC = メモリ構成ブロック） |
| 期待結果 | 例外が発生する（`ArgumentOutOfRangeException`） |
| 意図 | `BlockRange` を経由せずブロックリストを直接渡した場合でも、`ValidateBlocks` がシステムブロックを拒否することを確認する |

#### 4-02 システムブロック MC(0x88) は書き込み不可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:232](../test/PcscIfTest/SelfTest.cs#L232) |
| 対象 | `FelicaLiteS.BuildWrite(idm, new byte[] { 0x88 }, Payload(0, 16))` |
| 判定 | `Throws` |
| 前提・入力 | ブロック `0x88`、データ 16 バイト（長さは正しい） |
| 期待結果 | 例外が発生する |
| 意図 | 最も危険な操作（MC への書き込み）が、データ長が正しくても組み立てられないことを確認する |

#### 4-03 S_PAD13 を超えるブロックは拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:234](../test/PcscIfTest/SelfTest.cs#L234) |
| 対象 | `FelicaLiteS.BuildRead(idm, new byte[] { 0x0E })` |
| 判定 | `Throws` |
| 前提・入力 | ブロック `0x0E`（ユーザ領域の 1 つ外） |
| 期待結果 | 例外が発生する |
| 意図 | 上限の境界値（外側）。`>` と `>=` の取り違えを検出する |

#### 4-04 S_PAD13 は許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:236](../test/PcscIfTest/SelfTest.cs#L236) |
| 対象 | `FelicaLiteS.BuildRead(idm, new byte[] { 0x0D })` |
| 判定 | `Check` |
| 前提・入力 | ブロック `0x0D`（ユーザ領域の最後） |
| 期待結果 | 例外が発生せず、戻り値の長さが 16 |
| 意図 | 上限の境界値（内側）。1 ブロックの Read コマンドは 1+1+8+1+2+1+2 = 16 バイトになることも併せて確認する |

#### 4-05 ブロック重複は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:237](../test/PcscIfTest/SelfTest.cs#L237) |
| 対象 | `FelicaLiteS.BuildWrite(idm, new byte[] { 0, 0 }, Payload(0, 32))` |
| 判定 | `Throws` |
| 前提・入力 | ブロック `00` を 2 回指定、データ 32 バイト |
| 期待結果 | 例外が発生する（`ArgumentException`） |
| 意図 | 同じブロックを 2 回書くと、どちらのデータが残るかがカード依存になる。重複指定を拒否することを確認する |

#### 4-06 空のブロックリストは拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:239](../test/PcscIfTest/SelfTest.cs#L239) |
| 対象 | `FelicaLiteS.BuildRead(idm, new byte[0])` |
| 判定 | `Throws` |
| 前提・入力 | 長さ 0 のブロックリスト |
| 期待結果 | 例外が発生する |
| 意図 | ブロック数 0 のコマンドをカードへ送らないことを確認する |

#### 4-07 IDm 長の誤りは拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:241](../test/PcscIfTest/SelfTest.cs#L241) |
| 対象 | `FelicaLiteS.BuildRead(new byte[7], FelicaLiteS.BlockRange(0, 2))` |
| 判定 | `Throws` |
| 前提・入力 | 7 バイトの IDm |
| 期待結果 | 例外が発生する |
| 意図 | IDm が 8 バイトでないと、後続のフィールドがずれて別のコマンドとして解釈される恐れがある。`ValidateIdm` による拒否を確認する |

#### 4-08 書き込みデータ長はブロック数と一致必須

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:243](../test/PcscIfTest/SelfTest.cs#L243) |
| 対象 | `FelicaLiteS.BuildWrite(idm, FelicaLiteS.BlockRange(0, 2), Payload(0, 16))` |
| 判定 | `Throws` |
| 前提・入力 | ブロック 2 個、データ 16 バイト（本来は 32 バイト必要） |
| 期待結果 | 例外が発生する |
| 意図 | データ不足のまま組み立てると、2 ブロック目に何が書かれるか不定になる。「ブロック数 × 16 バイトちょうど」を要求することを確認する |

#### 4-09 BlockRange はユーザ領域を超えない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:245](../test/PcscIfTest/SelfTest.cs#L245) |
| 対象 | `FelicaLiteS.BlockRange(0, FelicaLiteS.UserBlockCount + 1)` |
| 判定 | `Throws` |
| 前提・入力 | 開始 0、ブロック数 15 |
| 期待結果 | 例外が発生する |
| 意図 | 範囲生成の段階で、ユーザ領域をはみ出す（`0x0E` を含む）範囲を作らないことを確認する |

#### 4-10 BlockRange は全 14 ブロックを許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:247](../test/PcscIfTest/SelfTest.cs#L247) |
| 対象 | `FelicaLiteS.BlockRange(0, FelicaLiteS.UserBlockCount)` |
| 判定 | `Check` |
| 前提・入力 | 開始 0、ブロック数 14 |
| 期待結果 | 戻り値の長さが 14 |
| 意図 | 4-09 の内側の境界値。ユーザ領域全体（`00..0D`）を指定できることを確認する。Write の 14 ブロック書き込み（8-26）の前提 |

#### 4-11 Read は 4 ブロックまで許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:250](../test/PcscIfTest/SelfTest.cs#L250) |
| 対象 | `FelicaLiteS.BuildRead(idm, FelicaLiteS.BlockRange(0, FelicaLiteS.MaxBlocksPerRead))` |
| 判定 | `Check` |
| 前提・入力 | ブロック `00..03`（4 ブロック） |
| 期待結果 | 例外が発生せず、戻り値の長さが 22（14 + 4 × 2） |
| 意図 | 実機で確認した Read の上限（1 コマンド 4 ブロック）の内側の境界値 |

#### 4-12 Read の 5 ブロック以上は組み立て時点で拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:252](../test/PcscIfTest/SelfTest.cs#L252) |
| 対象 | `FelicaLiteS.BuildRead(idm, FelicaLiteS.BlockRange(0, FelicaLiteS.MaxBlocksPerRead + 1))` |
| 判定 | `Throws` |
| 前提・入力 | ブロック `00..04`（5 ブロック。`BlockRange` 自体は有効） |
| 期待結果 | 例外が発生する（`ArgumentOutOfRangeException`） |
| 意図 | 実機の対象カードは 5 ブロック以上の Read を `SF1=FF SF2=A2` で拒否する。確実に失敗する要求をカードまで送らないことを確認する |

#### 4-13 blockCount の桁あふれは引数エラー（巨大確保に到達しない・開始 2）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:260](../test/PcscIfTest/SelfTest.cs#L260) |
| 対象 | `FelicaLiteS.BlockRange(2, int.MaxValue)` |
| 判定 | **`ThrowsArgument`** |
| 前提・入力 | 開始 2、ブロック数 `int.MaxValue` |
| 期待結果 | `ArgumentException`（またはその派生）が発生する |
| 意図 | 範囲検査を `startBlock + blockCount > 14` のような加算で書くと、`2 + int.MaxValue` が負数に桁あふれして検査をすり抜け、直後の `new byte[blockCount]` で約 2GB の確保に向かい `OutOfMemoryException` になる。`Throws` ではこれも合格してしまうため、**引数エラーとして弾かれたことまで**確認する。実装は `blockCount > UserBlockCount - startBlock` と減算で比較している |

#### 4-14 blockCount の桁あふれは引数エラー（開始 0）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:262](../test/PcscIfTest/SelfTest.cs#L262) |
| 対象 | `FelicaLiteS.BlockRange(0, int.MaxValue)` |
| 判定 | **`ThrowsArgument`** |
| 前提・入力 | 開始 0、ブロック数 `int.MaxValue` |
| 期待結果 | `ArgumentException`（またはその派生）が発生する |
| 意図 | 開始 0 では加算しても桁あふれしないため、4-13 の対照ケースとなる。巨大なブロック数そのものが、確保に到達する前に引数エラーになることを確認する |

#### 4-15 開始ブロックがユーザ領域外なら拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:264](../test/PcscIfTest/SelfTest.cs#L264) |
| 対象 | `FelicaLiteS.BlockRange(0x0E, 1)` |
| 判定 | `Throws` |
| 前提・入力 | 開始 `0x0E`、ブロック数 1 |
| 期待結果 | 例外が発生する |
| 意図 | 開始ブロック自体が範囲外の場合の検査。この検査が無いと `UserBlockCount - startBlock` が 0 以下になり、判定の前提（右辺が 1..14）が崩れる |

#### 4-16 境界: (13,1) は許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:266](../test/PcscIfTest/SelfTest.cs#L266) |
| 対象 | `FelicaLiteS.BlockRange(FelicaLiteS.LastUserBlock, 1)` |
| 判定 | `Check` |
| 前提・入力 | 開始 `0x0D`、ブロック数 1 |
| 期待結果 | 戻り値の長さが 1 |
| 意図 | 最後のユーザブロック 1 個だけの範囲が作れること（内側の境界値）を確認する |

#### 4-17 境界: (13,2) は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:268](../test/PcscIfTest/SelfTest.cs#L268) |
| 対象 | `FelicaLiteS.BlockRange(FelicaLiteS.LastUserBlock, 2)` |
| 判定 | `Throws` |
| 前提・入力 | 開始 `0x0D`、ブロック数 2（`0x0E` にはみ出す） |
| 期待結果 | 例外が発生する |
| 意図 | 開始位置は範囲内でも、終端がはみ出す場合の外側の境界値。4-16 と対で off-by-one を検出する |

---

## 5. 読み書き応答の解析（22 件）

メソッド `AccessResponseParsing()`（[SelfTest.cs:276](../test/PcscIfTest/SelfTest.cs#L276)）。
対象は `FelicaLiteS.ParseReadResponse` / `TryParseReadResponse` / `ParseWriteResponse` / `TryParseWriteResponse`（[L2_FelicaCommands.cs](../src/L2_FelicaCommands.cs)）です。

解析結果は `FelicaResponseKind` の 3 種類です。

| 値 | 意味 | SF1 / SF2 の出力 | `Pcsc` での扱い |
|---|---|---|---|
| `Ok` | 応答は妥当で、カードもエラーを返していない | `00` / `00` | `Ok` |
| `CardError` | 応答は妥当。カードがステータスフラグでエラーを返した | カードの実値 | `CardRejected` |
| `Malformed` | 途中切れ・別 IDm・応答コード違い・長さ不整合など、応答自体が信頼できない | `FF` / `FF`（未到達） | `Indeterminate` |

5-01〜5-11 は真偽値版（`TryParse...`）、5-12〜5-22 は区分版（`Parse...`）の確認です。
特に記載がない限り、`idm = Idm(1)`、`payload = Payload(0x10, 32)`（`10 11 ... 2F`、2 ブロック分）です。
`ReadFrame(idm, payload)` は `2D 07 <IDm> 00 00 02 <payload>`（45 バイト）になります。

#### 5-01 正常な読み出し応答を解析

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:283](../test/PcscIfTest/SelfTest.cs#L283) |
| 対象 | `FelicaLiteS.TryParseReadResponse(response, expectedIdm, expectedBlocks, out data, out sf1, out sf2)` |
| 判定 | `Check` |
| 前提・入力 | `ReadFrame(idm, payload)`、期待 IDm = `idm`、期待ブロック数 = 2 |
| 期待結果 | 戻り値 `true`、`data == payload`、`sf1 == 0`、`sf2 == 0` |
| 意図 | 正常な Read 応答からデータ部 32 バイトを過不足なく取り出すことを確認する基準 |

#### 5-02 別 IDm の読み出し応答は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:286](../test/PcscIfTest/SelfTest.cs#L286) |
| 対象 | `FelicaLiteS.TryParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `ReadFrame(Idm(2), payload)`、期待 IDm = `Idm(1)` |
| 期待結果 | 戻り値 `false` |
| 意図 | 重ね置きチェックで確定したカードとは別のカードの応答を、そのカードのデータとして返さないことを確認する |

#### 5-03 読み出しのステータスフラグ異常は拒否し、値は返す

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:288](../test/PcscIfTest/SelfTest.cs#L288) |
| 対象 | `FelicaLiteS.TryParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x07, idm, 0xFF, 0xA1)`（12 バイト、データ部無し） |
| 期待結果 | 戻り値 `false`、かつ `sf1 == 0xFF`、`sf2 == 0xA1` |
| 意図 | カードがエラーを返した場合、失敗にしたうえで**原因のステータスフラグを呼び出し側に渡す**ことを確認する。SF1=`FF` は未到達の既定値と同じだが、SF2=`A1` で実値が入ったことを判別できる |

#### 5-04 ブロック数不一致は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:291](../test/PcscIfTest/SelfTest.cs#L291) |
| 対象 | `FelicaLiteS.TryParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `ReadFrame(idm, Payload(0x10, 16))`（1 ブロック分の応答）、期待ブロック数 = 2 |
| 期待結果 | 戻り値 `false` |
| 意図 | 要求したブロック数と応答のブロック数が違う場合、短いデータを成功として返さないことを確認する |

#### 5-05 応答コード違いは拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:293](../test/PcscIfTest/SelfTest.cs#L293) |
| 対象 | `FelicaLiteS.TryParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `ReadFrame(idm, payload)` の `[1]` を `0x09`（Write 応答コード）に変更 |
| 期待結果 | 戻り値 `false` |
| 意図 | Read 応答（`07`）以外のフレームを Read の結果として解釈しないことを確認する |

#### 5-06 宣言長の不一致は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:295](../test/PcscIfTest/SelfTest.cs#L295) |
| 対象 | `FelicaLiteS.TryParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `ReadFrame(idm, payload)` の `[0]`（LEN）を 44 に変更（実際は 45 バイト） |
| 期待結果 | 戻り値 `false` |
| 意図 | フレーム先頭の LEN と実際の長さが 1 バイトでも食い違えば、フレームの区切りが信用できない。成功にしないことを確認する |

#### 5-07 読み出し応答のあらゆる途中切れを拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:297](../test/PcscIfTest/SelfTest.cs#L297) |
| 対象 | `FelicaLiteS.TryParseReadResponse` |
| 判定 | `Check`（45 通りをまとめて 1 件） |
| 前提・入力 | `n = 0, 1, ..., 44` のそれぞれで `Take(ReadFrame(idm, payload), n)`、期待ブロック数 = 2 |
| 期待結果 | すべての `n` で戻り値 `false` |
| 意図 | 0 バイトから全長 − 1 バイトまで 1 バイトずつ切り詰め、どの長さでも成功と誤認しないことを網羅的に確認する。12 バイト未満は長さ不足、12 バイト以上は LEN 不一致で弾かれる |

#### 5-08 正常な書き込み応答を解析

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:307](../test/PcscIfTest/SelfTest.cs#L307) |
| 対象 | `FelicaLiteS.TryParseWriteResponse(response, expectedIdm, out sf1, out sf2)` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x09, idm, 0, 0)` = `0C 09 <IDm> 00 00` |
| 期待結果 | 戻り値 `true`、`sf1 == 0`、`sf2 == 0` |
| 意図 | 正常な Write 応答を成功と判定する基準 |

#### 5-09 書き込みのステータスフラグ異常は拒否し、値は返す

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:310](../test/PcscIfTest/SelfTest.cs#L310) |
| 対象 | `FelicaLiteS.TryParseWriteResponse` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x09, idm, 0xFF, 0xA2)` |
| 期待結果 | 戻り値 `false`、かつ `sf1 == 0xFF`、`sf2 == 0xA2` |
| 意図 | 「応答が来たこと」を成功の根拠にせず、SF1/SF2 がともに 0 のときだけ成功にすることと、エラー時に実値を返すことを確認する |

#### 5-10 別 IDm の書き込み応答は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:313](../test/PcscIfTest/SelfTest.cs#L313) |
| 対象 | `FelicaLiteS.TryParseWriteResponse` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x09, Idm(2), 0, 0)`、期待 IDm = `Idm(1)` |
| 期待結果 | 戻り値 `false` |
| 意図 | 別のカードからの成功応答を、対象カードへの書き込み成功とみなさないことを確認する |

#### 5-11 読み出し応答コードは書き込み応答として拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:315](../test/PcscIfTest/SelfTest.cs#L315) |
| 対象 | `FelicaLiteS.TryParseWriteResponse` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x07, idm, 0, 0)`（Read 応答コード、SF は正常） |
| 期待結果 | 戻り値 `false` |
| 意図 | Write 応答（`09`）以外は、SF が 0 でも書き込み成功にしないことを確認する |

#### 5-12 Read 正常 -> Ok

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:320](../test/PcscIfTest/SelfTest.cs#L320) |
| 対象 | `FelicaLiteS.ParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `ReadFrame(idm, payload)`、期待ブロック数 = 2 |
| 期待結果 | 戻り値 `FelicaResponseKind.Ok`、`sf1 == 0`、`sf2 == 0` |
| 意図 | 区分版でも正常応答が `Ok` になることを確認する基準 |

#### 5-13 Read の SF 非ゼロ -> CardError（SF は実値）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:323](../test/PcscIfTest/SelfTest.cs#L323) |
| 対象 | `FelicaLiteS.ParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x07, idm, 0xFF, 0xA1)` |
| 期待結果 | 戻り値 `CardError`、`sf1 == 0xFF`、`sf2 == 0xA1` |
| 意図 | 形式（長さ・LEN・応答コード・IDm）が正しく SF が非ゼロの応答は「カードが拒否した」＝カードの状態は確定している、と区別されることを確認する。エラー時の Read 応答にはブロック数・データ部が無いため、SF 判定をブロック数の検査より前に行う必要がある |

#### 5-14 Read の別 IDm -> Malformed（SF は返さない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:326](../test/PcscIfTest/SelfTest.cs#L326) |
| 対象 | `FelicaLiteS.ParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `ReadFrame(Idm(2), payload)`、期待 IDm = `Idm(1)` |
| 期待結果 | 戻り値 `Malformed`、`sf1 == 0xFF`、`sf2 == 0xFF` |
| 意図 | 別カードの応答は対象カードの応答として信用できない。**別カードの SF を対象カードの SF として報告しない**ことまで確認する |

#### 5-15 Read の応答コード違い -> Malformed

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:329](../test/PcscIfTest/SelfTest.cs#L329) |
| 対象 | `FelicaLiteS.ParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `ReadFrame(idm, payload)` の `[1]` を `0x09` に変更 |
| 期待結果 | 戻り値 `Malformed` |
| 意図 | 応答コード違いは `CardError` ではなく `Malformed` に分類されることを確認する |

#### 5-16 Read の SF は正常だがブロック数不一致 -> Malformed

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:332](../test/PcscIfTest/SelfTest.cs#L332) |
| 対象 | `FelicaLiteS.ParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `ReadFrame(idm, Payload(0x10, 16))`（SF=`00 00`、ブロック数 1）、期待ブロック数 = 2 |
| 期待結果 | 戻り値 `Malformed`、`sf1 == 0xFF`、`sf2 == 0xFF` |
| 意図 | 電文上の SF は `00 00` でも、データ部が整合しない応答は信頼できない。途中まで解析した SF（`00 00`）を出力に残すと「カードは正常に応答した」と誤読されるため、`Malformed` のときは SF を `FF FF` のままにすることを確認する |

#### 5-17 Read の途中切れ -> Malformed

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:335](../test/PcscIfTest/SelfTest.cs#L335) |
| 対象 | `FelicaLiteS.ParseReadResponse` |
| 判定 | `Check` |
| 前提・入力 | `Take(ReadFrame(idm, payload), 11)`（SF2 の手前で切れている） |
| 期待結果 | 戻り値 `Malformed` |
| 意図 | ステータスフラグまで届いていない応答を `CardError` と誤分類しないことを確認する |

#### 5-18 Write 正常 -> Ok

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:339](../test/PcscIfTest/SelfTest.cs#L339) |
| 対象 | `FelicaLiteS.ParseWriteResponse` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x09, idm, 0, 0)` |
| 期待結果 | 戻り値 `Ok` |
| 意図 | 区分版の Write 正常応答の基準 |

#### 5-19 Write の SF 非ゼロ -> CardError（SF は実値）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:342](../test/PcscIfTest/SelfTest.cs#L342) |
| 対象 | `FelicaLiteS.ParseWriteResponse` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x09, idm, 0xFF, 0xA2)` |
| 期待結果 | 戻り値 `CardError`、`sf1 == 0xFF`、`sf2 == 0xA2` |
| 意図 | カードが書き込みを拒否した（＝そのブロックは書き換わっていない）ことを、実値の SF 付きで区別できることを確認する |

#### 5-20 Write の長さ不足 -> Malformed（SF は返さない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:345](../test/PcscIfTest/SelfTest.cs#L345) |
| 対象 | `FelicaLiteS.ParseWriteResponse` |
| 判定 | `Check` |
| 前提・入力 | `Take(StatusFrame(0x09, idm, 0, 0), 11)`（SF2 が欠落） |
| 期待結果 | 戻り値 `Malformed`、`sf1 == 0xFF`、`sf2 == 0xFF` |
| 意図 | Write 応答は 12 バイトちょうどでなければならない。欠けた応答は「書き込まれたか不明」であり、`CardError`（書き込まれていない）とも `Ok` とも言えないことを確認する |

#### 5-21 Write の応答コード違い -> Malformed

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:348](../test/PcscIfTest/SelfTest.cs#L348) |
| 対象 | `FelicaLiteS.ParseWriteResponse` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x07, idm, 0, 0)` |
| 期待結果 | 戻り値 `Malformed` |
| 意図 | 応答コード違いが `Malformed` に分類されることを確認する（5-11 の区分版） |

#### 5-22 Write の別 IDm -> Malformed

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:351](../test/PcscIfTest/SelfTest.cs#L351) |
| 対象 | `FelicaLiteS.ParseWriteResponse` |
| 判定 | `Check` |
| 前提・入力 | `StatusFrame(0x09, Idm(2), 0, 0)` |
| 期待結果 | 戻り値 `Malformed` |
| 意図 | 別 IDm の応答が `Malformed` に分類されることを確認する（5-10 の区分版） |

---

## 6. Connect / Disconnect（23 件）

メソッド `ConnectLifecycle()`（[SelfTest.cs:360](../test/PcscIfTest/SelfTest.cs#L360)）。
対象は `Pcsc.Connect` / `Disconnect` / `IsConnected` / `ReaderName` と、未接続時の `Read` / `Write`（[L1_Pcsc.cs](../src/L1_Pcsc.cs)）です。

6-01〜6-12 は**同じ `Pcsc` インスタンスの状態遷移を順番に**確認します（前のテストの結果が次のテストの前提になります）。

```
fake = new FakeTransport(1)
pcsc = New(fake)                        // 未接続
  6-01..6-03  未接続の振る舞い
  pcsc.Connect("fake reader")           // 6-04
  6-05..6-08  接続中の振る舞い
  pcsc.Disconnect()                     // 6-09
  6-10..6-12  切断後の振る舞い
```

#### 6-01 未接続では Read が NotConnected

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:365](../test/PcscIfTest/SelfTest.cs#L365) |
| 対象 | `Pcsc.Read()` |
| 判定 | `Check` |
| 前提・入力 | `Connect` 前の `pcsc` |
| 期待結果 | `Status == PcscStatus.NotConnected` |
| 意図 | 接続していない状態で読み出しを要求したとき、例外を投げず `NotConnected` を返すことを確認する（API は例外を投げない設計） |

#### 6-02 未接続では Write が NotConnected

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:366](../test/PcscIfTest/SelfTest.cs#L366) |
| 対象 | `Pcsc.Write(byte[])` |
| 判定 | `Check` |
| 前提・入力 | `Connect` 前の `pcsc`、データ `Payload(0x20, 32)`（引数としては有効） |
| 期待結果 | `Status == PcscStatus.NotConnected` |
| 意図 | 引数検証を通過する正しいデータでも、未接続なら `NotConnected` になることを確認する |

#### 6-03 未接続の IsConnected は false

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:368](../test/PcscIfTest/SelfTest.cs#L368) |
| 対象 | `Pcsc.IsConnected` |
| 判定 | `Check` |
| 前提・入力 | `Connect` 前の `pcsc` |
| 期待結果 | `IsConnected == false` |
| 意図 | 生成直後の初期状態を確認する |

#### 6-04 Connect が成功する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:370](../test/PcscIfTest/SelfTest.cs#L370) |
| 対象 | `Pcsc.Connect(string)` |
| 判定 | `Check` |
| 前提・入力 | リーダ名 `"fake reader"` |
| 期待結果 | `Ok == true` |
| 意図 | トランスポートファクトリで `FakeTransport` を取得し、`Open` が例外を投げなければ接続成功になることを確認する。リーダ名を明示しているので `ListReaders`（winscard）は呼ばれない |

#### 6-05 Connect 後の IsConnected は true

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:371](../test/PcscIfTest/SelfTest.cs#L371) |
| 対象 | `Pcsc.IsConnected` |
| 判定 | `Check` |
| 前提・入力 | 6-04 の後 |
| 期待結果 | `IsConnected == true` |
| 意図 | 接続成功後に状態が切り替わることを確認する |

#### 6-06 ReaderName を保持する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:372](../test/PcscIfTest/SelfTest.cs#L372) |
| 対象 | `Pcsc.ReaderName` |
| 判定 | `Check` |
| 前提・入力 | 6-04 の後 |
| 期待結果 | `ReaderName == "fake reader"` |
| 意図 | 接続したリーダ名をそのまま保持していることを確認する |

#### 6-07 Connect 時点では RF を起動しない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:373](../test/PcscIfTest/SelfTest.cs#L373) |
| 対象 | `Pcsc.Connect` の副作用 |
| 判定 | `Check` |
| 前提・入力 | 6-04 の後 |
| 期待結果 | `fake.OnCalls == 0` かつ `fake.StartCalls == 0` |
| 意図 | Connect はリーダハンドルの取得だけで、RF 起動と Transparent Session 開始は Read / Write のたびに行う設計である。Connect で RF を出しっぱなしにしないことを確認する |

#### 6-08 二重 Connect は AlreadyConnected

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:375](../test/PcscIfTest/SelfTest.cs#L375) |
| 対象 | `Pcsc.Connect(string)` |
| 判定 | `Check` |
| 前提・入力 | 接続中にもう一度 `Connect("fake reader")` |
| 期待結果 | `Status == PcscStatus.AlreadyConnected` |
| 意図 | 接続中に再接続すると、既存のトランスポートが解放されずに残る（ハンドルリーク）。上書きせず `AlreadyConnected` を返すことを確認する |

#### 6-09 Disconnect が成功する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:378](../test/PcscIfTest/SelfTest.cs#L378) |
| 対象 | `Pcsc.Disconnect()` |
| 判定 | `Check` |
| 前提・入力 | 接続中 |
| 期待結果 | `Ok == true` |
| 意図 | 接続中の切断が成功することを確認する |

#### 6-10 Disconnect でトランスポートを破棄する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:379](../test/PcscIfTest/SelfTest.cs#L379) |
| 対象 | `Pcsc.Disconnect()` の副作用 |
| 判定 | `Check` |
| 前提・入力 | 6-09 の後 |
| 期待結果 | `fake.Disposed == true` |
| 意図 | 切断時に `IPcscTransport.Dispose()` を呼び、実機ではリーダハンドル（`SCardDisconnect` / `SCardReleaseContext`）を解放することを確認する |

#### 6-11 Disconnect は冪等

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:380](../test/PcscIfTest/SelfTest.cs#L380) |
| 対象 | `Pcsc.Disconnect()` |
| 判定 | `Check` |
| 前提・入力 | 切断済みの状態でもう一度 `Disconnect()` |
| 期待結果 | `Ok == true` |
| 意図 | 未接続での切断を失敗にしないことを確認する。`using` / `Dispose` と明示的な `Disconnect` の併用や、後始末処理での重複呼び出しを安全にするため |

#### 6-12 Disconnect 後は NotConnected

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:381](../test/PcscIfTest/SelfTest.cs#L381) |
| 対象 | `Pcsc.Read()` |
| 判定 | `Check` |
| 前提・入力 | 切断後 |
| 期待結果 | `Status == PcscStatus.NotConnected` |
| 意図 | 破棄済みのトランスポートを使い続けないこと（切断で内部参照が確実にクリアされていること）を確認する |

#### 6-13 Open 失敗は ReaderError

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:383](../test/PcscIfTest/SelfTest.cs#L383) |
| 対象 | `Pcsc.Connect(string)` |
| 判定 | `Check` |
| 前提・入力 | `broken = new FakeTransport(1) { FailOpen = true }`（`Open` で `PcscException("SCardConnect", 0x8010000C)` を投げる）、新しい `Pcsc` で `Connect("fake reader")` |
| 期待結果 | `Status == PcscStatus.ReaderError` |
| 意図 | winscard の接続失敗（例外）を捕捉し、例外として外に出さず `ReaderError` に変換することを確認する |

#### 6-14 Open 失敗時はトランスポートを破棄する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:386](../test/PcscIfTest/SelfTest.cs#L386) |
| 対象 | `Pcsc.Connect` の失敗時の後始末 |
| 判定 | `Check` |
| 前提・入力 | 6-13 と同じ |
| 期待結果 | `broken.Disposed == true` |
| 意図 | `Open` の途中で失敗した場合も、生成済みのトランスポートを `Dispose` してコンテキスト等を解放することを確認する（失敗のたびにリソースが漏れない） |

#### 6-15 Open 失敗の原因例外を保持する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:387](../test/PcscIfTest/SelfTest.cs#L387) |
| 対象 | `PcscResult.Error` |
| 判定 | `Check` |
| 前提・入力 | 6-13 と同じ |
| 期待結果 | `Error is PcscException` |
| 意図 | 例外は投げないが、原因は失わない。呼び出し側が `PcscException.ReturnCode` から winscard の戻り値を調べられることを確認する |

### 6 の追加分: 接続・切断の失敗経路と既定コンストラクタ

メソッド `ConnectFailurePaths()`（[SelfTest.cs:795](../test/PcscIfTest/SelfTest.cs#L795)）。
6-01〜6-15 とは別のインスタンスで、トランスポートの生成失敗・破棄失敗と、リーダに触れない既定コンストラクタを確認します。

#### 6-16 トランスポートを生成できなければ ReaderError

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:799](../test/PcscIfTest/SelfTest.cs#L799) |
| 対象 | `Pcsc.Connect(string)` |
| 判定 | `Check` |
| 前提・入力 | `new Pcsc(FakeOptions(), delegate { return (IPcscTransport)null; })`（null を返すファクトリ）で `Connect("fake reader")` |
| 期待結果 | `Status == PcscStatus.ReaderError` かつ `Error is InvalidOperationException` |
| 意図 | 差し替えたファクトリの不備を `NullReferenceException` として曖昧にせず、「トランスポートを生成できなかった」という原因の分かる例外を付けて `ReaderError` を返すことを確認する |

#### 6-17 トランスポート生成失敗後は未接続のまま

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:801](../test/PcscIfTest/SelfTest.cs#L801) |
| 対象 | `Pcsc.IsConnected` / `ReaderName` |
| 判定 | `Check` |
| 前提・入力 | 6-16 の後 |
| 期待結果 | `IsConnected == false` かつ `ReaderName == ""` |
| 意図 | 失敗した Connect が、中途半端に接続済みの状態（リーダ名だけ設定されている等）を残さないことを確認する |

#### 6-18 Dispose が例外を投げたら Disconnect は ReaderError

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:807](../test/PcscIfTest/SelfTest.cs#L807) |
| 対象 | `Pcsc.Disconnect()` |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { FailDispose = true }` で接続し、`Disconnect()` |
| 期待結果 | `Status == PcscStatus.ReaderError` かつ `Error is InvalidOperationException`（`Dispose` が投げた例外） |
| 意図 | 切断処理の失敗を例外として外に出さず、`ReaderError` と原因例外で返すことを確認する |

#### 6-19 Disconnect の失敗後もハンドルは手放し、未接続になる

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:809](../test/PcscIfTest/SelfTest.cs#L809) |
| 対象 | `Pcsc.Disconnect()` の状態遷移 |
| 判定 | `Check` |
| 前提・入力 | 6-18 の後 |
| 期待結果 | `Dispose` が呼ばれている（`Disposed == true`）、`IsConnected == false`、`ReaderName == ""` |
| 意図 | `Pcsc` は `Dispose` を呼ぶ**前に**内部参照を手放す設計である。これにより、壊れたトランスポートを以後の Read / Write で使い続けることがなく、次の Connect も `AlreadyConnected` にならない。参照を手放すのを `Dispose` 成功後にすると失敗する |

#### 6-20 Disconnect の失敗後に再接続できる

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:811](../test/PcscIfTest/SelfTest.cs#L811) |
| 対象 | `Pcsc.Connect(string)` |
| 判定 | `Check` |
| 前提・入力 | 6-19 の後に、同じ `Pcsc` で `Connect("fake reader")` |
| 期待結果 | `Ok == true` かつ `IsConnected == true` |
| 意図 | 切断に失敗しても、アプリを再起動せずに接続し直して復旧できることを確認する（README の「`ReaderError` を受け取ったら再試行」の前提） |

#### 6-21 new Pcsc() はリーダに触れずに生成でき、未接続

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:815](../test/PcscIfTest/SelfTest.cs#L815) |
| 対象 | `new Pcsc()`（既定オプション・実機トランスポート） |
| 判定 | `Check` |
| 前提・入力 | 引数なしで生成し、`Read()` を呼ぶ |
| 期待結果 | 例外が発生しない、`IsConnected == false`、`ReaderName == ""`、`Read().Status == NotConnected` |
| 意図 | 実機トランスポート `Rcs300Transport` は Connect のときに初めて生成される。生成時点では winscard を呼ばないので、リーダの無い環境でもインスタンスを作れることを確認する |

#### 6-22 new Pcsc(null) は既定オプションで生成できる

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:818](../test/PcscIfTest/SelfTest.cs#L818) |
| 対象 | `new Pcsc(PcscOptions)` |
| 判定 | `Check` |
| 前提・入力 | `new Pcsc((PcscOptions)null)` |
| 期待結果 | 例外が発生しない |
| 意図 | オプションに null を渡した場合、`NullReferenceException` にならず既定値で生成されることを確認する（既定値が実際に使われることは 9-18 で確認） |

#### 6-23 未接続での Dispose は例外を投げない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:820](../test/PcscIfTest/SelfTest.cs#L820) |
| 対象 | `Pcsc.Dispose()` |
| 判定 | `Check` |
| 前提・入力 | 6-21 の未接続インスタンスで `Dispose()` |
| 期待結果 | 例外が発生しない |
| 意図 | `using` ブロックで Connect に失敗した場合など、未接続のまま破棄されても安全であることを確認する（`Dispose` は冪等な `Disconnect` を呼ぶ） |

---

## 7. Read と重ね置きチェック（37 件）

メソッド `ReadTests()`（[SelfTest.cs:394](../test/PcscIfTest/SelfTest.cs#L394)）。
対象は `Pcsc.Read` の全経路（[L1_Pcsc.cs](../src/L1_Pcsc.cs)）と、その中で呼ばれる `DuplicateGuard.Collect`（[L1_DuplicateGuard.cs](../src/L1_DuplicateGuard.cs)）です。

`Read` / `Write` は 1 回の呼び出しの中で次の手順を実行します（`Pcsc.Execute`）。

```
StartTransparentSession
  → TurnOffRf → SwitchProtocol(Felica) → TurnOnRf → (RF 待ち)
  → DuplicateGuard.Collect  … Polling を DuplicateCheckRounds 回（テストでは 2 回）
  → Gate（判定）
       Count >= 2   → MultipleCards
       Collision    → Indeterminate
       Unknown      → Indeterminate
       Count == 0   → NoCard
       NoResponse   → Indeterminate（途中で途切れた）
       それ以外     → 読み書き本体（DoRead / DoWrite）
  → TurnOffRf → EndTransparentSession
（途中で例外が出たら TurnOffRf / EndTransparentSession を試みて Indeterminate）
```

特に記載がない限り、`FakeOptions()`（ラウンド 2、RF 待ち 0）で `Connected(fake)` した `Pcsc` を使います。

#### 7-01 引数なし Read は先頭 16 バイト（1 ブロック）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:401](../test/PcscIfTest/SelfTest.cs#L401) |
| 対象 | `Pcsc.Read()` |
| 判定 | `Check` |
| 前提・入力 | `one = new FakeTransport(1)`、メモリ先頭に `Payload(0x10, 32)`（`10..2F`）を格納 |
| 期待結果 | `Ok == true`、`Data.Length == 16`、`Data == 10 11 ... 1F` |
| 意図 | 引数なし Read が S_PAD0 の 16 バイトだけを返し、S_PAD1（`20..2F`）を含めないことを確認する |

#### 7-02 対象 IDm を返す

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:403](../test/PcscIfTest/SelfTest.cs#L403) |
| 対象 | `PcscResult.Idm` |
| 判定 | `Check` |
| 前提・入力 | 7-01 の結果 |
| 期待結果 | `Idm == "0100000000000001"` |
| 意図 | 重ね置きチェックで確定したカードの IDm が結果に入ることを確認する。呼び出し側がどのカードを読んだか記録するために使う |

#### 7-03 指定ラウンド分の Polling を実行

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:404](../test/PcscIfTest/SelfTest.cs#L404) |
| 対象 | `DuplicateGuard.Collect` のラウンド数 |
| 判定 | `Check` |
| 前提・入力 | 7-01 の実行後 |
| 期待結果 | `one.Polls == 2` |
| 意図 | 1 枚しか見えていなくても、途中で打ち切らず `DuplicateCheckRounds`（2）回すべて Polling することを確認する。対象リーダは 1 回の応答で 1 枚分しか返さないため、ラウンドを省略すると 2 枚目を観測する機会が失われる |

#### 7-04 RF とセッションの手順が正しい

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:405](../test/PcscIfTest/SelfTest.cs#L405) |
| 対象 | `Pcsc.Execute` の手順 |
| 判定 | `Check` |
| 前提・入力 | 7-01 の実行後 |
| 期待結果 | `StartCalls == 1`、`SwitchCalls == 1`、`OnCalls == 1`、`OffCalls == 2`、`EndCalls == 1` |
| 意図 | 1 回の Read で、セッション開始 1 回・RF OFF 2 回（切替前と終了時）・プロトコル切替 1 回・RF ON 1 回・セッション終了 1 回が行われることを確認する。終了時の RF OFF やセッション終了の呼び忘れ（リーダが RF を出したまま残る）を検出する |

#### 7-05 Read コマンドは 1 回だけ

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:408](../test/PcscIfTest/SelfTest.cs#L408) |
| 対象 | `Pcsc.DoRead` |
| 判定 | `Check` |
| 前提・入力 | 7-01 の実行後 |
| 期待結果 | `one.Reads == 1` |
| 意図 | 1 ブロックの読み出しで Read コマンドを重複して送らないことを確認する |

#### 7-06 任意ブロック範囲を読み出す

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:410](../test/PcscIfTest/SelfTest.cs#L410) |
| 対象 | `Pcsc.Read(byte startBlock, int blockCount)` |
| 判定 | `Check` |
| 前提・入力 | `range = new FakeTransport(1)`、メモリ全体に `Payload(0, 224)`（オフセット n の値 = n）、`range.Wrap().Read(3, 4)` |
| 期待結果 | `Ok == true`、`Data.Length == 64`、`Data[0] == 48` |
| 意図 | ブロック 3 の先頭（オフセット 3 × 16 = 48）から 4 ブロック分を読むことを確認する。`Data[0]` がオフセット値そのものなので、開始位置のずれを検出できる |

#### 7-07 2 枚重なっているときはエラー（MultipleCards）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:417](../test/PcscIfTest/SelfTest.cs#L417) |
| 対象 | `Pcsc.Read()` → `Gate` |
| 判定 | `Check` |
| 前提・入力 | `two = new FakeTransport(1) { SecondCard = 2 }`（Polling 応答に `Card(1)` と `Card(2)` の DO97 が並ぶ） |
| 期待結果 | `Status == PcscStatus.MultipleCards` |
| 意図 | 本ライブラリの主目的である重ね置き検出の基本ケース。異なる IDm を 2 種類観測したら読み出しを中断することを確認する |

#### 7-08 重ね置き検出時は Read コマンドを送出しない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:421](../test/PcscIfTest/SelfTest.cs#L421) |
| 対象 | `Pcsc.Execute` |
| 判定 | `Check` |
| 前提・入力 | 7-07 の実行後 |
| 期待結果 | `two.Reads == 0` |
| 意図 | ステータスが `MultipleCards` でも、実はコマンドを送っていた、という実装を検出する。**カードへ 1 バイトも送っていないこと**をカウンタで確認する |

#### 7-09 重ね置きの検出枚数をメッセージに含む

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:422](../test/PcscIfTest/SelfTest.cs#L422) |
| 対象 | `PcscResult.Message` |
| 判定 | `Check` |
| 前提・入力 | 7-07 の結果 |
| 期待結果 | `Message` に `"2 種類"` を含む |
| 意図 | 運用時のログから「何枚分の IDm を観測したか」が分かることを確認する |

#### 7-10 ラウンドをまたいで別 IDm を観測したらエラー

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:424](../test/PcscIfTest/SelfTest.cs#L424) |
| 対象 | `DuplicateGuard.Collect` の IDm 和集合 |
| 判定 | `Check` |
| 前提・入力 | `swapped.Script(Ok(1), Ok(2))`（1 ラウンド目は ID 1 だけ、2 ラウンド目は ID 2 だけが応答） |
| 期待結果 | `Status == PcscStatus.MultipleCards` |
| 意図 | 実機のリーダは 1 回の応答で 1 枚分しか返さない。**ラウンドごとの結果ではなく、全ラウンドの IDm の和集合**で判定していることを確認する。ラウンドごとに集合をリセットする実装や、最後のラウンドだけを見る実装を検出する |

#### 7-11 1 応答に 2 枚分含まれてもエラー

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:429](../test/PcscIfTest/SelfTest.cs#L429) |
| 対象 | `DuplicateGuard.Collect` |
| 判定 | `Check` |
| 前提・入力 | `interleaved.Script(Parse(Response(Card(1), Card(2))))`（全ラウンドで 2 枚分の DO97） |
| 期待結果 | `Status == PcscStatus.MultipleCards` |
| 意図 | 1 回の応答内に複数の DO97 がある場合、先頭の DO97 だけでなくすべてから IDm を集めることを確認する。7-07 と同じ応答を `Script` で明示的に与え、`SecondCard` の模擬実装に依存しない形で固定している |

#### 7-12 無応答は NoCard で、コマンドを送出しない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:435](../test/PcscIfTest/SelfTest.cs#L435) |
| 対象 | `Gate` の `Count == 0` 分岐 |
| 判定 | `Check` |
| 前提・入力 | `none = new FakeTransport(1) { NoCard = true }`（全ラウンドで `C0 6401`） |
| 期待結果 | `Status == PcscStatus.NoCard` かつ `none.Reads == 0` |
| 意図 | 全ラウンドで明示的な「応答なし」だけを観測した場合は `NoCard` にし、IDm が無いのでコマンドも送らないことを確認する。`NoResponse` フラグは立っているが、`Count == 0` の判定が `NoResponse` の判定より先に来ることもこのケースで確認される |

#### 7-13 RF 衝突は Indeterminate（枚数を主張しない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:440](../test/PcscIfTest/SelfTest.cs#L440) |
| 対象 | `Gate` の `Collision` 分岐 |
| 判定 | `Check` |
| 前提・入力 | `collision.Script(Parse(Collision()))`（DO96 の衝突ビット付き応答） |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | 衝突は「複数枚の可能性」を示すが、何枚かは確定しない。`MultipleCards`（2 枚以上と断定）にも `NoCard`（0 枚）にもせず、`Indeterminate` で中断することを確認する |

#### 7-14 不正応答は 0 枚ではなく Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:445](../test/PcscIfTest/SelfTest.cs#L445) |
| 対象 | `DuplicateGuard.Collect` → `Gate` の `Unknown` 分岐 |
| 判定 | `Check` |
| 前提・入力 | `TransceiveResult { Ok = true, Data = 01 02 03 }`（`Frames` は空）を `Script` で返す |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | トランスポートは成功と言っているが、中身が対象カードのフレームとして解析できない。IDm が取れなかったからといって `NoCard` にせず、異常として扱うことを確認する。`Frames` が空のときは `Data` を解析する経路も併せて通る |

#### 7-15 矛盾したフラグは fail-closed

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:453](../test/PcscIfTest/SelfTest.cs#L453) |
| 対象 | `DuplicateGuard.Collect` の `FlagCount != 1` 検査 |
| 判定 | `Check` |
| 前提・入力 | `TransceiveResult { Ok = true, NoResponse = true, Data = Card(1) }`（フラグが 2 つ立つ） |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | `TransceiveResult.Parse` を通らない代替トランスポート（将来の別機種実装など）が矛盾した結果を返しても、有効な IDm が含まれているからと採用せず `Unknown` として扱うことを確認する |

#### 7-16 検査中に応答が途切れたら Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:460](../test/PcscIfTest/SelfTest.cs#L460) |
| 対象 | `Gate` の `NoResponse` 分岐 |
| 判定 | `Check` |
| 前提・入力 | `vanished.Script(Ok(1), Empty())`（1 ラウンド目は ID 1、2 ラウンド目は応答なし） |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | IDm は 1 種類だが途中で応答が途切れた。カードが動かされている・結合が不安定などで観測が不完全な可能性があるため、1 枚と確定させず中断することを確認する |

#### 7-17 途切れた DO を次の DO で補修しない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:465](../test/PcscIfTest/SelfTest.cs#L465) |
| 対象 | `DuplicateGuard.Collect` の DO 境界の扱い |
| 判定 | `Check` |
| 前提・入力 | `Card(1)` を前半 10 バイト・後半 10 バイトに分け、それぞれ別の DO97 にした応答 `Response(Take(Card(1), 10), Skip(Card(1), 10))` を `Script` で返す |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | 2 つの DO97 を連結すれば正しい 20 バイトのフレームになるが、DO 境界で切れたフレームはそれぞれ不完全である。`Frames` を DO ごとに解析し、平坦化した `Data` でつなぎ合わせて「補修」しないことを確認する |

#### 7-18 セッション終了の失敗は成功した読み出しも無効にする

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:470](../test/PcscIfTest/SelfTest.cs#L470) |
| 対象 | `Pcsc.Execute` の後始末 |
| 判定 | `Check` |
| 前提・入力 | `failEnd = new FakeTransport(1) { FailEnd = true }`、メモリに `Payload(0x10, 32)`（Read 自体は成功する） |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | 読み出しは成功しても、セッション終了に失敗するとリーダの状態が不明になる。成功結果を返してしまうと、次の操作が想定外の状態から始まる。後始末の失敗を `Indeterminate` に落とすことを確認する |

#### 7-19 プロトコル切替の失敗で検査を中止

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:475](../test/PcscIfTest/SelfTest.cs#L475) |
| 対象 | `Pcsc.Execute` の例外処理 |
| 判定 | `Check` |
| 前提・入力 | `failSwitch = new FakeTransport(1) { FailSwitch = true }` |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | フレーミング切替が失敗した状態で Polling を続けても、結果は信用できない。例外を捕捉して `Indeterminate` で中断し、例外を呼び出し側へ漏らさないことを確認する |

#### 7-20 カードの読み出し拒否は CardRejected

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:479](../test/PcscIfTest/SelfTest.cs#L479) |
| 対象 | `Pcsc.DoRead` の `CardError` 分岐 |
| 判定 | `Check` |
| 前提・入力 | `rejectRead = new FakeTransport(1) { RejectRead = true }`（Read に `SF1=FF SF2=A1`） |
| 期待結果 | `Status == PcscStatus.CardRejected`、`StatusFlag1 == 0xFF`、`StatusFlag2 == 0xA1` |
| 意図 | カードが明示的に拒否した場合は `CardRejected` とし、原因の SF を結果に載せることを確認する |

#### 7-21 別 IDm の読み出し応答は Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:487](../test/PcscIfTest/SelfTest.cs#L487) |
| 対象 | `Pcsc.DoRead` の `Malformed` 分岐 |
| 判定 | `Check` |
| 前提・入力 | `otherIdm = new FakeTransport(1) { ReadResponseIdm = 2 }`（Polling は ID 1、Read 応答だけ ID 2） |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | 応答が信頼できない場合は、カードが拒否したのか（`CardRejected`）も確定しない。`CardRejected` と `Indeterminate` を混同しないことを確認する |

#### 7-22 不正応答では SF を報告しない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:491](../test/PcscIfTest/SelfTest.cs#L491) |
| 対象 | `PcscResult.StatusFlag1/2` |
| 判定 | `Check` |
| 前提・入力 | 7-21 の結果 |
| 期待結果 | `StatusFlag1 == 0xFF` かつ `StatusFlag2 == 0xFF` |
| 意図 | 別カードの応答に含まれていた SF（`00 00`）を、対象カードの SF として報告しないことを確認する |

#### 7-23 不正応答でも対象 IDm は報告する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:493](../test/PcscIfTest/SelfTest.cs#L493) |
| 対象 | `PcscResult.Idm` |
| 判定 | `Check` |
| 前提・入力 | 7-21 の結果 |
| 期待結果 | `Idm == "0100000000000001"` |
| 意図 | 重ね置きチェックで確定した対象カードの IDm（応答に含まれていた ID 2 ではない）を報告し、どのカードへのアクセスで異常が起きたかを追跡できることを確認する |

#### 7-24 応答コード違いの読み出しは Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:496](../test/PcscIfTest/SelfTest.cs#L496) |
| 対象 | `Pcsc.DoRead` の `Malformed` 分岐 |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { WrongReadResponseCode = true }`（Read 応答の応答コードが `09`） |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | 応答コードが違う応答を `Indeterminate` として扱うことを、`Pcsc` の API レベルで確認する（5-15 の結合版） |

#### 7-25 システムブロックの読み出しは InvalidArgument

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:502](../test/PcscIfTest/SelfTest.cs#L502) |
| 対象 | `Pcsc.Read(0x88, 1)` |
| 判定 | `Check` |
| 前提・入力 | 接続済みの `Pcsc`、開始ブロック `0x88` |
| 期待結果 | `Status == PcscStatus.InvalidArgument` |
| 意図 | 公開 API からシステムブロックを指定した場合、例外ではなく `InvalidArgument` が返ることを確認する |

#### 7-26 ユーザ領域を超える読み出しは InvalidArgument

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:504](../test/PcscIfTest/SelfTest.cs#L504) |
| 対象 | `Pcsc.Read(0, 15)` |
| 判定 | `Check` |
| 前提・入力 | 開始 0、ブロック数 `UserBlockCount + 1`（15） |
| 期待結果 | `Status == PcscStatus.InvalidArgument` |
| 意図 | 範囲がユーザ領域をはみ出す指定を API レベルで拒否することを確認する |

#### 7-27 ブロック数 0 は InvalidArgument

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:506](../test/PcscIfTest/SelfTest.cs#L506) |
| 対象 | `Pcsc.Read(0, 0)` |
| 判定 | `Check` |
| 前提・入力 | ブロック数 0 |
| 期待結果 | `Status == PcscStatus.InvalidArgument` |
| 意図 | 空の読み出しを成功（長さ 0 のデータ）として返さないことを確認する |

#### 7-28 5 ブロック以上の Read は I/O 前に InvalidArgument

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:508](../test/PcscIfTest/SelfTest.cs#L508) |
| 対象 | `Pcsc.Read(0, 5)` |
| 判定 | `Check` |
| 前提・入力 | ブロック数 `MaxBlocksPerRead + 1`（5）。範囲自体はユーザ領域内 |
| 期待結果 | `Status == PcscStatus.InvalidArgument` |
| 意図 | `Pcsc.Read` は I/O の前にダミーの IDm で `BuildRead` を試し、実機が拒否する要求を事前に弾く。重ね置きチェック（RF 起動・Polling）を行ってからカードに拒否されるのではなく、引数エラーで即座に返すことを確認する |

#### 7-29 4 ブロックの Read は許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:510](../test/PcscIfTest/SelfTest.cs#L510) |
| 対象 | `Pcsc.Read(0, 4)` |
| 判定 | `Check` |
| 前提・入力 | 7-06 の `range`（メモリに `Payload(0, 224)`）を `Wrap()` で改めて接続 |
| 期待結果 | `Ok == true` |
| 意図 | 7-28 の内側の境界値。上限ちょうどの 4 ブロックは実際に読み出しまで成功することを確認する |

#### 7-30 Read(2, int.MaxValue) は引数エラー（巨大確保に到達しない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:514](../test/PcscIfTest/SelfTest.cs#L514) |
| 対象 | `Pcsc.Read(2, int.MaxValue)` |
| 判定 | `Check` |
| 前提・入力 | `overflow = new FakeTransport(1)` で接続済み |
| 期待結果 | `Status == PcscStatus.InvalidArgument` かつ `Error is ArgumentException` |
| 意図 | 4-13 の API レベル版。`Pcsc.Read` は内部の例外をすべて `InvalidArgument` に変換するため、ステータスだけでは `OutOfMemoryException` を捕まえた場合と区別できない。`Error` の型まで見て、配列確保に到達する前に引数エラーで弾かれたことを確認する |

#### 7-31 桁あふれ入力では I/O を行わない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:519](../test/PcscIfTest/SelfTest.cs#L519) |
| 対象 | `Pcsc.Read` の引数検証の位置 |
| 判定 | `Check` |
| 前提・入力 | 7-30 の実行後 |
| 期待結果 | `overflow.Polls == 0` かつ `overflow.Reads == 0` |
| 意図 | `InvalidArgument` は「I/O を行っていない」ことを意味する仕様である。RF を起動して Polling してから引数エラーを返す実装を検出する |

### 7 の追加分: Read の応答形式の異常と、例外時の後始末

メソッド `ReadFailurePaths()`（[SelfTest.cs:828](../test/PcscIfTest/SelfTest.cs#L828)）。
Read コマンドへの応答は「フラグが `Ok` だけ・フレームがちょうど 1 個」でなければ解析に進みません（`Pcsc.TrySingleFrame`）。
また例外で中断した場合、`Pcsc.Execute` はリーダを既知の状態に戻すため RF OFF とセッション終了を試みます。

#### 7-32 Read コマンドへの無応答は Indeterminate（解析できない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:833](../test/PcscIfTest/SelfTest.cs#L833) |
| 対象 | `Pcsc.DoRead` → `TrySingleFrame` |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { ReadResponseOverride = Empty() }`（Polling では 1 枚見えるが、Read コマンドには「応答なし」） |
| 期待結果 | `Status == Indeterminate`、`Message` に `"解析できない"` を含む、`Idm == "0100000000000001"` |
| 意図 | 重ね置きチェックの後にカードが応答しなくなった場合、`NoCard` ではなく `Indeterminate` にすることを確認する。読み出し対象のカードは確定しているので、IDm は報告する |

#### 7-33 Read 応答のフレームが 2 個なら Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:840](../test/PcscIfTest/SelfTest.cs#L840) |
| 対象 | `TrySingleFrame` のフレーム数検査 |
| 判定 | `Check` |
| 前提・入力 | Read コマンドへの応答を、正しい 1 ブロック分の Read 応答フレーム `ReadFrame(Idm(1), Payload(0x10, 16))` を 2 個並べた `Parse(Response(frame, frame))` にする |
| 期待結果 | `Status == Indeterminate` |
| 意図 | 特定 IDm 宛てのコマンドへの応答はちょうど 1 個のはず。2 個あれば別のカードの応答が混ざっている可能性があるので、先頭だけを採用しないことを確認する。**フレーム数の検査を消すとこのテストが失敗することを、ライブラリを意図的に壊して確認済み** |

#### 7-34 Read 応答のフラグが矛盾していたら Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:847](../test/PcscIfTest/SelfTest.cs#L847) |
| 対象 | `TrySingleFrame` のフラグ数検査 |
| 判定 | `Check` |
| 前提・入力 | `TransceiveResult { Ok = true, NoResponse = true, Frames = [正しい Read 応答フレーム] }` を Read コマンドに返す |
| 期待結果 | `Status == Indeterminate` |
| 意図 | 7-15 は Polling での矛盾フラグ、こちらは Read コマンドでの矛盾フラグ。中身が正しいフレームでも、フラグが矛盾していれば採用しないことを確認する |

#### 7-35 切替失敗時も RF OFF とセッション終了を試み、RF ON と Polling はしない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:853](../test/PcscIfTest/SelfTest.cs#L853) |
| 対象 | `Pcsc.Execute` の例外処理（後始末） |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { FailSwitch = true }` で `Read()` |
| 期待結果 | `StartCalls == 1`、`OffCalls == 2`（切替前の 1 回 + 後始末の 1 回）、`EndCalls == 1`（後始末）、`OnCalls == 0`、`Polls == 0` |
| 意図 | 7-19 はステータスだけを見ていた。こちらは、切替に失敗した時点で手順を打ち切り（RF ON・Polling をしない）、セッションを開いたまま放置しないことをカウンタで確認する。**後始末の処理を消すとこのテストが失敗することを確認済み** |

#### 7-36 例外で中断した結果は原因例外と操作名を保持する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:856](../test/PcscIfTest/SelfTest.cs#L856) |
| 対象 | `PcscResult.Error` / `Message` |
| 判定 | `Check` |
| 前提・入力 | 7-35 の結果 |
| 期待結果 | `Error is NotSupportedException`（`SwitchProtocol` が投げた例外）、`Message` が `"read: "` で始まる |
| 意図 | 例外は外に出さないが、原因の例外と「どの操作（read / write）で起きたか」を結果に残すことを確認する |

#### 7-37 セッション終了の失敗時は RF OFF とセッション終了をもう一度試みる

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:862](../test/PcscIfTest/SelfTest.cs#L862) |
| 対象 | `Pcsc.Execute` の例外処理（後始末） |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { FailEnd = true }` で `Read()` |
| 期待結果 | `Reads == 1`、`OffCalls == 3`（切替前・正常終了時・後始末）、`EndCalls == 2`（正常終了時・後始末） |
| 意図 | 7-18 はステータスだけを見ていた。こちらは、読み出しまで進んだ後にセッション終了が失敗しても、RF OFF とセッション終了をもう一度試みてリーダを既知の状態に戻そうとすることを確認する。後始末の中の失敗は握りつぶされ、結果は `Indeterminate` のまま |

---

## 8. Write と重ね置きチェック（45 件）

メソッド `WriteTests()`（[SelfTest.cs:526](../test/PcscIfTest/SelfTest.cs#L526)）。
対象は `Pcsc.Write` の全経路（`Pcsc.DoWrite`、[L1_Pcsc.cs](../src/L1_Pcsc.cs)）です。

`Pcsc.Write` の本体は次のとおりです。

```
for 各ブロック:
    1 ブロックだけの Write コマンドを送る（1 コマンド 1 ブロック）
    応答が解析できない / Malformed → Indeterminate（書き込まれたか不明）
    CardError                     → CardRejected
if VerifyAfterWrite == false → Ok（読み戻し未実施）
for 4 ブロックずつ（MaxBlocksPerRead）:
    Read して書き込んだ内容と比較
    Read 失敗 / 不一致 → Indeterminate
→ Ok
```

特に記載がない限り、`newData = Payload(0x20, 32)`（`20..3F`、2 ブロック分）、`oneBlock = Payload(0x30, 16)`（`30..3F`）です。

#### 8-01 16 バイト書き込みが成功し、読み戻しで一致を確認

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:535](../test/PcscIfTest/SelfTest.cs#L535) |
| 対象 | `Pcsc.Write(byte[])` |
| 判定 | `Check` |
| 前提・入力 | `single = new FakeTransport(1)`、メモリ先頭に `Payload(0x10, 32)`（`10..2F`）、`Write(oneBlock)` |
| 期待結果 | `Ok == true` かつ カードメモリの先頭 16 バイトが `30..3F` |
| 意図 | 原則の使い方（先頭 16 バイトの書き込み）が成功し、**模擬カードのメモリに実際に反映される**ことを確認する。戻り値だけでなくメモリを直接見る |

#### 8-02 16 バイト書き込みは 1 コマンド・読み戻し 1 回

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:537](../test/PcscIfTest/SelfTest.cs#L537) |
| 対象 | `Pcsc.DoWrite` のコマンド数 |
| 判定 | `Check` |
| 前提・入力 | 8-01 の実行後 |
| 期待結果 | `single.Writes == 1` かつ `single.Reads == 1` |
| 意図 | 1 ブロックの書き込みで Write コマンドを 1 回、読み戻しの Read を 1 回だけ送ることを確認する |

#### 8-03 16 バイト書き込みは次のブロックを壊さない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:539](../test/PcscIfTest/SelfTest.cs#L539) |
| 対象 | `Pcsc.Write` の書き込み範囲 |
| 判定 | `Check` |
| 前提・入力 | 8-01 の実行後 |
| 期待結果 | カードメモリのオフセット 16..31（S_PAD1）が元の `20..2F` のまま |
| 意図 | 16 バイトの書き込みが隣のブロックにはみ出さないことを確認する（実機でも同じ確認を実施済み） |

#### 8-04 32 バイト書き込みも成功し、読み戻しで一致を確認

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:546](../test/PcscIfTest/SelfTest.cs#L546) |
| 対象 | `Pcsc.Write(byte[])`（複数ブロック） |
| 判定 | `Check` |
| 前提・入力 | `written = new FakeTransport(1)`、メモリ先頭に `Payload(0x10, 32)`、`Write(newData)` |
| 期待結果 | `Ok == true` かつ カードメモリの先頭 32 バイトが `20..3F` |
| 意図 | 16 バイトの倍数を渡した場合に、複数ブロックへ書けることを確認する（原子的ではない） |

#### 8-05 1 ブロックにつき 1 コマンド送出

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:549](../test/PcscIfTest/SelfTest.cs#L549) |
| 対象 | `Pcsc.DoWrite` の分割 |
| 判定 | `Check` |
| 前提・入力 | 8-04 の実行後 |
| 期待結果 | `written.Writes == 2` |
| 意図 | 対象カードは 1 コマンドあたり 1 ブロックしか書けない（実機で 2 ブロック目が `SF1=02 SF2=A8`）。2 ブロックを 2 コマンドに分けて送ることを確認する。まとめて送ると `FakeTransport` が拒否するので 8-04 も失敗する |

#### 8-06 読み戻しは 1 回

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:550](../test/PcscIfTest/SelfTest.cs#L550) |
| 対象 | `Pcsc.DoWrite` の読み戻し |
| 判定 | `Check` |
| 前提・入力 | 8-04 の実行後 |
| 期待結果 | `written.Reads == 1` |
| 意図 | Read は 1 コマンド 4 ブロックまで読めるので、2 ブロックの読み戻しは 1 回にまとめることを確認する（ブロックごとに読み戻す無駄な実装を検出する） |

#### 8-07 2 枚重なっているときは書き込まない（MultipleCards）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:552](../test/PcscIfTest/SelfTest.cs#L552) |
| 対象 | `Pcsc.Write` → `Gate` |
| 判定 | `Check` |
| 前提・入力 | `twoCards = new FakeTransport(1) { SecondCard = 2 }`、`Write(newData)` |
| 期待結果 | `Status == PcscStatus.MultipleCards` |
| 意図 | 書き込みでも Read と同じ重ね置き判定が効くことを確認する。判定は `Execute` / `Gate` に集約されており、Write 側だけ緩むことを防ぐ |

#### 8-08 重ね置き検出時は Write コマンドを送出しない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:556](../test/PcscIfTest/SelfTest.cs#L556) |
| 対象 | `Pcsc.Execute` |
| 判定 | `Check` |
| 前提・入力 | 8-07 の実行後 |
| 期待結果 | `twoCards.Writes == 0` |
| 意図 | 重ね置き時に誤って別のカードへ書き込むことが最も重大な事故である。Write コマンドを 1 回も送っていないことをカウンタで確認する |

#### 8-09 無応答では書き込まない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:558](../test/PcscIfTest/SelfTest.cs#L558) |
| 対象 | `Pcsc.Write` → `Gate` |
| 判定 | `Check` |
| 前提・入力 | `noCard = new FakeTransport(1) { NoCard = true }`、`Write(newData)` |
| 期待結果 | `Status == PcscStatus.NoCard` かつ `noCard.Writes == 0` |
| 意図 | カードを観測できないときは書き込みコマンドを送らずに `NoCard` を返すことを確認する |

#### 8-10 カードの書き込み拒否は CardRejected

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:562](../test/PcscIfTest/SelfTest.cs#L562) |
| 対象 | `Pcsc.DoWrite` の `CardError` 分岐 |
| 判定 | `Check` |
| 前提・入力 | `rejected = new FakeTransport(1) { RejectWrite = true }`（Write に `SF1=FF SF2=A2`）、`Write(newData)` |
| 期待結果 | `Status == PcscStatus.CardRejected` かつ `StatusFlag2 == 0xA2` |
| 意図 | カードが明示的に書き込みを拒否した場合に `CardRejected` と SF を返すことを確認する。1 ブロック目で拒否されるので、2 ブロック目は送られない |

#### 8-11 11 バイトに欠けた書き込み応答は Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:568](../test/PcscIfTest/SelfTest.cs#L568) |
| 対象 | `Pcsc.DoWrite` の `Malformed` 分岐 |
| 判定 | `Check` |
| 前提・入力 | `truncated = new FakeTransport(1) { TruncateWriteResponse = true }`（メモリには書いたうえで、応答を 11 バイトに切り詰める）、`Write(oneBlock)` |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | 応答が壊れていると、カードに書き込まれたかどうか分からない（この模擬では実際には書かれている）。「書き込まれていない」を意味する `CardRejected` にしてしまうと、呼び出し側が安全と誤解して別の処理に進む恐れがある。`Indeterminate` にすることを確認する |

#### 8-12 不正な書き込み応答では SF を報告しない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:572](../test/PcscIfTest/SelfTest.cs#L572) |
| 対象 | `PcscResult.StatusFlag1/2` |
| 判定 | `Check` |
| 前提・入力 | 8-11 の結果 |
| 期待結果 | `StatusFlag1 == 0xFF` かつ `StatusFlag2 == 0xFF` |
| 意図 | 途中で切れた応答から読めた SF1（`00`）を報告しないことを確認する |

#### 8-13 不正な書き込み応答は「書き込まれたか不明」と伝える

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:574](../test/PcscIfTest/SelfTest.cs#L574) |
| 対象 | `PcscResult.Message` |
| 判定 | `Check` |
| 前提・入力 | 8-11 の結果 |
| 期待結果 | `Message` に `"書き込まれたか不明"` を含む |
| 意図 | ログを見た運用者が、カードの内容を確認し直す必要があると分かることを確認する |

#### 8-14 成功応答でも読み戻しが違えば Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:577](../test/PcscIfTest/SelfTest.cs#L577) |
| 対象 | `Pcsc.DoWrite` の読み戻し比較 |
| 判定 | `Check` |
| 前提・入力 | `dropped = new FakeTransport(1) { SilentlyDropWrite = true }`（成功応答を返すが書かない）、メモリ先頭に `Payload(0x10, 32)`、`Write(newData)` |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | カードの成功応答だけを信用せず、同一セッション内の読み戻しで内容を確認していることを確認する。読み戻しを省略・比較を誤った実装では `Ok` になって失敗する |

#### 8-15 複数ブロックをまとめて送ると実機同様に拒否される（SF=02A8）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:583](../test/PcscIfTest/SelfTest.cs#L583) |
| 対象 | **模擬トランスポート `FakeTransport.Transceive` 自体** |
| 判定 | `Check` |
| 前提・入力 | `multiBlockCmd = new FakeTransport(1)` に、`FelicaLiteS.BuildWrite(Idm(1), BlockRange(0, 2), Payload(0, 32))`（2 ブロックを 1 コマンドにまとめた Write）を直接 `Transceive` |
| 期待結果 | `multiResult.Ok == true`、`TryParseWriteResponse(Frames[0], Idm(1), ...)` が `false`、`SF1 == 0x02`、`SF2 == 0xA8` |
| 意図 | ライブラリではなく**テストの前提**の確認。模擬カードが実機の制約（Write は 1 コマンド 1 ブロック）を再現していることを保証する。この再現が壊れていると、ライブラリがまとめて送るように退行しても 8-04 などが合格してしまう |

#### 8-16 VerifyAfterWrite=false なら読み戻さない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:592](../test/PcscIfTest/SelfTest.cs#L592) |
| 対象 | `PcscOptions.VerifyAfterWrite` |
| 判定 | `Check` |
| 前提・入力 | `FakeOptions()` の `VerifyAfterWrite` を `false` にして `Pcsc` を生成・接続、`Write(newData)` |
| 期待結果 | `Ok == true` かつ `unverified.Reads == 0` |
| 意図 | 読み戻しを無効にしたときは Read コマンドを送らないことを確認する。オプションが無視されていないことの確認 |

#### 8-17 null データは InvalidArgument

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:602](../test/PcscIfTest/SelfTest.cs#L602) |
| 対象 | `Pcsc.Write(null)` |
| 判定 | `Check` |
| 前提・入力 | 接続済みの `Pcsc`、データ `null` |
| 期待結果 | `Status == PcscStatus.InvalidArgument` |
| 意図 | `NullReferenceException` を投げずに `InvalidArgument` を返すことを確認する |

#### 8-18 空データは InvalidArgument

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:604](../test/PcscIfTest/SelfTest.cs#L604) |
| 対象 | `Pcsc.Write(new byte[0])` |
| 判定 | `Check` |
| 前提・入力 | 長さ 0 のデータ |
| 期待結果 | `Status == PcscStatus.InvalidArgument` |
| 意図 | 何も書かない要求を成功として扱わないことを確認する |

#### 8-19 16 の倍数でないデータは InvalidArgument

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:606](../test/PcscIfTest/SelfTest.cs#L606) |
| 対象 | `Pcsc.Write(Payload(0, 20))` |
| 判定 | `Check` |
| 前提・入力 | 20 バイトのデータ |
| 期待結果 | `Status == PcscStatus.InvalidArgument` |
| 意図 | 端数のあるデータを勝手に切り捨て・ゼロ埋めして書き込まないことを確認する |

#### 8-20 ユーザ領域を超える書き込みは InvalidArgument

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:608](../test/PcscIfTest/SelfTest.cs#L608) |
| 対象 | `Pcsc.Write(0x0D, Payload(0, 32))` |
| 判定 | `Check` |
| 前提・入力 | 開始ブロック `0x0D`（最後のユーザブロック）、データ 32 バイト（2 ブロック目が `0x0E` にはみ出す） |
| 期待結果 | `Status == PcscStatus.InvalidArgument` |
| 意図 | 開始位置が正しくても、終端がユーザ領域外にはみ出す書き込みを 1 ブロック目も含めて一切行わないことを確認する |

#### 8-21 システムブロックへの書き込みは InvalidArgument

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:610](../test/PcscIfTest/SelfTest.cs#L610) |
| 対象 | `Pcsc.Write(0x88, Payload(0, 16))` |
| 判定 | `Check` |
| 前提・入力 | 開始ブロック `0x88`（MC） |
| 期待結果 | `Status == PcscStatus.InvalidArgument` |
| 意図 | カードを復旧不能にし得る MC への書き込みを、公開 API から要求できないことを確認する |

#### 8-22 処理中に呼び出し側が配列を書き換えても送出内容は変わらない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:613](../test/PcscIfTest/SelfTest.cs#L613)（本体は `CallerMutationIsIgnored()`、[SelfTest.cs:664](../test/PcscIfTest/SelfTest.cs#L664)） |
| 対象 | `Pcsc.Write` の入力配列の複製（`data.Clone()`） |
| 判定 | `Check` |
| 前提・入力 | `caller = Payload(0x20, 16)`、`fake.MutateOnPoll = caller`（最初の Polling 受信時に `caller[0] = 0xFF`）、`Write(caller)` |
| 期待結果 | `Ok == true`、`caller[0] == 0xFF`、カードメモリの先頭バイトが `0x20` |
| 意図 | 別スレッド等で呼び出し側が配列を書き換えても、検証済みのデータがそのまま書き込まれることを確認する。<br>・Write 完了**後**に書き換えるのでは複製しなくても合格するため、Write コマンド組み立て前（重ね置きチェックの Polling 中）に書き換える<br>・`caller[0] == 0xFF` で、書き換えが実際に起きた（テストが空振りしていない）ことも確認する<br>・複製していなければカードに `0xFF` が書かれて不合格になる |

#### 8-23 5 ブロック書き込みが成功する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:619](../test/PcscIfTest/SelfTest.cs#L619) |
| 対象 | `Pcsc.Write(byte[])`（Read 上限を超える範囲） |
| 判定 | `Check` |
| 前提・入力 | `five = new FakeTransport(1)`（メモリは全 0）、`Write(Payload(0x40, 80))`（ブロック `00..04`） |
| 期待結果 | `Ok == true` |
| 意図 | 書き込み範囲が Read の上限（4 ブロック）を超える場合でも成功することを確認する。読み戻しを分割しない実装では `BuildRead` が例外になり、**カードは書き換わったのに `Indeterminate`** が返る不具合があった |

#### 8-24 5 ブロックは 5 コマンド・読み戻し 2 回（4+1）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:623](../test/PcscIfTest/SelfTest.cs#L623) |
| 対象 | `Pcsc.DoWrite` の分割 |
| 判定 | `Check` |
| 前提・入力 | 8-23 の実行後 |
| 期待結果 | `five.Writes == 5` かつ `five.Reads == 2` |
| 意図 | Write は 1 ブロックずつ 5 回、読み戻しは 4 ブロック + 1 ブロックの 2 回に分割することを確認する |

#### 8-25 5 ブロックがカードに反映される

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:625](../test/PcscIfTest/SelfTest.cs#L625) |
| 対象 | 書き込み結果 |
| 判定 | `Check` |
| 前提・入力 | 8-23 の実行後 |
| 期待結果 | カードメモリの先頭 80 バイトが `Payload(0x40, 80)`（`40..8F`）と一致 |
| 意図 | 分割した各ブロックが正しい位置に正しい順で書かれたことをメモリで確認する |

#### 8-26 14 ブロック（ユーザ領域全体）書き込みが成功する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:628](../test/PcscIfTest/SelfTest.cs#L628) |
| 対象 | `Pcsc.Write(byte[])`（最大範囲） |
| 判定 | `Check` |
| 前提・入力 | `whole = new FakeTransport(1)`、`Write(Payload(0x60, 224))`（ブロック `00..0D` 全体。値は `60` から始まり `FF` の次は `00` に戻る） |
| 期待結果 | `Ok == true` |
| 意図 | 書き込める最大範囲（ユーザ領域全体）で成功することを確認する |

#### 8-27 14 ブロックは 14 コマンド・読み戻し 4 回（4+4+4+2）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:632](../test/PcscIfTest/SelfTest.cs#L632) |
| 対象 | `Pcsc.DoWrite` の分割 |
| 判定 | `Check` |
| 前提・入力 | 8-26 の実行後 |
| 期待結果 | `whole.Writes == 14` かつ `whole.Reads == 4` |
| 意図 | 読み戻しを 4 ブロックずつに分け、最後の端数（2 ブロック）も漏れなく読むことを確認する。端数を切り捨てる・余分に読むといった off-by-one を検出する |

#### 8-28 14 ブロックがカードに反映される

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:634](../test/PcscIfTest/SelfTest.cs#L634) |
| 対象 | 書き込み結果 |
| 判定 | `Check` |
| 前提・入力 | 8-26 の実行後 |
| 期待結果 | カードメモリ全 224 バイトが `Payload(0x60, 224)` と一致 |
| 意図 | ユーザ領域全体が正しく書かれたことをメモリで確認する |

#### 8-29 開始ブロックをずらした 5 ブロック書き込みが成功する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:637](../test/PcscIfTest/SelfTest.cs#L637) |
| 対象 | `Pcsc.Write(byte startBlock, byte[] data)` |
| 判定 | `Check` |
| 前提・入力 | `shifted = new FakeTransport(1)`、メモリ全体に `initial = Payload(0xF0, 224)`、`Write(3, Payload(0x50, 80))`（ブロック `03..07`） |
| 期待結果 | `Ok == true` かつ カードメモリのオフセット 48..127 が `50..9F` |
| 意図 | 開始ブロックが 0 でない場合、書き込み位置と読み戻しの比較位置（`offset` の計算）が正しいことを確認する。読み戻しの分割は 4 ブロック（`03..06`）+ 1 ブロック（`07`） |

#### 8-30 書き込み範囲の前後を壊さない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:644](../test/PcscIfTest/SelfTest.cs#L644) |
| 対象 | 書き込み範囲外のメモリ |
| 判定 | `Check` |
| 前提・入力 | 8-29 の実行後 |
| 期待結果 | オフセット 0..47（ブロック `00..02`）と 128..223（ブロック `08..0D`）が `initial` のまま |
| 意図 | 指定範囲の前にも後ろにも 1 バイトもはみ出していないことを確認する。初期値を `00` ではなく `F0..` にしているので、誤ってゼロを書いた場合も検出できる |

#### 8-31 1 チャンク目のブロックが書けていなければ Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:650](../test/PcscIfTest/SelfTest.cs#L650) |
| 対象 | `Pcsc.DoWrite` の読み戻し比較（1 チャンク目） |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { DropWriteForBlock = 1 }`（ブロック 1 だけ成功応答を返しつつ書かない）、`Write(Payload(0x40, 80))` |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | 読み戻しの 1 チャンク目（ブロック `00..03`）にある書き込み漏れを検出できることを確認する |

#### 8-32 2 チャンク目のブロックが書けていなければ Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:654](../test/PcscIfTest/SelfTest.cs#L654) |
| 対象 | `Pcsc.DoWrite` の読み戻し比較（2 チャンク目） |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { DropWriteForBlock = 4 }`（ブロック 4 だけ書かない）、`Write(Payload(0x40, 80))` |
| 期待結果 | `Status == PcscStatus.Indeterminate` |
| 意図 | 1 チャンク目は一致するので、2 チャンク目（ブロック `04`）を**読むだけでなく実際に比較している**ことを確認する。2 チャンク目の比較を省略したり、比較対象のオフセットを誤ったりすると `Ok` になって不合格になる |

### 8 の追加分: 2 ブロック目以降の失敗、読み戻しの失敗、応答形式の異常

メソッド `WriteFailurePaths()`（[SelfTest.cs:870](../test/PcscIfTest/SelfTest.cs#L870)）。
Write は 1 ブロックずつ送るので原子的ではありません。途中のブロックで失敗したとき、
**先に送ったブロックは書き換わったまま**になり、結果のメッセージでそれを伝えます。
以下、`initial = Payload(0xA0, 32)`（`A0..BF`）、`newData = Payload(0x20, 32)`（`20..3F`）です。

#### 8-33 2 ブロック目の拒否は CardRejected（SF は実値）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:879](../test/PcscIfTest/SelfTest.cs#L879) |
| 対象 | `Pcsc.DoWrite` の `CardError` 分岐（2 ブロック目） |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { RejectWriteForBlock = 1 }`、メモリ先頭に `initial`、`Write(newData)` |
| 期待結果 | `Status == CardRejected`、`StatusFlag1 == 0xFF`、`StatusFlag2 == 0xA2` |
| 意図 | 8-10 は 1 ブロック目での拒否。こちらは 1 ブロック目が成功した後の 2 ブロック目での拒否でも、カードの SF をそのまま報告することを確認する |

#### 8-34 2 ブロック目の拒否は「先行ブロックは書き換わっている」と伝える

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:882](../test/PcscIfTest/SelfTest.cs#L882) |
| 対象 | `PcscResult.Message` |
| 判定 | `Check` |
| 前提・入力 | 8-33 の結果 |
| 期待結果 | `Message` に `"先行ブロックは書き換わっている"` を含む |
| 意図 | `CardRejected` は通常「書き込まれていない」と読めるが、複数ブロックの途中で拒否された場合は前のブロックが書き換わっている。呼び出し側が復旧手順（書き戻しなど）を判断できるよう、それを伝えることを確認する |

#### 8-35 2 ブロック目の拒否時、1 ブロック目は書き換わり 2 ブロック目は元のまま

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:884](../test/PcscIfTest/SelfTest.cs#L884) |
| 対象 | 模擬カードのメモリ |
| 判定 | `Check` |
| 前提・入力 | 8-33 の実行後 |
| 期待結果 | オフセット 0..15 が `20..2F`（書き換わった）、16..31 が `B0..BF`（`initial` のまま） |
| 意図 | 8-34 のメッセージが事実と一致していること、つまり Write が原子的でないことを実際のメモリ状態で確認する。初期値と書き込み値を別の値にしているので、どちらのブロックが書かれたか区別できる |

#### 8-36 書き込みの拒否後は読み戻しを行わない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:887](../test/PcscIfTest/SelfTest.cs#L887) |
| 対象 | `Pcsc.DoWrite` の打ち切り |
| 判定 | `Check` |
| 前提・入力 | 8-33 の実行後 |
| 期待結果 | `Writes == 2` かつ `Reads == 0` |
| 意図 | 拒否された時点で処理を打ち切り、読み戻しに進まないことを確認する |

#### 8-37 1 ブロック目の拒否では「先行ブロック」に触れず、後続ブロックを送らない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:892](../test/PcscIfTest/SelfTest.cs#L892) |
| 対象 | `Pcsc.DoWrite` のメッセージ分岐（`i > 0`） |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { RejectWriteForBlock = 0 }`、`Write(newData)` |
| 期待結果 | `Status == CardRejected`、`Message` に `"先行ブロック"` を含まない、`Writes == 1` |
| 意図 | 1 ブロック目で拒否された場合はカードは一切書き換わっていないので、「先行ブロックは書き換わっている」と誤って伝えないことを確認する（8-34 と対） |

#### 8-38 2 ブロック目の応答不正は Indeterminate（SF は報告しない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:899](../test/PcscIfTest/SelfTest.cs#L899) |
| 対象 | `Pcsc.DoWrite` の `Malformed` 分岐（2 ブロック目） |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { TruncateWriteForBlock = 1 }`（2 ブロック目はメモリに書いたうえで応答を 11 バイトに切り詰める）、`Write(newData)` |
| 期待結果 | `Status == Indeterminate`、`StatusFlag1 == 0xFF`、`StatusFlag2 == 0xFF` |
| 意図 | 8-11 の 2 ブロック目版。途中のブロックの応答が壊れていても `CardRejected` にせず、SF を報告しないことを確認する |

#### 8-39 2 ブロック目の応答不正は「書き込まれたか不明」「先行ブロックは書き換わっている」と伝える

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:902](../test/PcscIfTest/SelfTest.cs#L902) |
| 対象 | `PcscResult.Message` |
| 判定 | `Check` |
| 前提・入力 | 8-38 の結果 |
| 期待結果 | `Message` に `"書き込まれたか不明"` と `"先行ブロックは書き換わっている"` の両方を含む |
| 意図 | 「そのブロックは不明」「前のブロックは確実に書き換わっている」という 2 つの事実を両方伝えることを確認する |

#### 8-40 読み戻しの Read が拒否されたら Indeterminate

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:909](../test/PcscIfTest/SelfTest.cs#L909) |
| 対象 | `Pcsc.DoWrite` の読み戻し失敗分岐（`!verify.Ok`） |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { RejectRead = true }`（Write は成功、Read だけ `SF1=FF SF2=A1`）、`Write(Payload(0x30, 16))` |
| 期待結果 | `Status == Indeterminate`、`Writes == 1`、`Reads == 1` |
| 意図 | 書き込み自体は成功したが確認の読み出しができない場合、`Ok` にも `CardRejected` にもせず `Indeterminate` にすることを確認する |

#### 8-41 読み戻し失敗は「読み戻しに失敗」と伝え、読み戻しの SF を報告する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:912](../test/PcscIfTest/SelfTest.cs#L912) |
| 対象 | `PcscResult.Message` / `StatusFlag1/2` |
| 判定 | `Check` |
| 前提・入力 | 8-40 の結果 |
| 期待結果 | `Message` に `"読み戻しに失敗"` を含む、`StatusFlag1 == 0xFF`、`StatusFlag2 == 0xA1`（読み戻しの Read で返った SF） |
| 意図 | 失敗の原因が読み戻し側にあることと、そのときのカードの SF を呼び出し側が調べられることを確認する |

#### 8-42 読み戻し不一致は「一致しない」と伝え、読み戻し失敗と区別する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:919](../test/PcscIfTest/SelfTest.cs#L919) |
| 対象 | `Pcsc.DoWrite` の不一致分岐 |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { SilentlyDropWrite = true }`、メモリ先頭に `initial`、`Write(newData)` |
| 期待結果 | `Status == Indeterminate`、`Message` に `"一致しない"` を含み、`"読み戻しに失敗"` を含まない |
| 意図 | 「読めたが内容が違う」（カードが成功と言いながら書いていない）と「読めなかった」（8-41）は原因が違う。ステータスは同じ `Indeterminate` でも、メッセージで区別できることを確認する |

#### 8-43 Write コマンドへの無応答は Indeterminate（解析できない・書き込まれたか不明）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:927](../test/PcscIfTest/SelfTest.cs#L927) |
| 対象 | `Pcsc.DoWrite` → `TrySingleFrame` |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { WriteResponseOverride = Empty() }`、`Write(newData)` |
| 期待結果 | `Status == Indeterminate`、`Message` に `"解析できない"` と `"書き込まれたか不明"` を含む |
| 意図 | Write コマンドの後に応答が返らなかった場合、カードが書き込んだかどうかは分からない。`NoCard` や `CardRejected` にせず、その旨を伝えることを確認する |

#### 8-44 Write 応答を解析できなければ後続ブロックも読み戻しも送らない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:931](../test/PcscIfTest/SelfTest.cs#L931) |
| 対象 | `Pcsc.DoWrite` の打ち切り |
| 判定 | `Check` |
| 前提・入力 | 8-43 の実行後（2 ブロック分のデータ） |
| 期待結果 | `Writes == 1` かつ `Reads == 0` |
| 意図 | 状態が不明なカードにそれ以上コマンドを送らないことを確認する |

#### 8-45 Write 応答のフレームが 2 個なら Indeterminate（読み戻しに頼らず判定）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:942](../test/PcscIfTest/SelfTest.cs#L942) |
| 対象 | `TrySingleFrame` のフレーム数検査（Write 側） |
| 判定 | `Check` |
| 前提・入力 | Write コマンドへの応答を、成功応答 `StatusFrame(09, Idm(1), 00, 00)` を 2 個並べたものにする。`VerifyAfterWrite = false` の `Pcsc` で `Write(Payload(0x30, 16))` |
| 期待結果 | `Status == Indeterminate` |
| 意図 | 7-33 の Write 版。<br>**読み戻しを有効にしたままだと、フレーム数の検査を消しても読み戻しの不一致で `Indeterminate` になり合格してしまう**ことを、ライブラリを意図的に壊して確認した。そのため読み戻しを切り、フレーム数の検査だけで判定されるようにしている（修正後、検査を消すとこのテストが失敗することを確認済み） |

---

## 9. オプション検証とログ（24 件）

メソッド `OptionTests()`（[SelfTest.cs:678](../test/PcscIfTest/SelfTest.cs#L678)）。
対象は `PcscOptions.Validate` / `Snapshot`（[L1_PcscModels.cs](../src/L1_PcscModels.cs)）と `Pcsc.Log`（[L1_Pcsc.cs](../src/L1_Pcsc.cs)）です。

| 項目 | 既定 | 有効範囲 |
|---|---|---|
| `DuplicateCheckRounds` | 8 | 2..64 |
| `TransceiveTimeoutUs` | 300000 | 100000..1000000 |
| `RfSettleDelayMs` | 20 | 0..1000 |

範囲外の値は `Pcsc` のコンストラクタで `ArgumentOutOfRangeException` になります（これは `PcscResult` ではなく例外で通知される唯一の箇所です）。

#### 9-01 ラウンド数 1 は拒否（重ね置きを検出できないため）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:680](../test/PcscIfTest/SelfTest.cs#L680) |
| 対象 | `new Pcsc(PcscOptions)` → `PcscOptions.Validate` |
| 判定 | `Throws` |
| 前提・入力 | `Options(1, 300000, 0)` |
| 期待結果 | 例外が発生する |
| 意図 | 対象リーダは 1 回の Polling で 1 枚分しか返さないため、1 ラウンドでは重ね置きを原理的に検出できない。安全機能を無効化する設定を受け付けないことを確認する（下限の外側） |

#### 9-02 ラウンド数 65 は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:682](../test/PcscIfTest/SelfTest.cs#L682) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Throws` |
| 前提・入力 | `Options(65, 300000, 0)` |
| 期待結果 | 例外が発生する |
| 意図 | 上限の外側。過大なラウンド数で 1 回の Read / Write が極端に長くなる（最悪 65 × タイムアウト）ことを防ぐ |

#### 9-03 タイムアウト下限未満は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:684](../test/PcscIfTest/SelfTest.cs#L684) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Throws` |
| 前提・入力 | `Options(8, 99999, 0)` |
| 期待結果 | 例外が発生する |
| 意図 | 下限（100000us = 100ms）の 1 つ外側。短すぎるタイムアウトでは、カードが存在しても応答が間に合わず `NoCard` と誤判定しやすくなる |

#### 9-04 タイムアウト上限超過は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:686](../test/PcscIfTest/SelfTest.cs#L686) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Throws` |
| 前提・入力 | `Options(8, 1000001, 0)` |
| 期待結果 | 例外が発生する |
| 意図 | 上限（1000000us = 1 秒）の 1 つ外側 |

#### 9-05 RF 待ち時間の上限超過は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:688](../test/PcscIfTest/SelfTest.cs#L688) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Throws` |
| 前提・入力 | `Options(8, 300000, 1001)` |
| 期待結果 | 例外が発生する |
| 意図 | 上限（1000ms）の 1 つ外側 |

#### 9-06 オプションは生成時にスナップショットされる

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:691](../test/PcscIfTest/SelfTest.cs#L691) |
| 対象 | `PcscOptions.Snapshot`（`Pcsc` コンストラクタ内） |
| 判定 | `Check` |
| 前提・入力 | `mutable = FakeOptions()`（ラウンド 2）で `Pcsc` を生成 → **生成後に** `mutable.DuplicateCheckRounds = 64` → `Connect` → `Read()`（カード 1 枚） |
| 期待結果 | `fake.Polls == 2` |
| 意図 | 生成後の書き換えが効くと、検証（`Validate`）を通っていない値で動作する、走査中に値が変わる、といった問題が起きる。コンストラクタ時点の値（2）で動作することを確認する。1 枚のときは途中で打ち切らないので、`Polls` がそのままラウンド数を表す（書き換えが効いていれば 64 になる） |

#### 9-07 ログの失敗は判定を迂回しない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:699](../test/PcscIfTest/SelfTest.cs#L699) |
| 対象 | `Pcsc.Log` の例外処理 |
| 判定 | `Check` |
| 前提・入力 | `Log` に常に例外を投げるデリゲートを設定、`logged = new FakeTransport(1) { NoCard = true }`、`Connect` → `Read()` |
| 期待結果 | `Read().Status == PcscStatus.NoCard`（例外が外に出ない） |
| 意図 | 利用者が渡したログ出力（ファイル書き込み等）が失敗しても、判定結果が変わったり例外で処理が中断したりしないことを確認する。ログの例外が伝播すると、`Read` は結果を返せず自己テスト自体が異常終了する |

#### 9-08 Connect / Read / Disconnect がそれぞれログされる

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:707](../test/PcscIfTest/SelfTest.cs#L707) |
| 対象 | `Pcsc.Log` の呼び出し回数 |
| 判定 | `Check` |
| 前提・入力 | `Log` に行数を数えるデリゲートを設定、カード 1 枚で `Connect` → `Read()` → `Disconnect()` |
| 期待結果 | ログ行数 `== 3` |
| 意図 | API 呼び出し 1 回につき結果が 1 行ずつ記録されること（記録漏れも重複もないこと）を確認する。ログは `"[pcsc] " + PcscResult.ToString()` の形式 |

### 9 の追加分: オプションの境界と既定値、RF 待ち、結果の書式

メソッド `OptionBoundariesAndResult()`（[SelfTest.cs:950](../test/PcscIfTest/SelfTest.cs#L950)）。
9-01〜9-05 は範囲の**外側**の境界でした。こちらは**内側**の境界（許可される端の値）と既定値、
RF 待ちの実際の効果、ログに出る `PcscResult.ToString` の書式を確認します。

#### 9-09 既定値はラウンド 8 / 300000us / RF 待ち 20ms / 読み戻し有効 / ログなし

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:953](../test/PcscIfTest/SelfTest.cs#L953) |
| 対象 | `new PcscOptions()` |
| 判定 | `Check` |
| 前提・入力 | 引数なしで生成 |
| 期待結果 | `DuplicateCheckRounds == 8`、`TransceiveTimeoutUs == 300000`、`RfSettleDelayMs == 20`、`VerifyAfterWrite == true`、`Log == null` |
| 意図 | README に記載した既定値と実装が一致していることを確認する。特に重ね置きチェックのラウンド数と読み戻しは安全性に直結するので、既定値が意図せず変わることを防ぐ |

#### 9-10 ラウンド数 2（下限）は許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:957](../test/PcscIfTest/SelfTest.cs#L957) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Check`（`!Throws`） |
| 前提・入力 | `Options(2, 300000, 0)` で `new Pcsc` |
| 期待結果 | 例外が発生しない |
| 意図 | 9-01（1 は拒否）と対の内側の境界値 |

#### 9-11 ラウンド数 64（上限）は許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:958](../test/PcscIfTest/SelfTest.cs#L958) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Check`（`!Throws`） |
| 前提・入力 | `Options(64, 300000, 0)` |
| 期待結果 | 例外が発生しない |
| 意図 | 9-02（65 は拒否）と対の内側の境界値 |

#### 9-12 タイムアウト 100000（下限）は許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:959](../test/PcscIfTest/SelfTest.cs#L959) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Check`（`!Throws`） |
| 前提・入力 | `Options(8, 100000, 0)` |
| 期待結果 | 例外が発生しない |
| 意図 | 9-03（99999 は拒否）と対の内側の境界値 |

#### 9-13 タイムアウト 1000000（上限）は許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:960](../test/PcscIfTest/SelfTest.cs#L960) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Check`（`!Throws`） |
| 前提・入力 | `Options(8, 1000000, 0)` |
| 期待結果 | 例外が発生しない |
| 意図 | 9-04（1000001 は拒否）と対の内側の境界値 |

#### 9-14 RF 待ち 0（下限）は許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:961](../test/PcscIfTest/SelfTest.cs#L961) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Check`（`!Throws`） |
| 前提・入力 | `Options(8, 300000, 0)` |
| 期待結果 | 例外が発生しない |
| 意図 | 9-16（-1 は拒否）と対の内側の境界値。0 は「待たない」の意味で有効 |

#### 9-15 RF 待ち 1000（上限）は許可

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:962](../test/PcscIfTest/SelfTest.cs#L962) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Check`（`!Throws`） |
| 前提・入力 | `Options(8, 300000, 1000)` |
| 期待結果 | 例外が発生しない |
| 意図 | 9-05（1001 は拒否）と対の内側の境界値 |

#### 9-16 RF 待ち -1 は拒否

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:963](../test/PcscIfTest/SelfTest.cs#L963) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Throws<ArgumentOutOfRangeException>` |
| 前提・入力 | `Options(8, 300000, -1)` |
| 期待結果 | `ArgumentOutOfRangeException` が発生する |
| 意図 | 負の待ち時間を許すと、`Thread.Sleep(-1)`（無限待ち）で Read / Write が返らなくなる恐れがある。生成時点で拒否することを確認する |

#### 9-17 範囲外の例外は ArgumentOutOfRangeException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:965](../test/PcscIfTest/SelfTest.cs#L965) |
| 対象 | `PcscOptions.Validate` |
| 判定 | `Throws<ArgumentOutOfRangeException>` |
| 前提・入力 | `Options(1, 300000, 0)` |
| 期待結果 | `ArgumentOutOfRangeException` が発生する |
| 意図 | 9-01〜9-05 は例外の型を問わない `Throws` だった。README に記載した例外の型（`ArgumentOutOfRangeException`）と一致することを確認する |

#### 9-18 options に null を渡すと既定のラウンド数 8 で Polling する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:971](../test/PcscIfTest/SelfTest.cs#L971) |
| 対象 | `new Pcsc(null, factory)` |
| 判定 | `Check` |
| 前提・入力 | `new Pcsc(null, fake.Factory())` で接続し、カード 1 枚で `Read()` |
| 期待結果 | `Ok == true` かつ `Polls == 8` |
| 意図 | オプション省略時に、重ね置きチェックが既定のラウンド数で実際に動くことを確認する（1 枚のときは打ち切らないので `Polls` がそのままラウンド数） |

#### 9-19 RF 待ちを指定すると待ってから処理する（ElapsedMs >= 40）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:979](../test/PcscIfTest/SelfTest.cs#L979) |
| 対象 | `Pcsc.Execute` の RF 待ち、`PcscResult.ElapsedMs` |
| 判定 | `Check` |
| 前提・入力 | `Options(2, 300000, 50)` の `Pcsc` で `Read()` |
| 期待結果 | `Ok == true` かつ `ElapsedMs >= 40` |
| 意図 | 他のテストは RF 待ち 0 で動かしているため、待ち処理そのものは検証されていなかった。50ms 待つ設定で所要時間が伸びることを確認する。OS のタイマ精度を考慮して下限を 40ms にしている（上限は環境で変わるので見ない）。**待ち処理を消すとこのテストが失敗することを確認済み** |

#### 9-20 I/O 前の引数エラーの ElapsedMs は 0

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:981](../test/PcscIfTest/SelfTest.cs#L981) |
| 対象 | `PcscResult.ElapsedMs` |
| 判定 | `Check` |
| 前提・入力 | 接続済みの `Pcsc` で `Read(0, 0)` |
| 期待結果 | `ElapsedMs == 0` |
| 意図 | 引数エラーは計測を始める前（I/O の前）に返るので、所要時間は 0 になる。`InvalidArgument` が I/O を行っていないことの補助的な確認 |

#### 9-21 ToString（Ok）はステータス・IDm・SF・データ長・メッセージ・所要時間の順

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:988](../test/PcscIfTest/SelfTest.cs#L988) |
| 対象 | `PcscResult.ToString()` |
| 判定 | `Check` |
| 前提・入力 | カード 1 枚・メモリ先頭に `Payload(0x10, 16)` で `Read()` した結果 |
| 期待結果 | `"Ok idm=0100000000000001 SF=0000 len=16 (" + Message + ") " + ElapsedMs + "ms"` と完全一致 |
| 意図 | ログ（`[pcsc] ` + `ToString()`）の 1 行の書式を固定する。ログを集計・検索する運用があると、書式の変更がそのまま障害になるため |

#### 9-22 ToString は IDm・SF（FFFF）・データが無ければ省略する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:992](../test/PcscIfTest/SelfTest.cs#L992) |
| 対象 | `PcscResult.ToString()` |
| 判定 | `Check` |
| 前提・入力 | 未接続の `Pcsc` で `Read()` した結果（`NotConnected`） |
| 期待結果 | `"NotConnected (" + Message + ") " + ElapsedMs + "ms"` と完全一致 |
| 意図 | IDm が空、SF が未到達（`FF FF`）、データ長 0 の項目は出力しないことを確認する |

#### 9-23 ToString は SF の片方でも FF 以外なら表示する

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:996](../test/PcscIfTest/SelfTest.cs#L996) |
| 対象 | `PcscResult.ToString()` |
| 判定 | `Check` |
| 前提・入力 | `RejectRead` のカードで `Read()` した結果（`CardRejected`、SF=`FF A1`） |
| 期待結果 | `"CardRejected idm=0100000000000001 SF=FFA1 (" + Message + ") " + ElapsedMs + "ms"` と完全一致 |
| 意図 | SF の省略条件は「両方が `FF`」のときだけ。SF1 が未到達値と同じ `FF` でも、SF2 が実値なら表示されることを確認する（`||` と `&&` の取り違えを検出する） |

#### 9-24 失敗時の Data は null ではなく長さ 0、IDm 未確定なら空文字

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:999](../test/PcscIfTest/SelfTest.cs#L999) |
| 対象 | `PcscResult.Data` / `Idm` |
| 判定 | `Check` |
| 前提・入力 | 9-22 の結果 |
| 期待結果 | `Data != null`、`Data.Length == 0`、`Idm == ""` |
| 意図 | 呼び出し側が `result.Data.Length` や `result.Idm.Length` を null チェックなしで使えることを確認する |

---

## 10. 重ね置きの観測（DuplicateGuard / ScanObservation）（13 件）

メソッド `ObservationTests()`（[SelfTest.cs:1009](../test/PcscIfTest/SelfTest.cs#L1009)）。
対象は `DuplicateGuard.Collect` と `ScanObservation`（[L1_DuplicateGuard.cs](../src/L1_DuplicateGuard.cs)）です。

セクション 7 は `Pcsc` を通した**判定結果**（`MultipleCards` / `Indeterminate` など）を見ていました。
このセクションは `Pcsc` を通さずに観測ループを直接呼び、**何ラウンド Polling したか**と**どのフラグが立ったか**を確認します。
特に記載がない限り、ラウンド数 8・タイムアウト 300000us で呼びます。

`Collect` の 1 ラウンドの流れは次のとおりです。

```
TransceiveResult t = Polling
  t が null / フラグ数が 1 以外 / Unknown   → Unknown を立てて打ち切り
  Collision                               → Collision を立てて打ち切り
  NoResponse                              → NoResponse を立てて次のラウンドへ（打ち切らない）
  Ok: DO ごとに IDm を抽出
      抽出できない DO がある               → Unknown を立てて打ち切り
      IDm を集合に追加。2 種類以上になった → 打ち切り
```

#### 10-01 Collect(null) は ArgumentNullException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1014](../test/PcscIfTest/SelfTest.cs#L1014) |
| 対象 | `DuplicateGuard.Collect(IPcscTransport, int, int)` |
| 判定 | `Throws<ArgumentNullException>` |
| 前提・入力 | `transport = null` |
| 期待結果 | `ArgumentNullException` が発生する |
| 意図 | 公開メソッドとして直接呼ばれた場合に、ループの途中で `NullReferenceException` になるのではなく、入口で引数エラーにすることを確認する |

#### 10-02 1 枚なら全ラウンド Polling し、同じ IDm は 1 種類として数える

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1019](../test/PcscIfTest/SelfTest.cs#L1019) |
| 対象 | `DuplicateGuard.Collect` |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1)`（毎ラウンド ID 1 だけが応答） |
| 期待結果 | `Polls == 8`、`Count == 1`、`Unknown` / `Collision` / `NoResponse` がすべて `false` |
| 意図 | 1 枚しか見えなくても途中で打ち切らないこと、同じ IDm を 8 回観測しても 1 種類として数えることを確認する |

#### 10-03 SingleIdm は観測した IDm を返す

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1022](../test/PcscIfTest/SelfTest.cs#L1022) |
| 対象 | `ScanObservation.SingleIdm` |
| 判定 | `Check` |
| 前提・入力 | 10-02 の観測結果 |
| 期待結果 | `SingleIdm` が `Idm(1)`（`01 00 00 00 00 00 00 01`）と一致 |
| 意図 | `Pcsc` はこの値を読み書きコマンドの宛先に使う。観測した IDm のバイト列がそのまま返ることを確認する |

#### 10-04 1 応答で 2 種類を観測したら 1 ラウンドで打ち切る

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1026](../test/PcscIfTest/SelfTest.cs#L1026) |
| 対象 | `DuplicateGuard.Collect` の打ち切り条件 |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { SecondCard = 2 }` |
| 期待結果 | `Polls == 1` かつ `Count == 2` |
| 意図 | 2 種類観測した時点で判定（`MultipleCards`）は確定するので、残りのラウンドを省略することを確認する。実機で 2 枚重ねのときに 1 枚のときより速く返る理由（README §8.1）がこの動作。**打ち切りを消すとこのテストが失敗することを確認済み** |

#### 10-05 ラウンドをまたいで 2 種類になった時点で打ち切る

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1032](../test/PcscIfTest/SelfTest.cs#L1032) |
| 対象 | `DuplicateGuard.Collect` の打ち切り条件 |
| 判定 | `Check` |
| 前提・入力 | `Script(Ok(3), Ok(3), Ok(1), Ok(2))`（ID 3、ID 3、ID 1、ID 2 の順に応答） |
| 期待結果 | `Polls == 3` かつ `Count == 2` |
| 意図 | 3 ラウンド目で 2 種類目（ID 1）が見えた時点で止まり、4 ラウンド目（ID 2）を観測しないことを確認する。10-04 と違い、打ち切り判定がラウンドの終わりに毎回行われていることを見る |

#### 10-06 IdList は観測順ではなく昇順

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1034](../test/PcscIfTest/SelfTest.cs#L1034) |
| 対象 | `ScanObservation.IdList` |
| 判定 | `Check` |
| 前提・入力 | 10-05 の観測結果（観測順は ID 3 → ID 1） |
| 期待結果 | `IdList` が `["0100000000000001", "0100000000000003"]` |
| 意図 | `MultipleCards` のメッセージに並ぶ IDm の順序が、観測のタイミングに左右されず毎回同じになることを確認する（ログの比較がしやすい） |

#### 10-07 RF 衝突を観測したらそのラウンドで打ち切る

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1041](../test/PcscIfTest/SelfTest.cs#L1041) |
| 対象 | `DuplicateGuard.Collect` |
| 判定 | `Check` |
| 前提・入力 | `Script(Ok(1), Parse(Collision()))`（1 ラウンド目は ID 1、2 ラウンド目以降は衝突） |
| 期待結果 | `Polls == 2`、`Collision == true`、`Unknown == false`、`Count == 1` |
| 意図 | 衝突を観測したら、それ以上 Polling しても枚数は確定しないので打ち切ることを確認する。それまでに観測した IDm は残り、衝突は `Unknown` とは別のフラグで報告される |

#### 10-08 対象外の応答を観測したら Unknown で打ち切る（観測済みの IDm は残る）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1047](../test/PcscIfTest/SelfTest.cs#L1047) |
| 対象 | `DuplicateGuard.Collect` |
| 判定 | `Check` |
| 前提・入力 | `Script(Ok(1), Parse(Response(01 02 03)))`（2 ラウンド目は、成功応答だが中身が 3 バイトの不正なフレーム） |
| 期待結果 | `Polls == 2`、`Unknown == true`、`Count == 1` |
| 意図 | 抽出できないフレームを観測したら、読み飛ばして続けるのではなく打ち切ることを確認する |

#### 10-09 Transceive が null を返したら Unknown で打ち切る

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1053](../test/PcscIfTest/SelfTest.cs#L1053) |
| 対象 | `DuplicateGuard.Collect` の null 検査 |
| 判定 | `Check` |
| 前提・入力 | `Script(new TransceiveResult[] { null })`（1 ラウンド目に null） |
| 期待結果 | `Polls == 1`、`Unknown == true`、`Count == 0` |
| 意図 | 代替トランスポートの実装不備で null が返っても、`NullReferenceException` にならず、また「0 枚」とも扱わず、異常として打ち切ることを確認する |

#### 10-10 無応答では打ち切らず全ラウンド続ける

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1058](../test/PcscIfTest/SelfTest.cs#L1058) |
| 対象 | `DuplicateGuard.Collect` |
| 判定 | `Check` |
| 前提・入力 | `new FakeTransport(1) { NoCard = true }`（全ラウンドで「応答なし」） |
| 期待結果 | `Polls == 8`、`NoResponse == true`、`Unknown == false`、`Count == 0` |
| 意図 | 「応答なし」は異常ではなく、次のラウンドでカードが応答する可能性がある。打ち切らずに全ラウンド観測することを確認する |

#### 10-11 無応答の後に応答したカードも数える（NoResponse も残る）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1064](../test/PcscIfTest/SelfTest.cs#L1064) |
| 対象 | `DuplicateGuard.Collect` |
| 判定 | `Check` |
| 前提・入力 | ラウンド数 4、`Script(Empty(), Ok(1))`（1 ラウンド目は応答なし、以降は ID 1） |
| 期待結果 | `Polls == 4`、`Count == 1`、`NoResponse == true` |
| 意図 | 無応答の後に見えたカードも数えつつ、途中で無応答があった事実は消さないことを確認する。`Pcsc.Gate` はこの組み合わせ（1 種類 + 無応答あり）を「検査中に応答が途切れた」として `Indeterminate` にする（7-16） |

#### 10-12 SingleIdm は 0 種類なら InvalidOperationException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1067](../test/PcscIfTest/SelfTest.cs#L1067) |
| 対象 | `ScanObservation.SingleIdm` |
| 判定 | `Throws<InvalidOperationException>` |
| 前提・入力 | 新しい `ScanObservation`（何も観測していない） |
| 期待結果 | `InvalidOperationException` が発生する |
| 意図 | 宛先の IDm が確定していないのに値（null や全 0 など）を返すと、誤った宛先にコマンドを送る恐れがある。例外で拒否することを確認する |

#### 10-13 SingleIdm は 2 種類なら InvalidOperationException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1069](../test/PcscIfTest/SelfTest.cs#L1069) |
| 対象 | `ScanObservation.SingleIdm` |
| 判定 | `Throws<InvalidOperationException>` |
| 前提・入力 | 10-04 の観測結果（2 種類） |
| 期待結果 | `InvalidOperationException` が発生する |
| 意図 | 重ね置き時に「どちらか 1 枚」を返さないことを確認する。`Pcsc.Gate` の判定を迂回して `SingleIdm` を使うコードがあっても、宛先を選べない |

---

## 11. 並行呼び出し（5 件）

メソッド `ConcurrencyTests()`（[SelfTest.cs:1081](../test/PcscIfTest/SelfTest.cs#L1081)）。
対象は `Pcsc` の内部 `lock`（[L1_Pcsc.cs](../src/L1_Pcsc.cs)）です。

`Pcsc` は Connect / Disconnect / Read / Write をすべて同じロックで直列化しています。
同じインスタンスを複数スレッドから呼んでも、1 回の Read / Write（重ね置きチェック → 読み書き → 後始末）が
別の呼び出しと混ざらないはずです。

| 項目 | 値 |
|---|---|
| スレッド数 | 4 |
| 1 スレッドあたりの呼び出し回数 | 25（合計 100 回） |
| 模擬トランスポート | `YieldInSession = true`（セッション開始と `Transceive` のたびに `Thread.Sleep(0)` でスレッドを譲る） |
| 重なりの検出 | `FakeTransport` がセッション開始時に、他のセッションが開いていれば `Overlap = true` にする |

**`Pcsc.Execute` の `lock` を外すと、このセクションの 5 件中 4 件（11-01・11-03・11-04・11-05）が失敗することを確認済み**です。

#### 11-01 複数スレッドから同時に Read してもセッションは重ならない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1102](../test/PcscIfTest/SelfTest.cs#L1102) |
| 対象 | `Pcsc.Execute` の `lock` |
| 判定 | `Check` |
| 前提・入力 | 接続済みの 1 つの `Pcsc` に対し、4 スレッドから `Read()` を 25 回ずつ呼ぶ |
| 期待結果 | `Overlap == false` |
| 意図 | セッションが重なると、あるスレッドの重ね置きチェックと別スレッドの読み書きが同じ RF セッションに混ざり、「検査した直後に同じセッション内で読み書きする」という安全性の前提が崩れる。重なりが一度も起きないことを確認する |

#### 11-02 同時 Read はすべて Ok

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1103](../test/PcscIfTest/SelfTest.cs#L1103) |
| 対象 | `Pcsc.Read()` |
| 判定 | `Check` |
| 前提・入力 | 11-01 と同じ |
| 期待結果 | `Ok` の件数 == 100 |
| 意図 | 直列化によって呼び出しが失敗（`Indeterminate` など）に化けないことを確認する |

#### 11-03 同時 Read でもコマンドの取りこぼしがない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1104](../test/PcscIfTest/SelfTest.cs#L1104) |
| 対象 | `Pcsc.Execute` の手順 |
| 判定 | `Check` |
| 前提・入力 | 11-01 の実行後 |
| 期待結果 | `StartCalls == 100`、`EndCalls == 100`、`Reads == 100`、`Polls == 200`（ラウンド 2 × 100 回） |
| 意図 | 100 回の呼び出しすべてで、セッション開始・終了、Polling、Read が過不足なく行われたことを確認する。直列化されていないと、模擬側のカウンタの加算が競合して数が合わなくなる |

#### 11-04 Read と Write を同時に呼んでもセッションは重ならない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1130](../test/PcscIfTest/SelfTest.cs#L1130) |
| 対象 | `Pcsc.Execute` の `lock` |
| 判定 | `Check` |
| 前提・入力 | 別の `Pcsc` で、スレッド 0 と 2 は `Write`（それぞれ `Payload(0x10, 32)` と `Payload(0x30, 32)`）、スレッド 1 と 3 は `Read(0, 2)` を 25 回ずつ呼ぶ |
| 期待結果 | `Overlap == false` |
| 意図 | Read と Write が混在しても、同じロックで直列化されていることを確認する |

#### 11-05 Read と Write を同時に呼んでも、読み戻しを含めてすべて Ok

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1131](../test/PcscIfTest/SelfTest.cs#L1131) |
| 対象 | `Pcsc.Write` の書き込み〜読み戻し |
| 判定 | `Check` |
| 前提・入力 | 11-04 と同じ |
| 期待結果 | `Ok` の件数 == 100 |
| 意図 | 書き込み・読み戻しの間に別スレッドの書き込みが割り込むと、読み戻しが一致せず `Indeterminate` になる。2 つのスレッドが異なる内容を書いていても全件 `Ok` になることで、Write が書き込みから読み戻しまで途切れずに実行されていることを確認する |

---

## 12. 16 進変換（Hex）（16 件）

メソッド `HexTests()`（[SelfTest.cs:1138](../test/PcscIfTest/SelfTest.cs#L1138)）。
対象は `Hex`（[Common_Hex.cs](../src/Common_Hex.cs)）です。
`Hex` はログ出力、IDm の文字列化（`Felica.IdmToString`）、読み戻しの比較（`Pcsc.DoWrite`）、
テスト用 CLI の `write` コマンドでの入力解析に使われています。
以下、`sample = 0A FF 00` です。

#### 12-01 ToString は大文字 2 桁を空白区切りで出力

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1142](../test/PcscIfTest/SelfTest.cs#L1142) |
| 対象 | `Hex.ToString(byte[])` |
| 判定 | `Check` |
| 前提・入力 | `sample` |
| 期待結果 | `"0A FF 00"` |
| 意図 | ログに出るバイト列の書式（大文字・0 埋め 2 桁・空白 1 個区切り・末尾に空白なし）を固定する |

#### 12-02 空配列の ToString は空文字

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1143](../test/PcscIfTest/SelfTest.cs#L1143) |
| 対象 | `Hex.ToString(byte[])` |
| 判定 | `Check` |
| 前提・入力 | 長さ 0 の配列 |
| 期待結果 | `""` |
| 意図 | 空の応答をログに出すときに例外にならず、区切りの空白も出ないことを確認する |

#### 12-03 ToString(null) は "(null)"

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1144](../test/PcscIfTest/SelfTest.cs#L1144) |
| 対象 | `Hex.ToString(byte[])` / `Hex.ToString(byte[], int, bool)` |
| 判定 | `Check` |
| 前提・入力 | `null`（1 引数版と 3 引数版の両方） |
| 期待結果 | どちらも `"(null)"` |
| 意図 | 異常時のログ出力で null を渡しても例外にならず、空配列（`""`）と区別できる表示になることを確認する |

#### 12-04 長さ指定・区切りなしの ToString

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1146](../test/PcscIfTest/SelfTest.cs#L1146) |
| 対象 | `Hex.ToString(byte[], int length, bool separated)` |
| 判定 | `Check` |
| 前提・入力 | `sample`、`length = 2`、`separated = false` |
| 期待結果 | `"0AFF"` |
| 意図 | 先頭から指定バイト数だけを、区切りなしで出力することを確認する（IDm の文字列化はこの形式） |

#### 12-05 範囲外の長さ指定は全体を出力

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1147](../test/PcscIfTest/SelfTest.cs#L1147) |
| 対象 | `Hex.ToString(byte[], int, bool)` |
| 判定 | `Check` |
| 前提・入力 | `sample` に対し `length = 5`（配列より長い）と `length = -1`（負） |
| 期待結果 | どちらも `"0A FF 00"` |
| 意図 | ログ出力の補助なので、長さの指定ミスで例外を投げず、配列全体の出力に丸めることを確認する |

#### 12-06 Parse は大文字・小文字と空白を受け付ける

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1150](../test/PcscIfTest/SelfTest.cs#L1150) |
| 対象 | `Hex.Parse(string)` |
| 判定 | `Check` |
| 前提・入力 | `"0A ff 00"` |
| 期待結果 | `sample` と一致 |
| 意図 | `ToString` の出力をそのまま貼り付けた入力や、小文字の入力を受け付けることを確認する |

#### 12-07 Parse はハイフン・コロン区切りを受け付ける

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1151](../test/PcscIfTest/SelfTest.cs#L1151) |
| 対象 | `Hex.Parse(string)` |
| 判定 | `Check` |
| 前提・入力 | `"0A-FF:00"` |
| 期待結果 | `sample` と一致 |
| 意図 | 他のツールがよく使う `-` / `:` 区切りの入力を受け付けることを確認する |

#### 12-08 Parse は先頭の 0x / 0X を受け付ける

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1152](../test/PcscIfTest/SelfTest.cs#L1152) |
| 対象 | `Hex.Parse(string)` |
| 判定 | `Check` |
| 前提・入力 | `"0x0AFF00"` と `"0X0aff00"` |
| 期待結果 | どちらも `sample` と一致 |
| 意図 | 先頭の `0x` 接頭辞を大文字・小文字を問わず取り除くことを確認する（接頭辞として扱うのは先頭の 1 個だけ） |

#### 12-09 Parse("") は長さ 0

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1154](../test/PcscIfTest/SelfTest.cs#L1154) |
| 対象 | `Hex.Parse(string)` |
| 判定 | `Check` |
| 前提・入力 | `""` |
| 期待結果 | 長さ 0 の配列 |
| 意図 | 空文字を例外にせず空の配列にすることを確認する（空データの書き込みは `Pcsc.Write` 側が `InvalidArgument` で拒否する） |

#### 12-10 奇数桁は ArgumentException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1155](../test/PcscIfTest/SelfTest.cs#L1155) |
| 対象 | `Hex.Parse(string)` |
| 判定 | `ThrowsArgument` |
| 前提・入力 | `"ABC"` |
| 期待結果 | `ArgumentException` が発生する |
| 意図 | 1 文字足りない入力を、末尾を捨てたり先頭に 0 を補ったりして別のバイト列に解釈しないことを確認する。カードに書く値を扱うので、推測で補完しない |

#### 12-11 16 進以外の文字は ArgumentException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1156](../test/PcscIfTest/SelfTest.cs#L1156) |
| 対象 | `Hex.Parse(string)` |
| 判定 | `ThrowsArgument` |
| 前提・入力 | `"0G"` |
| 期待結果 | `ArgumentException` が発生する |
| 意図 | 16 進として解釈できない文字を 0 などに置き換えず、入力エラーにすることを確認する |

#### 12-12 Parse(null) は ArgumentNullException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1157](../test/PcscIfTest/SelfTest.cs#L1157) |
| 対象 | `Hex.Parse(string)` |
| 判定 | `Throws<ArgumentNullException>` |
| 前提・入力 | `null` |
| 期待結果 | `ArgumentNullException` が発生する |
| 意図 | `NullReferenceException` ではなく、引数エラーとして原因が分かる例外にすることを確認する |

#### 12-13 全 256 値で ToString と Parse が往復一致

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1160](../test/PcscIfTest/SelfTest.cs#L1160) |
| 対象 | `Hex.ToString` と `Hex.Parse` の組み合わせ |
| 判定 | `Check` |
| 前提・入力 | `Payload(0, 256)`（`00` から `FF` までの全値） |
| 期待結果 | `Parse(ToString(data))` が `data` と一致 |
| 意図 | すべてのバイト値で、ログに出した文字列から元のバイト列を復元できることを確認する（大文字・小文字や 0 埋めの扱いの誤りを網羅的に検出する） |

#### 12-14 SequenceEqual は内容が同じなら true

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1162](../test/PcscIfTest/SelfTest.cs#L1162) |
| 対象 | `Hex.SequenceEqual(byte[], byte[])` |
| 判定 | `Check` |
| 前提・入力 | `01 02` と `01 02`（別インスタンス） |
| 期待結果 | `true` |
| 意図 | 参照ではなく内容で比較することを確認する。書き込み後の読み戻しの一致判定に使われる |

#### 12-15 SequenceEqual は長さや内容が違えば false

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1164](../test/PcscIfTest/SelfTest.cs#L1164) |
| 対象 | `Hex.SequenceEqual` |
| 判定 | `Check` |
| 前提・入力 | `01 02` と `01 02 03`（先頭は同じで長さが違う）、`01 02` と `01 03`（長さは同じで内容が違う） |
| 期待結果 | どちらも `false` |
| 意図 | 短い方の長さまでしか比べない実装（読み戻しが途中までしか一致していないのに一致と判定する）を検出する |

#### 12-16 SequenceEqual は null を含めば null 同士でも false（fail-closed）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1167](../test/PcscIfTest/SelfTest.cs#L1167) |
| 対象 | `Hex.SequenceEqual` |
| 判定 | `Check` |
| 前提・入力 | `(null, null)` と `(null, 長さ 0 の配列)` |
| 期待結果 | どちらも `false` |
| 意図 | 読み戻しのデータが得られなかった（null）場合に、「一致した」と判定しないことを確認する。一般的な比較関数と違い、null 同士も不一致とする安全側の仕様。**null 同士を一致扱いに変えるとこのテストが失敗することを確認済み** |

---

## 13. 実機トランスポートのリーダ不要な検査（11 件）

メソッド `OfflineTransportTests()`（[SelfTest.cs:1178](../test/PcscIfTest/SelfTest.cs#L1178)）。
対象は `Rcs300Transport`（[L4_PcscTransport.cs](../src/L4_PcscTransport.cs)）、`Interop` と `PcscException`（[L5_PcscNative.cs](../src/L5_PcscNative.cs)）です。

実機トランスポートは winscard.dll を呼ぶため、ほとんどの処理はリーダが無いと検証できません。
このセクションでは、**winscard を呼ぶ前に決まる振る舞い**（引数検証、未オープン時の拒否、後始末の安全性、定数）だけを確認します。
`Interop` の型の初期化は定数の計算だけで、winscard.dll を読み込みません。

最後の 13-11 だけは OS の PC/SC 層を呼びます。存在しないリーダ名に接続するので、次のどの環境でも必ず失敗し、結果は変わりません。

| 環境 | 失敗の仕方の例 |
|---|---|
| リーダがある / 無い Windows | `SCardConnect` が未知のリーダ名として失敗する |
| スマートカードサービスが停止している Windows | `SCardEstablishContext` が失敗する |
| winscard.dll が無い環境 | `DllNotFoundException` |

#### 13-01 Open(null) は ArgumentException（winscard を呼ばない）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1180](../test/PcscIfTest/SelfTest.cs#L1180) |
| 対象 | `Rcs300Transport.Open(string)` |
| 判定 | `ThrowsArgument` |
| 前提・入力 | `new Rcs300Transport().Open(null)` |
| 期待結果 | `ArgumentException` が発生する |
| 意図 | リーダ名が無い場合は winscard を呼ぶ前に引数エラーにすることを確認する。null をそのまま `SCardConnect` に渡すと、ドライバ側で予期しない動作になる恐れがある |

#### 13-02 Open(空白のみ) は ArgumentException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1182](../test/PcscIfTest/SelfTest.cs#L1182) |
| 対象 | `Rcs300Transport.Open(string)` |
| 判定 | `ThrowsArgument` |
| 前提・入力 | `Open("   ")` |
| 期待結果 | `ArgumentException` が発生する |
| 意図 | 空白だけのリーダ名も「名前が無い」として拒否することを確認する（`Pcsc.Connect` は空白だけの名前を「最初のリーダ」と解釈するので、トランスポートまで届くのは異常な使い方のときだけ） |

#### 13-03 対象外プロトコルへの切替は NotSupportedException（送信前に拒否）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1184](../test/PcscIfTest/SelfTest.cs#L1184) |
| 対象 | `Rcs300Transport.SwitchProtocol(RfProtocol)` |
| 判定 | `Throws<NotSupportedException>` |
| 前提・入力 | 未オープンの `Rcs300Transport` で `SwitchProtocol(RfProtocol.TypeA)` |
| 期待結果 | `NotSupportedException` が発生する（未オープンによる `InvalidOperationException` ではない） |
| 意図 | 対象カード以外のプロトコルを指定した場合は、リーダへの送信を試みる前に拒否することを確認する。例外の型で「送信前の検査で弾かれた」ことが分かる |

#### 13-04 未オープンでのセッション開始は InvalidOperationException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1186](../test/PcscIfTest/SelfTest.cs#L1186) |
| 対象 | `Rcs300Transport.StartTransparentSession()` |
| 判定 | `Throws<InvalidOperationException>` |
| 前提・入力 | `Open` していない `Rcs300Transport` |
| 期待結果 | `InvalidOperationException` が発生する |
| 意図 | カードハンドルが無いまま `SCardControl` を呼ばず、状態の誤りとして例外にすることを確認する |

#### 13-05 未オープンでの RF ON / OFF は InvalidOperationException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1188](../test/PcscIfTest/SelfTest.cs#L1188) |
| 対象 | `Rcs300Transport.TurnOnRf()` / `TurnOffRf()` |
| 判定 | `Throws<InvalidOperationException>`（2 回） |
| 前提・入力 | それぞれ別の未オープンの `Rcs300Transport` |
| 期待結果 | どちらも `InvalidOperationException` が発生する |
| 意図 | 13-04 と同じ。RF 制御でもハンドルの無い送信を行わないことを確認する |

#### 13-06 未オープンでの Transceive は InvalidOperationException

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1191](../test/PcscIfTest/SelfTest.cs#L1191) |
| 対象 | `Rcs300Transport.Transceive(byte[], int)` |
| 判定 | `Throws<InvalidOperationException>` |
| 前提・入力 | 未オープンの `Rcs300Transport` で `Transceive(Felica.BuildPolling(), 300000)` |
| 期待結果 | `InvalidOperationException` が発生する |
| 意図 | `Transceive` は送信失敗（winscard の戻り値エラー）を例外ではなく `Unknown` の結果で返すが、未オープンという呼び出し側の誤りは例外にすることを確認する。「カードが応答しなかった」と誤解される結果を返さない |

#### 13-07 未オープンの Dispose は例外を投げない

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1194](../test/PcscIfTest/SelfTest.cs#L1194) |
| 対象 | `Rcs300Transport.Dispose()` |
| 判定 | `Check`（`!Throws`） |
| 前提・入力 | 生成しただけの `Rcs300Transport` |
| 期待結果 | 例外が発生しない |
| 意図 | `Open` 失敗時の後始末（`Pcsc.Connect` と `Rcs300Transport.Open` の両方が `Dispose` を呼ぶ）で例外が起きないことを確認する |

#### 13-08 送信失敗の後の Dispose も例外を投げず、2 回呼んでもよい

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1196](../test/PcscIfTest/SelfTest.cs#L1196) |
| 対象 | `Rcs300Transport.Dispose()` の復旧処理 |
| 判定 | `Check`（`!Throws`） |
| 前提・入力 | 未オープンで `TurnOnRf()` と `StartTransparentSession()` を呼んで失敗させた後、`Dispose()` を 2 回 |
| 期待結果 | 例外が発生しない |
| 意図 | `TurnOnRf` / `StartTransparentSession` は送信前に「RF ON 中」「セッション中」のフラグを立てるので、`Dispose` は復旧として RF OFF とセッション終了を試み、それが失敗する。その失敗を握りつぶして破棄を完了すること、2 回目の破棄も安全であることを確認する |

#### 13-09 Escape 制御コードの算出（SCARD_CTL_CODE）

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1205](../test/PcscIfTest/SelfTest.cs#L1205) |
| 対象 | `Interop.ScardCtlCode(uint)` と定数 |
| 判定 | `Check` |
| 前提・入力 | なし |
| 期待結果 | `ScardCtlCode(3500) == 0x003136B0`、`IOCTL_CCID_ESCAPE_FALLBACK == 0x003136B0`、`CM_IOCTL_GET_FEATURE_REQUEST == 0x00313520` |
| 意図 | `SCARD_CTL_CODE(code) = (0x31 << 16) | (code << 2)` の計算と、実機で使う 2 つの制御コードの値を固定する。値が 1 ビットでも違うと Escape コマンドがリーダに届かない |

#### 13-10 PcscException は戻り値を ReturnCode と 16 進のメッセージで保持

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1210](../test/PcscIfTest/SelfTest.cs#L1210) |
| 対象 | `PcscException` |
| 判定 | `Check` |
| 前提・入力 | `new PcscException("SCardConnect", unchecked((int)0x8010000C))` |
| 期待結果 | `ReturnCode == unchecked((int)0x8010000C)`、`Message == "SCardConnect (0x8010000C)"` |
| 意図 | winscard の戻り値（負の int になる `0x8010xxxx`）を、符号付きのまま保持し、メッセージには 8 桁の 16 進で出すことを確認する。運用時にログのエラーコードを PC/SC のドキュメントと照合できる |

#### 13-11 既定トランスポートで存在しないリーダ名への Connect は ReaderError

| 項目 | 内容 |
|---|---|
| ソース | [SelfTest.cs:1217](../test/PcscIfTest/SelfTest.cs#L1217) |
| 対象 | `new Pcsc(options)`（実機トランスポート）→ `Connect` → `Rcs300Transport.Open` |
| 判定 | `Check` |
| 前提・入力 | `new Pcsc(FakeOptions())` で `Connect("PcscIfTest: no such reader")` |
| 期待結果 | `Status == ReaderError`、`Error != null`、`IsConnected == false` |
| 意図 | このテストで唯一、実機トランスポートの `Open` から OS の PC/SC 層まで実際に通る。winscard の失敗（または dll が無いことによる例外）が `Rcs300Transport.Open` → `Pcsc.Connect` と伝わり、例外を外に出さず `ReaderError` と原因例外に変換されることを確認する |

---

## 14. 集計とトレーサビリティ

### 14.1 セクション別件数

| # | セクション | 件数 | うち追加分 | ID |
|---|---|---:|---:|---|
| 1 | コマンド組み立ての既知ベクタ | 13 | 2 | 1-01〜1-13 |
| 2 | PC/SC 応答パーサ（fail-closed） | 45 | 17 | 2-01〜2-45 |
| 3 | 対象カードの Polling 応答の抽出 | 8 | 0 | 3-01〜3-08 |
| 4 | ユーザ領域の境界チェック | 17 | 0 | 4-01〜4-17 |
| 5 | 読み書き応答の解析 | 22 | 0 | 5-01〜5-22 |
| 6 | Connect / Disconnect | 23 | 8 | 6-01〜6-23 |
| 7 | Read と重ね置きチェック | 37 | 6 | 7-01〜7-37 |
| 8 | Write と重ね置きチェック | 45 | 13 | 8-01〜8-45 |
| 9 | オプション検証とログ | 24 | 16 | 9-01〜9-24 |
| 10 | 重ね置きの観測（DuplicateGuard / ScanObservation） | 13 | 13 | 10-01〜10-13 |
| 11 | 並行呼び出し | 5 | 5 | 11-01〜11-05 |
| 12 | 16 進変換（Hex） | 16 | 16 | 12-01〜12-16 |
| 13 | 実機トランスポートのリーダ不要な検査 | 11 | 11 | 13-01〜13-11 |
| | **合計** | **279** | **107** | |

### 14.2 判定ヘルパ別件数

`!Throws(...)`（例外が起きないこと）を条件にしているものは `Check` に数えています。

| 判定 | 件数 |
|---|---:|
| `Check`（条件式） | 229 |
| `Expect`（バイト列一致） | 6 |
| `Throws`（何らかの例外） | 27 |
| `ThrowsArgument`（引数例外に限定） | 7 |
| `Throws<T>`（指定した型の例外に限定） | 10 |
| **合計** | **279** |

### 14.3 対象コード → テスト

| 対象 | ファイル | テスト ID |
|---|---|---|
| `Felica.BuildPolling` | L2 | 1-01〜1-04 |
| `Felica.TryExtractLiteSIdm` | L2 | 3-01〜3-06 |
| `Felica.IdmToString` | L2 | 3-07、3-08 |
| `FelicaLiteS` の定数 | L2 | 1-11 |
| `FelicaLiteS.BuildRead` | L2 | 1-09、4-01、4-03、4-04、4-06、4-07、4-11、4-12 |
| `FelicaLiteS.BuildWrite` | L2 | 1-10、4-02、4-05、4-08 |
| `FelicaLiteS.BlockRange` | L2 | 4-09、4-10、4-13〜4-17 |
| `FelicaLiteS.TryParseReadResponse` | L2 | 5-01〜5-07 |
| `FelicaLiteS.TryParseWriteResponse` | L2 | 5-08〜5-11、8-15 |
| `FelicaLiteS.ParseReadResponse` | L2 | 5-12〜5-17 |
| `FelicaLiteS.ParseWriteResponse` | L2 | 5-18〜5-22 |
| `PcscResponse.Parse` / `TransceiveResult.Parse` | L3 | 2-01〜2-24、2-29〜2-42 |
| `PcscResponse.CommandSucceeded` | L3 | 2-25 |
| `Rcs300Transport.BuildFelicaSwitchApdu` | L4 | 1-05 |
| `Rcs300Transport.BuildTransceiveApdu` | L4 | 1-06〜1-08、1-12、1-13 |
| `Rcs300Transport.ValidateManagementResponse` | L4 | 2-26〜2-28、2-43〜2-45 |
| `Rcs300Transport` の `Open` / `SwitchProtocol` / セッション・RF 制御 / `Transceive` / `Dispose`（リーダ不要な部分） | L4 | 13-01〜13-08、13-11 |
| `Interop.ScardCtlCode` と制御コードの定数 | L5 | 13-09 |
| `PcscException` | L5 | 6-15、13-10 |
| `Pcsc` のコンストラクタ（既定値・null オプション） | L1 | 6-21、6-22、9-18 |
| `Pcsc.Connect` / `Disconnect` / `IsConnected` / `ReaderName` / `Dispose` | L1 | 6-01〜6-23、13-11 |
| `Pcsc.Execute`（RF・セッション手順、RF 待ち、例外時の後始末、`lock`） | L1 | 6-07、7-04、7-18、7-19、7-35〜7-37、9-19、11-01〜11-05 |
| `DuplicateGuard.Collect` | L1 | 7-03、7-07、7-10〜7-17、8-07、8-09、10-01〜10-11 |
| `ScanObservation`（`Count` / `IdList` / `SingleIdm`） | L1 | 10-02〜10-06、10-12、10-13 |
| `Pcsc.Gate` | L1 | 7-07〜7-17、8-07〜8-09 |
| `Pcsc.Read`（引数検証） | L1 | 7-25〜7-31 |
| `Pcsc.DoRead` / `TrySingleFrame` | L1 | 7-01、7-02、7-05、7-06、7-20〜7-24、7-29、7-32〜7-34 |
| `Pcsc.Write`（引数検証・入力の複製） | L1 | 8-17〜8-22 |
| `Pcsc.DoWrite` / `TrySingleFrame` | L1 | 8-01〜8-06、8-10〜8-14、8-16、8-23〜8-45 |
| `PcscOptions` の既定値 | L1 | 9-09 |
| `PcscOptions.Validate` | L1 | 9-01〜9-05、9-10〜9-17 |
| `PcscOptions.Snapshot` | L1 | 9-06 |
| `PcscOptions.VerifyAfterWrite` | L1 | 8-16、8-45 |
| `PcscResult`（`ToString` / `ElapsedMs` / `Data` / `Idm`） | L1 | 9-19〜9-24 |
| `Pcsc.Log` | L1 | 9-07、9-08 |
| `Hex`（`ToString` / `Parse` / `SequenceEqual`） | 共通 | 12-01〜12-16 |
| `FakeTransport`（テストの前提） | test | 8-15 |

### 14.4 `PcscStatus` の網羅

9 種類すべてのステータスについて、それを返す経路を最低 1 件は確認しています。

| ステータス | 確認しているテスト ID |
|---|---|
| `Ok` | 6-04、6-09、6-11、6-20、7-01、7-06、7-29、8-01、8-04、8-16、8-22、8-23、8-26、8-29、9-18、9-19、9-21、11-02、11-05 ほか |
| `InvalidArgument` | 7-25〜7-28、7-30、8-17〜8-21、9-20 |
| `NotConnected` | 6-01、6-02、6-12、6-21、9-22 |
| `AlreadyConnected` | 6-08 |
| `ReaderError` | 6-13、6-16、6-18、13-11 |
| `NoCard` | 7-12、8-09、9-07 |
| `MultipleCards` | 7-07、7-10、7-11、8-07 |
| `Indeterminate` | 7-13〜7-19、7-21、7-24、7-32〜7-34、8-11、8-14、8-31、8-32、8-38、8-40、8-42、8-43、8-45 |
| `CardRejected` | 7-20、8-10、8-33、8-37、9-23 |

### 14.5 ライブラリを意図的に壊して確認した結果（変異テスト）

追加したテストが「対象の処理を消したら本当に失敗するか」を、ライブラリのコピーを 1 か所ずつ書き換えて確認しました。
書き換えは作業用のコピーにだけ行い、`src/` は変更していません。

| # | 書き換え内容 | 結果 |
|---|---|---|
| M01 | `Pcsc.Execute` の `lock` を外す | 11-01・11-03・11-04・11-05 が失敗（検出） |
| M02 | `DuplicateGuard.Collect` の「2 種類で打ち切り」を外す | 10-04・10-05・10-06 が失敗（検出） |
| M03 | `Pcsc.Execute` の例外時の後始末（RF OFF・セッション終了）を外す | 7-35・7-37 が失敗（検出） |
| M07 | `Hex.SequenceEqual` で null 同士を一致扱いにする | 12-16 が失敗（検出） |
| M08 | `TrySingleFrame` のフレーム数検査を外す | 当初は 7-33 だけが失敗し、**8-45 は読み戻し不一致に助けられて合格していた**。8-45 を読み戻し無効で判定するよう直した後は、7-33・8-45 が失敗（検出） |
| M10 | RF 待ちの `Thread.Sleep` を外す | 9-19 が失敗（検出） |
| M13 | DO96 の予約ビット（上位 4 ビット）の検査を外す | 当初は**全件合格（2-37 が空振り）**。2-37 を衝突ビットと同時に立てる条件に直した後は、2-37 が失敗（検出） |
| M04 | 「先行ブロックは書き換わっている」の注記を外す | 未実行（想定: 8-34・8-39 が失敗） |
| M05 | `Disconnect` で内部参照を手放すのを `Dispose` 成功後にする | 未実行（想定: 6-19 が失敗） |
| M06 | 長さ `81` 形式の解釈を無効にする | 未実行（想定: 2-29 が失敗） |
| M09 | `RfSettleDelayMs` の下限検査を外す | 未実行（想定: 9-16 が失敗） |
| M11 | ファクトリが null を返したときの検査を外す | 未実行（想定: 6-16 が失敗） |
| M12 | `Collect` の null 検査を外す | 未実行（想定: 10-01 が失敗） |
| M14 | C0 が `9000` のときの先頭バイト検査を外す | 未実行（想定: 2-39 が失敗） |

「未実行」の 7 件は、書き換えてビルドした DLL の読み込みを端末のアプリケーション制御ポリシーが拒否したため、実行できませんでした。
「想定」はソースから読み取った期待で、実行による確認ではありません。

---

## 15. 現在のテストで確認していないこと

### 15.1 リーダ（winscard.dll）が必要なため自己テストの対象外

自己テスト（SelfTest）ではリーダを使わないので、次の項目は検証できません。§16 の実機テストでの確認状況を併記します。

| 項目 | 自己テストで扱えない理由 | 実機テスト（§16） |
|---|---|---|
| `Rcs300Transport` の実際の送受信（`SCardConnect` 成功後の `SCardControl`、`SendApduRaw`） | 13 はリーダを使わずに決まる振る舞いだけを検証している | **正常系は確認済み**（L・W・N・S・X の全試行で、セッション開始〜Polling〜読み書き〜後始末が動作） |
| `Transceive` が winscard のエラーを `Unknown` にする経路、`SendApduRaw` の返却長超過 | 同上 | 未確認（winscard がエラーを返す状況を作れない） |
| `Interop.TryGetEscapeControlCode` の応答 TLV 解析 | `SCardControl` の呼び出しと解析が 1 つのメソッドにまとまっている | 一部確認（Escape コマンドは動作した。機能一覧から取得したのか、フォールバック値を使ったのかは外から区別できない） |
| `Interop.ListReaders` / `Pcsc.ListReaders`（マルチ文字列の分解） | 同上 | 一部確認（L-01・N-05・S-04 でリーダ 1 台を列挙。複数台の分解は未確認） |
| `Connect()` / `Connect(null)` のリーダ自動選択 | リーダが接続されている環境では実機に接続してしまい、結果が環境に依存する | 一部確認（L-02・N-01・S-01。リーダが 1 台だけなので「最初の 1 台を選ぶ」ことの確認に限られる） |
| 存在しないリーダ名への `SCardConnect` の失敗 | — | 確認済み（L-07、`0x80100009`） |
| `Rcs300Transport.Open` がコンテキスト取得後に失敗したときの解放 | 13-11 で経路は通るが、解放されたことは外から確認できない | 未確認（同じ理由） |
| `SCardDisconnect` / `SCardReleaseContext` の失敗 | ソースに記載の既知の制限（戻り値を確認していない） | 未確認 |
| 実機トランスポートでの `new Pcsc(options)` による Read / Write | — | 確認済み（`PcscIfTest` は `new Pcsc(options)` を使う。引数なしの `new Pcsc()` はオプション以外同じ経路） |
| カードが拒否した場合の `CardRejected`、複数ブロックの途中での失敗 | — | 未確認（カードにその状態を作る手段が無い。模擬では 7-20・8-10・8-33〜8-39 で確認済み） |

### 15.2 模擬で確認できるが、現状テストが無い経路

| 経路 | 該当コード |
|---|---|
| `TransceiveTimeoutUs` が実際に `Transceive` に渡されていること | `Pcsc.DoRead` / `DoWrite` / `DuplicateGuard.Collect`（`FakeTransport` がタイムアウト値を記録していない） |
| RF 待ちが RF ON の**後**に入っていること（順序） | `Pcsc.Execute`（9-19 は所要時間だけを見ている） |
| `Transceive` が例外を投げた場合の後始末 | `Pcsc.Execute`（切替・セッション終了の失敗と同じ `catch` を通るが、`Transceive` からの例外は模擬していない） |
| IDm 1 種類を観測した後に異常応答で打ち切られた場合の `Pcsc` の判定 | `Pcsc.Gate`（観測ループ単体は 10-08 で確認済み） |
| Connect / Disconnect と Read / Write を同時に呼ぶ並行シナリオ | `Pcsc`（11 は Read / Write の組み合わせだけ） |
| `Hex.Parse` で先頭以外にある `0x`（例: `"0x0A 0xFF"`） | `Hex.Parse`（現状は例外になる。仕様として決めていない） |

---

## 16. 実機テストの結果（2026-09-16）

自己テストでは確認できないリーダ・カードとの実際の送受信を、テスト用 CLI（`PcscIfTest`、[source/PcscIfTest_Program.md](source/PcscIfTest_Program.md)）で確認しました。
**全 60 項目が期待どおり**でした（L 8・P / W 16・N 8・S 8・B / X / V 20）。

### 16.1 環境と進め方

| 項目 | 内容 |
|---|---|
| 日付 | 2026-09-16 |
| OS | Windows 11 |
| リーダ | 対象リーダ 1 台（接続中のリーダはこの 1 台だけ） |
| カード | 対象カード 2 枚。本書では「1 枚目」「2 枚目」と呼び、**IDm は記載しない** |
| 実行ファイル | `src/*.cs` と `test/PcscIfTest/*.cs` を C# 5 コンパイラ（csc 4.8）でビルドした `PcscIfTest`（.NET Framework 4.8 ランタイム）。.NET 10 のビルドは検証端末のアプリケーション制御ポリシーにより起動できなかった。ソースは同一 |
| オプション | すべて既定値（`DuplicateCheckRounds=8`、`TransceiveTimeoutUs=300000`、`RfSettleDelayMs=20`、`VerifyAfterWrite=true`） |
| テスト前のカードの内容 | 1 枚目: ブロック 0〜7 と 13 がすべて `00`（P-01〜P-03 で確認）。2 枚目: 14 ブロックすべて `00`（B-01 で確認） |

- 判定は、各コマンドの終了コードと結果行（`Connect =>` / `Read =>` / `Write =>` / `Disconnect =>`）で行いました。
- **書き込みの結果は、書き込みとは別のセッションで読み直して確認しました**（ライブラリ内部の読み戻しだけに頼らない）。
- 書き込みを伴う試験では、事前にカードの内容を保存し、最後に元の内容へ戻して一致を確認しました。
- 下表のコマンドの `<リーダ名>` は、`list` で表示された実際のリーダ名です。
- 所要時間は、特に記載がない限り結果行に出る `PcscResult.ElapsedMs` です。

### 16.2 リーダ一覧・自動選択・読み出し（L-01〜L-08、1 枚目を置いた状態）

| ID | 目的 | コマンド | 期待 | 結果 |
|---|---|---|---|---|
| L-01 | リーダ一覧の取得 | `list` | 接続中のリーダを列挙し、終了コード 0 | リーダ 1 台を列挙 / 終了コード 0 |
| L-02 | リーダの自動選択（`Connect()`） | `read` | 最初のリーダに接続し、先頭 16 バイトを読む | 接続 `Ok` → `Read` `Ok` / `SF=0000` / 16 バイト（すべて `00`）/ 181ms → 切断 `Ok` |
| L-03 | リーダ名を指定した読み出し | `read <リーダ名> 0 1` | `Ok` | `Ok` / 16 バイト / 153ms |
| L-04 | 1 コマンドの上限（4 ブロック） | `read <リーダ名> 0 4` | `Ok`、64 バイト | `Ok` / 64 バイト（すべて `00`）/ 169ms |
| L-05 | ユーザ領域の最後のブロック | `read <リーダ名> 13 1` | `Ok` | `Ok` / 16 バイト / 147ms |
| L-06 | 5 ブロックは I/O 前に拒否 | `read <リーダ名> 0 5` | 接続は成功し、`Read` は `InvalidArgument` | 接続 `Ok` / `Read` `InvalidArgument`（0ms）/ 終了コード 1 |
| L-07 | 存在しないリーダ名 | `read "PcscIfTest: no such reader" 0 1` | `ReaderError` | `ReaderError`（`SCardConnect` が `0x80100009` = SCARD_E_UNKNOWN_READER）/ 終了コード 1 |
| L-08 | 繰り返しの安定性 | `loop <リーダ名> 20` | すべて `Ok`、毎回同じ IDm | `Ok` 20 / 毎回同じ IDm / median 167ms / max 192ms（`loop` の集計） |

- L-02〜L-08 の成功で、実機トランスポートの一連の送受信（Escape 制御コードの取得、セッション開始、RF OFF / ON、フレーミング切替、Polling 8 ラウンド、Read、後始末）がすべて動作したことになります。
- リーダが 1 台だけなので、L-02 の自動選択の確認は「1 台のときにそのリーダを選ぶ」ことに限られます。

### 16.3 書き込み（P-01〜P-03、W-01〜W-07、1 枚目）

| ID | 目的 | コマンド | 期待 | 結果 |
|---|---|---|---|---|
| P-01〜P-03 | 書き込み前の内容を保存 | `read <リーダ名> 0 4` / `4 4` / `13 1` | すべて `Ok` | すべて `Ok`。ブロック 0〜7 と 13 はすべて `00` |
| W-01 | 1 ブロック（16 バイト）の書き込み | `write <リーダ名> 00112233445566778899AABBCCDDEEFF --yes` | `Ok`、読み戻し一致 | `Ok` / `SF=0000` /「1 ブロックを書き込み、読み戻しで一致を確認」/ 193ms |
| W-02a | 別セッションで書いた値を確認 | `read <リーダ名> 0 2` | ブロック 0 が書いた値 | ブロック 0 = `00 11 22 … EE FF` |
| W-02b | 隣のブロックを壊さない | （W-02a と同じ） | ブロック 1 は元のまま | ブロック 1 = `00` × 16 |
| W-03 | 5 ブロック（80 バイト）の書き込み。読み戻しが 4 + 1 に分かれる経路 | `write <リーダ名> <40..8F の 80 バイト> --yes 0` | `Ok`、読み戻し一致 | `Ok` / `SF=0000` /「5 ブロックを書き込み、読み戻しで一致を確認」/ 248ms |
| W-04a | 別セッションで確認（ブロック 0〜3） | `read <リーダ名> 0 4` | `40..7F` | `40..7F` |
| W-04b | 別セッションで確認（ブロック 4） | `read <リーダ名> 4 2` | `80..8F` | `80..8F` |
| W-04c | 範囲外を壊さない | （W-04b と同じ） | ブロック 5 は元のまま | ブロック 5 = `00` × 16 |
| W-05a | 16 の倍数でないデータ | `write <リーダ名> 00112233445566778899 --yes` | `InvalidArgument` | `InvalidArgument`（「16 バイトの倍数にしてください」）/ 終了コード 1 |
| W-05b | ユーザ領域をはみ出す書き込み | `write <リーダ名> <FF × 32> --yes 13` | `InvalidArgument` | `InvalidArgument`（「ユーザ領域（0x00..0x0D）を超えています」）/ 終了コード 1 |
| W-05c | 不正な要求で書き込まれていない（ブロック 0） | `read <リーダ名> 0 1` | W-03 の値のまま | `40..4F` |
| W-05d | 不正な要求で書き込まれていない（ブロック 13） | `read <リーダ名> 13 1` | 元のまま | `00` × 16 |
| W-06 | 元の内容へ書き戻し | `write <リーダ名> <00 × 80> --yes 0` | `Ok` | `Ok` / 245ms |
| W-07 | 書き戻しの確認 | `read <リーダ名> 0 4` / `4 4` / `13 1` | ブロック 0〜7 と 13 が P-01〜P-03 と完全一致 | 完全一致 |

### 16.4 カード無し（N-01〜N-06）

| ID | 目的 | コマンド | 期待 | 結果 |
|---|---|---|---|---|
| N-01 | カード無しでの読み出し（自動選択） | `read` | `NoCard`、終了コード 1 | `NoCard` / 2468ms / 終了コード 1 |
| N-01b | メッセージ | （N-01 と同じ） | 不存在の証明ではないことを伝える | 「対象カードの応答を観測しなかった（物理的な不存在の証明ではない）」 |
| N-01c | 未確定の情報を報告しない | （N-01 と同じ） | IDm・SF・データを出さない | 出力なし |
| N-02 | リーダ名指定・4 ブロック | `read <リーダ名> 0 4` | `NoCard` | `NoCard` / 2461ms |
| N-03 | カード無しでの書き込み | `write <リーダ名> <00 × 16> --yes` | `NoCard`、終了コード 1 | `NoCard` / 2460ms / 終了コード 1 |
| N-04 | 繰り返し | `loop <リーダ名> 5` | すべて `NoCard` | `NoCard` 5 / median 2467ms / max 2476ms |
| N-05 | カード無しでもリーダは使える | `list` | リーダを列挙 | 列挙 / 終了コード 0 |
| N-06 | カード無しでの接続・切断 | `read <リーダ名> 0 1` | 接続・切断は `Ok`、読み出しだけ `NoCard` | そのとおり |

- N-03 は、直前の N-01・N-02 がどちらも `NoCard` のときだけ実行する条件にし、万一カードがあっても内容が変わらないよう `00` を書く要求にしました。
- カード無しの判定に約 2.5 秒かかるのは、8 ラウンドの Polling がそれぞれタイムアウト（300ms）まで待つためです（8 × 300ms + RF 起動待ち 20ms）。カードがあるとき（約 170ms）との差に注意してください。
- カード無しで書き込みコマンドを送らないこと自体は、実機では観測できません（模擬で 8-09 により確認済み）。

### 16.5 2 枚重ね: 読み出し（S-01〜S-04、中心・密着）

| ID | 目的 | コマンド | 期待 | 結果 |
|---|---|---|---|---|
| S-01 | 2 枚重ねの検出（自動選択） | `read` | `MultipleCards`、終了コード 1 | `MultipleCards` / 55ms / 終了コード 1 |
| S-01b | メッセージ | （S-01 と同じ） | 「2 種類」と 2 枚の IDm | 「異なる対象カードの IDm を 2 種類検出（重ね置き）。読み書きしない」と 2 枚の IDm |
| S-01c | 読み出しを行っていない | （S-01 と同じ） | データ・SF を出さない | 出力なし |
| S-02 | リーダ名指定・4 ブロック | `read <リーダ名> 0 4` | `MultipleCards`、S-01 と同じ 2 枚 | そのとおり / 63ms |
| S-03 | 見逃しの有無 | `loop <リーダ名> 50` | 50 回すべて `MultipleCards` | **`MultipleCards` 50 / 見逃し 0** / median 58ms / max 704ms（`loop` の集計） |
| S-03b | 検出の一貫性 | （S-03 と同じ） | 毎回同じ 2 枚 | 50 回とも同じ組み合わせ |
| S-03c | 2 種類観測での打ち切り（目安） | （S-03 と同じ） | 1 枚のとき（約 170ms）より速い回が多い | 50 回中 29 回が 170ms 未満（最短 41ms） |
| S-04 | リーダ一覧 | `list` | リーダを列挙 | 列挙 |

S-03 の所要時間は 3 つのまとまりに分かれました。

| 所要時間 | 回数 | 考えられる状況（推測） |
|---|---:|---|
| 41〜82ms | 29 | 最初のラウンドで 2 枚とも見え、残りのラウンドを打ち切った |
| 353〜414ms | 17 | 応答の得られないラウンド（タイムアウト 300ms）を 1 回挟んでから 2 枚目が見えた |
| 651〜702ms | 4 | 同じく 2 回挟んだ |

S-03c は所要時間を目安として見ているだけで、必ず成り立つ性質ではありません。

### 16.6 2 枚重ね: 書き込みの阻止（B-01、X-00〜X-99、V-1・V-2）

万一検出を逃すとカードが書き換わるため、次の手順で進めました。

1. 2 枚目だけを置いて 14 ブロック全体を保存する（B-01）。
2. 2 枚重ねに戻し、2 枚とも検出されることを確認してから書き込みを試す（X-00）。**`MultipleCards` 以外の結果が 1 回でも出たら中断する**条件にした。
3. 試行データはカードの内容（すべて `00`）と異なる `A5` / `5A` にし、万一書き込まれた場合に必ず検出できるようにした。
4. 試行後に 2 枚重ねのままであることを確認し（X-99）、1 枚ずつ置いて読み直す（V-1・V-2）。

| ID | 目的 | コマンド | 期待 | 結果 |
|---|---|---|---|---|
| B-01 | 2 枚目の内容を保存 | 2 枚目だけを置いて `read <リーダ名> 0 4` / `4 4` / `8 4` / `12 2` | 2 枚目の IDm で `Ok` | すべて `Ok`（172〜193ms）。14 ブロックすべて `00` |
| X-00 | 試行前に 2 枚重ねを確認 | `read <リーダ名> 0 1` | 2 枚の IDm で `MultipleCards` | `MultipleCards` / 54ms |
| X-1-1〜X-1-10 | 1 ブロックの書き込みを阻止 | `write <リーダ名> <A5 × 16> --yes`（10 回） | すべて `MultipleCards`、終了コード 1 | **10 回とも `MultipleCards`** / 53〜87ms（1 回だけ 375ms） |
| X-5-1〜X-5-5 | 5 ブロックの書き込みを阻止 | `write <リーダ名> <5A × 80> --yes 0`（5 回） | すべて `MultipleCards`、終了コード 1 | **5 回とも `MultipleCards`** / 60〜79ms |
| X-99 | 試行後も 2 枚重ねのまま | `read <リーダ名> 0 1` | `MultipleCards` | `MultipleCards` / 375ms |
| V-1 | 1 枚目が書き換わっていない | 1 枚目だけを置いて 14 ブロックを読む | ブロック 0〜7 と 13 が試行前と一致、試行データ無し | **一致、試行データ無し**。ブロック 8〜12 は試行前の記録が無いため比較せず（試行の対象外。読み出した値はすべて `00`） |
| V-2 | 2 枚目が書き換わっていない | 2 枚目だけを置いて 14 ブロックを読む | 14 ブロックすべて B-01 と一致、試行データ無し | **14 ブロックすべて一致、試行データ無し** |

- X の 15 回はいずれも重ね置きチェックの段階で止まっており、書き込みコマンドを送った可能性がある結果（`Ok`、`CardRejected`、書き込み処理中の `Indeterminate`）は出ていません。
- V-1・V-2 により、2 枚重ねでの書き込み要求で**どちらのカードも書き換わっていない**ことを実際のカードの内容で確認しました。

### 16.7 実機でもまだ確認していないこと

| 項目 | 補足 |
|---|---|
| カードが拒否した場合の `CardRejected`、複数ブロックの途中での失敗 | カードにその状態を作る手段が無い（模擬では確認済み） |
| winscard がエラーを返す経路、リソース解放の失敗 | §15.1 を参照 |
| 複数台のリーダが接続された環境（リーダ一覧の分解、自動選択で先頭を選ぶこと） | リーダ 1 台で実施 |
| 他機種のリーダ | 1 機種で実施 |
| 重ね方の違い（ずらし置き・浮き）、3 枚以上 | 中心・密着の 2 枚だけで実施。RF 応答を返さない 2 枚目は原理的に検出できない（README §3） |
| .NET 10 でビルドした `PcscIfTest` での実機動作 | 検証端末のアプリケーション制御ポリシーにより起動できなかった |

---

## 付録: テストを追加・変更したとき

1. `SelfTest.cs` の該当セクションに `Check` / `Expect` を追加する。既存の ID を変えたくない場合は、セクションの追加分のメソッド（`ReadFailurePaths` など）の末尾に書く。
2. `dotnet run --project test/PcscIfTest -- selftest` で全件 `OK` と件数を確認する。
3. 本書の該当セクションに、出力順どおりの位置へ項目を追加する。途中に挿入した場合は以降の ID を振り直し、行番号も更新する。
4. 対象の処理を消したらそのテストが失敗するか（別の検査に助けられて合格していないか）を考える（14.5）。
5. 本書 §14 の件数表・トレーサビリティ、§15（テストを追加した項目は削除）を更新する。
6. [source/PcscIfTest_SelfTest.md](source/PcscIfTest_SelfTest.md) の件数表と、README の件数（§1.3、§7、§8）も更新する。
