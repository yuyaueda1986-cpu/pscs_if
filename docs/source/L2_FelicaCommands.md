# L2_FelicaCommands.cs — カードコマンドの生成と応答解析

| 項目 | 内容 |
|---|---|
| ソース | [`src/L2_FelicaCommands.cs`](../../src/L2_FelicaCommands.cs) |
| 層 | 層2 カードコマンド |
| 含まれる型 | `Felica`（static class）/ `FelicaLiteS`（static class）/ `FelicaResponseKind`（enum） |
| 使う側 | `Pcsc`（[L1_Pcsc.md](L1_Pcsc.md)）、`DuplicateGuard` / `ScanObservation`（[L1_DuplicateGuard.md](L1_DuplicateGuard.md)） |

バイト列の仕様（各フィールドの意味・エンディアン・出所）は [protocol.md](../protocol.md) §4 にあります。
このページは、それをコードでどう組み立て・検査しているかの解説です。

---

## 1. このファイルの役割

**カードが解釈するフレーム**（リーダへの封筒の中身）を扱う層です。リーダや winscard のことは知りません。
入力も出力も `byte[]` だけなので、リーダが無くても完全にテストできます。

| 型 | 役割 |
|---|---|
| `Felica` | Polling フレームの組み立てと、応答からの IDm 抽出。対象カードを識別する定数を持つ |
| `FelicaLiteS` | ユーザ領域の Read / Write フレームの組み立てと、応答の解析。アクセス範囲の境界を守る |
| `FelicaResponseKind` | Read / Write 応答の解析結果の分類 |

すべてのフレームは**先頭 1 バイトが LEN（自分自身を含む全長）**です。

---

## 2. `Felica` — Polling

### 2.1 定数

| 定数 | 値 | 説明 |
|---|---|---|
| `LiteSSystemCode` | `0x88B4` | 対象カードのシステムコード。Polling で指定する |
| `RcS966IcType` | `0xF1` | Polling 応答の PMm に入る IC 種別。**チップ種別の確認であって、認証ではない** |
| `TimeSlot16` | `15` | タイムスロット数 16 を表す値 |

### 2.2 `BuildPolling`

```csharp
public static byte[] BuildPolling()
{
    return BuildPolling(LiteSSystemCode, 1, TimeSlot16);
}

public static byte[] BuildPolling(ushort systemCode, byte requestCode, byte timeSlot)
{
    // requestCode は 0..2、timeSlot は 0 / 1 / 3 / 7 / 15 のみ
    return new byte[] { 6, 0, (byte)(systemCode >> 8), (byte)systemCode, requestCode, timeSlot };
}
```

| バイト | 値（既定） | 意味 |
|---|---|---|
| 0 | `06` | LEN = 6 |
| 1 | `00` | コマンドコード（Polling） |
| 2-3 | `88 B4` | システムコード（**ビッグエンディアン**） |
| 4 | `01` | リクエストコード（`01` = システムコードを応答に含める） |
| 5 | `0F` | タイムスロット（16 スロット） |

既定の呼び出しでは常に `06 00 88 B4 01 0F` になります。

| 引数の誤り | 例外 |
|---|---|
| `requestCode` が 3 以上 | `ArgumentOutOfRangeException` |
| `timeSlot` が 0 / 1 / 3 / 7 / 15 以外 | `ArgumentOutOfRangeException` |

タイムスロットは「スロット数 − 1」で、有効なスロット数は 1 / 2 / 4 / 8 / 16 の 5 通りです。

### 2.3 `TryExtractLiteSIdm`

```csharp
public static bool TryExtractLiteSIdm(byte[] buffer, out List<byte[]> ids)
```

1 個のバッファ（リーダ応答の `97` データオブジェクト 1 個分）から、対象カードの Polling 応答を
20 バイトずつ区切って IDm を取り出します。

Polling 応答 1 枚分の形:

| 位置 | 内容 | 検査 |
|---|---|---|
| 0 | LEN = `0x14`（20） | 一致必須 |
| 1 | 応答コード = `0x01` | 一致必須 |
| 2-9 | IDm（8 バイト） | **取り出す** |
| 10-17 | PMm（8 バイト） | `[11]`（PMm の 2 バイト目）が `0xF1` であること |
| 18-19 | システムコード = `88 B4` | 一致必須 |

処理:

```csharp
ids = new List<byte[]>();
if (buffer == null || buffer.Length == 0) return false;
for (int i = 0; i < buffer.Length; i += 20)
{
    if (buffer.Length - i < 20 || buffer[i] != 20 || buffer[i + 1] != 1
        || buffer[i + 11] != RcS966IcType
        || buffer[i + 18] != 0x88 || buffer[i + 19] != 0xB4)
    {
        ids.Clear();
        return false;
    }
    byte[] id = new byte[8];
    Buffer.BlockCopy(buffer, i + 2, id, 0, 8);
    ids.Add(id);
}
return true;
```

- **1 か所でも条件を満たさなければ、バッファ全体を失敗にします。** 取り出し途中の IDm も捨てます（`ids.Clear()`）。
- **ずれた位置から探し直す（再同期する）ことはしません。** 壊れた応答の中から、たまたま条件を満たす
  20 バイトを拾ってしまうのを防ぐためです。
- 長さが 20 の倍数でない（末尾にゴミがある、途中で切れている）場合も失敗です。

| 入力 | 結果 |
|---|---|
| 正しい 20 バイト × 1 | `true`、IDm 1 個 |
| 正しい 20 バイト × 2（連結） | `true`、IDm 2 個 |
| 正しい 20 バイト + `00` | `false` |
| 19 バイト | `false` |
| `[11]` が `0xF0` | `false`（対象外の IC） |
| `[18..19]` が `12 FC` | `false`（別のシステムコード） |

注意: システムコードの検査は、定数 `LiteSSystemCode` ではなくリテラル `0x88` / `0xB4` で書かれています。
システムコードを変える場合は、`BuildPolling` の定数と、ここの 2 か所の両方を直す必要があります。

### 2.4 `IdmToString`

```csharp
public static string IdmToString(byte[] idm)
{
    if (idm == null || idm.Length != 8)
        throw new ArgumentException("IDm は 8 バイトです。", "idm");
    return Hex.ToString(idm, 8, false);
}
```

IDm を区切り無しの 16 進大文字 16 桁にします（例: `01 00 00 00 00 00 00 AB` → `"01000000000000AB"`）。
`PcscResult.Idm` と、重ね置きチェックの集合のキーに使います。

---

## 3. `FelicaLiteS` — ユーザ領域の Read / Write

**暗号化なし（Without Encryption）の Read / Write だけを扱います。** MAC 付きアクセス・相互認証・鍵の運用は
実装していません。平文のアクセスなので、改ざん検知や機密保持が必要な用途には使えません。

### 3.1 定数

| 定数 | 値 | 説明 |
|---|---|---|
| `BlockSize` | `16` | 1 ブロックのバイト数 |
| `FirstUserBlock` | `0x00` | ユーザブロックの先頭（S_PAD0） |
| `LastUserBlock` | `0x0D` | ユーザブロックの末尾（S_PAD13） |
| `UserBlockCount` | `14` | ユーザブロック数 |
| `DefaultAccessLength` | `16` | 既定のアクセスサイズ（先頭 1 ブロック） |
| `DefaultAccessBlocks` | `1` | 既定のアクセスブロック数 |
| `MaxBlocksPerRead` | `4` | Read 1 コマンドで読める最大ブロック数（**実機で確認**） |
| `MaxBlocksPerWrite` | `1` | Write 1 コマンドで書ける最大ブロック数（**実機で確認**） |
| `ReadServiceCode` | `0x000B` | 読み出し用サービスコード（認証不要） |
| `WriteServiceCode` | `0x0009` | 読み書き用サービスコード（認証不要） |
| `CommandRead` / `ResponseRead` | `0x06` / `0x07` | Read のコマンドコードと応答コード |
| `CommandWrite` / `ResponseWrite` | `0x08` / `0x09` | Write のコマンドコードと応答コード |
| `TwoByteElement`（private） | `0x80` | 2 バイト形式のブロックリスト要素の先頭 |
| `StatusOnlyResponseLength`（private） | `12` | ステータスフラグだけの応答の長さ |

`MaxBlocksPerRead` と `MaxBlocksPerWrite` は、実機の対象カードで次の応答を観測して決めた値です。

| 要求 | カードの応答 |
|---|---|
| Read を 5 ブロック以上 | `SF1=FF SF2=A2`（ブロック数エラー） |
| Write を 2 ブロック以上まとめて | 2 ブロック目で `SF1=02 SF2=A8` |

**システムブロック（MC / CK / CKV / ID など）には、意図的にアクセスできないようにしています。**
MC を誤って書くと、カードが復旧できなくなるためです。

### 3.2 `BlockRange` — 連続したブロック番号を作る

```csharp
public static byte[] BlockRange(byte startBlock, int blockCount)
{
    if (blockCount <= 0) throw ...;                          // (1)
    if (startBlock > LastUserBlock) throw ...;               // (2)
    if (blockCount > UserBlockCount - startBlock) throw ...; // (3)
    byte[] blocks = new byte[blockCount];
    for (int i = 0; i < blockCount; i++) blocks[i] = (byte)(startBlock + i);
    return blocks;
}
```

**(3) を引き算で書いているのが要点です。** `startBlock + blockCount - 1 > LastUserBlock` のように足し算で書くと、
`blockCount = int.MaxValue` のとき桁あふれして負の数になり、判定をすり抜けます。
その直後の `new byte[blockCount]` で約 2GB の配列を確保しに行ってしまいます。
(2) で `startBlock` が 0..13 に確定しているので、(3) の右辺は 1..14 に収まり、桁あふれしません。

| 呼び出し | 結果 |
|---|---|
| `BlockRange(0, 1)` | `{ 0x00 }` |
| `BlockRange(3, 4)` | `{ 0x03, 0x04, 0x05, 0x06 }` |
| `BlockRange(0, 14)` | `{ 0x00 .. 0x0D }` |
| `BlockRange(13, 1)` | `{ 0x0D }` |
| `BlockRange(13, 2)` | `ArgumentOutOfRangeException`（領域を超える） |
| `BlockRange(0x0E, 1)` | `ArgumentOutOfRangeException`（開始位置が領域外） |
| `BlockRange(0, 0)` | `ArgumentOutOfRangeException` |
| `BlockRange(2, int.MaxValue)` | `ArgumentOutOfRangeException`（配列を確保しない） |

`BlockRange` は Read / Write 共通の範囲検査です。Read の 4 ブロック上限はここでは見ません（`BuildRead` で見ます）。

### 3.3 `BuildRead` — Read フレームの組み立て

```csharp
public static byte[] BuildRead(byte[] idm, byte[] blocks)
```

検査:

| 検査 | 例外 |
|---|---|
| `idm` が 8 バイトでない | `ArgumentException` |
| `blocks` が空、ユーザ領域外、重複あり | `ArgumentException` / `ArgumentOutOfRangeException` |
| `blocks` が 5 個以上 | `ArgumentOutOfRangeException` |
| フレーム長が 255 を超える | `ArgumentException`（4 ブロック上限があるので実際には起きない） |

フレームの形（長さ = 14 + 2 × ブロック数。1 ブロックで 16 バイト、4 ブロックで 22 バイト）:

| 位置 | 内容 |
|---|---|
| 0 | LEN |
| 1 | `06`（Read） |
| 2-9 | IDm |
| 10 | サービス数 = `01` |
| 11-12 | サービスコード `0B 00`（**リトルエンディアン**） |
| 13 | ブロック数 |
| 14.. | ブロックリスト（1 ブロックにつき `80 <ブロック番号>`） |

例: IDm `01 00 00 00 00 00 00 01`、ブロック `0x00` と `0x01`

~~~
12 06 01 00 00 00 00 00 00 01 01 0B 00 02 80 00 80 01
~~~

システムコード（Polling）はビッグエンディアン、サービスコードはリトルエンディアンと、
**エンディアンが異なる**点に注意してください。

### 3.4 `ParseReadResponse` — Read 応答の解析

```csharp
public static FelicaResponseKind ParseReadResponse(byte[] response, byte[] expectedIdm,
    int expectedBlocks, out byte[] data, out byte statusFlag1, out byte statusFlag2)
```

応答の形:

| 位置 | 内容 |
|---|---|
| 0 | LEN |
| 1 | `07`（Read 応答） |
| 2-9 | IDm |
| 10 | ステータスフラグ 1 |
| 11 | ステータスフラグ 2 |
| 12 | ブロック数（SF が正常のときだけ） |
| 13.. | データ（ブロック数 × 16 バイト。SF が正常のときだけ） |

判定の順序（上から順に評価し、最初に当てはまったものを返す）:

| 順 | 条件 | 戻り値 | 出力 |
|---|---|---|---|
| — | `expectedBlocks <= 0` | 例外 `ArgumentOutOfRangeException`（呼び出し側の誤り） | — |
| — | `expectedIdm` が 8 バイトでない | 例外 `ArgumentException`（呼び出し側の誤り） | — |
| 1 | `null` または 12 バイト未満 | `Malformed` | SF = `FF FF` |
| 2 | `[0]`（LEN）が実際の長さと違う | `Malformed` | SF = `FF FF` |
| 3 | `[1]` が `07` でない | `Malformed` | SF = `FF FF` |
| 4 | IDm が期待値と違う | `Malformed` | SF = `FF FF` |
| 5 | SF1 か SF2 が `00` でない | **`CardError`** | SF = カードの値 |
| 6 | 13 バイト未満 | `Malformed` | SF = `FF FF` |
| 7 | `[12]` が期待したブロック数と違う | `Malformed` | SF = `FF FF` |
| 8 | 長さが `13 + ブロック数 × 16` でない | `Malformed` | SF = `FF FF` |
| 9 | すべて妥当 | **`Ok`** | SF = `00 00`、`data` にコピー |

**「応答の形を信用できるか」を先に確認し、信用できる場合だけ SF を読みます。** 別の IDm の応答や、
途中で切れた応答から SF を読むと、他人の（あるいは意味の無い）値をカードの状態として報告してしまうためです。

`Malformed` のとき SF を `FF FF` のままにしているのは、`PcscResult` で「カードまで届いていない」を表すためです。

### 3.5 `BuildWrite` — Write フレームの組み立て

```csharp
public static byte[] BuildWrite(byte[] idm, byte[] blocks, byte[] data)
```

検査は `BuildRead` と同じ（IDm、ブロックリスト）に加えて、`data` の長さが
「ブロック数 × 16」ちょうどであることを確認します。

フレームの形（長さ = 14 + 18 × ブロック数。1 ブロックで 32 バイト）:

| 位置 | 内容 |
|---|---|
| 0 | LEN |
| 1 | `08`（Write） |
| 2-9 | IDm |
| 10 | サービス数 = `01` |
| 11-12 | サービスコード `09 00`（リトルエンディアン） |
| 13 | ブロック数 |
| 14.. | ブロックリスト（1 ブロックにつき `80 <ブロック番号>`） |
| その後 | データ（ブロック数 × 16 バイト） |

例: IDm `01 00 00 00 00 00 00 01`、ブロック `0x00`、データ `10 11 .. 1F`

~~~
20 08 01 00 00 00 00 00 00 01 01 09 00 01 80 00 10 11 12 13 14 15 16 17 18 19 1A 1B 1C 1D 1E 1F
~~~

**`BuildWrite` は `MaxBlocksPerWrite` を検査しません。** 複数ブロックのフレームも組み立てられます
（自己テストでは、まとめて送るとカードに拒否されることの模擬に使っています）。
1 ブロックずつ送る制約は、`Pcsc.DoWrite` が 1 ブロックごとに `BuildWrite` を呼ぶことで守っています。

### 3.6 `ParseWriteResponse` — Write 応答の解析

応答の形は、ステータスフラグだけの 12 バイトです。

| 位置 | 内容 |
|---|---|
| 0 | LEN = `0C` |
| 1 | `09`（Write 応答） |
| 2-9 | IDm |
| 10 | ステータスフラグ 1 |
| 11 | ステータスフラグ 2 |

| 順 | 条件 | 戻り値 |
|---|---|---|
| — | `expectedIdm` が 8 バイトでない | 例外 `ArgumentException` |
| 1 | `null` または長さが 12 でない | `Malformed`（SF = `FF FF`） |
| 2 | `[0]` が 12 でない | `Malformed` |
| 3 | `[1]` が `09` でない | `Malformed` |
| 4 | IDm が期待値と違う | `Malformed` |
| 5 | SF1 = SF2 = `00` | **`Ok`** |
| 6 | それ以外 | **`CardError`**（SF = カードの値） |

**「応答が返ってきた」ことを成功の根拠にしません。** SF が両方 `00` のときだけ成功です。

### 3.7 `TryParseReadResponse` / `TryParseWriteResponse`

`ParseReadResponse` / `ParseWriteResponse` の結果が `Ok` かどうかだけを返す、真偽値版のラッパです。
失敗の理由（`CardError` か `Malformed` か）を区別したい場合は、元のメソッドを使ってください。
ライブラリ本体（`Pcsc`）は元のメソッドを使っています。

### 3.8 内部の検査メソッド

| メソッド | 内容 |
|---|---|
| `ValidateIdm(byte[] idm)` | `null` または 8 バイト以外なら `ArgumentException` |
| `ValidateBlocks(byte[] blocks)` | 空なら `ArgumentException`、`0x0D` を超える番号があれば `ArgumentOutOfRangeException`、重複があれば `ArgumentException` |
| `IdmMatches(response, offset, expected)` | 応答の `offset` から 8 バイトが期待する IDm と一致するか |

`ValidateBlocks` が**システムブロックの破壊を防ぐ最後の境界**です。`Pcsc` を通さずに `BuildRead` / `BuildWrite` を
直接呼んでも、ユーザ領域の外は指定できません。重複の確認は二重ループですが、ブロック数は最大 14 なので問題になりません。

---

## 4. `FelicaResponseKind` — 応答の分類

| 値 | 数値 | 意味 | `Pcsc` での扱い |
|---|---|---|---|
| `Ok` | 0 | 応答は妥当で、カードもエラーを返していない | `Ok`（Read）/ 次のブロックへ（Write） |
| `CardError` | 1 | 応答は妥当。カードがステータスフラグでエラーを返した | `CardRejected` |
| `Malformed` | 2 | 途中切れ・別の IDm・応答コード違い・長さの不整合など。応答を信頼できない | `Indeterminate` |

`CardError` と `Malformed` を分けている理由は、**カードの状態が分かっているかどうか**が違うためです。

- `CardError` なら、カードは要求を受け取って明確に拒否した。その要求は実行されていない。
- `Malformed` なら、カードが何をしたか分からない。Write なら書き込まれた可能性も残る。

同じ「失敗」として扱うと、呼び出し側が「書き込まれていない」と誤解して再試行し、二重に書き込む、
といった判断ミスにつながります。

---

## 5. C# 5 で書くための工夫

| 箇所 | 書き方 | 使っていない C# 6 以降の構文 |
|---|---|---|
| 解析結果の返し方 | `out byte[] data, out byte statusFlag1` を宣言済みの変数で受ける | `out var` |
| 複数の値を返す | `out` 引数 + 列挙型の戻り値 | タプル |
| 例外メッセージ | `"ブロック 0x" + blocks[i].ToString("X2") + " ..."` | 文字列補間 |

## 6. 関連テスト

[`test/PcscIfTest/SelfTest.cs`](PcscIfTest_SelfTest.md)

| セクション | 確認内容 |
|---|---|
| 1. コマンド組み立ての既知ベクタ | Polling / Read / Write のバイト列が期待どおり、不正なタイムスロット・リクエストコードの拒否 |
| 3. 対象カードの Polling 応答の抽出 | 1 枚・連結 2 枚の解析、末尾のゴミ・途中切れ・IC 種別違い・システムコード違いの拒否、IDm の文字列化 |
| 4. ユーザ領域の境界チェック | システムブロックの拒否、`0x0D` の許可と `0x0E` の拒否、重複、Read の 4 ブロック上限、`BlockRange` の桁あふれ |
| 5. 読み書き応答の解析 | 正常応答、別 IDm、SF 異常、ブロック数不一致、あらゆる途中切れ、`Ok` / `CardError` / `Malformed` の区別 |
