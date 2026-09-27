# L4_PcscTransport.cs — トランスポートの契約と実機実装

| 項目 | 内容 |
|---|---|
| ソース | [`src/L4_PcscTransport.cs`](../../src/L4_PcscTransport.cs) |
| 層 | 層4 リーダ制御 |
| 含まれる型 | `IPcscTransport`（interface）/ `RfProtocol`（enum）/ `Rcs300Transport`（class） |
| 使う側 | `Pcsc`（[L1_Pcsc.md](L1_Pcsc.md)）、`DuplicateGuard`（[L1_DuplicateGuard.md](L1_DuplicateGuard.md)）、自己テストの `FakeTransport` |

封筒（`FF 50 ...`）の各バイトの意味は [protocol.md](../protocol.md) §2 にあります。
このページは、それをコードでどう組み立て、どう送っているかの解説です。

---

## 1. このファイルの役割

「リーダをどう操作するか」を扱う層です。

| 型 | 役割 |
|---|---|
| `IPcscTransport` | 上位層（`Pcsc` / `DuplicateGuard`）が使うリーダ操作の**契約**。テストではここを差し替える |
| `RfProtocol` | `SwitchProtocol` に渡す RF プロトコルの種別 |
| `Rcs300Transport` | `IPcscTransport` の**実機実装**。カードフレームを `FF 50` の封筒に包み、Escape（`SCardControl`）で送る |

インタフェースを挟んでいるのは、**リーダ無しで上位層をテストするため**です。`Pcsc` は `IPcscTransport` しか知らず、
コンストラクタで生成方法を差し替えられます。自己テストは模擬実装 `FakeTransport` を渡して、
2 枚重ね・無応答・応答異常などを再現しています。

---

## 2. `IPcscTransport` — 契約

```csharp
public interface IPcscTransport : IDisposable
{
    void Open(string readerName);
    void StartTransparentSession();
    void TurnOffRf();
    void SwitchProtocol(RfProtocol protocol);
    void TurnOnRf();
    TransceiveResult Transceive(byte[] frame, int timeoutUs);
    void EndTransparentSession();
}
```

### 2.1 メソッドと呼ばれる場面

| メソッド | 役割 | 呼ぶ側 |
|---|---|---|
| `Open` | リーダに接続する（RF はまだ起動しない） | `Pcsc.Connect` |
| `StartTransparentSession` | カードと生のフレームをやり取りするセッションを開始 | `Pcsc.Execute` |
| `TurnOffRf` | RF を OFF | `Pcsc.Execute`（切替前と終了時の 2 回） |
| `SwitchProtocol` | RF のフレーミングを切り替える | `Pcsc.Execute` |
| `TurnOnRf` | RF を ON | `Pcsc.Execute` |
| `Transceive` | カードフレームを送り、応答を受け取る | `DuplicateGuard.Collect`、`Pcsc.DoRead` / `DoWrite` |
| `EndTransparentSession` | セッションを終了 | `Pcsc.Execute` |
| `Dispose` | 接続を解放する | `Pcsc.Disconnect`、`Pcsc.Connect`（Open 失敗時） |

### 2.2 `Pcsc` が呼ぶ順序

~~~
Connect     : Open
Read / Write: StartTransparentSession
              TurnOffRf
              SwitchProtocol(Felica)
              TurnOnRf
              Transceive × N（Polling）
              Transceive × M（Read / Write、重ね置きチェックを通過したときだけ）
              TurnOffRf
              EndTransparentSession
Disconnect  : Dispose
~~~

### 2.3 実装する側が守るべきこと

別のリーダ向けにこのインタフェースを実装する場合の目安です（`Rcs300Transport` はこれに従っています）。

| 項目 | 期待される振る舞い |
|---|---|
| 管理系メソッド（`Start` / `Off` / `Switch` / `On` / `End`） | 成功と言い切れなければ例外を投げる。`Pcsc` は `Indeterminate` にして後始末する |
| `Transceive` の戻り値 | 4 つのフラグのうち**ちょうど 1 つ**を立てる。矛盾していると `Unknown` 扱いになる |
| `Transceive` の RF レベルの失敗 | 例外より `Unknown` の結果で返すのが望ましい（例外でも `Indeterminate` になるので安全側ではある） |
| `TransceiveResult.Frames` | リーダ応答の `97` ごとに 1 要素。途切れたフレームを連結しない |
| `Dispose` | 例外を投げない。RF やセッションが残っていれば落とす |

---

## 3. `RfProtocol`

```csharp
public enum RfProtocol { TypeA = 0, TypeB = 1, Iso15693 = 2, Felica = 3 }
```

`Pcsc` が使うのは `Felica` だけです。`Rcs300Transport.SwitchProtocol` はそれ以外を渡されると
`NotSupportedException` を投げます。値は一般的な RF プロトコルの種類を並べたもので、
封筒に入るバイト値（`03`）とは直接対応していません。

---

## 4. `Rcs300Transport` — 実機実装

対象リーダの Transparent Session を使う実装です。対象カード専用です。

- CRC はリーダが付けるので、ソフトウェアでは計算しません。
- 成功と言い切れない応答は、すべて失敗として扱います（fail-closed）。

### 4.1 フィールド

| フィールド | 型 | 内容 |
|---|---|---|
| `_context` | `IntPtr` | `SCardEstablishContext` で得たコンテキスト |
| `_card` | `IntPtr` | `SCardConnect` で得たリーダのハンドル |
| `_escapeCode` | `uint` | `SCardControl` に渡す Escape の制御コード |
| `_sessionStarted` | `bool` | セッションが「開いているかもしれない」 |
| `_rfOn` | `bool` | RF が「ON かもしれない」 |

### 4.2 `Open`

| # | 処理 | 失敗したとき |
|---|---|---|
| 1 | リーダ名が `null` / 空白だけでないか確認 | `ArgumentException` |
| 2 | すでに開いていないか確認 | `InvalidOperationException` |
| 3 | `SCardEstablishContext(SCARD_SCOPE_USER)` | `PcscException` |
| 4 | `SCardConnect(SCARD_SHARE_DIRECT, SCARD_PROTOCOL_UNDEFINED)` | `PcscException` |
| 5 | `Interop.TryGetEscapeControlCode` で Escape の制御コードを取得。取れなければ `0x003136B0` | 失敗しない（フォールバック） |

3〜5 の途中で例外が起きたら、`Dispose()` で取得済みのハンドルを解放してから再送出します。

- **`SCARD_SHARE_DIRECT`** — カードが載っていなくてもリーダ自体に接続するモードです。
  リーダに Escape コマンドを送るために必要です。Windows では排他接続になります。
- **`SCARD_PROTOCOL_UNDEFINED`** — T=0 / T=1 のネゴシエーションを行いません。カードとの通信は後で自前で行います。

### 4.3 管理コマンド

```csharp
private static byte[] Manage(byte tag)
{
    return new byte[] { 0xFF, 0x50, 0, 0, 2, tag, 0, 0 };
}
```

| メソッド | tag | 送るバイト列 |
|---|---|---|
| `StartTransparentSession` | `0x81` | `FF 50 00 00 02 81 00 00` |
| `EndTransparentSession` | `0x82` | `FF 50 00 00 02 82 00 00` |
| `TurnOffRf` | `0x83` | `FF 50 00 00 02 83 00 00` |
| `TurnOnRf` | `0x84` | `FF 50 00 00 02 84 00 00` |

#### フラグを立てる・下ろすタイミング

| メソッド | フラグ | 送信との前後 |
|---|---|---|
| `StartTransparentSession` | `_sessionStarted = true` | **送信の前** |
| `EndTransparentSession` | `_sessionStarted = false` | **送信が成功した後** |
| `TurnOnRf` | `_rfOn = true` | **送信の前** |
| `TurnOffRf` | `_rfOn = false` | **送信が成功した後** |

「開く・ON にする」ときは送る前に立て、「閉じる・OFF にする」ときは成功してから下ろします。
つまりフラグは「**開いている／ON になっているかもしれない**」を表し、常に安全側に倒れます。
応答が失敗しても、`Dispose` がフラグを見て後始末を試みられるようにするためです。

### 4.4 `SwitchProtocol` と `BuildFelicaSwitchApdu`

```csharp
public static byte[] BuildFelicaSwitchApdu()
{
    return new byte[] { 0xFF, 0x50, 0, 2, 4, 0x8F, 2, 3, 0, 0 };
}
```

`FF 50 00 02 04 8F 02 03 00 00` を送ります。`03` がプロトコル種別（対象カードのフレーミング）、
その次の `00` は**フレーミングの指定であって、ビットレートではありません**。
カードをアクティベートするコマンドではない点に注意してください。

`RfProtocol.Felica` 以外を渡すと `NotSupportedException` です。

### 4.5 `Transceive` と `BuildTransceiveApdu`

```csharp
public TransceiveResult Transceive(byte[] frame, int timeoutUs)
{
    int rc;
    byte[] response = SendApduRaw(BuildTransceiveApdu(frame, timeoutUs), out rc);
    if (rc != Interop.SCARD_S_SUCCESS)
    {
        TransceiveResult failed = new TransceiveResult();
        failed.Unknown = true;
        failed.RawResponse = response;
        return failed;
    }
    return TransceiveResult.Parse(response);
}
```

- **`SCardControl` が失敗しても例外を投げず、`Unknown` の結果を返します。** RF レベルの失敗は
  重ね置きチェックや応答の検査の経路で `Indeterminate` として扱われます。
- 成功したら、応答の解釈は層3 の `TransceiveResult.Parse` に任せます。

封筒の組み立て:

```csharp
int doLen = 11 + frame.Length;
a.AddRange(new byte[] {
    0xFF, 0x50, 0, 1, 0, (byte)(doLen >> 8), (byte)doLen,
    0x5F, 0x46, 4, (byte)timeoutUs, (byte)(timeoutUs >> 8),
    (byte)(timeoutUs >> 16), (byte)(timeoutUs >> 24),
    0x95, 0x82, (byte)(frame.Length >> 8), (byte)frame.Length });
a.AddRange(frame);
a.AddRange(new byte[] { 0, 0, 0 });
```

| 位置 | 内容 | エンディアン |
|---|---|---|
| 0-4 | `FF 50 00 01 00` | — |
| 5-6 | 以降のデータ長 = 11 + フレーム長 | **ビッグ** |
| 7-9 | `5F 46 04`（タイムアウト指定） | — |
| 10-13 | タイムアウト（マイクロ秒） | **リトル** |
| 14-15 | `95 82`（送信データ） | — |
| 16-17 | フレーム長 | **ビッグ** |
| 18.. | カードフレーム | — |
| 末尾 | `00 00 00` | — |

例: Polling フレーム `06 00 FF FF 01 00`、タイムアウト 100000µs（`0x000186A0`）

~~~
FF 50 00 01 00 00 11
5F 46 04 A0 86 01 00
95 82 00 06
06 00 FF FF 01 00
00 00 00
~~~

| 引数の誤り | 例外 |
|---|---|
| `frame` が `null` / 空 / 65524 バイト超 | `ArgumentException` |
| `timeoutUs` が 0 以下 | `ArgumentOutOfRangeException` |

65524 は、データ長（11 + フレーム長）が 2 バイトの長さフィールドの上限 65535 に収まる最大値です。

末尾の `00 00 00` は、対象リーダの実機で観測した封筒の形をそのまま踏襲したものです。
「拡張 Le はデータの後ろに 3 バイト」という一般的な規則ではありません。

### 4.6 送信の内部処理

| メソッド | 役割 | 失敗の扱い |
|---|---|---|
| `SendApduRaw(apdu, out rc)` | `SCardControl` を呼び、応答をコピーして返す | `rc` を返すだけ（失敗時は空の配列）。未オープンなら `InvalidOperationException` |
| `SendApdu(apdu, what)` | 管理コマンドを送り、応答を検証する | `rc` が失敗なら `PcscException`、応答が成功と言い切れなければ `InvalidOperationException` |
| `ValidateManagementResponse(response, what)`（public static） | `PcscResponse.Parse(response).CommandSucceeded` を確認 | 失敗なら `InvalidOperationException`（応答の 16 進ダンプ付き） |
| `Check(rc, what)` | `rc` が成功でなければ例外 | `PcscException` |

- 応答バッファは 4096 バイトです。`SCardControl` がそれより大きい値を返した場合は `InvalidOperationException` にします。
- 管理コマンドは `SendApdu`（失敗で例外）、`Transceive` は `SendApduRaw`（失敗で `Unknown`）と使い分けています。
  管理コマンドの失敗は「リーダを制御できていない」ので処理を止めるべきですが、
  送受信の失敗は「カードとの通信で何かあった」ので観測結果として扱う、という違いです。

### 4.7 `Dispose`

```csharp
public void Dispose()
{
    try { if (_rfOn) TurnOffRf(); }
    catch { }
    try { if (_sessionStarted) EndTransparentSession(); }
    catch { }
    if (_card != IntPtr.Zero)
    {
        Interop.SCardDisconnect(_card, Interop.SCARD_LEAVE_CARD);
        _card = IntPtr.Zero;
    }
    if (_context != IntPtr.Zero)
    {
        Interop.SCardReleaseContext(_context);
        _context = IntPtr.Zero;
    }
}
```

1. RF が ON かもしれなければ OFF にする（失敗は無視）。
2. セッションが開いているかもしれなければ終了する（失敗は無視）。
3. リーダのハンドルを解放する（`SCARD_LEAVE_CARD` = カードには何もしない）。
4. コンテキストを解放する。

正常な流れでは `Pcsc.Execute` が RF とセッションを明示的に閉じているので、1 と 2 は復旧のためだけの処理です。
ハンドルをゼロに戻すので、2 回呼んでも問題ありません。

**既知の制限:** `SCardDisconnect` と `SCardReleaseContext` は失敗を例外ではなく戻り値で通知しますが、
ここでは戻り値を確認していません。そのため解放に失敗しても、`Pcsc.Disconnect` は `Ok` を返します。
呼び出し側へ通知する仕組みは実装していません。

### 4.8 例外のまとめ

| メソッド | 例外 | 条件 |
|---|---|---|
| `Open` | `ArgumentException` | リーダ名が `null` / 空白 |
| `Open` | `InvalidOperationException` | すでに開いている |
| `Open` | `PcscException` | コンテキスト取得・接続の失敗 |
| 管理コマンド 4 種 | `PcscException` | `SCardControl` の失敗 |
| 管理コマンド 4 種 | `InvalidOperationException` | 応答が成功と言い切れない / 未オープン / バッファ超過 |
| `SwitchProtocol` | `NotSupportedException` | `Felica` 以外 |
| `SwitchProtocol` | 管理コマンドと同じ | 送信の失敗 |
| `Transceive` | `ArgumentException` / `ArgumentOutOfRangeException` | フレーム長・タイムアウトが不正 |
| `Transceive` | `InvalidOperationException` | 未オープン / バッファ超過 |
| `Transceive` | **投げない** | `SCardControl` の失敗（`Unknown` を返す） |
| `Dispose` | **投げない** | — |

### 4.9 スレッド安全性

`Rcs300Transport` 自体は排他制御をしていません。`Pcsc` がロックで呼び出しを直列化している前提です。
`Pcsc` を通さずに直接使う場合は、1 つのインスタンスを複数のスレッドから同時に呼ばないでください。

---

## 5. 関連テスト

[`test/PcscIfTest/SelfTest.cs`](PcscIfTest_SelfTest.md)

| セクション | 確認内容 |
|---|---|
| 1. コマンド組み立ての既知ベクタ | `BuildFelicaSwitchApdu`、`BuildTransceiveApdu` のバイト列、空フレーム・タイムアウト 0 の拒否 |
| 2. PC/SC 応答パーサ（fail-closed） | `ValidateManagementResponse` が C0 の失敗・SW エラー・空応答を拒否する |
| 6〜8 | `FakeTransport`（`IPcscTransport` の模擬実装）を通じて、`Pcsc` が契約どおりの順序で呼ぶこと |

`Rcs300Transport` の `Open` / 管理コマンド / `Dispose` は実機が必要なため、自己テストの対象外です。
実機での確認結果は README §8.1 にあります。
