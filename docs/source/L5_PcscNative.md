# L5_PcscNative.cs — winscard.dll との接続

| 項目 | 内容 |
|---|---|
| ソース | [`src/L5_PcscNative.cs`](../../src/L5_PcscNative.cs) |
| 層 | 層5 PC/SC |
| 含まれる型 | `Interop`（internal static class）/ `PcscException`（class） |
| 使う側 | `Rcs300Transport`（[L4_PcscTransport.md](L4_PcscTransport.md)）、`Pcsc.ListReaders`（[L1_Pcsc.md](L1_Pcsc.md)） |

各 API に渡すパラメータの意味は [protocol.md](../protocol.md) §1 にあります。
このページは、C# からどう宣言し、どう呼んでいるかの解説です。

---

## 1. このファイルの役割

Windows のスマートカード API（`winscard.dll`）を C# から呼ぶための層です。

| 型 | 役割 |
|---|---|
| `Interop` | `winscard.dll` の P/Invoke 宣言と定数、それを使う小さな補助処理 |
| `PcscException` | winscard の関数が失敗したときに、その戻り値を運ぶ例外 |

`Interop` は `internal` なので、ライブラリの外（業務アプリ）からは見えません。

---

## 2. 定数

| 定数 | 値 | 意味 |
|---|---|---|
| `SCARD_S_SUCCESS` | `0x00000000` | 成功 |
| `SCARD_SCOPE_USER` | `0` | ユーザ単位のコンテキスト |
| `SCARD_SHARE_DIRECT` | `3` | カードが無くてもリーダに直接接続する（Escape を送るのに使う） |
| `SCARD_PROTOCOL_UNDEFINED` | `0` | プロトコルのネゴシエーションをしない |
| `SCARD_LEAVE_CARD` | `0` | 切断時にカードに何もしない |
| `CM_IOCTL_GET_FEATURE_REQUEST` | `ScardCtlCode(3400)` = `0x00313520` | リーダの機能一覧を取得する制御コード |
| `IOCTL_CCID_ESCAPE_FALLBACK` | `ScardCtlCode(3500)` = `0x003136B0` | Escape の制御コード（取得できなかったときの代わり） |
| `FEATURE_CCID_ESC_COMMAND` | `0x13` | 機能一覧の中で Escape の制御コードを表すタグ |

### 2.1 `ScardCtlCode`

```csharp
public static uint ScardCtlCode(uint code) { return (0x31u << 16) | (code << 2); }
```

Windows の `SCARD_CTL_CODE` マクロと同じ計算です。

| code | 計算 | 結果 |
|---|---|---|
| 3400 | `0x310000 \| (3400 << 2 = 0x3520)` | `0x00313520` |
| 3500 | `0x310000 \| (3500 << 2 = 0x36B0)` | `0x003136B0` |

2 つの制御コードは `const` ではなく `static readonly` です。メソッド呼び出しの結果は `const` にできないためです。

---

## 3. P/Invoke 宣言

| C# の宣言 | winscard の関数 | 引数の対応 |
|---|---|---|
| `int SCardEstablishContext(uint dwScope, IntPtr, IntPtr, out IntPtr phContext)` | `SCardEstablishContext` | 予約の 2 引数は `IntPtr.Zero` |
| `int SCardReleaseContext(IntPtr hContext)` | `SCardReleaseContext` | — |
| `int SCardConnect(IntPtr hContext, string szReader, uint dwShareMode, uint dwPreferredProtocols, out IntPtr phCard, out uint pdwActiveProtocol)` | **`SCardConnectW`** | リーダ名は UTF-16 |
| `int SCardDisconnect(IntPtr hCard, uint dwDisposition)` | `SCardDisconnect` | — |
| `int SCardControl(IntPtr hCard, uint dwControlCode, [In] byte[] lpInBuffer, uint nInBufferSize, [Out] byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned)` | `SCardControl` | 送信・受信バッファ |
| `int SCardListReaders(IntPtr hContext, string mszGroups, [Out] char[] mszReaders, ref uint pcchReaders)` | **`SCardListReadersW`** | 文字数単位 |

型の対応:

| Windows の型 | C# の型 | 理由 |
|---|---|---|
| `LONG`（戻り値） | `int` | 32 ビット符号付き |
| `DWORD` | `uint` | 32 ビット符号なし |
| `SCARDCONTEXT` / `SCARDHANDLE` | `IntPtr` | ポインタ幅（32 / 64 ビットで変わる）。これにより AnyCPU でビルドできる |
| `LPCWSTR` | `string` + `CharSet.Unicode` | UTF-16 の文字列 |
| `LPDWORD`（出力） | `out uint` | — |
| `LPDWORD`（入出力） | `ref uint` | `SCardListReaders` は必要な長さを受け取り、実際の長さを返す |

宣言上の注意:

- **文字列を扱う関数は `W`（Unicode）版を明示しています**（`EntryPoint = "SCardConnectW"` など）。
  リーダ名に日本語などが含まれていても正しく渡せます。
- `[In] byte[]` は送信専用、`[Out] byte[]` / `[Out] char[]` は受信専用であることをマーシャラに伝えます。
- `SetLastError = true` を指定していますが、winscard はエラーを戻り値で返すため、コードでは `GetLastError` を使っていません。

---

## 4. `Interop.ListReaders`

```csharp
public static List<string> ListReaders(IntPtr hContext)
```

**2 回呼ぶパターン**です。

1. バッファ無し（`null`）で呼び、必要な文字数を `len` に受け取る。
2. その長さの `char[]` を用意して、もう一度呼ぶ。

戻ってくるのは「`\0` 区切りで、最後が `\0\0`」のマルチ文字列です。

~~~
"Reader A\0Reader B\0\0"  →  { "Reader A", "Reader B" }
~~~

```csharp
StringBuilder sb = new StringBuilder();
for (int i = 0; i < len && i < buf.Length; i++)
{
    if (buf[i] == '\0')
    {
        if (sb.Length > 0) { result.Add(sb.ToString()); sb.Length = 0; }
    }
    else sb.Append(buf[i]);
}
```

- どちらの呼び出しで失敗しても、空のリストを返します。
  リーダが 1 台も無いときも、winscard は失敗の戻り値を返すため、同じく空のリストになります。
- 1 回目と 2 回目の間にリーダが抜き差しされると、2 回目が失敗して空のリストになる可能性があります。
- 空の文字列（連続した `\0`）は追加しません。

## 5. `Interop.TryGetEscapeControlCode`

```csharp
public static uint? TryGetEscapeControlCode(IntPtr hCard)
```

リーダに機能一覧を問い合わせ、Escape の制御コードを取り出します。

1. `SCardControl(hCard, CM_IOCTL_GET_FEATURE_REQUEST, 空, 256 バイトのバッファ)` を呼ぶ。
2. 失敗、または応答が 3 バイト未満なら `null`。
3. 応答を `tag(1) len(1) value(len)` の並びとして読む。
4. `tag == 0x13` かつ `len == 4` なら、値の 4 バイトを**ビッグエンディアン**で `uint` にして返す。
5. 見つからない、または長さが範囲を超えたら `null`。

例: 応答に `13 04 00 31 36 B0` が含まれていれば `0x003136B0` を返します。

`null` のとき、呼び出し側（`Rcs300Transport.Open`）は `IOCTL_CCID_ESCAPE_FALLBACK` を使います。

**制御コードを決め打ちにせず、まず機能一覧から取るのが正しい手順です。**
Escape の制御コードはリーダやドライバによって異なる可能性があるためです。

---

## 6. `PcscException`

```csharp
public sealed class PcscException : Exception
{
    public int ReturnCode { get; }   // 実際は readonly フィールド + get
    public PcscException(string message, int rc)
        : base(message + " (0x" + rc.ToString("X8") + ")") { ... }
}
```

| メンバ | 内容 |
|---|---|
| `ReturnCode` | winscard の関数が返した値 |
| `Message` | 「失敗した処理名 (0x戻り値)」。例: `SCardConnect (0x8010000B)` |

### 6.1 投げられる場所

`Rcs300Transport.Check` だけが投げます。

| 処理 | 例 |
|---|---|
| `Open` の中 | `SCardEstablishContext` / `SCardConnect` の失敗 |
| 管理コマンドの送信 | `SCardControl` の失敗（`StartTransparentSession` など） |

`Transceive` の `SCardControl` の失敗では投げません（`Unknown` の結果を返します）。

### 6.2 業務アプリへの届き方

`PcscException` が業務アプリまで例外として届くことはありません。`Pcsc` が捕捉して、次のように変換します。

| 発生した API | 結果 |
|---|---|
| `Connect` | `ReaderError`、`Error` に `PcscException` |
| `Read` / `Write` | `Indeterminate`、`Error` に `PcscException` |

原因を調べるときは `result.Error as PcscException` で `ReturnCode` を見てください。

### 6.3 よく見る戻り値

Windows SDK で定義されている値のうち、このライブラリで遭遇しやすいものです。

| 値 | 名前 | 典型的な状況 |
|---|---|---|
| `0x80100008` | `SCARD_E_INSUFFICIENT_BUFFER` | 受信バッファが足りない |
| `0x80100009` | `SCARD_E_UNKNOWN_READER` | リーダ名が間違っている |
| `0x8010000B` | `SCARD_E_SHARING_VIOLATION` | 他のアプリがリーダを使用中（`SHARE_DIRECT` は排他） |
| `0x8010000C` | `SCARD_E_NO_SMARTCARD` | カードが無い（自己テストの模擬で使用） |
| `0x80100017` | `SCARD_E_READER_UNAVAILABLE` | リーダが外された、または使えない |
| `0x8010001D` | `SCARD_E_NO_SERVICE` | スマートカードサービスが動いていない |
| `0x8010002E` | `SCARD_E_NO_READERS_AVAILABLE` | リーダが 1 台も接続されていない |

Escape コマンドが Windows の CCID ドライバで無効になっていると、`SCardControl` が失敗して
管理コマンドの段階で止まります。その場合はドライバのレジストリ設定 `EscapeCommandEnable` が必要です
（[protocol.md](../protocol.md) §1.5）。

---

## 7. C# 5 で書くための工夫

| 箇所 | 書き方 | 使っていない C# 6 以降の構文 |
|---|---|---|
| `PcscException.ReturnCode` | `private readonly int _returnCode;` + `get { return _returnCode; }` | get のみの自動プロパティ |
| `TryGetEscapeControlCode` の引数 | `new byte[0]` | `Array.Empty<byte>()`（.NET Framework 4.6 以降） |
| 戻り値の宣言 | `uint returned;` を先に宣言してから `out returned` | `out var` |

## 8. 関連テスト

このファイルは実際の `winscard.dll` を呼ぶため、自己テストでは直接テストしていません。

- 自己テストの `FakeTransport` は、Open 失敗を `new PcscException("SCardConnect", 0x8010000C)` で模擬し、
  `Connect` が `ReaderError` を返して `Error` に `PcscException` が入ることを確認しています
  （「6. Connect / Disconnect」）。
- 実機での動作は、`PcscIfTest list` / `read` / `write` / `loop` と README §8.1 で確認しています。
