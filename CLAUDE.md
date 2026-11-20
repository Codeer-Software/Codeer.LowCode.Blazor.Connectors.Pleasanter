# Codeer.LowCode.Blazor.Connectors.Pleasanter - Project Guide

## プロジェクト概要

Codeer.LowCode.Blazor (CLB) のモジュールをプリザンター (Implem.Pleasanter) のテーブル (サイト) に結び付けるコネクター。
NuGet パッケージ `Codeer.LowCode.Blazor.Connectors.Pleasanter.Server` / `Codeer.LowCode.Blazor.Connectors.Pleasanter` (画面のフィールド) / `Codeer.LowCode.Blazor.Connectors.Pleasanter.Designer` (MIT) として配布する。

- **ターゲット**: .NET 8.0 / C# (Nullable 有効, ImplicitUsings 有効)
- **本体リポジトリ**: `C:\tfs\codeer\Codeer.LowCode.Blazor` (参照用、直接編集しない)。本体は NuGet パッケージで参照する
- **作りの手本**: `C:\GitHub\Codeer.LowCode.Blazor.Connectors.FileMaker` (同じ構成の先行コネクター。プロジェクトの分け方・csproj・テストのハーネス・Example・Designer の結線はここに揃える。迷ったら FileMaker 版と同じにする)
- **プリザンターのソース**: `C:\GitHub\Implem.Pleasanter` (1.5.9.1。読むだけ)。検索条件の実装は `Implem.Pleasanter/Libraries/Settings/View.cs` の `SetColumnsWhere` 以降
- **TODO**: `Docs/TODO.md` (これから対応する予定のもの。TODO はこのファイルに置いて Git で管理する)
- **調査資料**: `Docs/01_pleasanter_api_inventory.md` (API 調査) / `Docs/02_pleasanter_specification_draft.md` (仕様案)。外部の調査者の提案で、このリポの決定ではない (食い違うところはこのファイルが優先)

## リポジトリ構成

```
Source/
├── Codeer.LowCode.Blazor.Connectors.Pleasanter.slnx
├── Codeer.LowCode.Blazor.Connectors.Pleasanter.Server/   # サーバー側ライブラリ (WPF・ASP.NET に依存しない)
│   ├── PleasanterDbAccessor.cs          # IDbAccessor のデコレータ (公開の入口)。書き込み (作成・更新・削除) の組み立てもここ
│   ├── PleasanterConnection.cs          # 接続 1 つ: 送信 (再試行・エラー分類)・定義 (getsite)・一覧の読み出し・書き込み・ブラウザのセッションの確認 (VerifySessionAsync)
│   ├── PleasanterSessionResult.cs       # VerifySessionAsync の結果 (Authenticated / Unauthenticated / Failed) と本人 (PleasanterIdentity)
│   ├── PleasanterConnectionSettings.cs  # ConnectionString "Provider=Pleasanter;..." の読み取り
│   ├── PleasanterTableDefinition.cs     # getsite のサイトの設定 → 表と列の定義 (列の種類・読み取り専用の判定・選択肢とリンク)
│   ├── PleasanterMetadataFile.cs        # 定義を保存したファイル (デザインのプロジェクトのサーバー専用ファイル。デザイナと Web アプリが読む)
│   ├── SqlTranslator.cs                 # 本体の SQL → 読み出し・集計・書き込みの計画 (条件の構文木 FilterNode)
│   ├── SqlPlans.cs                      # 計画と条件の構文木の型
│   ├── PleasanterView.cs                # 条件の構文木 → View (ColumnFilterHash / ColumnFilterSearchTypes) と、読んだ行のメモリでの評価
│   ├── QueryExecutor.cs                 # 計画の実行 (値を集める条件の読み替え・ページ・メモリでの絞り込み / 並び / 集計・結合)
│   ├── PleasanterValues.cs              # 値の変換 (プリザンターの JSON ⇔ .NET・比較)
│   ├── PleasanterAttachments.cs         # 添付の列の値 (JSON) の読み解き: 書き込みの値 → 足すファイル・消す Guid / 読んだ値のファイルの Guid
│   ├── PleasanterAttachmentFile.cs      # 添付のダウンロード (PleasanterDbAccessor.GetAttachmentAsync) の結果
│   └── PleasanterException.cs
├── Codeer.LowCode.Blazor.Connectors.Pleasanter/          # 画面のフィールド (Razor クラスライブラリ・net8.0)。.Server と互いに参照しない (値は JSON の文字で渡る)
│   ├── PleasanterAttachmentsField*.cs / PleasanterAttachmentsFieldComponent.razor   # 添付のフィールド (デザイン・データ・実行時・コンポーネントの 4 つ)
│   └── PleasanterFieldsInitializer.cs   # Initialize() = このアセンブリを読み込む (本体は読み込み済みのアセンブリからフィールドの型を探す)
├── Codeer.LowCode.Blazor.Connectors.Pleasanter.Designer/ # デザイナ用 (WPF)。SpecDocs の登録・メタ情報の更新メニュー・デザイナ用の IDbAccessor
│   ├── PleasanterDesignerInitializer.cs # Initialize(env) = SpecDocs の登録・添付のフィールドをツールボックスに出す / Setup(env) = Tools のメニュー (メタ情報の更新) / CreateDbAccessor (定義はプロジェクトのメタ情報のファイルから読む)
│   └── SpecDocs/Pleasanter.md           # AI がプリザンターのモジュールを設計するための仕様 (デザイン側のことだけを書く)
├── Codeer.LowCode.Blazor.Connectors.Pleasanter.Test/     # NUnit。HTTP の偽物 (Harness/FakePleasanter) の単体テストと、実物のプリザンターを相手にした結合テスト (Live/)
└── Example/                                              # サンプル (パッケージにしない)。動かし方は Docs/Example.md
    ├── LowCodeApp.Designer/         # コネクターを組み込んだデザイナ (WPF・net8)。結線の見本 (App.xaml.cs / DbAccessorFactory.cs)。Design の AppUser のため Extras.Designer も組み込む
    ├── LowCodeApp.Server/ LowCodeApp.Client/ LowCodeApp.Client.Shared/   # Web アプリ (net10)。Starter の Cookie ホストを書き出したもの (FileMaker 版の Example と同じ)
    │                                # テンプレートとの違い: DataService.cs の PleasanterDbAccessor の 2 行 (表の定義は配られたデザインのメタ情報のファイル)・.Server の ProjectReference・appsettings (Main = SQLite / Pleasanter)・SSO (下記)・
    │                                # 添付のダウンロードの Controller (Controllers/PleasanterAttachmentsController.cs)・画面のフィールドのパッケージの参照と PleasanterFieldsInitializer.Initialize() (Program.cs と AppInfoService)
    │                                # プリザンターの接続文字列は appsettings に書かない (環境変数 ConnectionStrings__Pleasanter)
    ├── Design/                      # サンプルのデザイン (取引先・案件・作業・取引先別の案件金額・ユーザー)。ServerFiles/pleasanter.Pleasanter.metadata.json はサンプルのサイトから書き出したもの
    │                                # designer.settings.Development.json (接続文字列・配置先) は gitignore。作り方は Docs/Example.md
    ├── Data/main.sql                # ログインするユーザーの表と admin / admin
    ├── Data/setup-pleasanter.ps1    # プリザンターにサンプルのサイト (取引先・案件・作業) とデータを作るスクリプト
    ├── Pleasanter/ExtendedSqls/ClbWhoAmI.json   # SSO のためにプリザンターに置く拡張 SQL
    └── Tests/sso/                   # SSO の結合テスト (Playwright。実物のプリザンターにブラウザでログインする)
```

パッケージは 3 つ: サーバーは `.Server`、画面のフィールドは `Codeer.LowCode.Blazor.Connectors.Pleasanter` (クライアント用。本体だけに依存)、デザイナは `.Designer` (`.Server` と画面のフィールドに依存)。
`.Server` は画面のフィールドのパッケージを参照しない (フィールドの型は名前で見る)。**モックのプロジェクトは作らない** (実物のプリザンターで確かめる)。
csproj は FileMaker 版と同じく `GeneratePackageOnBuild` なので、ビルドすると bin に nupkg ができる。

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
- 偽物だけのテストを流すなら `--filter "FullyQualifiedName!~.Live."` (数秒)。`Live/` は 10 分あまり (行を 230 行ほど 1 件ずつ作る・差分のテストが条件ごとに 2 回読むため)
- `Live/LiveExtendedSqlTest` はプリザンターに拡張 SQL `ClbConnectorTestEcho` と `ClbWhoAmI` (API から使える `Api: true`) が置いてあるときだけ動く (無ければ Ignore)
- `.Designer` と `Example/LowCodeApp.Designer` は WPF なので dotnet CLI ではなく VS の MSBuild でビルドする
  (`"C:/Program Files/Microsoft Visual Studio/2022/Professional/MSBuild/Current/Bin/MSBuild.exe" <csproj> -restore -m:1 -v:m -p:UseSharedCompilation=false`)。
  `Example/LowCodeApp.Server` (net10) は dotnet CLI (`MSBUILDDISABLENODEREUSE=1 dotnet build Example/LowCodeApp.Server -m:1`)

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

結線はホストの 2 行 (`PleasanterConnection.FromDataSources(dataSources)` と `new PleasanterDbAccessor(inner, connections, PleasanterMetadataFile.FromDeployedDesign(designFileDirectory))`)。DI の登録は要らない。

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
- `TimeoutSeconds` (既定 100)・`EmptyDate` (既定 `1899-12-30`。プリザンターが空の日付として返す値 = OLE 日付の 0 の固定値で、General.json の MinTime ではない。空にする書き込みが効くのはこの値が MinTime (既定 1900/1/1) より前だから。MinTime を 1899-12-30 より前にした環境では空にできない = 未確認)
- 書き方の誤りは黙って既定値にしない (`PleasanterException`)。メッセージに API キーを入れない

### 公開の型

| 型 | 内容 |
|---|---|
| `PleasanterDbAccessor(IDbAccessor inner, IEnumerable<PleasanterConnection> connections, PleasanterMetadataFile? metadataFile = null)` | デコレータ。`metadataFile` を渡すと表の定義をそのファイルから読み、プリザンターに取りに行かない (ファイルに無い表はエラー)。null なら表ごとに最初の 1 回 getsite を読む |
| `PleasanterDbAccessor.GetAttachmentAsync(ModuleDataIO io, DesignData designData, string? moduleName, string? id, string? fieldName, string? guid)` | 添付のフィールドのファイル 1 つを現在の利用者の権限で読む (ホストのダウンロードの Controller が呼ぶ)。読めなければ null (理由は区別しない・例外を投げない)。下の「添付」 |
| `PleasanterAttachmentFile` | `GetAttachmentAsync` の結果。`FileName` (レコードの添付の一覧の名前)・`Content` |
| `PleasanterConnection` | `PleasanterConnection(PleasanterConnectionSettings)` / `static List<PleasanterConnection> FromDataSources(IEnumerable<DataSource>)` (同じ設定には同じ接続) / `Settings` / `VerifySessionAsync(...)` / `DefaultSessionCookiePrefixes` / `DefaultWhoAmISqlName` |
| `PleasanterConnectionSettings` | `FromDataSource(DataSource)` (プリザンター用でなければ null)・各設定のプロパティ・`ProviderName`・`DefaultEmptyDate` |
| `PleasanterMetadataFile` | `FromProject(projectDirectory)` (デザイナ: プロジェクトのサーバー専用ファイル) / `FromDeployedDesign(designFileDirectory)` (Web アプリ: 配られた App.zip の中) / `static UpdateAsync(PleasanterConnection, projectDirectory)` (getsite を読んでプロジェクトに書く) |
| `PleasanterException` | `Kind` (`PleasanterErrorKind`: Authentication / Forbidden / NotFound / InvalidRequest / NotSupported / ServiceUnavailable / WriteResultUnknown / Unknown)・`StatusCode` (HTTP)・`ApiStatusCode` (JSON の StatusCode) |
| `PleasanterSessionResult` / `PleasanterSessionStatus` / `PleasanterIdentity` | `Status` (Authenticated / Unauthenticated / Failed)・`Identity` (TenantId / UserId / LoginId / Name)・`Message` |

表と列の定義 (`PleasanterTableDefinition` / `PleasanterColumnDefinition` / `PleasanterChoice` / `PleasanterColumnKind`) は internal (外へは定義のファイルと本体の DbTableDefinition で見せる)。

画面のフィールドのパッケージ (`Codeer.LowCode.Blazor.Connectors.Pleasanter`) の公開の型:

| 型 | 内容 |
|---|---|
| `PleasanterAttachmentsFieldDesign` | 添付のフィールドの設計。`DbColumn` (添付の列)・`MaxFileSizeMB` (1 ファイルの上限・既定 10)・`const DownloadUrl` (`api/pleasanter/attachments/download`。ホストのダウンロードの口のパス) |
| `PleasanterAttachmentsFieldData` | 値 = 添付の列の JSON の文字 (`ValueFieldDataBase<string>`) |
| `PleasanterAttachmentsField` | 実行時のフィールド (`FieldBase<Design>`)。スクリプトに出すのは本体の FieldBase のものだけ (値・ファイルの操作は出さない) |
| `PleasanterAttachmentsFieldComponent` | 画面 (Bootstrap のクラスとインラインのスタイルだけ。スコープ付き CSS を持たない) |
| `PleasanterFieldsInitializer.Initialize()` | このアセンブリを読み込む。クライアントとサーバーの起動時に呼ぶ (デザイナは `.Designer` の Initialize が呼ぶ) |

### 表と列

- 列名 (DbColumn) = プリザンターの内部の列名そのまま。記録テーブル (Results): `ResultId` (キー)・`Title`・`Body`、期限付きテーブル (Issues): `IssueId` (キー)・`Title`・`Body`・`StartTime`・`CompletionTime`・`WorkValue`・`ProgressRate`・`RemainingWorkValue`、
  共通: `Status`・`Manager`・`Owner`・`Ver`・`Creator`・`Updator`・`CreatedTime`・`UpdatedTime`・`Comments`、拡張の列: `ClassA`…・`NumA`…・`DateA`…・`DescriptionA`…・`CheckA`…・`AttachmentsA`…
- **拡張の列は `SiteSettings.EditorColumnHash` (全部のタブ) と `GridColumns` に出てくるものだけ。** `Columns` (列の設定) だけにある列は定義に入れず、表示名・選択肢などの設定として使う。
  理由: プリザンターの API が行に入れる列は `SiteSettings.ReadableColumns` = エディタの列 ∪ 一覧の列 ∪ 標準の列で、`Columns` だけにある列はいつも空で返るため (ソースで確認。仕様の指示「`Columns` に出てくる列も入れる」から外した)。
  セクション (`_Section-n`)・リンク (`_Links-n`) は名前の形で外れる。空のサイトでも定義が作れる (先頭のレコードから推し量らない)
- `Comments` (コメント) は文字の列: 読むとプリザンターが返す JSON の文字 (`[{"CommentId","CreatedTime","Creator","Body"}...]`・新しい順・無ければ `"[]"`)、
  書くと空でない文字を新しいコメント 1 件として足す (空・null は送らない。前のコメントは変わらない・消せない)。条件・並び・集計に使うと `NotSupported`
  (View の条件 `{"Comments":[...]}` は実物で 1 行も返さなかった・値が JSON のため)。読み込んだレコードのフィールドには JSON が入り、それを書き換えて保存すると書き換えた文字全体が 1 件のコメントになる
  (本体は変えたフィールドだけ送るので、変えなければ送らない)。本体に「読むだけ・書くだけ」の列を設計で選ぶ口が無いので、読みと書きで値の意味が違うのはこの列の制限として文書に書いてある
- `AttachmentsA`… (添付) は種類 `Attachments` の列 (本体へは文字・`RawDbTypeName` は `pleasanter_attachments`)。値はファイルの並びの JSON の文字。読み書きの意味は下の「添付」。条件・並び・集計に使うと `NotSupported`
- `Locked`・`SiteId` などは列にしない (未対応)
- 型: Class / Description / Title / Body → string、Num と Status / Manager / Owner / Creator / Updator / Ver / キー → decimal、
  Date → 列の設定 `EditorFormat` が無いか `Ymd` なら DateOnly (`date`)、それ以外 DateTime (`timestamp`)、CreatedTime / UpdatedTime / StartTime / CompletionTime → DateTime、Check → bool
- 数値の列が null を持てるかは列の設定 `Nullable` (既定は持てない = 空は 0)。本体へは全部の列を null 可で見せる (NOT NULL にすると本体が空を型の既定値に置き換えて書くため。FileMaker と同じ)
- 読み取り専用 (判定は `PleasanterTableDefinition.IsReadOnlyColumn` の 1 か所): キー・`Ver`・`Creator`・`Updator`・`CreatedTime`・`UpdatedTime`・`RemainingWorkValue` (作業量と進捗率から計算される)。
  作成ではキー以外 (プリザンターが決める列) は値があっても送らない・作成でキーに値があればエラー・更新の対象に入っていたら (null でも) エラー。
  作成で黙って外すのは、レコードのコピー (CopyModuleButtonField) が作成日時などを表示しているフィールドの値も写して作成に乗せるため (2026-11-20)。
  本体のシステムフィールドの名前 (`UpdatedAt` / `Updater` は更新で、`DeletedAt` / `Deleter` は論理削除で書かれる) を読み取り専用の列に付けると保存がエラーになる。`CreatedAt` / `Creator` は作成のときだけ書かれるのでエラーにはならない (値は送らない)
- タイトルを複数の列で作るサイト (`TitleColumns` が `["Title"]` 以外) は `TitleIsRaw = false`。そのサイトの `Title` の条件・並びはプリザンターでは表示用のタイトルで評価されるので、メモリで評価・並べる
- 選択肢 (`ChoicesText`) は 1 行 1 つ (`値,表示[,短い表示,CSS]`)。`[[サイトID]]` (オプションは `[[1200,NoAddButton]]` のように括弧の中) の行はリンク (`Sites` にあれば相手の表名)。ほかの `[[...]]` (ユーザー・組織) は選択肢にしない。
  画面へは Enum の定義・LinkField に写す (実行時は使わない)。複数選択 (`MultipleSelections`) の列は条件をメモリで評価する (値の持ち方を解釈しない)
- 日時は「API キーの利用者のタイムゾーンの壁時計の値」。UTC に変換しない (CLB 側は SaveAsUtc オフの前提)
- 日付だけの列 (`Ymd`) も時刻を持てる (API で時刻を送ると残る。実物で確認)。読むと日付に丸める (DateOnly)
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
| 文字の値が `Own` (`=` / In / like) | — | 緩める (プリザンターはユーザー・組織の選択肢の列 (`[[Users]]` など) の条件の `Own` を API キーの利用者の ID に読み替える。実物で確認。どの文字の列でも緩め、メモリで文字の `Own` として比べる) |
| 数値 `=` / In / `>=` / `<=` | `["v,v"]` / `["v,"]` / `[",v"]` (一致も範囲で書く) | 正確 |
| 数値 `<` / `>` | `<=` / `>=` で送る | 緩める (メモリで評価) |
| 数値 `is null` | `["\t"]` | null を持てる列だけ正確 (持てない列は緩める。読むと 0) |
| 日時 `=` / In / `>=` / `<=` / `is null` | ISO の形 `yyyy-MM-ddTHH:mm:ss` の範囲 / `["\t"]` | 正確。値が EmptyDate なら緩める |
| 日付だけの列 (`Ymd`) `=` / In / `<=` / `<` / `>=` / `>` | 日単位の範囲: `= d` → `d 0:00:00〜d 23:59:59`・`<= d` → `〜d 23:59:59`・`< d` → `〜前の日 23:59:59`・`>= d` → `d 0:00:00〜`・`> d` → `次の日 0:00:00〜` | 正確 (時刻の入った値もその日として比べる = 読むと日付に丸めるのと同じ)。値に時刻があれば (SQL を直接渡したときだけ。本体は値を列の型の日付にしてから渡す) 列の値をその日の 0:00 として比べ (PostgreSQL の date と timestamp の比較と同じ)、`=` はその日の範囲で読んで緩める |
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
行を選ぶ集計 (`WithinGroupsOf` = 別の集計の上位の組の中での集計) を受け付ける。入れ子の WithinGroupsOf・別の表の組の中での集計は拒否する。

日付の軸 (本体の `DateGroup`) は、本体が書く PostgreSQL の丸めの式だけを受け付け (`SqlTranslator.ReadGroupKey`)、メモリで同じ意味に計算する (`QueryExecutor.Truncate`):
`date_trunc('hour' | 'day' | 'week' | 'month', C)`・`(date_trunc('quarter' | 'year', S)[ + interval 'F months'])` (C = 列 か `(列 + interval 'N minutes')`、S = C か `(C - interval 'F months')`)。
週は月曜始まり・年と四半期は年度の開始月で切る・UTC 保存の日時は時差 (N 分。負もある) を足してから丸める・月の加減は日が無ければ月末 (PostgreSQL の interval と同じ)。
値は期間の開始の日時 (日付の列は 0:00 の日時として丸める)。本体が時は日時、それ以外は日付にする。空の値は 1 つのグループ (並びは昇順で末尾)。ほかの日付の関数 (`to_char` など) は拒否する

#### 受け付ける SQL / 拒否する SQL (まとめ)

- 受け付ける: 本体の `SelectSqlCreator` の一覧・件数 (`count(*)`)・LinkField の left join・exists・`AggregateSqlCreator` の集計・`insert ... values (...), (...) returning キー`・`update ... set ... where キー = @p [and ...]`・`delete from ... where キー = @p [and ...]`
- 拒否する (`PleasanterException`): union・関数の比較 (`lower(...)` など)・inner join・前方一致 / 後方一致の like (`'%x%'` の形でない like)・escape の無い like (本体は escape をいつも付ける)・上記の受け付けない exists・上記以外の日付の式・`Comments` と添付の列の条件 / 並び / 集計・`ExecuteSqlCommandAsync` の SQL 文 (`CommandType = Sql`)・`GetConnection`

### 書き込み

- 作成: `POST api/items/{siteId}/create`。拡張の列は `ClassHash` / `NumHash` / `DateHash` / `DescriptionHash` / `CheckHash` の中に入れる。複数行の insert は 1 件ずつ作り、途中で失敗したら作った件数をメッセージに入れる (取り消さない)
- 更新・削除: `POST api/items/{id}/update` / `delete`。**レコードの URL は別のサイトの行でも通るので、先に「サイト ID の URL + SQL の where 全体」で件数を読み、0 件なら API を呼ばずに 0 件を返す。**
  確かめてから書くまでの間は防げない (FileMaker の楽観ロックと同じ扱い)。OptimisticLockingField (UpdatedTime など) もこの確認の条件に入る
- 空にする値: 文字 → `""`、拡張の数値 → null、標準の数値 (null を持てない) → 0、日付 → EmptyDate (`null` と `""` は HTTP 400 になるため)、チェック → false (`CheckA: null` は 400)
- 書き込みはやり直さない。送ったあとの通信の失敗・5xx・想定外の応答は `WriteResultUnknown`
- 保存はデータソースごと・1 件ごとに確定する (トランザクションに入らない・ロールバックで戻らない)。DB と混ざる保存も拒否しない
- `Comments` は空でない値だけ送る (足すだけ)。更新の本文が空になれば update を送らない
- 添付の列は足すファイルと消すファイルだけ `AttachmentsHash` に入れて送る (下の「添付」)

### 添付

プリザンターの添付の列 (`AttachmentsA`…) は、列ごとにファイルの並び。行の `AttachmentsHash` にはメタ情報 (Guid・Name・Size など) だけが入り、中身は `api/binaries/{guid}/get` で読む。
本体の FileField (1 ファイル・中身はホストのファイルストレージ) は使わず、専用のフィールド (画面のフィールドのパッケージの `PleasanterAttachmentsField`) で扱う。

- 値: 列の JSON の文字。読むと `[{"Guid","Name","Size",...}]` (ファイルが無ければ `"[]"`)
- 書く: 同じ形の JSON の配列の文字。**送るのは Base64 を持つ要素 (追加: `{Name, ContentType, Base64}`) と `Deleted: true` の要素 (削除: `{Guid, Deleted: true}`) だけ。** ほかの要素 (読んだままのファイル) は送らない
  (読んだ値をそのまま書いても何も変わらない)。足すものも消すものも無ければその列は送らない (本文が空なら update も送らない)。null・空の文字も送らない。
  追加の `ContentType` が無ければ `application/octet-stream` を入れる (無いとプリザンターは `text/plain` にする)。送る追加の要素は Name・ContentType・Base64 の 3 つだけ
- 形が不正ならエラー (`InvalidRequest`): JSON の配列でない・要素がオブジェクトでない・追加に Name が無い / Base64 が読めない・削除に Guid (32 桁の 16 進) が無い・Base64 と Deleted の両方を持つ
- **消す Guid は、そのレコードのその列に今あるものだけ送る** (更新の前に、存在の確認と同じ条件で行を読んで絞る。無いものは黙って外す)。
  プリザンターは追加と一緒に送られた削除を、別のレコードの添付の Guid でも消してしまう (`Attachment.SqlStatement` が Guid だけで binaries を消す) ため。作成では削除を送らない
- 中身の読み出し: `PleasanterConnection.GetBinaryAsync(guid)` (internal・読み出しなのでやり直す)。**連携用の API キーはテナント内のどの添付も読めるので、Guid だけで中身を返す口を作らない。**
- `GetAttachmentAsync(io, designData, module, id, field, guid)` (ホストの Controller が呼ぶ): モジュールがこのアクセサのプリザンターのデータソースで、フィールドが添付のフィールド (型の名前で見る) で、その DbColumn が添付の列で、
  Id のフィールドがキーの列であることを確かめる → `io.GetListAsync` (通常の読み出し = モジュール・行・フィールドの権限が効く) で Id が一致する行のそのフィールドだけを読む →
  その値に guid のファイルがあることを確かめる → 中身を読み、`ReferenceId` がそのレコードであることを確かめる → 名前 (レコードの一覧の名前) と中身を返す。どれかが満たせなければ null。例外は投げない (中で握る)
- `designData` を引数で受けるのは、本体の `ModuleDataIO` がデザインを外へ出さないため (ホストは `ModuleDataIO` を作ったのと同じデザインを渡す)

画面のフィールド (`PleasanterAttachmentsField`):

- 表示: ファイル名と大きさ。保存済みのレコードの読んだときからあるファイルだけダウンロードのリンク (`DownloadUrl?module=&id=&field=&guid=` の GET・`download` 属性)。足したファイル・新しいレコードにはリンクを出さない
- 編集: ファイルを足す (複数選べる)・足したファイルを取り消す・読んだファイルに削除の印を付ける / 外す。保存で反映し、保存の後は本体の読み直しで一覧が新しくなる (読み直さない軽い保存の後は、消したものを外し足したものを名前だけにする)
- 変更が無ければ値は読んだ文字のまま (保存で送らない)。変更があれば、読んだファイル (消すものは `Deleted: true`) と足すファイル (Base64) の並び
- 1 ファイルの上限 (`MaxFileSizeMB`) を超えるファイルは中身を読まずに入力エラーにする (画面での確かめだけ。サーバー側では効かない)。`IsRequired` は削除の印の無いファイルが 1 つ以上
- レコードのコピーでは写さない (設計クラスが `IConfirmCopyFieldDesign.NeedCopy => false`。写すと元のレコードの添付が「あるように見えて」保存では送られない)
- 消す印は Guid の分かる読んだファイルだけ (`CanDelete`)。読み直さない軽い保存 (AutoSubmitField など) の直後の足したファイルは名前だけで Guid が無いので、読み直すまで消せない (印を付けると次の保存が Guid の無い削除でエラーになるため)
- ViewOnly・無効・権限は本体の FieldBase どおり。一覧の行 (`ModuleLayoutType.List`) ではファイル名だけ (カンマ区切り)。検索の部品は無い。スクリプトに出すメンバーは足していない
- ファイルの選択欄は選ぶたびに作り直す (同じファイルを選び直しても届くように)。一度に選べるのは 100 個まで (`e.FileCount` で先に入力エラー。`GetMultipleFiles` は超えると例外のため)
- 読み直さない保存の後 (`AcceptChanges`) は、送った値の追加 (Base64 のある要素。名前と中身で 1 つずつ突き合わせる) と削除 (`Deleted: true`) だけを保存済みにし、送信の間に足した・印を付けた分は変更として残す
  (残さないと次の保存で同じファイルが二重に付く)。送った追加を送信の間に取り消していたら (今の一覧に無い)、プリザンターには付いたので名前だけの保存済みの行 (中身・Guid なし) として足す。保存がエラー (結果が分からないときも) ならフィールドは足したファイルを持ったままなので、プリザンター側だけ書けていると保存し直しで同じファイルがもう一度付く (Docs に注意として書いた)。
  テスト: `AttachmentsFieldTest` (本体の公開の口 `ModuleCreationService` で Module を作り、自動保存の送信の間に操作する。`Harness/FieldHarness`)
- 中身は保存の要求 (JSON) に Base64 で入るので、要求の大きさの上限 (ホストの Kestrel の既定はおよそ 30 MB) を超える保存はホストの設定次第

### ExecuteSqlField → 拡張 SQL

`ExecuteSqlCommandAsync` は、ストアドプロシージャ (`CommandType = StoredProcedure`) をプリザンターの拡張 SQL として実行する (FileMaker 版のスクリプトの実行と同じ形)。SQL 文 (`CommandType = Sql`) は拒否する。

- `POST {Url}api/extended/sql`・本文 `{ApiVersion, ApiKey, Name: CommandText (前後の空白を除く), Params}`。拡張 SQL はプリザンター側に `Api: true` で置く
- `Params` = 入力のパラメータ (Input / InputOutput) の名前 (先頭の `@` / `:` を外す) → 値。数値・真偽は JSON の数値・真偽、日付と日時は `yyyy-MM-ddTHH:mm:ss` (プリザンターが日時として受け取る)、時刻は `HH:mm:ss`、それ以外は文字、null は JSON の null
- 結果は最初の表の最初の行: Scalar の `ReturnValue` は先頭の列の値、NonQuery は -1 (件数は分からない)、Reader は null。
  出力のパラメータ (Output / InputOutput / ReturnValue) は同じ名前の列の値 (無ければ null)・入力は送った値。数値は整数なら long・それ以外は decimal
- 書き込みとして扱う (やり直さない)。プリザンターは拡張 SQL が無い・`Api` が true でない・パラメータが足りない (null を含む)・SQL のエラーを、どれもエラー画面へのリダイレクト (302) で返すので区別できない → `WriteResultUnknown` (理由の候補をメッセージに入れる)

### 接続 (PleasanterConnection)

- 全部 POST・JSON。本文に `ApiVersion: 1.1` と `ApiKey`。**成功は「HTTP 200 かつ JSON の StatusCode が 200」。** リダイレクトには従わない (本文の API キーが別の宛先へ送られるため) → エラー
- 分類: HTTP が成功で JSON の StatusCode があればその値、なければ HTTP の状態で 401 Authentication / 403 Forbidden / 404 NotFound / 429・5xx ServiceUnavailable / ほかの 4xx InvalidRequest / それ以外 Unknown。応答の `Message` を入れる。API キー・ホスト名は入れない (通信の失敗の詳細は InnerException だけ)
- 再試行: 読み出しは 502 / 503 / 504・通信の失敗を 3 回まで (最大 4 回・指数の待ち)。429 はやり直さない (日次の上限のこともある)
- 定義 (メタ情報のファイルを渡さないとき): 表ごとに `api/items/{siteId}/getsite` を最初の 1 回読んで接続が生きている間持つ (サイトの設定を変えたらアプリを再起動する)。応答の `SiteId` と `ReferenceType` (Results / Issues) を確かめる。
  Wiki・フォルダ・ダッシュボードなどのサイト・サイト統合 (`IntegratedSites`) のサイトは理由つきのエラー

### 定義のファイル (メタ情報)

`pleasanter.<データソース名>.metadata.json` に表と列 (種類・読み取り専用・null を持てるか・表示名・選択肢・リンク・複数選択) を書く。接続先・API キーは書かない。
ファイル名のデータソース名は、文字 (`char.IsLetterOrDigit` = 日本語などの文字も含む)・数字・`_`・`-`・`.` を残し、それ以外を `_` にする。
**置き場所は本体のサーバー専用ファイル** (デザインのプロジェクトの ServerFiles。デザインと一緒に App.zip で配られ、画面へは送られない)。前の置き場所 (プロジェクトの直下) は読まない。

- 書く: `PleasanterMetadataFile.UpdateAsync(connection, projectDirectory)` (static) が Sites の全部の getsite を読み、内容を読み直して確かめてから `DesignDataFileManager.WriteProjectServerFile` で書く (書き終えてから置き換える・読めなければファイルを変えない・同じファイルの更新は直列)
- 読む: `PleasanterDbAccessor` の 3 つ目の引数。デザイナは `FromProject(projectDirectory)` (`ReadProjectServerFile`)、Web アプリは `FromDeployedDesign(designFileDirectory)` (`GetServerFile`)。
  **ファイルを渡したアクセサは実行時に getsite を呼ばない。** ファイルに無い表は `NotFound` のエラー (デザイナでメタ情報を更新して配り直すよう案内する。黙って getsite に戻らない)。
  ファイルがまったく無ければ表は無い (表の一覧は空)。サイト ID は接続文字列の `Sites` のものを使う (ファイルのサイト ID は書き出した環境のもの)。表の一覧は「ファイルにあって `Sites` にもある表」
- キャッシュ: 場所 (プロジェクト / 配られたデザイン・フォルダ・データソース) ごとに static に持つ。配られたデザインは `App.zip` の更新日時と大きさが変わったときだけ zip を開き直す (読む前に日時を取る。読む間に差し替わっても次の読み出しで読み直す)。
  プロジェクトはファイルを毎回読み、中身が変わったときだけ解釈し直す。読み直しは場所ごとに 1 つずつ (ロック)・解釈した定義は差し替えるだけで中を変えない (読む側はロックなし)。
  App.zip の名前は本体の配置の決まり (`<designFileDirectory>/App.zip`) を前提にしている (Extras の DesignDataStore と同じ)
- ファイルを渡さない 2 引数の形は従来どおり表ごとに最初の 1 回 getsite を読む
- `FromProject("")` / `FromDeployedDesign("")` の空のフォルダは今のフォルダ (本体が App.zip を探す場所と同じ)。手で直したファイルの null (`Tables` の要素・`Columns`・列の要素)・名前の無い表 / 列・Results / Issues でない表は無いものとして読む

### ブラウザのセッションの確認 (SSO)

ブラウザが持つプリザンターの認証 Cookie を、CLB のサーバーがプリザンターの拡張 SQL API (`POST {Url}api/extended/sql`・`Name: ClbWhoAmI`) に転送し、プリザンターが解決した本人を受け取る。
プリザンター側に `App_Data/Parameters/ExtendedSqls/ClbWhoAmI.json` (本人の TenantId / UserId / LoginId / Name を返す拡張 SQL) を置く。

- **ライブラリは確認だけ**: `Task<PleasanterSessionResult> VerifySessionAsync(IEnumerable<KeyValuePair<string, string>> cookies, IEnumerable<string>? cookieNamePrefixes = null, string extendedSqlName = "ClbWhoAmI", CancellationToken cancellationToken = default)`
- 結果は例外でなく型で返す (Authenticated + 本人 / Unauthenticated / Failed)。転送する Cookie は名前の前方一致 (既定 `.AspNetCore.Cookies` / `Pleasanter_SessionGuid` / `.AspNetCore.Session`)。`;`・改行を含む値・`=` を含む名前は送らない
- API キーは送らない。Cookie は要求ごとにヘッダに書き (Cookie を持たない別の HttpClient)、どこにも保存しない。1 回だけ送る (やり直さない)
- HTTP 401・JSON の StatusCode 401・UserId が 0 以下 → Unauthenticated。リダイレクト・HTML・ほかのエラー・行が無い → Failed
- Controller・Cookie の発行・再検証・待ち画面はホスト (Example)

## Designer

パッケージ `.Designer` (WPF・net8.0-windows。`.Server` に依存)。デザイナのアプリ (利用者のもの・`Example/LowCodeApp.Designer`) が 3 か所で呼ぶ。FileMaker 版と同じ形。

| 呼ぶもの | 呼ぶ所 | 内容 |
|---|---|---|
| `PleasanterDesignerInitializer.Initialize(DesignerEnvironment)` | `OnStartup` の `base.OnStartup(e)` より前 | `SpecDocs/*.md` (埋め込み) を `SpecDocCatalog` に登録する (ai-refresh が `_specs/Pleasanter.md` に書き出す)。デザイナの環境を覚え、下の IDbAccessor がプロジェクトのフォルダを知る |
| `PleasanterDesignerInitializer.Setup(DesignerEnvironment)` | `base.OnStartup(e)` の後 | メニュー Tools > プリザンターのメタ情報を更新 (英語の UI では Update Pleasanter Metadata) |
| `PleasanterDesignerInitializer.CreateDbAccessor(inner, dataSources)` | `IDbAccessorFactory` の実装 (`DbAccessorFactory.cs`) | `new PleasanterDbAccessor(inner, PleasanterConnection.FromDataSources(dataSources), PleasanterMetadataFile.FromProject(プロジェクトのフォルダ))`。プリザンターの表の定義はファイルから読む (プリザンターに取りに行かない)。プロジェクトを開いていなければファイルを渡さない (getsite を読む) |

- メタ情報の更新: デザイナの設定のデータソースのうちプリザンター用のもの全部について、`PleasanterMetadataFile.UpdateAsync` でプロジェクトのサーバー専用ファイル `pleasanter.<データソース名>.metadata.json` を書き、データベースの表示を読み直す。次のデプロイで Web アプリへ配られる。
  プリザンターのデータソースが無ければトーストで知らせる。失敗はメッセージのウィンドウ (前のファイルは残る)
- headless (CLI) の専用のサブコマンドは無い (FileMaker 版と同じ)。headless のデザイナも `IDbAccessorFactory` を通るので、表の定義は保存したファイルから読む。ファイルの更新はメニューから
- `SpecDocs/Pleasanter.md`: AI (デザインを作る Claude Code) 向けの仕様。接続文字列の見分け方・表名 (Sites の別名)・列名 (内部の列名)・列の種類とフィールド・選択肢 → Enum・リンク → LinkField・読み取り専用の列・
  検索と集計で使える / 使えないもの・保存・やってはいけないこと。**ライブラリの仕様だけを書く** (Example のホストの挙動・SSO は書かない)。章立ては FileMaker 版の SpecDocs に揃える

## Example

`Source/Example` は FileMaker 版と同じ構成のサンプル (パッケージにしない)。動かし方は `Docs/Example.md`。

- コネクターの結線: `LowCodeApp.Server/Infrastructure/DataService.cs` の 2 行 (`PleasanterConnection.FromDataSources(SystemConfig.Instance.DataSources)` と `new PleasanterDbAccessor(DbAccess, connections, PleasanterMetadataFile.FromDeployedDesign(SystemConfig.Instance.DesignFileDirectory))`)・
  csproj の `.Server` への ProjectReference (利用者のアプリでは PackageReference)。デザイナは `LowCodeApp.Designer/App.xaml.cs` と `DbAccessorFactory.cs`
- appsettings: データソース `Main` (SQLite・ログインするユーザー) と `Pleasanter` (`DataSourceType: PostgreSQL`)。プリザンターの接続文字列は環境変数 `ConnectionStrings__Pleasanter` で渡す (API キーを含むので appsettings に書かない)
- 添付: `LowCodeApp.Client.Shared` が画面のフィールドのパッケージを参照し (サーバーは Client 経由で届く)、`AppInfoService` のコンストラクタと `Program.cs` で `PleasanterFieldsInitializer.Initialize()` を呼ぶ。
  ダウンロードは `Controllers/PleasanterAttachmentsController.cs` (`[Authorize]`・GET `PleasanterAttachmentsFieldDesign.DownloadUrl`・見つからなければ 404・`application/octet-stream` + `Content-Disposition: attachment` + `X-Content-Type-Options: nosniff`・
  ファイル名から制御文字・パスの区切り・`"` を外す)。`DataService` が `PleasanterDbAccessor` を `Pleasanter` プロパティで出す。案件 (`Project`) の詳細と一覧に「資料」(`AttachmentsA`) を置いた (一覧の列は `CanUserSort: false`)

### SSO (プリザンターのログインを使うシングルサインオン)

ライブラリの `PleasanterConnection.VerifySessionAsync` で本人を確かめ、ホストが CLB の Cookie を発行する。

| ファイル | 内容 |
|---|---|
| `LowCodeApp.Server/Sso/PleasanterSsoSettings.cs` | appsettings の `PleasanterSso` (`Enabled`・`DataSourceName`・`TenantId`・`LoginUrl`・`LogoutUrl`・`AutoProvisionUser`・`RevalidateSeconds`・`TimeoutSeconds`) |
| `LowCodeApp.Server/Sso/PleasanterSso.cs` | 本人の確認 (`VerifyAsync`)・Cookie の発行 (`LoginPrincipal` + プリザンターの TenantId / UserId・確かめた時刻)・確かめ直し (`RevalidateAsync`) |
| `LowCodeApp.Server/Sso/PleasanterSsoRevalidationMiddleware.cs` | `/api/` (`/api/account/` を除く) の入口で確かめ直す。ログアウト済み・別の人なら 401、確かめられなければ 503 (どちらも CLB からサインアウト) |
| `LowCodeApp.Server/Controllers/AccountController.cs` | `sso/enter` (ブラウザの遷移の入口)・`sso/check` (待ち画面が 0.8 秒ごとに呼ぶ。`Authenticated` と、プリザンターでは本人だがユーザーモジュールに登録が無いときの `NotRegistered`。待ち画面は `NotRegistered` で確かめるのをやめて「登録されていません」と出す)・`current_user` の確かめ直し・`login_options` の SSO の項目・ログアウト |
| `LowCodeApp.Client/wwwroot/index.html`・`sso-login.html` | ログインしていないブラウザを待ち画面へ送る・待ち画面 (プリザンターのログイン画面を別のタブで開き、`sso/check` を呼んで待つ) |

- 問い合わせ先はプリザンターのデータソースの接続文字列の `Url` だけ (SSO の設定に URL を二重に持たない)。データソースは `PleasanterSso.DataSourceName` (空なら最初のプリザンターのデータソース)。
  接続は `PleasanterConnection.FromDataSources` が返すもの (DataService と同じ接続。要求ごとに作らない)。プリザンターのデータソースが無ければ確かめられない (Failed と同じ扱い・ログに警告)
- `PleasanterSso.TenantId` (必須): 入れるプリザンターのテナントの ID。入るとき (`sso/enter`・`sso/check`) と確かめ直し (`RevalidateAsync`) の両方で、プリザンターが返した本人の TenantId と照合する。
  違えば入れない (待ち画面には未登録と同じ「登録されていません」・監査ログは拒否 `tenant_mismatch`・確かめ直しでは CLB からサインアウト)。未設定なら誰も SSO では入れない (問い合わせずに確かめられないのと同じ扱い・ログに警告)
- `AutoProvisionUser`: 登録の無い人の行を作るのは、ユーザーモジュールのログインアカウント契約の外部ログイン名 (`ExternalLoginName`) が空かログイン名と同じ設計のときだけ
  (作る行にはログイン名しか入らないので、別の列の設計では次のログインで引けない)。Example の設計は外部ログイン名が `email` (メール) なので、`true` でも作らない (未登録として扱う)。同じログイン名の行が既にあっても作らない
- `returnUrl` (`sso/enter`) は `Url.IsLocalUrl` で CLB の中のパスだけ通し、それ以外 (`//`・`/\`・タブ入り・`https://` など) は `/` にする。待ち画面 (`sso-login.html`)・`index.html`・`login.js` も CLB の中のパスだけに戻る
- ユーザーの表 (`app_users`) は 2 つのモジュールで扱う:
  - `AppUser`: `CurrentUser` の元 (アプリの設定の CurrentUserModule・ログインアカウント契約を持つ)。読めるのは自分の行だけ (行の読み取り条件 `Id = CurrentUser.Id`)・作成 / 更新 / 削除はできない (`CanCreate` / `CanUpdate` / `CanDelete` が false)
  - `UserAdmin`: 同じ表の管理用 (サイドバーの「ユーザー」・パスワードの設定は Extras の PasswordHashField)。ユーザーの読み取り / 書き込みの条件が `CurrentUser` の `管理者` (`is_admin`) = true なので、管理者だけが読み書きできる
  - 分けた理由: `CurrentUser` の元のモジュールは、ログインする全員が自分の行を読めなければならないので、管理者だけに絞れない。1 つのモジュールのままだと、SSO で入った一般の利用者がほかの人の行を読み・`is_admin` を自分に付けるなど書けてしまう。
    `CurrentUser` の元は「自分の行だけ・読むだけ」に絞り、管理の画面は管理者の条件を付けた別のモジュールにした
- 全端末ログアウト (Extras の SessionRevokeButtonField) は Example のデザインに置いていない (`session_stamp` の列と `DbColumnSessionStamp` はある)
- `PleasanterSso.TimeoutSeconds` (既定 5 秒) は本人の問い合わせだけのタイムアウト。`VerifySessionAsync` の CancellationToken で実現する (要求の中断とつなぐ)。接続文字列の `TimeoutSeconds` (既定 100 秒) はデータの読み書き用で、短い方が効く
- 監査ログ (Extras): `sso/check` には `[Audit]` の分類を付けない。分類の無い操作は成功が記録されないので、待ち画面の「まだログインしていない」は記録されない。
  プリザンターで本人が確かめられたときだけ `AuditContext.StartAsync(AuditCategory.Authentication, ...)` で認証の分類にする (試行と結果 (成功・未登録の拒否) が記録される)。`sso/enter` は `[Audit(Authentication)]` のまま
- CLB の認証 Cookie の名前は `.CLB.Auth` (テンプレートの既定 `.AspNetCore.Cookies` はプリザンターの Cookie と同じ名前で、同じホストでは上書きし合うため)

## 決めてあること (2026-11-16)

- **`IDbAccessor` のデコレータ。本体は変えない。** `DataSourceType` は `PostgreSQL` と書かせる
- **モックは作らない。** 実物のプリザンターにテスト用のサイトを作って確かめる
- **表名は接続文字列の `Sites` で付ける別名。** ここに無いサイトは触れない
- **連携用の API キー 1 つでつなぐ。** 利用者ごとの認可は CLB (モジュールの権限) が担保する
- **保存はデータソースごと・1 件ごとに確定する。** 調査資料 02 の「複数変更の事前拒否」「編集識別子」は作らない
- **View に正確に写せない条件は、広めの条件で全部読んでメモリで評価する。** 黙って違う結果を返さない・`MaxRows` を超えたらエラー
- **否定フィルタは使わない** (サイトの設定次第で黙って無視される)
- **更新・削除は先にサイトの URL で行を確かめる** (レコードの URL は別のサイトの行でも通る)
- **集計はメモリ。** 日付の期間の軸は本体の PostgreSQL の丸めの式だけを受け付けてメモリで計算する (2026-11-19)
- **表の定義はメタ情報のファイル (本体のサーバー専用ファイル) から読む。** Web アプリは実行時に getsite を呼ばない。ファイルに無い表はエラー (2026-11-19)
- **ExecuteSqlField はストアドプロシージャ = 拡張 SQL だけ。** SQL 文は拒否 (2026-11-19)
- **要求の分割 (FileMaker 版の「1 回の要求に入らない数の値を組に分ける」) は移植しない。** 値が 500 個を超える一致は緩めてメモリで評価する
- **拡張の列はエディタと一覧に出てくるものだけ** (上記。`Columns` だけの列は API の行に入らない)
- **SSO はライブラリは確認だけ・結線はホスト**
- **添付は専用のフィールド。本体の FileField は使わない。** 中身のダウンロードはホストの Controller が `GetAttachmentAsync` を呼ぶ (ライブラリに Controller を持たせない)。
  Guid だけで中身を返す口は作らない・消す Guid はそのレコードの列にあるものだけ送る (2026-11-20)
- **添付の `MaxFileSizeMB` は画面での確かめ。サーバー側では効かない。** サーバー側の上限はプリザンターの項目の設定 `LimitSize` (超えると HTTP 442) とアプリの要求の大きさの上限 (Kestrel など) (2026-11-20)
- **添付はレコードのコピーで写さない。読み直さない保存の直後に足したファイルは、読み直すまで消せない** (2026-11-20)

## 守ること

- 本体は core のリポで変える (このリポから直接編集しない)。足りない口はユーザーに相談
- ホスト (Starter・Example) に置くべきもの (Controller・DI の要求) をライブラリに持ち込まない
- 実物のプリザンターで確かめていないことを「確認済み」と書かない
- 公開リポジトリ。API キー・パスワード・接続文字列・ホスト名の実物をコード・テスト・ドキュメント・ログ出力に書かない
- テストで作ったサイトは消す。既存のサイトとそのレコードは触らない
- 文書 (README・Docs・CLAUDE.md・コメント) は日本語。会社名の表記は Codeer

## 確認済み・未確認・未実装 (2026-11-20 時点)

### 実物で確かめたこと (プリザンター 1.5.9.1・PostgreSQL・`Live/` のテストと手で送った要求)

- 一覧・件数・200 行を超えるページ (205 行のサイトで 2 ページ目・`TotalCount`)・`MaxRows` を超えたときのエラー
- 上の表の View の書き方 (文字の一致・部分一致・空・数値と日付の範囲・チェック・`and_n` / `or_n` の入れ子) が SQL の条件と同じ行を返すこと
  (`Live/LiveDifferentialTest` が約 75 の条件で View の道とメモリだけの道の結果・順・件数を突き合わせる)
- 結合先の列での条件・表示名・並び、exists、リンクの値での検索、集計 (リンクの軸・軸なし)
- 作成・更新・削除・空にする値 (上記の対応)・読み取り専用の列 (更新と作成のキーはエラー・作成で Ver / Creator / Updator / CreatedTime / UpdatedTime / RemainingWorkValue に値があってもプリザンターが決めた値になる)・別のサイトの行の ID では書かないこと・UpdatedTime の楽観ロック・期限付きテーブルの作成と RemainingWorkValue の計算・複数行の insert
- `RemainingWorkValue` を View の条件に書くと HTTP 302 (エラー画面 `/errors` へ)。並び (`ColumnSorterHash`) はエラーにならず、値の順に並んだ (3 行)
- 定義のファイルの書き出し (実物の getsite から)。書き出したファイルを zip (App.zip の形) に入れ、`FromDeployedDesign` のアクセサで一覧・リンクの条件・作成・更新・削除ができること (`Live/LiveMetadataFileTest`)
- 日付だけの列 (`Ymd`) に時刻つきの値 (`2026-03-10T15:45:00`) を入れたレコードで、日単位の範囲の View (`=`・`<=`・`<`・`>=`・`>`) がメモリの道と同じ行を返すこと (`Live/LiveDifferentialTest`・`Live/LiveReadTest`)
- ユーザーの選択肢の列 (`[[Users]]`) の条件 `Own` が API キーの利用者の ID に読み替えられること・緩めた読み出しで文字の `Own` と比べた結果がメモリの道と同じこと
- 日付の軸の集計 (月・年度開始 4 月の四半期・日) が期待どおりのグループと値を返すこと (`Live/LiveAggregateTest`)
- コメント: 作成と更新で 2 件足され、空の更新では足されず、読むと新しい順の JSON の文字 (`Body`) で返ること。条件に使うとエラー (`Live/LiveCommentsTest`)
- 添付 (`Live/LiveAttachmentsTest`): 作成で複数足せる・更新で足すと消すが一緒にできる (消したファイルの binaries/get は 404)・読んだままの値を書いても変わらない・
  別のレコードの Guid を追加と一緒に送っても消えない (コネクターの絞り込みを外すと実物で消えることも確かめた)・ダウンロードはレコードとフィールドの添付だけ中身を返し、
  無い Guid・別のレコードの Guid・別のフィールド・添付のフィールドでない・プリザンターでないモジュール・読めないレコード (行の閲覧条件) は null・条件に使うとエラー
- 添付の API (手で送った要求): 行の `AttachmentsHash` の列はオブジェクトの配列 (エディタにある列でファイルが無ければ `[]`)・`api/binaries/{guid}/get` は `Response` に `ReferenceId`・`FileName`・`ContentType`・`Base64` などを返し、無い Guid は HTTP 404・
  列の `LimitSize` を超えると HTTP 442 (StatusCode 442)・`LimitSize` が無ければ 1 MB を超えるファイルも入る・`..\x.txt` のような名前・空の Base64 (大きさ 0) も入る・Base64 が読めなければ HTTP 400・`ContentType` が無ければ `text/plain`・
  エディタに無い列への書き込みは黙って無視される・別のレコードの Guid の削除は単独では無視されるが追加と一緒だと消える
- 拡張 SQL: `api/extended/sql` でパラメータ (文字・数値) を渡して出力のパラメータと Scalar の戻り値が返ること・`@ipU` が API キーの利用者になること・無い名前の拡張 SQL が `WriteResultUnknown` になること (`Live/LiveExtendedSqlTest`)
- セッションの確認: ログインしていない Cookie を送ると認証済みにならないこと。ブラウザでログイン (パスワード + TOTP) したセッションで本人が返ること (Example の SSO で確認。下記)
- getsite の形・Issues の既定の列・CompletionTime の往復・リンクの値は ID の文字・システムの日時は秒より細かい値を持つ (+1 秒の範囲で当たる)・無い ID の update / delete は 403・JSON でない条件の値は黙って無視される・deletesite は 200

### Example で実物を相手に確かめたこと (2026-11-16・Playwright のヘッドレスのブラウザ)

- `Example/Design` のデザインを配置した Web アプリで: 一覧・ページ送り・検索 (部分一致・文字の選択肢・数値の列の選択肢 (Enum の `ValueType: Number` + SelectField)・LinkField 越しの名前・日付の範囲・数値の範囲・日時の範囲)・
  並び (数値・LinkField 越しの名前・日付の降順)・詳細・作成・更新・削除・LinkField の選択ダイアログ・子の一覧での明細の作成と更新 (RemainingWorkValue が計算されて読み直される)・
  UpdatedTime の楽観ロック (画面を開いた後の別の更新で保存が止まる)・CrossTabField の集計 (取引先 × 区分の金額の合計)
- 更新しても `Ver` が上がらないことがある (プリザンターの版の上げ方による。楽観ロックは `UpdatedTime` で行う)
- SSO (`Tests/sso/sso_e2e.py` 全件と追加の確認): プリザンターでログイン済みなら待ち画面なしで入る・未ログインなら待ち画面 → 別のタブでログイン → 入る・
  TOTP 未完了では入れない・未登録の利用者は待ち画面に「登録されていません」が出て確かめるのが止まる・プリザンターでログアウトすると `RevalidateSeconds` の後で `current_user` が 401・CLB のログアウトで `.CLB.Auth` が消える
- 監査ログ (Extras・File): 待ち画面の確かめ (ログイン前) では行が増えない・SSO の入場で認証の試行と成功・未登録で試行と拒否 (`user_not_registered`) が 1 回分だけ出る
- プリザンターのログアウト画面は同じホスト (localhost) の Cookie を消すので、同じホストで動かすと CLB の Cookie も消える (確かめ直しの 401 は Cookie を戻して確認)

### Example で実物を相手に確かめたこと (2026-11-19・デザイナは Friendly で操作・Web は Playwright のヘッドレスのブラウザ)

- デザイナ: メニュー Tools > プリザンターのメタ情報を更新 で `Design/ServerFiles/pleasanter.Pleasanter.metadata.json` が書き直される (前のファイルとの差は 3 つの表に `Comments` の列が足されたことだけ)。
  プロジェクトの直下に前の名前のファイルは作られない。データベースの表示 (テーブルの一覧 Customers / Projects / Tasks・列) はファイルから出る。デザインチェックの指摘 0 件
- デザイナの「送信」で作った App.zip に `ServerFiles\pleasanter.Pleasanter.metadata.json` (プロジェクトのファイルと同じ内容) が入り、その zip を配置した Web アプリで下の確認が通った。デザイナのソリューションのツリーには ServerFiles は出ない
- 配られたメタ情報は画面へ出ない: ログインして読んだ `api/module_data/design` に含まれず、`api/module_data/resource` に `ServerFiles/…`・`..\ServerFiles\…`・ファイル名だけ を渡しても空が返る
- `AppUser` / `UserAdmin` をデザイナで開いて保存しても中身は変わらない (末尾の改行が落ちるだけ)
- Web アプリ (SSO なし): 一覧・ページ送り・検索 (部分一致・LinkField 越しの名前・文字の選択肢・数値の列の選択肢・数値 / 日付 / 日時の範囲)・並び (数値・LinkField 越しの名前)・詳細 (LinkField と子の一覧)・
  取引先の作成と更新・LinkField の選択ダイアログでの案件の作成・子の一覧での作業の作成 (残作業量が計算されて読み直される)・一覧のボタンでの削除・
  楽観ロック (「他のユーザーが更新しています。再読み込みしてからやり直してください」と「更新に失敗しました」)・CrossTabField の集計 (取引先 × 区分・合計 24,050,000)・ユーザーの管理の一覧 (管理者)
- 日付の軸の集計: `AmountByCustomer` に受注日の月 × 区分の CrossTabField (`GroupByMonth`) を足し、月ごと (2026-04 〜 07) と受注日の無い案件 ((空白)) に分かれて合計が取引先の集計と一致した
- 配られたメタ情報のファイルから表 (Tasks) を抜いた App.zip の配置先で作業の一覧を開くと、一覧の要求が 500 になり、画面に
  「Table 'Tasks' is not in the Pleasanter metadata (pleasanter.Pleasanter.metadata.json) of the deployed design. Update the Pleasanter metadata in the designer and deploy the design again.」が出る。ほかの表 (取引先) の一覧は動く
- SSO (`Tests/sso/sso_e2e.py` 全 11 件が成功・テナントの違う CLB は `TenantId` を 999 にした別のポートのサーバー): 認証前の拡張 SQL が 401・Cookie なしは匿名・`returnUrl` の外向きの値は `/`・TOTP 未完了では入れない・
  テナントが違うと `notRegistered` で入れず「このアプリの利用者として登録されていません」が出る・入場・入った後の `returnUrl` も外へは出ない・SSO の利用者は `AppUser` で自分の行だけ読めて書けず `UserAdmin` は読めない・
  ログアウトで `.CLB.Auth` が消える・SSO を有効にしたままパスワードでもログインできる・管理者は `UserAdmin` で読み書きできる。SSO の一般の利用者が `/Main/UserAdmin` を開くと「不正なアクセスです」
- 数値の入力欄はプリザンターが返した桁のまま出る (金額 `4800000.0`・作業量 `24.0000`。プリザンターの API の JSON がこの形)

### Example で実物を相手に確かめたこと (2026-11-20・添付・デザイナは Friendly で操作・Web は Playwright のヘッドレスのブラウザ)

- デザイナ: メタ情報の更新で案件 (Projects) に `AttachmentsA` (種類 `Attachments`・表示名「資料」) が足された (前のファイルとの差はこれだけ)。デザインチェックの指摘 0 件・送信
- 新しい案件で 2 つ選ぶと「未保存」で並びリンクは出ない → 保存で作成と一緒に送られる・保存後は名前・大きさ・ダウンロードのリンク・
  ダウンロードの中身が一致し `attachment` / `application/octet-stream` / `nosniff`・リンクのクリックで保存される・削除の印と追加 (足したものの取り消し・同じファイルの選び直し) を保存して読み直した画面と API の値が一致・
  消したファイルのダウンロードは 404・バイナリの中身が一致・10 MB を超えるファイルは「… 10 MB を超えています。」で足さない・
  URL の Guid を別のレコードの添付に / フィールドを添付でないものに / Id を別のレコードに差し替えると 404・一覧の行は名前だけ・ログインしていないと 401・別のユーザー (同じ権限) はダウンロードできる・
  添付のある案件の金額の更新で添付は変わらない・楽観ロックで添付の保存も止まる・案件の検索
- 一覧に添付の列を足すと案件名の列が狭くなる (幅を指定すると一覧がはみ出したので幅は指定していない)
- 確認用に手元だけで案件にコピーのボタン (CopyModuleButtonField) / 自動保存 (AutoSubmitField) を足したデザインで (リポのデザインは変えていない):
  コピーすると添付は並ばず (ほかの項目は写る)、保存した新しいレコードの添付は空・元のレコードの添付はそのまま。
  自動保存で足したファイルはプリザンターに入り、画面では「未保存」と取り消し・削除のボタン・リンクが消える。その後に読んだファイルを消す自動保存はエラーにならない。読み直すと足したファイルも消せる。
  1 つ目を足した自動保存の要求をブラウザで止めている間に 2 つ目を足すと、次の自動保存は 2 つ目だけを送り、プリザンターと読み直した画面のファイルはちょうど 2 つ (二重にならない)
- 確認用のデザインで分かった組み合わせ: OptimisticLockingField は AutoSubmitField と併用できない (本体のエラー)。
  作成日時 (`CreatedTime`) のフィールドがあるモジュールでのレコードのコピーの保存は「読み取り専用の列」のエラーになっていた (コピーが作成日時の値も写すため) → 作成ではキー以外の読み取り専用の列を送らないように変えた (上の「表と列」)。
  変えた後、作成日時のフィールドを残したまま手元だけでコピーのボタンを足したデザインで、コピー (作成日時も写る) → 保存が通り、新しいレコードの作成日時はプリザンターが決めた値になった

### 未確認

- 別のホスト (別のドメイン) のプリザンターとの SSO (Cookie の届き方。確認は同じ localhost の別ポートだけ)
- SQL Server・MySQL のプリザンターでの文字の比較 (大文字・小文字・全角半角・照合)。メモリでの評価は PostgreSQL のプリザンターに合わせてある (`=` は区別・like は区別しない)。照合の違う DB では View の道とメモリの道で結果が変わりうる
- メモリでの並び (文字は序数) とプリザンターの並び (DB の照合) の違い。メモリで並べるのは結合先の列・リンクの列・`TitleIsRaw = false` の Title のときだけ
- サイト統合・Wiki のサイトを Sites に書いたときの実物の getsite の応答 (エラーにする判定はソースと偽物のテストだけ)
- 複数選択の列・ユーザー / 組織の選択肢の列の値の形 (条件はメモリで評価・値は文字のまま)
- タイトルを複数の列で作るサイトでの Title の条件・並び (メモリにする判定は偽物のテストだけ)
- 書き込みの応答を受け取れなかったときの実物の挙動 (`WriteResultUnknown` は偽物のテストだけ)・429 (日次の上限) の応答
- 1 回の上限 (既定 200) をサイトの設定で変えた環境 (受け取った行数で Offset を進めるので動くはずだが未確認)
- General.json の MinTime を変えた環境 (`EmptyDate`)
- 拡張 SQL の日付・日時・真偽・null のパラメータ (値の形は偽物のテストだけ)・複数行を返す拡張 SQL・`Api` が false の拡張 SQL の応答
- コメントの列を画面 (デザイナ・Example) に置いたときの見え方
- 添付: 子の一覧 (明細) の行に置いたときの動き・SSO の利用者でのダウンロード・数十 MB のファイル (保存の要求の大きさ・プリザンターの上限)・列の `LimitSize` を超えたときの画面の見え方・
  一覧の列の見出しでの並べ替え (`CanUserSort` が true のままだと条件と同じくエラーになるはず)・デザイナのプロパティの画面の見え方 (撮影できなかった。撮り直しも画面が真っ黒)

### 未実装 (コネクターが未対応なだけで、プリザンターの制限ではないもの)

- ロック (`Locked`)・プロセス・サイトのスクリプト / サーバースクリプトの起動
- 添付の名前の変更・並べ替え・画像のプレビュー
- コメントの編集・削除 (足すだけ)
- 利用者本人の API キーでの接続 (利用者ごとの認可はプリザンター側では見ない)

### 添付の調査 (2026-11-19) → 専用のフィールドで実装 (2026-11-20)

- 本体の FileField は 1 フィールド 1 ファイルで、中身がホストのファイルストレージにあり `IDbAccessor` を通らないので使わない。
  本体の口を足さずに済むよう、値をファイルの並びの JSON の文字にした専用のフィールドと、ホストが呼ぶダウンロードのメソッド (`GetAttachmentAsync`) にした (上の「添付」)
