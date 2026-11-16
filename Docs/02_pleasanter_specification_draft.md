# プリザンターコネクタの仕様・設計案

**Codeer.LowCode.Blazor.Connectors.Pleasanter**　2026年11月16日 改訂  
対象はCLBのServer・WPF Designer・導入先アプリ。プリザンターのAPI契約と実機確認項目は [API調査](01_pleasanter_api_inventory.md) を参照。

## 1. 採用案と前回からの変更

**プリザンターをCLBのデータソースとして登録し、既存のプリザンターのテーブルから通常のCLB Moduleを作る。** 開発者はDesignerのDB設定で表と列を選ぶ。利用者が一覧・検索・詳細・保存を操作すると、サーバーの `PleasanterDbAccessor` がそのデータソースに対するCLB生成SQLをプリザンターAPIの要求に変換する。

11月13日案で未確定だった接続点は、両先行コネクタの実装に合わせて **`IDbAccessor`** とする。プリザンター用の入出力Module、クライアント用Web API呼出フィールド、汎用データソースの全製品共通化は初期実装の前提に置かない。上位の保存経路には、アクセサー単体で判断できない保存単位・利用者・編集時元値の確認だけを加える。

| 論点 | FileMaker版で確認 | NetSuite版で確認 | プリザンター版の決定 |
|---|---|---|---|
| 通常のModule操作 | PostgreSQL方言の生成SQLをOData要求に翻訳 | Oracle方言の読取をSuiteQL、書込をRESTへ変換 | PostgreSQL方言を入口にし、対応する式だけを `View` とItem JSONへ変換 |
| Designer | Toolsでメタ情報を更新し、保存済みファイルからテーブル定義を返す | `Records` で許可した種別を提示。同じオフライン方式 | 登録サイトの設定を正規化・保存。共通のDBテーブル選択UIとヘッドレス設計で共用 |
| キーと関連 | テーブル定義からキーを確認。参照先をまとめて読み、CLBの別名で結果を返す | 親レコードとサブリスト、読取用明細表を区別 | `ResultId`／`IssueId` を使う。親子は別々のサイト・レコード。子の分類列に親IDを保存 |
| 保存と障害 | 作成結果のキー不明を `WriteResultUnknown` に分類 | 外部書込はDBトランザクション外。公開レビューは部分成功と画面表示を検討 | 単件保存を初期単位とし、混在・複数は書込前に判定。保存済み・結果不明を画面まで伝える |
| 証拠と開発手順 | 実際の `ModuleDataIO` を使うテスト、モック、実応答形状の検討がある | モックのほかDesigner・Seleniumのシナリオがある | 同じくCLB通常経路で試験する。実機応答を採り、モックとの差異を記録する |

確認したのは11月16日のFileMaker `a0227e3`、NetSuite `d7d35a1` のソース。両READMEとも実サービスでの確認は未完了と明記している。この文書中のPleasanter用クラス・設定キーは新規実装の提案である。[FileMaker][FM]・[NetSuite][NS]・[FileMakerのSQL変換テスト][fmtest]・[NetSuiteのレビューとその対象版][review]

## 2. 導入後の操作例

例は「案件」と「取引先」。プリザンターではそれぞれ独立した記録テーブルを持ち、案件の `ClassB` が取引先を参照している。

1. 管理者がデータソース `Pleasanter` にアプリケーションURL、実行キーの参照、`Projects → 1200`、`Customers → 1300` の対応を登録する。検証・本番でサイトIDが違っても、CLBデザインの別名は維持できる。
2. Designerで **Tools → プリザンターのメタ情報を更新**。設計用キーで登録サイトを取得する。保存後は資格情報なしでもテーブル・列候補を開ける。
3. 通常のテーブル選択から `Projects` を選び、下表のModuleを作る。項目表示名とレイアウトはアプリ側で調整できる。すでに手で作ったModuleにも同じDB設定を指定できる。
4. 実行時に案件を検索し、取引先名を表示して金額を編集する。変更後にプリザンターの案件画面を開くと、同一レコードの金額が更新されている。単純リンクのタイトル表示の詳細は第6節。

| CLB設定 | 例 | プリザンターでの意味 |
|---|---|---|
| `Module.Name / DataSourceName / DbTable` | `Project / Pleasanter / Projects` | サーバー登録のサイトID 1200 |
| IdFieldの `DbColumn` | `ResultId` | 自動採番。Issuesを選ぶと `IssueId` |
| TextField `ProjectName` | `Title` | ルートの案件名 |
| SelectField `Category` | `ClassA` | 固定の区分コード。表示名は別に持つ |
| LinkField `Customer` | `ClassB` | 取引先のID。対象Moduleは `Customers` に結び付いたもの |
| NumberField `Amount` | `NumA` | `NumHash.NumA`。精度はdecimal |
| BooleanField `Checked` | `CheckA` | `CheckHash.CheckA` |

モジュールのDB設定は既存の `DataSourceName / DbTable / DbColumn` を使う。PleasanterのURL、サイトID、Hash名、検索JSONをフィールドごとに設定する必要はない。[FileMakerの導入例][fmguide]・[NetSuiteの導入例][nsguide]

## 3. Server・Designerへの組込み

先行版と同様にServerとDesignerの2パッケージを想定する。調査した先行版はServerが `net8.0`、Designerが `net8.0-windows`、CLBパッケージ参照が `1.3.68`。この組合せを再現基準にし、配布対象のCLBでも生成SQLと戻り値を照合する。[Serverプロジェクト][fmproj]・[Designerプロジェクト][fmdproj]

| 配置・責務 | 仮のクラス名または既存の接続点 | 入出力 |
|---|---|---|
| Server／接続・資格情報 | `PleasanterConnectionSettings`、`PleasanterConnection` | 設定から対象サイト・HTTPSルートを確定。マスク済み診断、JSON送受信、エラー分類 |
| Server／テーブル定義 | `PleasanterTableDefinition`、`PleasanterMetadataFile` | 取得済みの有効列・ID・型・候補・能力を `DbTableDefinition` に投影。実行時にも同じ定義を配布 |
| Server／データ操作 | `PleasanterDbAccessor : IDbAccessor` | 自分の接続名だけ処理。他の接続は同じ引数で内側のアクセサーへ委譲 |
| Server／翻訳 | `PleasanterQueryTranslator` とItemの変換処理 | SQL＋型付きパラメータ→単一表の要求計画→View／更新JSON。HTTP送信前に全文を解析 |
| Designer／初期化 | `PleasanterDesignerInitializer.Initialize / Setup / CreateDbAccessor` | 起動前に `SpecDocCatalog` に設計ガイドを登録し、起動後にToolsを追加。`IDbAccessorFactory.Create` でラップ |
| ホスト／通常経路 | `DataService`、`CustomizedModuleDataIO`、Submit受付 | ラップ済みアクセサーを通常の `ModuleDataIO` に渡す。権限や入力検証を既存の経路に残し、保存要求全体の事前確認を行う |

[FileMakerのアクセサー][fmaccess]・[NetSuiteのアクセサー][nsaccess]・[Designer初期化][fminit]・[ホストの組込み][fmhost]

### 接続設定例（提案）

```json
{
  "Name": "Pleasanter",
  "DataSourceType": "PostgreSQL",
  "ConnectionString": "Provider=Pleasanter;BaseUrl=https://example.invalid/fs/;ApiKeyRef=PleasanterRuntime;Sites=Projects:1200,Customers:1300"
}
```

`PostgreSQL` はCLBに生成させるSQL方言。プリザンターをホストするDBの種類に依存せず、DB接続を開くこともない。`ApiKeyRef` はホストのシークレット名。設計用キーはToolsの実行時に別名で解決する。スナップショット利用時にはどちらのキーもDesigner起動条件にしない。Serverはキー参照が欠落した場合、そのデータソースだけを構成エラーにする。指定名が重複するデータソースや、同一接続内の別名重複・不正なサイトIDは設定時に検出する。

`Sites` は明示的な許可リストで、左辺はSQL識別子として使える固定の別名、右辺は環境固有の正のInt64。例では英数字・アンダースコアの別名を使う。接続先やSiteIdをブラウザー入力で変更できないようにする。`GetDataSource` から返す情報は接続名とSQL方言のみ。ServerとDesignerの資格情報、不要な生のサイト設定やスクリプトをデザイン・ログへ書き出さない。

ホスト側の組込み例（提案するAPIの形）。`dataSources` は既存ホストの設定、`runtimeSecrets` はサーバー側の資格情報解決、`designDirectory` はその環境の定義配置先を表す。

```csharp
var inner = new DbAccessor(dataSources);
var connections = PleasanterConnection.FromDataSources(dataSources, runtimeSecrets);
var accessor = new PleasanterDbAccessor(inner, connections, designDirectory);
// この accessor を、ホストが作る CustomizedModuleDataIO に渡す。
```

Designerのファクトリは `PleasanterDesignerInitializer.CreateDbAccessor(new DbAccessor(dataSources), dataSources)` の形に揃える。Toolsからの定義更新だけが設計用資格情報を解決する。

### アクセサーの操作契約

| CLBの入口 | Pleasanter接続で行うこと | 戻り値・制約 |
|---|---|---|
| `GetCustomTableDefinitionsAsync`（リスト／単表） | 登録別名に対応する保存済み定義を検索 | リスト・単表とも同じ列を返す。未知の表名は設定エラー |
| `QueryAsync` | SELECT／COUNTを解析し、対象サイトのgetを実行 | 列を型変換してCLBが付けたSQL別名をキーに返す。COUNTは1行の `count` |
| `InsertAsync` | 単件INSERTと採番列を確認しcreate | 応答ルートの実IDを10進文字列で返す。CLBが仮IDと対応付ける |
| `ExecuteAsync` | 単件主キー付きUPDATE／DELETE | 成功1、条件不一致が確認できた場合0。権限エラー・障害・結果不明を0にしない |
| `GetConnection / GetTransaction` | 外部APIにDB接続は存在しない | 前者は未対応、後者はnull。通常DB向けは委譲 |
| `StartTransaction / CommitAsync / RollbackAsync` | 内側のDBに委譲 | 外部で確定した要求は対象外。保存前検査は第7節 |
| `ExecuteSqlCommandAsync`、任意SQL・ストアド等 | 未対応としてエラー | 不明なSQLを通常DB、拡張SQL API、全件取得へ転送しない |

`Provider=Pleasanter` と認識した構成でキーやスナップショットが不正な場合も通常DBへ委譲しない。FileMaker・NetSuiteとの重ね掛けを含め、適切な1個のアクセサーだけが各呼出しを処理するテストを置く。[FileMakerの実装][fmaccess]・[NetSuiteの実装][nsaccess]

## 4. メタ情報の取得と配布

**通常起動・CLI・実行サーバーはスナップショットを読む。** 実行サーバーから毎回 `getsite` を呼ぶと設計用の権限が必要になるため、ここは先行版の実行時メタデータ取得と変える。ファイル名は `pleasanter.<データソース名>.metadata.json`。Designerで採取するか、同じ形式の検証済みファイルを導入先で作成し、対象環境のサーバーへ配置する。[公式サイト取得][site]・[メタ情報の保存方式][fmmeta]・[NetSuiteの保存方式][nsmeta]

1. 許可したサイトごとに `getsite`。環境・TenantId・SiteId・ReferenceTypeを照合する。標準項目と `EditorColumnHash`、`GridColumns`、`Columns` の差分を合成し、空のサイトでも定義を作る。
2. 表別名、表示名、ID列、内部列名、CLB/.NETの型、API格納箇所、必須・小数桁・静的候補、単純参照、操作別の可否を保存する。サイト統合、未知の型、判断できない候補規則には理由を付ける。列名だけから編集可と推定しない。
3. 取得時刻・形式版・環境識別子・対象サイト・定義の指紋を持つ。ハッシュ対象は正規化した業務定義。APIキーは含めない。検証と本番で同じ `Projects` を使う場合、本番のID・定義で再取得し、列契約を突き合わせる。
4. 同じファイルへの更新を直列化し、同一ディレクトリの一時ファイルに書く。再読込・全サイトの検証が通ってから置換し、Designerの `RefreshDatabase()` を呼ぶ。途中失敗は既存の定義で引き続き設計できる。別プロセスからの同時更新はロック、または前版の指紋比較で上書き競合を防ぐ。
5. 改名は表示名だけ追従。内部列名が同じ既存Moduleやレイアウトはそのまま使う。型変更、リンク先変更、重要列消失は差分表示し、その列を使う操作を保留する。権限による欠落と削除を混同しない。スナップショットなしの初回は「メタ情報を更新」を案内する。

`DbColumnDefinition.IsNullable` と業務の入力必須を別に管理する。先行版ではNOT NULLと見せるとCLBが空値を型既定値へ変えるケースが記録されている。プリザンター版も初期は空値を保持する列定義とし、表示上の必須はFieldに、最終判定は対象APIに任せる。作成時の未指定が0や最小日付に変わらないことを通常Submitから試験する。[FileMakerのテーブル定義][fmtable]

保存形式の例（1列を抜粋）。`schemaVersion` はコネクタのファイル形式の版で、プリザンターの `Ver` や `SiteSettings.Version` と区別する。候補・能力はこの内部形式に持ち、CLB側の `DbTableDefinition` には表現できる情報だけを渡す。

```json
{
  "schemaVersion": 1,
  "dataSourceName": "Pleasanter",
  "tables": [
    {
      "name": "Projects",
      "siteId": 1200,
      "referenceType": "Results",
      "idColumn": "ResultId",
      "columns": [
        { "name": "NumA", "label": "金額", "jsonGroup": "NumHash", "kind": "Decimal" }
      ]
    }
  ]
}
```

## 5. 検索・件数・射影の設計

SQLはCLBが構成した形に限定する。FileMakerのテストから、引用符付き識別子、列別名、型付き引数、LIKEのエスケープ、LIMIT/OFFSET、採番用RETURNINGの試験ケースを流用できる。ODataのフィルタ生成と型変換はプリザンター向けに置き換える。[SQL変換の実例][fmtest]・[公式View][view]

| CLBが要求する形 | APIへの変換・結果 | 初期の扱い |
|---|---|---|
| 単表SELECT、列別名、ID指定の1件取得 | サイト内get。Itemの基本列・Hashから必要列を選び、SQL別名どおりに返す | 対応。対象サイト、条件、返却IDの一致を検査 |
| 単表 `count(*)` | 一覧と同じViewで `Offset=0, PageSize=1`。総件数を返す | 対応。行データ全体を集めて数えない |
| 文字列の等価・部分一致 | `ColumnFilterHash` と `ExactMatch`／`PartialMatch`。CLBのLIKEエスケープを先に復元 | 対応対象。値に `%`、`_`、引用符が含まれるケースを検証 |
| 同じ列の候補集合、異なる列のAND | 候補集合は対応列の値配列をJSON文字列にし、列別条件を合成 | 対応対象。同じ列への複数条件は積集合・空集合として扱うか、表現できなければ拒否 |
| 数値・日時範囲、真偽 | 列型に応じて列フィルタへ変換 | 対象版で境界・小数桁・false・時刻帯が一致した組合せを有効化。`<` を `<=` に緩めない |
| ORDER BY、LIMIT/OFFSET | `ColumnSorterHash`、`PageSize`、`Offset`。同順位の最後にIDを追加 | 基本列で対応。表示名順と内部値順が違うリンク・選択列は意味が合う場合だけ有効 |
| AND/ORの任意の入れ子、NOT、NULL判定、算術式、GROUP BY、SUM等 | Viewで等価に表現できる能力は操作ごとに異なる | 初期は未対応形を実行前に報告。算術や集計は後続 |
| LinkFieldが生成する親へのLEFT JOIN | 単純参照の表示専用形だけ第6節の読取に展開 | 親列フィルタ・親列ソート・一般JOINは初期対象外 |

例えばCLBの問合せが次の形なら、サイト1200の `get` で `ColumnFilterHash.Title="検品"`、`ColumnFilterSearchTypes.Title="ExactMatch"`、ID昇順、`PageSize=20, Offset=40` とする。Itemの `NumHash.NumA` は結果の `Projects_NumA` として返す。COUNT要求にも同じTitle条件・利用者範囲を適用する。

```sql
select "Projects"."NumA" as "Projects_NumA"
from "Projects"
where "Projects"."Title" = @p0
order by "Projects"."ResultId"
limit 20 offset 40
```

実装ではパラメータ名と値を別に保持し、SQLの解析を最後まで完了してからJSONを組み立てる。不明な末尾や複合条件を読み飛ばさない。列フィルタとソートはメタ定義で内部名を解決する。定数偽・候補の空集合は外部要求0回で0件を返せる。

**ページング。** CLBページ番号は0始まり。上限不足なら必要行まで追加取得し、毎回受信行数だけ進める。空ページ・進捗なしで停止し、応答サイズ・無制限要求の最大読取行数を設ける。上限到達時に不完全な一覧を全件として返さない。COUNTと一覧が別SQLならまず各1回で実装し、最適化する場合も、権限・条件・要求単位を越えて件数を使い回さない。サイト統合で件数の母集団を確認できない接続は対象外。計数だけの経路でも単一サイトの条件が確定していることを確認する。

## 6. 選択肢、親子、権限

**固定選択肢**はプリザンターの値と表示名を正規化し、対応するCLB Enum／SelectFieldを作れるようにする。ラベル重複・改名があっても保存は内部値。既存Enumを自動上書きせず、変更点を提示する。書式を解釈できない選択肢を自由入力へ自動変更しない。

**単純リンク**は、許可された親サイト1個を参照する単一選択に限る。子ページを得た後、そのページの親IDを重複除去し、親サイトにID集合の条件を付けて分割取得する。親IDとTitleの辞書でCLBが求める列別名を組み立てる。N行につきN回取得しない。候補検索も親Moduleの権限・固定条件で行う。保存時には実行主体が選べる親かを確認し、子のClassHashに親IDの文字列だけを送る。閲覧不可・削除済みの親はラベルを非表示にし、子の別項目を編集しても既存参照を勝手に消さない。複数選択、条件連動、Lookupの転記は別の能力として扱う。[プリザンターのリンク][link]・[FileMakerのリンク結合実装][fmaccess]

**親子一覧**は子サイトの親IDによる絞込みで構成し、親子レコードは個別に編集する。親の保存時に画面へ出ていない子を削除する処理は作らない。NetSuiteのサブリスト用JSON、明細の全置換、専用の仮想明細表はプリザンターのサイト間リンクには持ち込まない。[NetSuiteの明細設計][nsguide]・[先行レビューの並行編集例][review]

**共有APIキーと各利用者の権限。** 初期のデータ接続はサーバーの連携用キーを使う。プリザンターが評価するのはそのキーの権限で、CLBへのログインと連動する個人の権限ではない。CLBサーバーが確定したModule・操作・列・行条件を一覧、詳細、件数、親候補、保存前と保存後の返却に一貫して適用する。行を取得後に除外する方法では総件数を守れない。アクセス範囲を決める所有者等は新規値・変更後の値にも適用する。変更可能な行条件を保存時点で原子的に強制する必要がある案件では、共有キーによる事前確認だけの書込は提供せず、個人の権限で実行する方式等を別途選定する。SSO・APIキーの個人管理はデータソース機能と独立して検討できる。[APIキー][key]

## 7. 保存、競合、結果の契約

**保存単位は外部1レコードへの1操作**。登録はサイトID、更新・削除はレコードID。IdFieldの手入力を無効にし、UPDATE／DELETEのWHEREには主キーを必須とする。追加の固定条件や元値条件は省略せず、対象サイト・レコードの再取得で評価する。条件が合わない場合は書き込まない。権限判定で参照できない行の存在は利用者に開示しない。

| 操作 | 送信前 | 成功後 |
|---|---|---|
| 新規 | 書込可能列と必須項目を検査。未指定は省略し、外部既定値を生かす | ルート `Id` を仮IDに対応付け、必要な列を再読込。実IDの取得不能は結果不明 |
| 更新 | 編集開始時の比較情報と現在値を照合。変更した列のみを組み立てる | 未変更列・読取専用列を送らず、0・false・明示クリアは区別。計算・更新情報を再読込 |
| 削除 | 所属・権限・元値・削除設定を確認。単件のdeleteを使用 | 削除成功を返す。確認目的で同じdeleteを再送しない |

**比較元は編集時に取る。** 先行例のアクセサーが受け取るSQLと引数だけでは、元値や提出者を常に確定できない。CLBの元値条件で必要値が渡るか、実際のSubmitで採取する。不足分は表示時にサーバー発行の編集識別子で利用者・Module・サイト・ID・期限・元値を結び、ホストの保存境界で検査する。複数タブで同じ行を開けるので、利用者＋行IDだけで比較元を上書きしない。更新直前に取った値を編集開始時の値として扱わない。元値取得を保証できない編集経路は読取までを提供する。

比較は `UpdatedTime`、必要列の元値、利用可能なら `Ver` を併用。事前読取と書込の間の同時変更は残る。原子的な条件付き更新が必要なModuleでは、標準APIだけでCLBの厳密な `OptimisticLockingField` に対応すると表示しない。明示クリアは検証済みの列型だけ通す。日付の環境依存値は接続側に置く。[API調査](01_pleasanter_api_inventory.md)・[Resultsの更新処理][resultutil]

**複数変更の事前判定。** アクセサーの `StartTransaction` は要求全体の内容を知らない。通常のSubmitで `SubmitWithTransactionAsync` に渡す前に、サーバーのModule設計から対象接続を解決し、子・削除・一括追加を展開した変更予定を検査する。複数のPleasanter行、親子同時保存、通常DBとの混在を検出したら**最初のDB変更・外部書込の前**に要求全体を拒否する。単に2本目のSQLで止める方法では1本目が残る。ファイル取込、Bulk、スクリプト・ジョブ経由にも同じ検査を通すか、その入口で対象Moduleを選べないようにする。通常DBのみのSubmitでは既存のトランザクション動作を維持する。[通常・一括の受付例][fmcontroller]・[外部トランザクションの実装][nsaccess]

監査ログ等のinterceptorによる追加書込も原子性の範囲に注意する。外部保存成功後にローカル監査・後処理が失敗しても、画面には「未保存」と返さず、確定した外部IDと処理段階を残す。複数変更を事前に列挙できない拡張経路は、外部編集での有効化前に個別検証する。

| 結果 | CLB画面・サーバーの扱い |
|---|---|
| 入力／権限による送信前拒否 | 修正可能な項目または権限不足を表示。書込0件 |
| 他者の更新を事前比較で検出 | 競合を表示。入力を保ち再取得・比較を案内 |
| 書込成功・再取得失敗 | 「保存済み。再読み込みしてください」。確定IDを保持し、書込は繰り返さない |
| 作成等を送信後に応答喪失／確定不能 | 「保存結果を確認できません。再登録前に照合してください」。操作ID・既知の業務キーで確認。自動再送しない |
| 日次上限／一時制限 | 回数枯渇と一時制限を区別。再試行は意味上の読取に限定し上限を設定 |
| 未対応の検索・保存形式 | 該当Module・列・操作名を表示。利用者の入力値やAPIキーはエラー・ログに含めない |

## 8. 提供範囲と実装順

| 段階 | 実装・確認する内容 | 完了の判断 |
|---|---|---|
| 1. 最小の縦切り | Results 1サイト、接続登録、メタ取得・再読込、Designerの表・列、通常Moduleの一覧・詳細・件数 | 空テーブルでModule生成、データ投入後にCLBとプリザンターの同じ行を確認。実APIと同じ形のfixtureを確立 |
| 2. 単件CRUD | 生成SQL、採番、変更列保存、元値、エラー分類、複数保存の事前拒否 | 標準Submit、応答喪失、混在操作で第9節の受入条件を満たす |
| 3. 実用的な初期版 | Issues、固定候補、単純リンク、2親ID以上の一括表示、日時・数値の確認、開発・本番の別名対応 | 記録・期限付きの2種でDesignerから画面まで試験。実環境ごとの対応能力を明記 |
| 4. 個別拡張 | 複数選択、Lookup、動的候補、添付・コメント追記、プロセス、集計、一括同期、個人別API実行 | 利用する機能ごとにAPIの権限・応答・失敗・再送・画面動作を確認して追加 |

初期の対象プリザンターは `getsite` と要求 `PageSize` が利用できる版を候補とする。両機能の導入版からは1.4.13.0以降が候補だが、製品として保証する範囲は実機試験後に定める。新たなサーバースクリプト・DBドライバーを導入せず、標準APIだけで最初の3段階が成立することを目標にする。[API共通][common]・[サイト取得][site]

## 9. 受入試験と実装担当への引継ぎ

試験は「翻訳単体」「実 `ModuleDataIO` ＋HTTP記録モック」「WPF Designer／ブラウザー」「実プリザンター」の4通りを使い分ける。実サービスの版・設定、要求本文のキーを伏せた記録、CLB画面の値、外部での確定結果を同じケースIDで結ぶ。モック独自の便宜的な成功応答は製品仕様に加えない。[FileMakerの結合試験][fmio]・[NetSuiteのDesigner試験][nsuitest]・[NetSuiteのブラウザー試験][nswebtest]

| ID | 具体的な操作 | 合格条件 |
|---|---|---|
| P01 | 許可2サイトを取得。表示名の重複・変更、空テーブル、Columnsの省略を含める | 表・列の内部名が一意。再取得で既存レイアウトを壊さない。未許可サイトは選べない |
| P02 | 途中でメタ取得失敗。再起動し、通信と設計用キーなしでDesigner・CLIを開く | 直前の正常定義を使える。CLIと画面で同じ列。ファイルにキーや生のサイト設定なし |
| P03 | サーバー上限2、CLBページ3、状態が同じ5行を作り2ページ読む | 3行＋2行、総件数5、同順位をIDで安定化。COUNTも同じ固定条件。未対応のOR等は検索範囲を拡げない |
| P04 | Resultsを通常画面から作成。金額0・確認falseだけを更新し、削除 | 実IDは文字列で一致。未変更の分類・既定値を保持。更新後の値は外部画面でも一致 |
| P05 | Issuesの期限をキー利用者の時刻帯で往復。未指定・空・日付消去・閲覧不可列を試す | 日付ずれ、未指定値の上書きなし。未検証の消去は送信前エラー |
| P06 | 同じ親を持つ子を含む一覧、読めない親、別サイトのID、動的候補を表示 | 参照可能な親だけタイトル表示。N+1読取なし。越境・未対応条件で保存しない。読めない親を変更なしで消さない |
| P07 | 他者が画面を開いた後に更新。2タブで開いて片方を先に保存 | 先行更新済みなら後の更新・削除は事前比較で停止。確認後の競合余地は仕様どおりに示す |
| P08 | 登録成功後に応答を失わせる。別ケースで再読込だけを失敗させる | 外部書込はそれぞれ1回。利用者は「結果不明」「保存済み」を区別できる |
| P09 | DB＋外部1件、外部2件、親子、ファイル取込・Bulk、条件削除を試す | 対象外要求は通常DB・外部とも変更0。通常DBのみの既存保存・ロールバックが通る |
| P10 | 共有キーで閲覧範囲が異なるCLB利用者、列権限・行条件の変更、統合サイトの有効化 | 一覧・件数・詳細・候補・保存が同じ範囲に従う。範囲不明なら返さない。管理者キーへの自動切替なし |

実装の最初の一巡は、先行版のHarnessに倣って1件の `GetListAsync` と1件のSubmitが生成するSQL・引数・結果を記録し、対応するプリザンター側の要求と突き合わせる。特に先に決める項目は、**①実機のgetsiteから復元できる有効列、②通常Submitで取得できる編集時元値と差分、③件数・権限条件のViewでの再現性**。成立した経路から対応範囲を広げる。

[FM]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/README.md
[NS]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.NetSuite/blob/d7d35a1703aeb2456397787074d6e1d7fe8d20c9/README.md
[fmtest]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Codeer.LowCode.Blazor.Connectors.FileMaker.Test/ODataTranslatorTest.cs
[review]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.NetSuite/blob/d7d35a1703aeb2456397787074d6e1d7fe8d20c9/Docs/07_connector_review.md
[fmguide]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Docs/FileMaker.md
[nsguide]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.NetSuite/blob/d7d35a1703aeb2456397787074d6e1d7fe8d20c9/Docs/NetSuite.md
[fmproj]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Codeer.LowCode.Blazor.Connectors.FileMaker.Server/Codeer.LowCode.Blazor.Connectors.FileMaker.Server.csproj
[fmdproj]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Codeer.LowCode.Blazor.Connectors.FileMaker.Designer/Codeer.LowCode.Blazor.Connectors.FileMaker.Designer.csproj
[fmaccess]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Codeer.LowCode.Blazor.Connectors.FileMaker.Server/FileMakerDbAccessor.cs
[nsaccess]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.NetSuite/blob/d7d35a1703aeb2456397787074d6e1d7fe8d20c9/Source/Codeer.LowCode.Blazor.Connectors.NetSuite.Server/NetSuiteDbAccessor.cs
[fminit]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Codeer.LowCode.Blazor.Connectors.FileMaker.Designer/FileMakerDesignerInitializer.cs
[fmhost]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Example/LowCodeApp.Server/Infrastructure/DataService.cs
[site]: https://pleasanter.org/ja/manual/developers-guide/api/site-operations/api-site-get/
[fmmeta]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Codeer.LowCode.Blazor.Connectors.FileMaker.Server/FileMakerMetadataFile.cs
[nsmeta]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.NetSuite/blob/d7d35a1703aeb2456397787074d6e1d7fe8d20c9/Source/Codeer.LowCode.Blazor.Connectors.NetSuite.Server/NetSuiteMetadataFile.cs
[fmtable]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Codeer.LowCode.Blazor.Connectors.FileMaker.Server/FileMakerTableDefinition.cs
[view]: https://pleasanter.org/ja/manual/developers-guide/json-data-layout/api-view/
[link]: https://www.pleasanter.org/ja/manual/users-guide/hands-on/advanced/advanced-operations-link/
[key]: https://pleasanter.org/ja/manual/developers-guide/api/basics/api-key/
[resultutil]: https://github.com/Implem/Implem.Pleasanter/blob/664c833b144d9da9b6bb58e60f7e48eb01141e58/Implem.Pleasanter/Models/Results/ResultUtilities.cs
[fmcontroller]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Example/LowCodeApp.Server/Controllers/ModuleDataController.cs
[common]: https://pleasanter.org/ja/manual/developers-guide/json-data-layout/api-common/
[fmio]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Codeer.LowCode.Blazor.Connectors.FileMaker.Test/FileMakerDbAccessorTest.cs
[nsuitest]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.NetSuite/blob/d7d35a1703aeb2456397787074d6e1d7fe8d20c9/Source/Codeer.LowCode.Blazor.Connectors.NetSuite.DesignerTest/NetSuiteDesignerTest.cs
[nswebtest]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.NetSuite/blob/d7d35a1703aeb2456397787074d6e1d7fe8d20c9/Source/Codeer.LowCode.Blazor.Connectors.NetSuite.SeleniumTest/Scenario/CustomerTest.cs
