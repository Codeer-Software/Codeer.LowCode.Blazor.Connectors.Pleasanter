# Codeer.LowCode.Blazor.Connectors.Pleasanter - Project Guide

## プロジェクト概要

Codeer.LowCode.Blazor (CLB) のモジュールをプリザンター (Implem.Pleasanter) のテーブル (サイト) に結び付けるコネクター。
NuGet パッケージ `Codeer.LowCode.Blazor.Connectors.Pleasanter.Server` / `Codeer.LowCode.Blazor.Connectors.Pleasanter.Designer` (MIT) として配布する。

- **ターゲット**: .NET 8.0 / C# (Nullable 有効, ImplicitUsings 有効)
- **本体リポジトリ**: `C:\tfs\codeer\Codeer.LowCode.Blazor` (参照用、直接編集しない)。本体は NuGet パッケージで参照する
- **作りの手本**: `C:\GitHub\Codeer.LowCode.Blazor.Connectors.FileMaker` (同じ構成の先行コネクター。プロジェクトの分け方・csproj・テストのハーネス・Example・Designer の結線はここに揃える。迷ったら FileMaker 版と同じにする)
- **プリザンターのソース**: `C:\GitHub\Implem.Pleasanter` (1.5.9.1。読むだけ)。検索条件の実装は `Implem.Pleasanter/Libraries/Settings/View.cs` の `SetColumnsWhere` 以降
- **調査資料**: `Docs/01_pleasanter_api_inventory.md` (API 調査) / `Docs/02_pleasanter_specification_draft.md` (仕様案)。外部の調査者の提案で、このリポの決定ではない (食い違うところはこのファイルが優先)

## リポジトリ構成

```
Source/
├── Codeer.LowCode.Blazor.Connectors.Pleasanter.slnx       # Server と Test (Designer・Example は別に足す)
├── Codeer.LowCode.Blazor.Connectors.Pleasanter.Server/   # サーバー側ライブラリ (WPF・ASP.NET に依存しない)
│   ├── PleasanterDbAccessor.cs          # IDbAccessor のデコレータ (公開の入口)。書き込み (作成・更新・削除) の組み立てもここ
│   ├── PleasanterConnection.cs          # 接続 1 つ: 送信 (再試行・エラー分類)・定義 (getsite)・一覧の読み出し・書き込み・ブラウザのセッションの確認 (VerifySessionAsync)
│   ├── PleasanterSessionResult.cs       # VerifySessionAsync の結果 (Authenticated / Unauthenticated / Failed) と本人 (PleasanterIdentity)
│   ├── PleasanterConnectionSettings.cs  # ConnectionString "Provider=Pleasanter;..." の読み取り
│   ├── PleasanterTableDefinition.cs     # getsite のサイトの設定 → 表と列の定義 (列の種類・読み取り専用の判定・選択肢とリンク)
│   ├── PleasanterMetadataFile.cs        # 定義を保存したファイル (デザイナ用)
│   ├── SqlTranslator.cs                 # 本体の SQL → 読み出し・集計・書き込みの計画 (条件の構文木 FilterNode)
│   ├── SqlPlans.cs                      # 計画と条件の構文木の型
│   ├── PleasanterView.cs                # 条件の構文木 → View (ColumnFilterHash / ColumnFilterSearchTypes) と、読んだ行のメモリでの評価
│   ├── QueryExecutor.cs                 # 計画の実行 (値を集める条件の読み替え・ページ・メモリでの絞り込み / 並び / 集計・結合)
│   ├── PleasanterValues.cs              # 値の変換 (プリザンターの JSON ⇔ .NET・比較)
│   └── PleasanterException.cs
├── Codeer.LowCode.Blazor.Connectors.Pleasanter.Test/     # NUnit。HTTP の偽物 (Harness/FakePleasanter) の単体テストと、実物のプリザンターを相手にした結合テスト (Live/)
└── Example/                                              # サンプル (パッケージにしない)
```

パッケージは 2 つ: サーバーは `.Server`、デザイナは `.Designer` (`.Server` に依存)。**モックのプロジェクトは作らない** (実物のプリザンターで確かめる)。
`.Server` の csproj は FileMaker 版と同じく `GeneratePackageOnBuild` なので、ビルドすると bin に nupkg ができる。

## ビルド・テスト

```bash
cd Source
MSBUILDDISABLENODEREUSE=1 dotnet build Codeer.LowCode.Blazor.Connectors.Pleasanter.slnx -m:1
PLEASANTER_TEST_APIKEY_FILE=<API キーを書いたファイルのパス> dotnet test Codeer.LowCode.Blazor.Connectors.Pleasanter.Test --no-build
```

- `Live/` のテストは実物のプリザンターを相手にする。接続先は環境変数 `PLEASANTER_TEST_URL` (既定 `http://localhost:59803/`)、API キーは
  `PLEASANTER_TEST_APIKEY_FILE` (キーを書いたファイルのパス) か `PLEASANTER_TEST_APIKEY`。どちらも無ければ `Live/` は Ignore になる
- `Live/LiveFixture` が最初にテスト用のサイトを 4 つ作り (`api/items/0/createsite`・名前は `CLBコネクタテスト_..._<日時>`)、行を入れ、最後に消す (`deletesite`)。既存のサイトは触らない。
  テストが途中で落ちて消し損ねたら、名前で探して手で消す
- API キーはテナントの管理者 (サイトを作れる利用者) のもの。キーの中身をログ・テストの出力に出さない
- 偽物だけのテストを流すなら `--filter "FullyQualifiedName!~.Live."` (数秒)。`Live/` は 4〜5 分 (行を 230 行ほど 1 件ずつ作るため)
- `.Designer` と `Example/LowCodeApp.Designer` は WPF なので dotnet CLI ではなく VS の MSBuild でビルドする (コマンドは FileMaker 版の CLAUDE.md)

## 設計: プリザンターを IDbAccessor として見せる

本体の `ModuleDataIO` にデータソースごとに処理を差し替える口は無いが、`IDbAccessor` はホストが実装を渡す公開のインターフェース。
プリザンター用のデータソースだけを横取りするデコレータ (`PleasanterDbAccessor`) にする。**本体への変更は無い。** (FileMaker 版と同じ形)

```
ModuleDataIO (認可・インターセプタ・一覧/保存の組み立て = 本体のまま)
  └─ ModuleDataIOForDb が SQL を組む (データソースは PostgreSQL の方言)
       └─ PleasanterDbAccessor
            ├─ プリザンターのデータソース → SqlTranslator (計画) → QueryExecutor / 書き込み → PleasanterConnection (POST api/items/...)
            └─ それ以外 → 元の IDbAccessor
```

結線はホストの 2 行 (`PleasanterConnection.FromDataSources(dataSources)` と `new PleasanterDbAccessor(inner, connections)`)。DI の登録は要らない。

### 設定

データソースの ConnectionString に書く:

```
Provider=Pleasanter;Url=https://example.com/pleasanter/;ApiKey=...;Sites=Projects:1200,Customers:1300;MaxRows=10000;TimeoutSeconds=100;EmptyDate=1899-12-30
```

- `DataSourceType` は `PostgreSQL` と書く (`GetDataSource` も PostgreSQL を返す)。本体の enum にプリザンターを足さない
- `Url`: アプリのルート (仮想ディレクトリまで)。`https://` だけ。`http://` は localhost / 127.0.0.1 だけ許す。クエリ・フラグメント・ユーザー名は不可
- `ApiKey`: 連携用の利用者の API キー (接続文字列ごと秘密として扱う)
- `Sites`: `表名:サイトID` の並び。**表名 (モジュールの DbTable) はここで付ける別名**。検証と本番でサイト ID が違ってもデザインは変わらない。ここに無いサイトは触れない。別名は英数字と `_`。重複・不正な ID は設定エラー
- `MaxRows` (既定 10000): ページングなしの読み出し・メモリで評価する読み出しで受け取る行の上限。超えたらエラー (黙って切り捨てない)
- `TimeoutSeconds` (既定 100)・`EmptyDate` (既定 `1899-12-30`。プリザンターの「空の日付」の値。General.json の MinTime を変えた環境用)
- 書き方の誤りは黙って既定値にしない (`PleasanterException`)。メッセージに API キーを入れない

### 公開の型

| 型 | 内容 |
|---|---|
| `PleasanterDbAccessor(IDbAccessor inner, IEnumerable<PleasanterConnection> connections, string? metadataDirectory = null)` | デコレータ。`metadataDirectory` を渡すと表の定義を保存したファイルから答える (デザイナ用) |
| `PleasanterConnection` | `PleasanterConnection(PleasanterConnectionSettings)` / `static List<PleasanterConnection> FromDataSources(IEnumerable<DataSource>)` (同じ設定には同じ接続) / `Settings` / `VerifySessionAsync(...)` / `DefaultSessionCookiePrefixes` / `DefaultWhoAmISqlName` |
| `PleasanterConnectionSettings` | `FromDataSource(DataSource)` (プリザンター用でなければ null)・各設定のプロパティ・`ProviderName`・`DefaultEmptyDate` |
| `PleasanterTableDefinition` / `PleasanterColumnDefinition` / `PleasanterChoice` / `PleasanterColumnKind` | 表と列の定義 (`ToTableDefinition()` で本体の DbTableDefinition) |
| `PleasanterMetadataFile` | `GetPath(directory, dataSourceName)` (`pleasanter.<データソース名>.metadata.json`) / `UpdateAsync(PleasanterConnection, directory)` (取りに行って書き換える。フォルダは作らない) |
| `PleasanterException` | `Kind` (`PleasanterErrorKind`: Authentication / Forbidden / NotFound / InvalidRequest / NotSupported / ServiceUnavailable / WriteResultUnknown / Unknown)・`StatusCode` (HTTP)・`ApiStatusCode` (JSON の StatusCode) |
| `PleasanterSessionResult` / `PleasanterSessionStatus` / `PleasanterIdentity` | `Status` (Authenticated / Unauthenticated / Failed)・`Identity` (TenantId / UserId / LoginId / Name)・`Message` |

### 表と列

- 列名 (DbColumn) = プリザンターの内部の列名そのまま。記録テーブル (Results): `ResultId` (キー)・`Title`・`Body`、期限付きテーブル (Issues): `IssueId` (キー)・`Title`・`Body`・`StartTime`・`CompletionTime`・`WorkValue`・`ProgressRate`・`RemainingWorkValue`、
  共通: `Status`・`Manager`・`Owner`・`Ver`・`Creator`・`Updator`・`CreatedTime`・`UpdatedTime`、拡張の列: `ClassA`…・`NumA`…・`DateA`…・`DescriptionA`…・`CheckA`…
- **拡張の列は `SiteSettings.EditorColumnHash` (全部のタブ) と `GridColumns` に出てくるものだけ。** `Columns` (列の設定) だけにある列は定義に入れず、表示名・選択肢などの設定として使う。
  理由: プリザンターの API が行に入れる列は `SiteSettings.ReadableColumns` = エディタの列 ∪ 一覧の列 ∪ 標準の列で、`Columns` だけにある列はいつも空で返るため (ソースで確認。仕様の指示「`Columns` に出てくる列も入れる」から外した)。
  セクション (`_Section-n`)・リンク (`_Links-n`) は名前の形で外れる。空のサイトでも定義が作れる (先頭のレコードから推し量らない)
- `Comments`・`Attachments*`・`Locked`・`SiteId` などは列にしない (未対応)
- 型: Class / Description / Title / Body → string、Num と Status / Manager / Owner / Creator / Updator / Ver / キー → decimal、
  Date → 列の設定 `EditorFormat` が無いか `Ymd` なら DateOnly (`date`)、それ以外 DateTime (`timestamp`)、CreatedTime / UpdatedTime / StartTime / CompletionTime → DateTime、Check → bool
- 数値の列が null を持てるかは列の設定 `Nullable` (既定は持てない = 空は 0)。本体へは全部の列を null 可で見せる (NOT NULL にすると本体が空を型の既定値に置き換えて書くため。FileMaker と同じ)
- 読み取り専用 (判定は `PleasanterTableDefinition.IsReadOnlyColumn` の 1 か所): キー・`Ver`・`Creator`・`Updator`・`CreatedTime`・`UpdatedTime`・`RemainingWorkValue` (作業量と進捗率から計算される)。
  作成で値があればエラー・更新の対象に入っていたらエラー
- タイトルを複数の列で作るサイト (`TitleColumns` が `["Title"]` 以外) は `TitleIsRaw = false`。そのサイトの `Title` の条件・並びはプリザンターでは表示用のタイトルで評価されるので、メモリで評価・並べる
- 選択肢 (`ChoicesText`) は 1 行 1 つ (`値,表示[,短い表示,CSS]`)。`[[サイトID]]` (オプションは `[[1200,NoAddButton]]` のように括弧の中) の行はリンク (`Sites` にあれば相手の表名)。ほかの `[[...]]` (ユーザー・組織) は選択肢にしない。
  画面へは Enum の定義・LinkField に写す (実行時は使わない)。複数選択 (`MultipleSelections`) の列は条件をメモリで評価する (値の持ち方を解釈しない)
- 日時は「API キーの利用者のタイムゾーンの壁時計の値」。UTC に変換しない (CLB 側は SaveAsUtc オフの前提)
- 値の読み方: 文字の `""` は null、日付の EmptyDate は null、チェックの値が無ければ false (チェックの列は null を持たない)

### 読み出し: SQL → View とメモリでの評価

SQL の where を構文木 (`FilterNode`) に読み、**プリザンターの View に正確に写せる条件だけ View に書く。写せない条件は「真」に緩めた View で読み、読んだ行に SQL の条件の全体をメモリで評価する (SQL の 3 値の論理)。**

- 緩めたとき (`ViewFilter.IsExact = false`): 条件に合う行を全部読み (200 行ずつページをたどる・`MaxRows` を超えたらエラー)、メモリで絞り・ページを切る・件数を数える。並びはサーバーの並びのまま (絞るだけなので順は保たれる)
- 正確なとき: ページ・件数ともサーバー (`Offset` / `PageSize`、件数は `PageSize=1` の `TotalCount`)。1 回の上限 (200 行) より大きいページは、受け取った行数だけ Offset を進めて続きを読む
- 並び: order by を `ColumnSorterHash` に。**最後にキーの列の昇順を必ず足す** (書いてあれば足さない。足さないとプリザンターが更新日時の降順を足す)。
  結合先の列・リンクの列 (プリザンターでは参照先のタイトルで並ぶ)・`TitleIsRaw = false` の `Title` での並びは、条件に合う行を全部読んでメモリで並べる (文字は序数・空は昇順で先、数値・日付の空は昇順で末尾 = 実物のプリザンターの並びに合わせた)

View に写す葉 (値は ColumnFilterHash に JSON の配列の文字で書く):

| 葉 | View | 正確か |
|---|---|---|
| 文字 `=` / In | `["a","b"]` + `ExactMatchMultiple` | 正確 |
| 文字 `like '%x%'` | `["x"]` + `PartialMatchMultiple` | 正確。ただし同じ列に一致 (`=` / In) もある条件では緩める (検索の種類は列ごとに 1 つ) |
| 文字 `is null` | `["\t"]` | 正確 (`Title` の `is null` は緩める) |
| 文字の値が `""` / タブ | — | 緩める |
| 数値 `=` / In / `>=` / `<=` | `["v,v"]` / `["v,"]` / `[",v"]` (一致も範囲で書く) | 正確 |
| 数値 `<` / `>` | `<=` / `>=` で送る | 緩める (メモリで評価) |
| 数値 `is null` | `["\t"]` | null を持てる列だけ正確 (持てない列は緩める。読むと 0) |
| 日付 `=` / In / `>=` / `<=` / `is null` | ISO の形 `yyyy-MM-ddTHH:mm:ss` の範囲 / `["\t"]` | 正確。値が EmptyDate なら緩める |
| CreatedTime / UpdatedTime・秒より細かい値の日時 | 前後 1 秒に広げた範囲 | 緩める (システムの日時は秒より細かい値を持つ・API は秒までを返す) |
| bool `=` | `"true"` / `"false"` | 正確 (`false` は値の無い行も含む) |
| `RemainingWorkValue` の条件 | — | 緩める (View に書くとプリザンターがエラー画面へのリダイレクトを返す。実物で確認) |
| 複数選択の列・`TitleIsRaw = false` の `Title` | — | 緩める |
| `<>`・`not (...)`・`is not null`・Not In | — | 緩める (否定フィルタ `ColumnFilterNegatives` はサイトの設定 `UseNegativeFilters` がオフだと黙って無視されるので使わない) |
| 集めた値との一致 (In) が 500 個を超える・bool の In | — | 緩める |

- and は `and_n`、or は `or_n` の中に子ごとの `and_n` (同じ列を 2 回書けるように子を 1 段包む)。or の中に緩めた子があれば or 全体が真。同じ列・同じ検索の種類の葉の or は 1 つの葉 (値の並び) にまとめる
- 列の種類で表せない値 (数値の列に文字など) はどの行とも一致しない (メモリでも同じ)
- メモリでの評価: 文字の `=` は序数 (大文字・小文字を区別)、like は大文字・小文字を区別しない (PostgreSQL のプリザンターで `ilike` だったため)、文字の `""` は null

#### 別の表を読んで値を集める条件 (FileMaker 版から移植)

- 結合先の列の条件 (`Customer.Name like ...`) と一覧をまたぐ条件 (`exists (select ... where 子.リンク = 親.キー and ...)`) は、相手の表を先に読んで値を集め、主表の「値のどれかに一致」(`ValuesNode`) にする。値は主表の列の種類に変換する
- 受け付けない形 (2 列の比較・リンク以外で外の表を指す条件・or / not の中のリンク・結合先を経由したリンク・リンクの列が複数) は `NotSupported` で拒否する

#### 結合 (LinkField の表示名の left join)

主表を読んだあと、結合元の値を集めて結合先を読み (50 個ずつの In の条件)、メモリで突き合わせる。キャストつきの結合 (`CAST(... AS text)`) も受け付ける。

#### 集計

プリザンターの API に集計は無いので、**条件に合う行を全部読んでメモリで集計する** (`MaxRows` まで)。軸・count / count(distinct) / sum / avg / min / max・having・order by・limit / offset・
行を選ぶ集計 (`WithinGroupsOf` = 別の集計の上位の組の中での集計) を受け付ける。日付を期間に丸める軸 (`date_trunc` など) は拒否する (サイトに期間の列を足して軸にするよう案内する)。
入れ子の WithinGroupsOf・別の表の組の中での集計は拒否する。

#### 受け付ける SQL / 拒否する SQL (まとめ)

- 受け付ける: 本体の `SelectSqlCreator` の一覧・件数 (`count(*)`)・LinkField の left join・exists・`AggregateSqlCreator` の集計・`insert ... values (...), (...) returning キー`・`update ... set ... where キー = @p [and ...]`・`delete from ... where キー = @p [and ...]`
- 拒否する (`PleasanterException`): union・関数の比較 (`lower(...)` など)・inner join・前方一致 / 後方一致の like・escape 付きの like・上記の受け付けない exists・日付の期間の軸・`ExecuteSqlCommandAsync` (ExecuteSqlField の SQL / ストアド)・`GetConnection`

### 書き込み

- 作成: `POST api/items/{siteId}/create`。拡張の列は `ClassHash` / `NumHash` / `DateHash` / `DescriptionHash` / `CheckHash` の中に入れる。複数行の insert は 1 件ずつ作り、途中で失敗したら作った件数をメッセージに入れる (取り消さない)
- 更新・削除: `POST api/items/{id}/update` / `delete`。**レコードの URL は別のサイトの行でも通るので、先に「サイト ID の URL + SQL の where 全体」で件数を読み、0 件なら API を呼ばずに 0 件を返す。**
  確かめてから書くまでの間は防げない (FileMaker の楽観ロックと同じ扱い)。OptimisticLockingField (UpdatedTime など) もこの確認の条件に入る
- 空にする値: 文字 → `""`、拡張の数値 → null、標準の数値 (null を持てない) → 0、日付 → EmptyDate (`null` と `""` は HTTP 400 になるため)、チェック → false (`CheckA: null` は 400)
- 書き込みはやり直さない。送ったあとの通信の失敗・5xx・想定外の応答は `WriteResultUnknown`
- 保存はデータソースごと・1 件ごとに確定する (トランザクションに入らない・ロールバックで戻らない)。DB と混ざる保存も拒否しない

### 接続 (PleasanterConnection)

- 全部 POST・JSON。本文に `ApiVersion: 1.1` と `ApiKey`。**成功は「HTTP 200 かつ JSON の StatusCode が 200」。** リダイレクトには従わない (本文の API キーが別の宛先へ送られるため) → エラー
- 分類: HTTP が成功で JSON の StatusCode があればその値、なければ HTTP の状態で 401 Authentication / 403 Forbidden / 404 NotFound / 429・5xx ServiceUnavailable / ほかの 4xx InvalidRequest / それ以外 Unknown。応答の `Message` を入れる。API キー・ホスト名は入れない (通信の失敗の詳細は InnerException だけ)
- 再試行: 読み出しは 502 / 503 / 504・通信の失敗を 3 回まで (最大 4 回・指数の待ち)。429 はやり直さない (日次の上限のこともある)
- 定義: 表ごとに `api/items/{siteId}/getsite` を最初の 1 回読んで接続が生きている間持つ (サイトの設定を変えたらアプリを再起動する)。応答の `SiteId` と `ReferenceType` (Results / Issues) を確かめる。
  Wiki・フォルダ・ダッシュボードなどのサイト・サイト統合 (`IntegratedSites`) のサイトは理由つきのエラー

### 定義のファイル (デザイナ用)

`PleasanterMetadataFile.UpdateAsync` が `pleasanter.<データソース名>.metadata.json` に表と列 (種類・読み取り専用・null を持てるか・表示名・選択肢・リンク・複数選択) を書く。
一時ファイルに書いてから置き換える (読めなければファイルを変えない)。接続先・API キーは書かない。`PleasanterDbAccessor` に `metadataDirectory` を渡すとこのファイルから答え、プリザンターに取りに行かない (ファイルが無ければ表は無い)。

### ブラウザのセッションの確認 (SSO)

ブラウザが持つプリザンターの認証 Cookie を、CLB のサーバーがプリザンターの拡張 SQL API (`POST {Url}api/extended/sql`・`Name: ClbWhoAmI`) に転送し、プリザンターが解決した本人を受け取る。
プリザンター側に `App_Data/Parameters/ExtendedSqls/ClbWhoAmI.json` (本人の TenantId / UserId / LoginId / Name を返す拡張 SQL) を置く。

- **ライブラリは確認だけ**: `Task<PleasanterSessionResult> VerifySessionAsync(IEnumerable<KeyValuePair<string, string>> cookies, IEnumerable<string>? cookieNamePrefixes = null, string extendedSqlName = "ClbWhoAmI", CancellationToken cancellationToken = default)`
- 結果は例外でなく型で返す (Authenticated + 本人 / Unauthenticated / Failed)。転送する Cookie は名前の前方一致 (既定 `.AspNetCore.Cookies` / `Pleasanter_SessionGuid` / `.AspNetCore.Session`)。`;`・改行を含む値・`=` を含む名前は送らない
- API キーは送らない。Cookie は要求ごとにヘッダに書き (Cookie を持たない別の HttpClient)、どこにも保存しない。1 回だけ送る (やり直さない)
- HTTP 401・JSON の StatusCode 401・UserId が 0 以下 → Unauthenticated。リダイレクト・HTML・ほかのエラー・行が無い → Failed
- Controller・Cookie の発行・再検証・待ち画面はホスト (Example)

## Designer

(別の担当が書く)

## Example

(別の担当が書く)

## 決めてあること (2026-11-16)

- **`IDbAccessor` のデコレータ。本体は変えない。** `DataSourceType` は `PostgreSQL` と書かせる
- **モックは作らない。** 実物のプリザンターにテスト用のサイトを作って確かめる
- **表名は接続文字列の `Sites` で付ける別名。** ここに無いサイトは触れない
- **連携用の API キー 1 つでつなぐ。** 利用者ごとの認可は CLB (モジュールの権限) が担保する
- **保存はデータソースごと・1 件ごとに確定する。** 調査資料 02 の「複数変更の事前拒否」「編集識別子」は作らない
- **View に正確に写せない条件は、広めの条件で全部読んでメモリで評価する。** 黙って違う結果を返さない・`MaxRows` を超えたらエラー
- **否定フィルタは使わない** (サイトの設定次第で黙って無視される)
- **更新・削除は先にサイトの URL で行を確かめる** (レコードの URL は別のサイトの行でも通る)
- **集計はメモリ。** 日付の期間の軸は拒否
- **要求の分割 (FileMaker 版の「1 回の要求に入らない数の値を組に分ける」) は移植しない。** 値が 500 個を超える一致は緩めてメモリで評価する
- **拡張の列はエディタと一覧に出てくるものだけ** (上記。`Columns` だけの列は API の行に入らない)
- **SSO はライブラリは確認だけ・結線はホスト**

## 守ること

- 本体は core のリポで変える (このリポから直接編集しない)。足りない口はユーザーに相談
- ホスト (Starter・Example) に置くべきもの (Controller・DI の要求) をライブラリに持ち込まない
- 実物のプリザンターで確かめていないことを「確認済み」と書かない
- 公開リポジトリ。API キー・パスワード・接続文字列・ホスト名の実物をコード・テスト・ドキュメント・ログ出力に書かない
- テストで作ったサイトは消す。既存のサイトとそのレコードは触らない
- 文書 (README・Docs・CLAUDE.md・コメント) は日本語。会社名の表記は Codeer

## 確認済み・未確認・未実装 (2026-11-16 時点)

### 実物で確かめたこと (プリザンター 1.5.9.1・PostgreSQL・`Live/` のテストと手で送った要求)

- 一覧・件数・200 行を超えるページ (205 行のサイトで 2 ページ目・`TotalCount`)・`MaxRows` を超えたときのエラー
- 上の表の View の書き方 (文字の一致・部分一致・空・数値と日付の範囲・チェック・`and_n` / `or_n` の入れ子) が SQL の条件と同じ行を返すこと
  (`Live/LiveDifferentialTest` が約 75 の条件で View の道とメモリだけの道の結果・順・件数を突き合わせる)
- 結合先の列での条件・表示名・並び、exists、リンクの値での検索、集計 (リンクの軸・軸なし)
- 作成・更新・削除・空にする値 (上記の対応)・読み取り専用の列のエラー・別のサイトの行の ID では書かないこと・UpdatedTime の楽観ロック・期限付きテーブルの作成と RemainingWorkValue の計算・複数行の insert
- `RemainingWorkValue` を View の条件に書くと HTTP 302 (エラー画面 `/errors` へ)。並び (`ColumnSorterHash`) はエラーにならず、値の順に並んだ (3 行)
- 定義のファイルの書き出し (実物の getsite から)
- セッションの確認: ログインしていない Cookie を送ると認証済みにならないこと
- getsite の形・Issues の既定の列・CompletionTime の往復・リンクの値は ID の文字・システムの日時は秒より細かい値を持つ (+1 秒の範囲で当たる)・無い ID の update / delete は 403・JSON でない条件の値は黙って無視される・deletesite は 200

### 未確認

- **ログインしたセッションでの VerifySessionAsync の成功** (自動のテストではブラウザのログインを再現していない。`ClbWhoAmI` の応答の形はソースと既存の SSO の実装から)
- SQL Server・MySQL のプリザンターでの文字の比較 (大文字・小文字・全角半角・照合)。メモリでの評価は PostgreSQL のプリザンターに合わせてある (`=` は区別・like は区別しない)。照合の違う DB では View の道とメモリの道で結果が変わりうる
- メモリでの並び (文字は序数) とプリザンターの並び (DB の照合) の違い。メモリで並べるのは結合先の列・リンクの列・`TitleIsRaw = false` の Title のときだけ
- サイト統合・Wiki のサイトを Sites に書いたときの実物の getsite の応答 (エラーにする判定はソースと偽物のテストだけ)
- 複数選択の列・ユーザー / 組織の選択肢の列の値の形 (条件はメモリで評価・値は文字のまま)
- タイトルを複数の列で作るサイトでの Title の条件・並び (メモリにする判定は偽物のテストだけ)
- 書き込みの応答を受け取れなかったときの実物の挙動 (`WriteResultUnknown` は偽物のテストだけ)・429 (日次の上限) の応答
- 1 回の上限 (既定 200) をサイトの設定で変えた環境 (受け取った行数で Offset を進めるので動くはずだが未確認)
- General.json の MinTime を変えた環境 (`EmptyDate`)

### 未実装 (コネクターが未対応なだけで、プリザンターの制限ではないもの)

- 添付 (`Attachments*`)・コメント (`Comments`)・ロック (`Locked`)・プロセス・サイトのスクリプト / サーバースクリプトの起動
- ExecuteSqlField (拡張 SQL の呼び出しを含む)
- 日付を期間に丸める集計の軸
- 利用者本人の API キーでの接続 (利用者ごとの認可はプリザンター側では見ない)
