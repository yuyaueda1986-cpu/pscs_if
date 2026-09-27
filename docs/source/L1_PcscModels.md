# L1_PcscModels.cs — 設定・戻り値・ステータス

| 項目 | 内容 |
|---|---|
| ソース | [`src/L1_PcscModels.cs`](../../src/L1_PcscModels.cs) |
| 層 | 層1 公開 API |
| 含まれる型 | `PcscOptions`（class）/ `PcscResult`（class）/ `PcscStatus`（enum） |
| 使う側 | 業務アプリ、`Pcsc`（[L1_Pcsc.md](L1_Pcsc.md)） |

---

## 1. このファイルの役割

業務アプリが `Pcsc` とやり取りするときの「入れ物」をまとめたファイルです。処理はほとんど持ちません。

| 型 | 向き | 役割 |
|---|---|---|
| `PcscOptions` | 業務アプリ → `Pcsc` | Read / Write の動作パラメータ |
| `PcscResult` | `Pcsc` → 業務アプリ | 4 つの API 共通の戻り値 |
| `PcscStatus` | `Pcsc` → 業務アプリ | 結果の種類。`PcscResult.Status` に入る |

---

## 2. `PcscOptions` — 動作パラメータ

### 2.1 プロパティ

| プロパティ | 型 | 既定 | 範囲 | 使われる場所 |
|---|---|---|---|---|
| `TransceiveTimeoutUs` | `int` | `300000` | `100000`..`1000000` | 重ね置きチェックの Polling と、Read / Write の送受信すべて |
| `DuplicateCheckRounds` | `int` | `8` | `2`..`64` | `DuplicateGuard.Collect` のラウンド数 |
| `RfSettleDelayMs` | `int` | `20` | `0`..`1000` | `Pcsc.Execute` で RF ON の直後に待つ時間 |
| `VerifyAfterWrite` | `bool` | `true` | — | `Pcsc.DoWrite` で読み戻して比較するか |
| `Log` | `Action<string>` | `null` | — | `Pcsc.Log` の出力先 |

各項目の意味:

- **`TransceiveTimeoutUs`** — 1 回の送受信で、リーダがカードの応答を待つ時間（マイクロ秒）。
  リーダへの封筒の `5F 46` タグに、リトルエンディアン 4 バイトで入ります（[protocol.md](../protocol.md) §2.3）。
  カードが無いときは全ラウンドがこの時間まで待つため、既定値では 8 × 300ms ≒ 2.4 秒かかります
  （実測 2464ms）。書き込みは読み出しより時間がかかるので、読み出しに必要な値より長めにしています。
- **`DuplicateCheckRounds`** — 重ね置きチェックで Polling する回数。対象リーダは 1 回の送受信で
  1 枚分の応答しか返さないため、**複数ラウンドで観測した IDm の和集合が唯一の検出手段**です。
  1 では原理的に重ね置きを検出できないので、下限を 2 にしています。
- **`RfSettleDelayMs`** — RF を ON にしてから最初の Polling までの待ち時間。カードの起動を待つための保守的な値です。
- **`VerifyAfterWrite`** — `true` なら、書き込み後に同じセッションで読み戻し、送った内容と一致することを確認します。
  `false` にすると速くなりますが、「カードは成功と応答したが実際には書かれていない」を検出できません。
- **`Log`** — 診断ログの出力先。`Console.WriteLine` などを渡します。例外を投げても判定には影響しません。

### 2.2 コンストラクタ

```csharp
public PcscOptions()
{
    TransceiveTimeoutUs = 300000;   // 書き込みは読み出しより時間がかかる
    DuplicateCheckRounds = 8;
    RfSettleDelayMs = 20;
    VerifyAfterWrite = true;
    Log = null;
}
```

既定値はコンストラクタで設定しています。C# 6 の「自動プロパティ初期化子」
（`public int X { get; set; } = 8;`）を使わないためです。

### 2.3 `Snapshot`（internal）

```csharp
internal PcscOptions Snapshot()
{
    PcscOptions o = new PcscOptions();
    o.TransceiveTimeoutUs = TransceiveTimeoutUs;
    // ...5 項目すべてをコピー
    return o;
}
```

`Pcsc` のコンストラクタが呼びます。**生成後に呼び出し側が元の `PcscOptions` を書き換えても、
`Pcsc` の動作は変わりません。** 検査の途中でラウンド数が減る、といった事故を防ぐためです。

`Log` はデリゲートの参照をコピーするだけです。デリゲートの中身（出力先の状態など）までは固定されません。

### 2.4 `Validate`（internal）

次の順に検査し、最初に範囲外だった項目で `ArgumentOutOfRangeException` を投げます。

| 順 | 項目 | 条件 | 例外の `ParamName` |
|---|---|---|---|
| 1 | `TransceiveTimeoutUs` | `100000`..`1000000` の外 | `"TransceiveTimeoutUs"` |
| 2 | `DuplicateCheckRounds` | `2`..`64` の外 | `"DuplicateCheckRounds"` |
| 3 | `RfSettleDelayMs` | `0`..`1000` の外 | `"RfSettleDelayMs"` |

`VerifyAfterWrite` と `Log` は検査しません。

`Validate` は `Pcsc` のコンストラクタから呼ばれます。**`new Pcsc(options)` の時点で例外になり、
`Connect` まで進みません。** これは `Pcsc` が例外を投げる唯一の場所です。

---

## 3. `PcscResult` — 4 つの API 共通の戻り値

### 3.1 プロパティ

| プロパティ | 型 | 説明 | 空のとき |
|---|---|---|---|
| `Status` | `PcscStatus` | 結果の種類 | — |
| `Ok` | `bool` | `Status == PcscStatus.Ok` のときだけ `true` | — |
| `Data` | `byte[]` | Read 成功時の読み出しデータ | 長さ 0 の配列（`null` にはならない） |
| `Idm` | `string` | アクセス対象として確定した IDm（16 進大文字 16 桁） | `""`（`null` にはならない） |
| `StatusFlag1` | `byte` | カードのステータスフラグ 1 | `0xFF` |
| `StatusFlag2` | `byte` | カードのステータスフラグ 2 | `0xFF` |
| `Message` | `string` | 人が読むための説明 | `""`（`null` にはならない） |
| `ElapsedMs` | `long` | API の開始からの経過時間 | — |
| `Error` | `Exception` | 例外が原因の失敗のときだけ入る | `null` |

- **`Ok` プロパティは fail-closed です。** `Status` が `Ok` のときだけ `true` で、未知の値
  （将来値が増えた場合など）はすべて `false` になります。業務アプリは `if (result.Ok)` だけで判定してかまいません。
- **`StatusFlag1/2` の `0xFF` は「カードまで届いていない」の目印です。** 実際のカードが
  `SF1=FF` を返すこともあるため（例: Read のブロック数エラーは `FF A2`）、両方が `0xFF` のときだけ
  「未到達」とみなします。`ToString` もこの規則で SF の表示を省きます。
- `Data` は内部の配列をそのまま返します（コピーではありません）。受け取った配列を書き換えると、
  同じ `PcscResult` から見える内容も変わります。

### 3.2 生成

コンストラクタは `internal` なので、ライブラリの外からは作れません。内部では次の 3 つのヘルパと、
コンストラクタの直接呼び出しを使い分けています。

| ヘルパ | `Idm` | SF | `Error` | 主な用途 |
|---|---|---|---|---|
| `Create(status, message, elapsedMs)` | 空 | `FF FF` | `null` | 接続関係、`Gate` の判定結果 |
| `Create(status, message, elapsedMs, idm)` | あり | `FF FF` | `null` | IDm 確定後の `Indeterminate` |
| `Failure(status, message, elapsedMs, error)` | 空 | `FF FF` | あり | 例外の変換（`InvalidArgument` / `ReaderError` / `Indeterminate`） |
| コンストラクタ直接 | あり | 実値 | `null` | `Ok` / `CardRejected` / 読み戻し失敗 |

結果の種類ごとに、どのプロパティが入るか:

| 結果 | `Data` | `Idm` | SF | `Error` |
|---|---|---|---|---|
| Read の `Ok` | 読み出しデータ | あり | `00 00` | — |
| Write の `Ok` | 空 | あり | 最後の書き込み応答（`00 00`） | — |
| Connect / Disconnect の `Ok` | 空 | 空 | `FF FF` | — |
| `CardRejected` | 空 | あり | カードが返した値 | — |
| 読み書き中の `Indeterminate`（応答不正） | 空 | あり | `FF FF` | — |
| 読み戻しの失敗・不一致による `Indeterminate` | 空 | あり | 読み戻しの SF | — |
| `Gate` による `MultipleCards` / `NoCard` / `Indeterminate` | 空 | 空 | `FF FF` | — |
| 例外による `Indeterminate` | 空 | 空 | `FF FF` | あり |
| `InvalidArgument` | 空 | 空 | `FF FF` | あり（`ArgumentException`） |
| `ReaderError` | 空 | 空 | `FF FF` | あり（リーダが 1 台も無いときだけ `null`） |

### 3.3 `ToString`

```csharp
sb.Append(_status.ToString());
if (_idm.Length > 0) { sb.Append(" idm="); sb.Append(_idm); }
if (!(_sf1 == 0xFF && _sf2 == 0xFF))
    sb.Append(" SF=").Append(_sf1.ToString("X2")).Append(_sf2.ToString("X2"));
if (_data.Length > 0) { sb.Append(" len="); sb.Append(_data.Length); }
sb.Append(" (").Append(_message).Append(") ").Append(_elapsedMs).Append("ms");
```

書式は `ステータス [idm=...] [SF=XXXX] [len=N] (メッセージ) 経過ms` です。値が無い項目は省きます。

~~~
Ok idm=0100000000000001 SF=0000 len=16 (1 ブロック（16 バイト）を読み出した) NNms
CardRejected idm=0100000000000001 SF=FFA2 (カードが読み出しを拒否した) NNms
NoCard (対象カードの応答を観測しなかった（物理的な不存在の証明ではない）) NNms
~~~

`Pcsc` のログは、この文字列の先頭に `[pcsc] ` を付けたものです。

### 3.4 不変性

フィールドはすべて `private readonly` で、`get` だけのプロパティから返します。
C# 6 の「get のみの自動プロパティ」（`public PcscStatus Status { get; }`）を使わずに、
生成後に値が変わらない型にしています（ただし `Data` の配列の中身は前述のとおり書き換えられます）。

---

## 4. `PcscStatus` — ステータス

| 値 | 数値 | 意味 | 返す場所 | 業務処理の目安 |
|---|---|---|---|---|
| `Ok` | 0 | 成功。応答の検証まで完了している | すべての API | 続行してよい |
| `InvalidArgument` | 1 | 引数が不正。I/O は行っていない | `Read` / `Write` | 呼び出し側の誤り |
| `NotConnected` | 2 | 未接続で Read / Write を呼んだ | `Read` / `Write` | 呼び出し側の誤り |
| `AlreadyConnected` | 3 | 接続中に Connect を呼んだ | `Connect` | 呼び出し側の誤り |
| `ReaderError` | 4 | リーダが見つからない、または winscard の呼び出しが失敗した | `Connect` / `Disconnect` | 中断。再試行を推奨（README §8.1） |
| `NoCard` | 5 | 対象カードの応答を観測しなかった。**物理的な不存在の証明ではない** | `Read` / `Write` | 中断・置き直して再検査 |
| `MultipleCards` | 6 | 異なる IDm を 2 種類以上観測した（重ね置き）。**読み書きしていない** | `Read` / `Write` | 中断 |
| `Indeterminate` | 7 | 通信異常・RF 衝突・応答途切れ・読み戻し不一致など。**結果を保証しない** | `Read` / `Write` | 中断。Write ならカードの内容を確認 |
| `CardRejected` | 8 | カードがステータスフラグでエラーを返した | `Read` / `Write` | 中断。`StatusFlag1/2` を確認 |

**`Ok` 以外はすべて失敗として扱ってください。**

`Indeterminate` と `CardRejected` の違いは「カードの状態が分かっているか」です。

- `CardRejected` — カードの応答は正常に読めた。カードが明確に拒否したので、その要求は実行されていない。
- `Indeterminate` — 応答が壊れている、途中で途切れた、などで何が起きたか分からない。
  Write の場合は、**書き込まれたかどうかも分からない**ので、読み出して確認する必要があります。

各ステータスがどの条件で返るかの詳細は、[L1_Pcsc.md](L1_Pcsc.md) の「ステータスの出どころ」を参照してください。

---

## 5. C# 5 で書くための工夫

| 箇所 | 書き方 | 使っていない C# 6 以降の構文 |
|---|---|---|
| `PcscOptions` の既定値 | コンストラクタで代入 | 自動プロパティ初期化子 |
| `PcscResult` の読み取り専用プロパティ | `private readonly` フィールド + `get { return _x; }` | get のみの自動プロパティ、式形式メンバ |
| `null` を空に置き換える | `message == null ? "" : message` | —（`??` でも C# 5 で書けるが、明示している） |
| 例外の引数名 | `"TransceiveTimeoutUs"` などの文字列 | `nameof` |

## 6. 関連テスト

[`test/PcscIfTest/SelfTest.cs`](PcscIfTest_SelfTest.md)

| セクション | 確認内容 |
|---|---|
| 9. オプション検証とログ | 各項目の範囲外が `new Pcsc(...)` で例外になる、生成後の書き換えが効かない（スナップショット）、ログ処理の例外で判定が変わらない |
| 7. Read と重ね置きチェック | 不正応答で SF を報告しない（`FF FF`）、IDm は報告する |
| 8. Write と重ね置きチェック | 不正な書き込み応答で SF を報告しない、メッセージに「書き込まれたか不明」が入る |
