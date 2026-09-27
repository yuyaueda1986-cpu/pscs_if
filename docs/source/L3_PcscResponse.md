# L3_PcscResponse.cs — リーダ応答（TLV）の解析

| 項目 | 内容 |
|---|---|
| ソース | [`src/L3_PcscResponse.cs`](../../src/L3_PcscResponse.cs) |
| 層 | 層3 リーダ応答 TLV |
| 含まれる型 | `PcscResponse`（class）/ `TransceiveResult`（class） |
| 使う側 | `Rcs300Transport`（[L4_PcscTransport.md](L4_PcscTransport.md)）、`DuplicateGuard`（[L1_DuplicateGuard.md](L1_DuplicateGuard.md)）、`Pcsc`（[L1_Pcsc.md](L1_Pcsc.md)） |

タグの意味や値の出所は [protocol.md](../protocol.md) §3 にあります。
このページは、それをコードでどう解析し、どう判定しているかの解説です。

---

## 1. このファイルの役割

`SCardControl` で返ってくる**リーダの応答バイト列**を解釈する層です。

| 型 | 役割 |
|---|---|
| `PcscResponse` | 応答バイト列を TLV として分解し、各タグの値を取り出す |
| `TransceiveResult` | `PcscResponse` の結果を、上位層が使う 4 つのフラグ（`Ok` / `NoResponse` / `Collision` / `Unknown`）に落とす |

この層の方針は **fail-closed** です。知らないタグ、途中で切れた TLV、仕様にない長さなどは、
**決して読み飛ばさず、すべて失敗にします。** 読み飛ばすと、壊れた応答を「成功」と誤認する可能性があるためです。

## 2. 応答の形

~~~
<データオブジェクト（TLV）の並び> SW1 SW2
~~~

自己テストで使っている「カード 1 枚の正常な Polling 応答」を例にすると、次のようになります。

| 位置 | バイト | 内容 |
|---|---|---|
| 0-4 | `C0 03 00 90 00` | 汎用ステータス（正常） |
| 5-7 | `92 01 00` | 最終バイトの受信ビット数 = 0（端数なし） |
| 8-11 | `96 02 00 00` | RF エラーフラグ = なし |
| 12-13 | `97 14` | 受信データ、長さ 20 |
| 14-33 | 20 バイト | カードの応答フレーム |
| 34-35 | `90 00` | SW（正常） |

---

## 3. `PcscResponse` — TLV の解析

### 3.1 フィールド

すべて `internal` です。ライブラリの外からは `CommandSucceeded` と `Parse` しか見えません。

| フィールド | 型 | 内容 |
|---|---|---|
| `Valid` | `bool` | 応答全体を最後まで矛盾なく解析できたか |
| `Sw` | `ushort` | 末尾 2 バイトの SW |
| `GenericSw` | `ushort?` | `C0` の中の SW。`C0` が無ければ `null` |
| `RfErrors` | `byte` | `96` の RF エラーフラグ（複数あれば OR） |
| `PartialByte` | `bool` | `92` が 0 以外（最終バイトに端数ビットがある） |
| `Frames` | `List<byte[]>` | `97` の中身。出てきた順にすべて |

### 3.2 `Parse` の手順

```csharp
public static PcscResponse Parse(byte[] response)
```

1. `null` または 2 バイト未満なら、`Valid = false` のまま返す。
2. 末尾 2 バイトを `Sw` にする。解析対象は、その手前までの範囲。
3. 範囲の先頭から、タグ・長さ・値を順に読む。
4. 範囲の最後まで矛盾なく読み切れたら `Valid = true` にする。

**途中で 1 つでも問題があれば、その場で `Valid = false` のまま返します。**
`Valid` を `true` にするのは最後の 1 行だけです。

### 3.3 長さフィールド

| 長さの 1 バイト目 | 意味 |
|---|---|
| `00`..`7F` | その値が長さ |
| `81` | 続く 1 バイトが長さ |
| `82` | 続く 2 バイト（ビッグエンディアン）が長さ |
| `80`、`83`..`FF` | **拒否**（不定長の BER などは受け付けない） |

長さが残りのバイト数を超える場合も拒否します。

### 3.4 タグごとの規則

| タグ | 受け付ける長さ | 規則 | 違反したとき |
|---|---|---|---|
| `C0` | 3 | 2 回目の `C0` は拒否。値の 2-3 バイト目を `GenericSw` に。`GenericSw` が `9000` なら 1 バイト目は `00` でなければならない | 失敗 |
| `92` | 1 | 値は 0..7。0 以外なら `PartialByte = true` | 失敗 |
| `96` | 2（または値 `00` の 1 バイト） | 1 バイト目の上位 4 ビットが 0 であること。1 バイト目を `RfErrors` に OR | 失敗 |
| `97` | 1 以上 | 値をコピーして `Frames` に追加（最後の 1 個だけでなく、すべて保持） | 長さ 0 は失敗 |
| その他 | — | **未知のタグは失敗** | 失敗 |

`96` は仕様上 2 バイトですが、仕様書の例に 1 バイトのゼロ（エラーなし）も載っているため、それだけは受け付けています。

### 3.5 `CommandSucceeded`

```csharp
return Valid && Sw == 0x9000
    && (!GenericSw.HasValue || GenericSw.Value == 0x9000)
    && RfErrors == 0 && !PartialByte && Frames.Count == 0;
```

**リーダの管理コマンド**（セッション開始・RF ON など）が成功したと言い切れるときだけ `true` です。
管理コマンドはカードと通信しないので、`97`（受信データ）が付いていたらおかしい、とみなして失敗にします。
`Rcs300Transport.ValidateManagementResponse` がこれを使います。

---

## 4. `TransceiveResult` — 送受信 1 回分の結果

### 4.1 プロパティ

| プロパティ | 型 | 内容 |
|---|---|---|
| `Ok` | `bool` | カードの応答フレームを正常に受信した |
| `NoResponse` | `bool` | 明示的な「カードからの応答なし」。**物理的な不存在の証明ではない** |
| `Collision` | `bool` | RF 衝突（複数枚が同時に応答した可能性） |
| `Unknown` | `bool` | 上のどれでもない（通信異常・不正な応答） |
| `Data` | `byte[]` | `Ok` のとき、全フレームを連結したもの。それ以外は長さ 0 |
| `Frames` | `IList<byte[]>` | `Ok` のとき、`97` ごとのフレーム。それ以外は空 |
| `RfErrors` | `byte` | `96` の RF エラーフラグ |
| `StatusWord` | `ushort` | 末尾の SW |
| `GenericStatusWord` | `ushort?` | `C0` の SW |
| `RawResponse` | `byte[]` | 元の応答バイト列（診断用） |
| `FlagCount` | `int` | 立っているフラグの数 |

プロパティはすべて公開の setter を持っています。別のトランスポート実装やテストが、
任意の結果を組み立てられるようにするためです。

### 4.2 `Parse` — フラグの決め方

```csharp
PcscResponse r = PcscResponse.Parse(response);
bool transportSuccess = r.Valid && r.Sw == 0x9000;
bool commandSuccess = transportSuccess && (!r.GenericSw.HasValue || r.GenericSw.Value == 0x9000);
bool collision = commandSuccess && (r.RfErrors & 2) != 0;
bool ok = commandSuccess && r.RfErrors == 0 && !r.PartialByte && r.Frames.Count > 0;
// 6400 / 6700 / 6Axx は失敗。完全な C0 6401 だけが「応答なし」。
bool noResponse = transportSuccess && r.GenericSw.HasValue && r.GenericSw.Value == 0x6401
    && r.RfErrors == 0 && !r.PartialByte && r.Frames.Count == 0;

t.Ok = ok;
t.NoResponse = noResponse;
t.Collision = collision;
t.Unknown = !ok && !noResponse && !collision;
```

| フラグ | 条件（すべて満たす） |
|---|---|
| `Ok` | TLV 正常 / SW = `9000` / `C0` 無しか `9000` / RF エラー無し / 端数ビット無し / `97` が 1 個以上 |
| `Collision` | TLV 正常 / SW = `9000` / `C0` 無しか `9000` / RF エラーの衝突ビット（`0x02`）が立っている |
| `NoResponse` | TLV 正常 / SW = `9000` / **`C0` が `6401`** / RF エラー無し / 端数ビット無し / `97` が 0 個 |
| `Unknown` | 上の 3 つのどれでもない |

### 4.3 フラグは必ず 1 つだけ立つ

`Parse` が作る結果では、4 つのフラグのうち**ちょうど 1 つ**が立ちます。

- `Ok` と `Collision` — `Ok` は RF エラー 0 が条件、`Collision` は衝突ビットが条件なので同時に成り立たない。
- `Ok` と `NoResponse` — `Ok` は `97` が 1 個以上、`NoResponse` は 0 個が条件。
- `Collision` と `NoResponse` — `Collision` は `C0` が無いか `9000`、`NoResponse` は `6401` が条件。
- `Unknown` — 他の 3 つがすべて偽のときだけ真。

それでも `FlagCount` を用意しているのは、公開の setter で組み立てられた結果（別実装・模擬実装）に
矛盾が無いかを、使う側（`DuplicateGuard` / `Pcsc.TrySingleFrame`）が確認できるようにするためです。

### 4.4 衝突の扱い

- 衝突ビットが立っていれば、**データが付いていても `Ok` にはしません**（`Collision`）。
- 衝突以外の RF エラー（CRC `0x01`、パリティ `0x04`、フレーミング `0x08`）は `Unknown` です。
  データが付いていても信用しません。
- 衝突ビットと他のエラービットが両方立っている場合は `Collision` になります。どちらにしても読み書きには進みません。

### 4.5 「応答なし」の厳密さ

**`C0` の SW が `6401` のときだけ**を「カードが応答しなかった」とみなします。
`6400`、`6700`、`6A80` などの似た値はすべて `Unknown` です。
これらを無応答として扱うと、通信の異常を「カード無し」と誤って報告してしまうためです。

### 4.6 `Frames` と `Data`

`Ok` のときだけ、`Frames` に `97` ごとのフレームを、`Data` にそれらを連結したものを入れます。
それ以外のときは、どちらも空です（`null` にはなりません）。

上位層は、DO の境界を保つために基本的に `Frames` を使います（[L1_DuplicateGuard.md](L1_DuplicateGuard.md)）。

### 4.7 `ToString`

診断用の 1 行表示です。

~~~
Ok=True NoResp=False Collision=False Unknown=False RF=0x00 SW=0x9000 C0=9000 Raw=[C0 03 00 90 00 92 01 00 ...]
~~~

---

## 5. 入力と判定の例

自己テストで確認しているものから抜粋します（`<frame>` は正しい 20 バイトの Polling 応答）。

| 応答 | 判定 | 理由 |
|---|---|---|
| `C0 03 00 90 00 92 01 00 96 02 00 00 97 14 <frame> 90 00` | `Ok` | 正常 |
| 上の SW を `6F 00` に | `Unknown` | SW エラー |
| 上の `C0` を `C0 03 00 6A 81` に | `Unknown` | 汎用ステータスのエラー |
| 上の `96` を `96 02 02 00` に | `Collision` | 衝突ビット（データ付きでも `Ok` にしない） |
| 上の `96` を `01` / `04` / `08` に | `Unknown` | CRC / パリティ / フレーミング異常 |
| 上の `92` を `92 01 07` に | `Unknown` | 端数ビットあり |
| `C0 03 02 64 01 90 00` | `NoResponse` | 完全な「応答なし」 |
| `C0 03 02 64 00 90 00`（`6700` / `6A80` / `6A81` / `6F00` も同様） | `Unknown` | `6401` 以外は「応答なし」ではない |
| `C0 03 02 64 01` + `97 14 <frame>` + `90 00` | `Unknown` | 「応答なし」とデータの同居は矛盾 |
| `90 00` のみ | `Unknown` | TLV は正常だが、データも「応答なし」も無い |
| `92 01 64 90 00` | `Unknown` | `92` の値が 7 を超える |
| 正常な応答を途中で切ったもの（あらゆる長さ） | `Ok` にならない | 途中切れ |
| `97 80 90 00` | `Unknown` | 不定長 BER |
| `C0` が 2 回 | `Unknown` | 重複 |
| `AA 00 90 00` | `Unknown` | 未知のタグ |
| `97` が 2 個 | `Ok`、`Frames.Count == 2` | すべて保持する |

## 6. 関連テスト

[`test/PcscIfTest/SelfTest.cs`](PcscIfTest_SelfTest.md) の「2. PC/SC 応答パーサ（fail-closed）」（28 件）で、
上の表の内容と、`CommandSucceeded` / `ValidateManagementResponse` の判定を確認しています。
