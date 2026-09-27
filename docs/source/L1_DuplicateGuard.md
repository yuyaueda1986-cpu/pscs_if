# L1_DuplicateGuard.cs — 重ね置きチェックの観測

| 項目 | 内容 |
|---|---|
| ソース | [`src/L1_DuplicateGuard.cs`](../../src/L1_DuplicateGuard.cs) |
| 層 | 層1 重ね置きチェック |
| 含まれる型 | `DuplicateGuard`（static class）/ `ScanObservation`（class） |
| 使う側 | `Pcsc.Execute`（[L1_Pcsc.md](L1_Pcsc.md)） |

このページは**コードの読み方**の解説です。なぜ複数ラウンドが必要なのか、検出率はどの程度か、
といった仕組みの話は [DuplicateGuard.md](../DuplicateGuard.md) にまとめています。

---

## 1. このファイルの役割

pcsc_dup_guard の `CardDuplicateGuard` に相当する部分です。

| 型 | 役割 |
|---|---|
| `DuplicateGuard` | 決められた回数だけ Polling し、観測できたことを `ScanObservation` に記録する |
| `ScanObservation` | 1 回のスキャンで観測した事実（IDm の集合と 3 つの異常フラグ）の入れ物 |

**ここでは観測だけを行い、判定はしません。** 「読み書きしてよいか」は `Pcsc.Gate` が決めます。
観測と判定を分けることで、判定の根拠が 1 か所（`Gate`）に集まり、Read と Write で片方だけ基準が緩む、
ということを防いでいます。

## 2. 依存関係

| 使うもの | 層 | 用途 |
|---|---|---|
| `Felica.BuildPolling()` | 層2 | Polling フレーム `06 00 88 B4 01 0F` を作る |
| `Felica.TryExtractLiteSIdm()` | 層2 | 応答フレームから IDm を厳密に取り出す |
| `Felica.IdmToString()` | 層2 | IDm を集合のキー（16 進文字列）にする |
| `IPcscTransport.Transceive()` | 層4 | Polling を送受信する |
| `TransceiveResult` | 層3 | 送受信 1 回分の結果（4 つのフラグとフレーム） |

---

## 3. `DuplicateGuard.Collect`

```csharp
public static ScanObservation Collect(IPcscTransport transport, int rounds, int timeoutUs)
```

| 引数 | 説明 |
|---|---|
| `transport` | 送受信に使うトランスポート。`null` なら `ArgumentNullException` |
| `rounds` | Polling の回数。`Pcsc` からは `PcscOptions.DuplicateCheckRounds`（2..64）が渡る |
| `timeoutUs` | 1 回の送受信のタイムアウト。`PcscOptions.TransceiveTimeoutUs` が渡る |

`rounds` はこのメソッドでは検査しません。0 以下を渡すとループが 1 回も回らず、IDm 0 種類の観測結果
（`Gate` では `NoCard`）になります。範囲の保証は `PcscOptions.Validate` の役割です。

### 3.1 処理の流れ

```csharp
ScanObservation observation = new ScanObservation();
byte[] polling = Felica.BuildPolling();

for (int round = 0; round < rounds; round++)
{
    TransceiveResult t = transport.Transceive(polling, timeoutUs);

    // (A) 矛盾したフラグは信用しない
    if (t == null || t.FlagCount != 1 || t.Unknown) { observation.Unknown = true; break; }
    // (B) 衝突
    if (t.Collision) { observation.Collision = true; break; }
    // (C) 無応答
    if (t.NoResponse) { observation.NoResponse = true; continue; }

    // (D) 応答あり: DO ごとに IDm を取り出す
    IList<byte[]> buffers = t.Frames;
    if (buffers == null || buffers.Count == 0)
    {
        buffers = new List<byte[]>();
        buffers.Add(t.Data);
    }

    bool malformed = false;
    for (int b = 0; b < buffers.Count; b++)
    {
        List<byte[]> ids;
        if (!Felica.TryExtractLiteSIdm(buffers[b], out ids)) { malformed = true; break; }
        for (int k = 0; k < ids.Count; k++) observation.Add(ids[k]);
    }
    if (malformed) { observation.Unknown = true; break; }

    // (E) 2 種類見えたら打ち切り
    if (observation.Count >= 2) break;
}
return observation;
```

### 3.2 1 ラウンドの結果と処理

| 送受信の結果 | 記録するフラグ | ループ |
|---|---|---|
| `null` / フラグが 1 つでない / `Unknown` | `Unknown` | **打ち切り** |
| `Collision` | `Collision` | **打ち切り** |
| `NoResponse` | `NoResponse` | **継続**（次のラウンドへ） |
| `Ok` だが、どれかのフレームが対象カードの応答として不正 | `Unknown` | **打ち切り** |
| `Ok` で、すべてのフレームが正しい | IDm を集合に追加 | 2 種類以上なら**打ち切り**、そうでなければ継続 |

### 3.3 各部分の意図

**(A) 矛盾したフラグを信用しない**

`TransceiveResult.Parse` が作る結果は、必ずフラグがちょうど 1 つだけ立ちます。それでも
`FlagCount != 1` を確認しているのは、`TransceiveResult` のプロパティが公開の setter を持っており、
別のトランスポート実装やテスト用の模擬実装が「`Ok` かつ `NoResponse`」のような結果を返しうるためです。
矛盾した結果は最も安全側の `Unknown` として扱います。

**(B) 衝突は打ち切り**

RF 衝突は「複数枚いるかもしれないが、何枚かは分からない」状態です。続けても判定は良くならないので
打ち切ります。`Gate` は衝突を `Indeterminate`（枚数を主張しない）にします。

**(C) 無応答だけは継続**

対象リーダは 1 回の送受信で 1 枚分しか返さず、どのカードが応答するかはラウンドごとに変わります。
1 ラウンド無応答でも、次のラウンドで 2 枚目が応答するかもしれないので、打ち切らずに続けます。
ただし「無応答があった」ことは `NoResponse` に記録し、`Gate` で「1 枚見えたのに途中で途切れた」を
`Indeterminate` にするのに使います。

**(D) DO の境界を保つ**

リーダ応答の `97` データオブジェクト（DO）1 個ずつを別々に解析します。

- 1 個の DO の中に、完全なフレームが複数連結されている場合は、連結のまま解析します（20 バイトずつ区切れる）。
- DO の途中で途切れたフレームを、次の DO の先頭とつなげて「補修」することはしません。
  補修すると、壊れた応答からたまたま正しい形の 20 バイトを拾ってしまう危険があるためです。

`t.Frames` が空のときは `t.Data` を 1 個のバッファとして扱います。`TransceiveResult.Parse` は
`Ok` なら必ず `Frames` を埋めるので、これは `Frames` を埋めない別実装への備えです。

不正なフレームが見つかったときの注意点:

- 1 個の DO の中では、`TryExtractLiteSIdm` が失敗すると取り出し途中の IDm を捨てるので、その DO からは何も追加されません。
- ただし**同じ応答の、それより前の DO から取り出した IDm は集合に残ります。**
  その結果 `Count >= 2` になれば `Gate` は `MultipleCards`、そうでなければ `Unknown` により `Indeterminate` です。
  どちらでも読み書きには進みません。

**(E) 2 種類で打ち切り**

2 種類見えた時点で「重ね置き」の判定は確定し、それ以上数えても変わりません。
2 枚重ねのほうが 1 枚のときより速く終わる（実測 56ms と 166ms）のはこのためです。

---

## 4. 観測の具体例

`rounds = 8` の場合です。IDm は説明用の値です。

| 例 | 各ラウンドの結果 | 実行ラウンド | 観測結果 | `Gate` の判定 |
|---|---|---|---|---|
| 1 枚 | A, A, A, A, A, A, A, A | 8 | `Count=1` | 続行（読み書きする） |
| 2 枚（交互に応答） | A, B | 2 | `Count=2` | `MultipleCards` |
| 2 枚（1 応答に 2 フレーム） | A+B | 1 | `Count=2` | `MultipleCards` |
| カード無し | 無応答 × 8 | 8 | `Count=0`, `NoResponse` | `NoCard` |
| 途中で離した | A, 無応答, 無応答, ... | 8 | `Count=1`, `NoResponse` | `Indeterminate` |
| 衝突 | A, 衝突 | 2 | `Count=1`, `Collision` | `Indeterminate` |
| 壊れた応答 | 不正フレーム | 1 | `Count=0`, `Unknown` | `Indeterminate` |

同じ IDm を何度観測しても、集合のキーが同じなので `Count` は増えません。

---

## 5. `ScanObservation`

### 5.1 メンバ

| メンバ | 種別 | 説明 |
|---|---|---|
| `_ids` | `Dictionary<string, byte[]>` | IDm の 16 進文字列 → IDm のバイト列。`StringComparer.Ordinal` |
| `Unknown` | `bool`（setter は internal） | 通信異常・対象外の応答・矛盾したフラグを観測した |
| `Collision` | `bool`（setter は internal） | RF 衝突を観測した |
| `NoResponse` | `bool`（setter は internal） | いずれかのラウンドで明示的な無応答を観測した |
| `Count` | `int` | 観測した異なる IDm の数 |
| `IdList` | `List<string>` | IDm の文字列を昇順に並べた新しいリスト |
| `Add(byte[] idm)` | internal | IDm を集合に追加する（同じ IDm は上書き） |
| `SingleIdm` | `byte[]` | IDm がちょうど 1 種類のときだけ、そのバイト列を返す |

### 5.2 設計のポイント

- **集合のキーを文字列にしている理由。** `byte[]` は参照で比較されるため、そのままでは同じ IDm を
  別物として数えてしまいます。`Felica.IdmToString` で 16 桁の文字列にし、`Ordinal`（バイト単位の比較）で
  重複を除いています。
- **`IdList` は毎回ソートした新しいリストを返します。** `MultipleCards` のメッセージに IDm を並べるとき、
  観測順に依存せず同じ順序になるようにするためです。呼び出し側がリストを書き換えても、観測結果には影響しません。
- **`SingleIdm` は 1 種類でなければ `InvalidOperationException` を投げます。**
  0 種類や 2 種類のまま読み書きに進むことを、型の側でも防いでいます。`Pcsc.Execute` は `Gate` を通過した後
  （= 1 種類のとき）にだけ呼びます。
- **フラグの setter は `internal` です。** ライブラリの外から観測結果を書き換えられないようにしています。
  `{ get; internal set; }` は C# 3 の構文なので、.NET Framework 4.5 でも使えます。

---

## 6. 関連テスト

[`test/PcscIfTest/SelfTest.cs`](PcscIfTest_SelfTest.md) の「7. Read と重ね置きチェック」「8. Write と重ね置きチェック」
「9. オプション検証とログ」で確認しています。模擬トランスポートの `Script(...)` で、ラウンドごとの応答を固定して検証します。

| テスト名 | 確認している経路 |
|---|---|
| 指定ラウンド分の Polling を実行 | 1 枚のとき全ラウンドを実行する（模擬のラウンド数 2 → Polling 2 回） |
| 2 枚重なっているときはエラー（MultipleCards） | 1 応答に 2 フレーム → (E) |
| ラウンドをまたいで別 IDm を観測したらエラー | A, B → (E) |
| 1 応答に 2 枚分含まれてもエラー | 1 個の DO に 2 フレーム連結 → (D) |
| 無応答は NoCard で、コマンドを送出しない | 全ラウンド無応答 → (C) |
| RF 衝突は Indeterminate（枚数を主張しない） | (B) |
| 不正応答は 0 枚ではなく Indeterminate | `Frames` が空で `Data` が不正 → (D) の代替経路 |
| 矛盾したフラグは fail-closed | `Ok` と `NoResponse` が同時に立つ → (A) |
| 検査中に応答が途切れたら Indeterminate | A, 無応答 → (C) |
| 途切れた DO を次の DO で補修しない | 1 フレームを 2 個の DO に分割 → (D) |
| 2 枚重なっているときは書き込まない | Write 側も同じ観測を通る |
| オプションは生成時にスナップショットされる | `rounds` が生成時の値のまま |
