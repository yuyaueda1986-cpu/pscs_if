# Properties/AssemblyInfo.cs — アセンブリ属性

| 項目 | 内容 |
|---|---|
| ソース | [`src/Properties/AssemblyInfo.cs`](../../src/Properties/AssemblyInfo.cs) |
| 層 | —（ビルド設定） |
| 使う側 | 従来形式のプロジェクト `src/PcscIf.csproj` だけ |

---

## 1. このファイルの役割

ビルドされる `PcscIf.dll` に埋め込むアセンブリ属性（名前・説明・バージョンなど）を定義します。
処理のコードは含みません。

Visual Studio 2013 の従来形式（非 SDK）のプロジェクトは、アセンブリ属性を自動生成しないため、
このファイルで明示的に書く必要があります。

## 2. 属性

| 属性 | 値 | 意味 |
|---|---|---|
| `AssemblyTitle` | `"PcscIf"` | ファイルのプロパティに表示される説明 |
| `AssemblyDescription` | `"対象カード / 対象リーダ向け PC/SC インタフェース API（重ね置きチェック込み）"` | コメント |
| `AssemblyProduct` | `"PcscIf"` | 製品名 |
| `AssemblyCulture` | `""` | ニュートラル（サテライトアセンブリではない） |
| `ComVisible` | `false` | COM からは見えない |
| `Guid` | `"6c3f1e2a-5b47-4d9c-9e31-0a7b2d5f8c10"` | COM に公開する場合の型ライブラリの ID |
| `AssemblyVersion` | `"1.0.0.0"` | 参照解決に使うバージョン |
| `AssemblyFileVersion` | `"1.0.0.0"` | ファイルのプロパティに表示されるバージョン |

- `Guid` はプロジェクトの `ProjectGuid`（`{6C3F1E2A-5B47-4D9C-9E31-0A7B2D5F8C10}`）と同じ値です。
  `ComVisible(false)` なので、実際に COM で使われることはありません。
- `AssemblyDescription` には、公開リポジトリ向けに製品名を伏せた表現を使っています。

## 3. バージョンの上げ方

| 変更の種類 | 目安 |
|---|---|
| 不具合修正のみ（API 互換） | `AssemblyFileVersion` を上げる。`AssemblyVersion` は据え置いてもよい |
| API の追加・変更 | `AssemblyVersion` と `AssemblyFileVersion` の両方を上げる |

`AssemblyVersion` を変えると、強い名前付きで参照している側は再ビルドが必要になります
（このライブラリは強い名前を付けていないので、通常は影響しません）。

## 4. テストプロジェクトに含めない理由

`test/PcscIfTest/PcscIfTest.csproj`（SDK 形式、.NET 10）は、`src/*.cs` だけをリンクしており、
`src/Properties/` 配下は含めていません。

SDK 形式のプロジェクトは同じ属性を自動生成するため、このファイルも含めると
同じ属性が 2 回定義されてビルドエラー（CS0579）になるためです。

## 5. 文字コード

ほかのソースと同じく、**UTF-8（BOM 付き）**で保存しています。
`AssemblyDescription` に日本語を含むため、Visual Studio 2013 のコンパイラが文字コードを正しく判別できるように BOM を付けています
（プロジェクトにも `<CodePage>65001</CodePage>` を指定しています）。
