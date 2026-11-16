# プリザンターコネクタのAPI調査

**Codeer.LowCode.Blazor.Connectors.Pleasanter**　2026年11月16日 改訂  
読者はCLBコネクタの実装担当者。設計上の採否、CLBとの接続点、受入条件は [仕様・設計案](02_pleasanter_specification_draft.md) に記載した。

## 1. 採用するAPI

既存のプリザンターテーブルを、そのままCLB Moduleのデータソースにする。設計時に対象サイトの定義を取り込み、実行時は同じサイトのテーブル操作APIで一覧・件数・詳細・保存を扱う。サイトの新設や既存データの移行は導入手順に含まない。

| 目的 | APIで利用するもの | CLB実装への影響 |
|---|---|---|
| Designerにテーブル・列を提示 | サイトID指定の `getsite` | サイト種別・内部列名と設定差分から仮想テーブルを組み立てる |
| 一覧・検索・件数 | サイトID指定の `get`、`View`、`Offset`、`PageSize` | 同じ条件での総件数は `Response.TotalCount`。返却配列の長さはそのページの件数 |
| 詳細 | レコードID指定の `get`、またはサイト内でIDを絞る `get` | 対象サイト・行条件・IDを確かめて1行を返す |
| 新規・更新・削除 | `create` はサイトID、`update` と `delete` はレコードID | 登録後のルート `Id` をCLBに返す。更新は変更列だけを送る |
| 固定選択肢・単純リンク | サイトの列設定、参照先サイトの `get` | 内部値と表示名を別に扱う。リンクの保存値は参照先レコードのID |

公式マニュアルで確認した通信契約と、公開ソースの特定版で確認した挙動を区別した。以下のJSONは説明用の架空値。プリザンター実機への接続・測定、コネクタのビルド・実行は今回の調査に含まない。コード比較はFileMaker `a0227e3`、NetSuite `d7d35a1`、プリザンター `664c833` を使用した。[FileMakerの実装][FM]・[NetSuiteの実装][NS]・[プリザンターのソース][PL]

## 2. URL、認証、操作別の応答

`{app}` は仮想ディレクトリまで含めたアプリケーションルート。例えば `https://pleasanter.net/fs/` に相対パス `api/items/1200/get` を結合する。以下はすべてHTTP **POST**、本文はUTF-8のJSONで、`Content-Type: application/json` を送る。外部からのサーバー連携では本文の `ApiKey` で利用者が決まり、取得日時もキー発行者のタイムゾーンに従う。例では数値の `ApiVersion: 1.1` を明示する。プリザンターの製品バージョンとは別の番号である。[APIキー][key]・[共通パラメータ][common]

| POST先（`{app}` に続く相対パス） | 主な入力 | 成功応答の読み方 |
|---|---|---|
| `api/items/{siteId}/getsite` | 設計用キー | `Response.Data` は単一オブジェクト。ここから `SiteId`、`ReferenceType`、`SiteSettings` を読む。[サイト取得][site] |
| `api/items/{siteId}/get` | 実行用キー、任意のView・ページ指定 | `Response.Data[]`、`Offset`、`PageSize`、`TotalCount`。サイト統合の設定があると他サイトの行も含み得る。[複数取得][multi] |
| `api/items/{recordId}/get` | 実行用キー | 単一指定でも `Response.Data[]`。取得不可、0件、想定外の複数件、所属サイト不一致を区別する。[単一取得][one] |
| `api/items/{siteId}/create` | 基本列と拡張列Hash | 公式成功例はHTTP 200、本文ルートに `StatusCode`、採番済み `Id`、`Message`。[作成][create] |
| `api/items/{recordId}/update` | 変更列だけ | 本文ルートの `StatusCode`、`Id`。計算や自動設定を含む保存後の値は、必要に応じて読む。[更新][update] |
| `api/items/{recordId}/delete` | 対象のレコードID、実行用キー | 単件削除の応答の `StatusCode` と `Id`。子の同時削除など、サイト側の設定による影響を導入先で確認する。[削除][delete]・[子の同時削除][cascade] |

HTTP結果、JSONの `StatusCode`、操作固有の構造を検査する。公開ソースの `ToHttpResponse` は本文のステータスをHTTPにも反映している。認証画面のHTML、認証リダイレクト、作成成功形式なのにIDが取得できない応答を通常の成功に扱わない。[応答生成][status]

## 3. サイト定義から得られるもの

記録テーブルは `Results`、期限付きテーブルは `Issues`。両者でID名と一部の標準列が違う。`getsite` の公式条件は製品版1.3.4.0以降、テナント管理者での実行、Wiki非対応。通常動作に管理者キーを配布する必要がないよう、設計時に取得し、必要な定義だけを保存する。実行用キーでの参照・更新範囲は別に検証する。[サイト取得][site]

| 取得元 | 採用する情報 | 読み取り時の注意 |
|---|---|---|
| `Response.Data` | `SiteId`、`TenantId`、`Title`、`ReferenceType` | 表示名は改名・重複し得る。IDとサイト種別を接続先の登録内容と照合 |
| `SiteSettings.EditorColumnHash`、`GridColumns` | 編集・一覧で使う列の候補 | セクション等のレイアウト要素を除く。画面にあるかどうかでAPI権限を推定しない |
| `SiteSettings.Columns` | `ColumnName`、`LabelText`、`ChoicesText`、`MultipleSelections`、桁・必須等の設定差分 | 既定値を省略できる。`Columns` のみに依存すると、例えば有効な `ClassA` が変更なしのため欠落する |
| `ReferenceType` ごとの標準定義と列名 | `ResultId`／`IssueId`、標準型、Hashの種類 | 使用列の集合と合わせて型を復元。実行時の型・権限キャッシュまでエクスポートされるわけではない |
| `SiteSettings.IntegratedSites` | サイト統合の有無 | 空配列は統合なし。非空なら複数サイトをまたぐ可能性がある。単一サイトの件数・更新先の前提から外す |

公開ソースでは `SiteModel.GetByApi` が `SiteSettings.RecordingData(...)` を返す。`TypeCs` 等には非直列化の実行時プロパティがある。先頭レコードから型を推定すると空テーブル、null、閲覧制限列を区別できない。標準項目・有効列・差分のうち確定できないものは、未解決と記録する。[SiteModel][sitecode]・[SiteSettings][settingscode]・[Column][columncode]

全サイトを探索する必要はない。許可したサイトIDを接続に登録する。`getclosestsiteid` は起点と `FindSiteNames` を受け、同じサイト名が複数あると意図したものと異なる可能性がある。初期登録の補助に使う場合も、解決したIDと種別を確認する。[サイト名検索][closest]

## 4. レコードとCLB列の対応

通常のItem形式を読み書きの基準にする。次の例では「案件金額」の表示名を変更しても、`DbColumn=NumA` と `NumHash.NumA` の対応を保てる。[Item][item]・[APIモデル][base]・[Resultsの更新処理][resultcode]

| 内部列 | 読取・書込時のJSON | 変換方針 |
|---|---|---|
| `ResultId`／`IssueId`、`SiteId` | ルートの数値 | .NET側はInt64。CLBのIdFieldへは10進文字列。自動採番、手入力不可 |
| `Title`、`Body` | ルートの文字列 | Titleの値と表示用タイトル構成を区別。Bodyの原文を保持 |
| `Status`、`Manager`、`Owner` | ルートの番号 | 状況コード、ユーザーID。ラベルや氏名の文字列を送らない |
| `ClassA` 等 | `ClassHash.ClassA` | 文字列、固定選択値、またはリンクID。サイト・列設定で意味が変わる |
| `NumA` 等 | `NumHash.NumA` | decimalとnullable。0を明示値として保持 |
| `DateA` 等 | `DateHash.DateA` | 日付と日時の意味、キー発行者の時刻帯を保持。表示文字列をUTCと仮定しない |
| `DescriptionA`／`CheckA` 等 | `DescriptionHash.DescriptionA`／`CheckHash.CheckA` | 文字列またはJSON真偽値。falseは変更値 |
| `StartTime`、`CompletionTime`、`WorkValue`、`ProgressRate`、`RemainingWorkValue` | Issuesのルート項目 | 表示・書込能力は項目単位。自動計算対象は読取扱いから始める |
| `Ver`、`UpdatedTime`、`Creator`、`Updator`、`CreatedTime` | ルート | 参照・表示用。版や更新者の上書きには使わない |
| `Comments`、`AttachmentsA` 等 | 専用形式、添付は `AttachmentsHash` | 値の全置換で追記・添付操作を実装すると意味が変わるため専用処理が必要 |

通常のHash形式では射影をコネクタ側で行う。平坦な表示用応答が必要なら `View.ApiDataType="KeyValues"` と `ApiColumnKeyDisplayType="ColumnName"`、`ApiColumnValueDisplayType="Value"`、`GridColumns` を個別に評価できる。表示名、単位付き文字、書式化された日付を保存値に転用しない。権限で返らない項目を、空文字・0・falseで補って更新しない。[出力形式][keyvalues]

## 5. 検索・件数・ページング

例えば「検品」を含み、状況が100または200の案件を新しい順に50件取得する場合、次の本文を `api/items/1200/get` へ送る。

```json
{
  "ApiVersion": 1.1,
  "ApiKey": "<runtime-key>",
  "Offset": 0,
  "PageSize": 50,
  "View": {
    "ColumnFilterHash": { "Title": "検品", "Status": "[\"100\",\"200\"]" },
    "ColumnFilterSearchTypes": { "Title": "PartialMatch" },
    "ColumnSorterHash": { "UpdatedTime": "desc", "ResultId": "asc" }
  }
}
```

`ColumnFilterHash` のキーは `ClassA` 等の内部列名。複数候補を表す値は、この例のように外側のJSON内で配列を表現する**文字列**になる。文字列の一致には `ExactMatch`／`ForwardMatch`／`PartialMatch` がある。数値や日時の範囲と、SQLの厳密な不等号が同じ意味になるかは列型ごとに確認する。自由検索の `View.Search` は指定列の部分一致と検索対象が異なる。[View][view]・[検索種別][searchtypes]・[公式の検索例][multi]

**件数とページは同一条件で計算する。** `TotalCount=135`、`Offset=100`、`Data` が35行なら、総件数135、当ページ35行である。要求 `PageSize` は1.4.13.0以降。サーバーの既定上限は200で環境設定に従う。調査版のResultsでは、要求値が正で設定上限より小さいときのみ要求値を使い、それ以外は設定値を使う。サーバー上限0は無制限になり得るため、要求で1を指定しただけでは軽量なCOUNTを保証できない。[共通パラメータ][common]・[Api.json][apiconfig]・[Resultsの取得処理][resultutil]

設計案ではCLBのCOUNT要求を `PageSize=1` と同一Viewの `get` に変換し、`TotalCount` を使う。通常のページ取得では、設定上限がCLBページより小さい場合に**実際の受信行数**だけOffsetを進める。安定した並べ替えの末尾にIDを加える。Offset方式では並行した挿入・削除によってページ間の重複・欠落が起こり得る。IDの並べ替えは同値行の順を決めるためのもの。[複数取得][multi]・[FileMakerの変換テスト][fmtest]

**ID指定の注意点。** 調査版の `ResultUtilities.GetByApi` はレコードIDのURLを使うと `View.ColumnFilterHash` に `ResultId` を追加する。同じキーを呼出側でも指定すると衝突し得る。所属サイトや固定条件付き詳細は、サイトIDのURLにIDを含むViewを適用する方式を優先する。IDの一致・所属・件数を照合し、Issuesでも同じ受入試験を行う。[Resultsの取得処理][resultutil]・[単一取得][one]

## 6. 作成・更新と欠損値

記録テーブルへの作成例。設定上の列名は「案件番号」「金額」「確認済」を想定した。キーは送信直前にサーバーで設定する。

```json
{
  "ApiVersion": 1.1,
  "ApiKey": "<runtime-key>",
  "Title": "大阪倉庫の検品",
  "ClassHash": { "ClassA": "A-024" },
  "NumHash": { "NumA": 12500.50 },
  "CheckHash": { "CheckA": true }
}
```

作成の成功応答例は `{"Id":8401,"StatusCode":200,"Message":"…"}`。後で金額を0、確認済をfalseにしたときは `api/items/8401/update` に次の本文を送る。Titleや案件番号には触れない。[作成][create]・[更新][update]

```json
{
  "ApiVersion": 1.1,
  "ApiKey": "<runtime-key>",
  "NumHash": { "NumA": 0 },
  "CheckHash": { "CheckA": false }
}
```

| 入力状態 | API契約と実装時の扱い |
|---|---|
| 未指定・未変更 | 該当するキーを省略。公開 `SetByApi` は指定されたHash要素を処理する。作成時の省略は外部既定値を生かす |
| 空文字・null | `Title:null` 等は更新スキップとなる実装がある。文字列の明示消去と区別。数値nullはAPIモデルが受け得ても、実際に消せるかは対象列の設定に依存 |
| 日付の消去 | 公式FAQは `General.json` のMinTime〜MaxTime外の値を指定する方式。消去値を全環境の固定値にせず、検証した環境で有効化 |
| 保存後に確定する値 | 採番、計算、既定値、更新日時は保存成功後に再取得。成功後の再読込失敗で書込を繰り返さない |

[更新処理][resultcode]・[日付をNULLにする公式FAQ][dateclear]

### 競合と障害

公開APIの範囲では、画面表示時の版を条件に更新・削除する原子的な契約を確認できなかった。Resultsの更新はサーバーで対象行を読み直し、`SetByApi` を適用して更新する。`VerUp` は履歴版を上げる指定で、期待版を渡す機能とは用途が異なる。表示時の元値との事前比較は既発生の競合を検出できるが、比較後から書込までの同時変更は残る。[Resultsの更新入口][resultutil]・[Resultsモデル][resultcode]・[Issuesモデル][issuecode]

日次API上限と一時的な過負荷を分ける。応答に `LimitPerDate`／`LimitRemaining` がある場合は診断に使う。実行前に拒否されたと確定できる入力・権限エラー、送信後のタイムアウト／結果不明、保存成功後の読取失敗を別の結果にする。書込後には通知や関連処理もあるので、5xxを受けた事実だけでは未保存と判定できない。結果不明の自動再送は行わない。[Api.json][apiconfig]・[応答生成][status]・[Resultsモデル][resultcode]

| 観測 | 診断・再試行の判断 |
|---|---|
| HTTP 401／403、本文の失敗 | キーの有効性・再発行、接続元制限、操作権限を管理画面側で確認。実行用から設計用のキーへ昇格しない |
| HTTP 429、または残回数の枯渇 | 日次枠の場合はその場で繰り返さない。一時的な制限では、読取に限りサーバーの待機指示と再試行上限に従う |
| HTTP成功でもHTML・未知のJSON、本文で失敗 | 読取はプロトコルエラー。書込送信後で確定を示す証跡がなければ結果不明とする |
| createは成功、直後のgetが失敗 | 保存済みIDを返し、必要ならgetだけやり直す。利用者の再登録を誘発しない |

## 7. 関連・一括・業務操作の位置付け

| 目的 | APIまたは設定 | コネクタでの判断 |
|---|---|---|
| 固定候補 | `ChoicesText` の値・表示名 | 単一選択を初期対象にする。値・表示名・短縮表示・エスケープを区別 |
| 他テーブル参照 | 分類列の単純な `[[siteId]]`、参照先の `get` | 子の分類列に親のIDを保持。選択候補は親サイトで検索。参照不能な親のラベルを共有キャッシュから補わない。[リンク][link] |
| Lookup、複数選択、動的選択肢 | 選択肢JSON、他項目依存、転記設定等 | 転記タイミング、書込形式、候補権限を検証して追加。親の現在値と過去に子へ転記した値を区別する |
| ユーザー・組織・グループ | `api/users/get`、`api/depts/get`、`api/groups/get` | 追加の権限・取得範囲を要確認。通常CRUDに全社名簿の読取権限を要求しない |
| 添付・コメント | `api/binaries/{guid}/get`、`AttachmentsHash`、`Comments` | 添付のIDはGUID。転送量、閲覧権限、追記の再送を考慮し、通常列の全置換から分ける。[添付取得][attachment]・[更新][update] |
| プロセス・通知 | create/updateの `ProcessId`／`ProcessIds` | API実行時の入力検証や副作用は画面操作と相違し得る。Statusの数値変更を承認ボタンに見立てない。[作成][create]・[更新][update] |
| 外部同期・一括操作 | `upsert`、`bulkupsert`、`bulkdelete` | キーnullで新規化する場合や途中まで確定する場合がある。通常Submitは単件とし、一括は別契約にする。[Upsert][upsert]・[一括作成・更新][bulk] |
| 独自SQL | プリザンターの拡張SQL実行API | サーバー側への個別SQL配備と独自の権限確認が必要。標準一覧・JOIN・集計の代替には採用しない。[拡張SQL][extended] |

## 8. 実環境で最初に採る証跡

検証用にResults「案件」、Issues「作業」、親Results「取引先」を用意する。案件には0・false・null・重複ラベル・参照できない親を含める。管理者キーと権限を絞った実行用キーを別に用意し、疎通後にマスク済み要求と応答、導入版・設定、プリザンター画面での保存結果を対応付ける。

| 順 | 確認内容 | 判断に必要な観測 |
|---|---|---|
| 1 | 2種のテーブル、空サイト、権限の違うキーで `getsite/get` | 有効列と欠落列、サイト・レコードID、日時、実行時に管理者キーが不要なこと |
| 2 | 状況を絞った一覧の1・2ページと件数。サーバー上限をCLBより小さくする | 同じ対象集合、同順位行の順序、総件数、実受信行数、空ページと上限0の応答 |
| 3 | 1件作成→一部更新→再取得→単件削除 | 応答ID、未変更列・既定値・0・false・null、日付消去、Issuesの計算値、必要なら関連削除 |
| 4 | 単純リンク、参照不可の親、動的候補・サイト統合 | 行と総件数の範囲、参照先タイトル、対象外の設定を検知できること |
| 5 | 二者の同時編集、送信直後の応答喪失、保存後再読込失敗 | 競合の検出範囲、確定した実ID、再送なしで照合できること |
| 6 | APIキー主体の時刻帯、列閲覧制限、共有キーで異なるCLB利用者 | 日付の往復、一覧・詳細・候補・件数で公開範囲が一致すること |

これらは実機試験の判定条件。CLBからの通常操作とDesignerの試験は [仕様・設計案](02_pleasanter_specification_draft.md) の受入表に記載した。

[FM]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/README.md
[NS]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.NetSuite/blob/d7d35a1703aeb2456397787074d6e1d7fe8d20c9/README.md
[PL]: https://github.com/Implem/Implem.Pleasanter/tree/664c833b144d9da9b6bb58e60f7e48eb01141e58
[key]: https://pleasanter.org/ja/manual/developers-guide/api/basics/api-key/
[common]: https://pleasanter.org/ja/manual/developers-guide/json-data-layout/api-common/
[site]: https://pleasanter.org/ja/manual/developers-guide/api/site-operations/api-site-get/
[multi]: https://pleasanter.org/ja/manual/developers-guide/api/table-operations/api-record-get-multi/
[one]: https://pleasanter.org/ja/manual/developers-guide/api/table-operations/api-record-get/
[create]: https://pleasanter.org/ja/manual/developers-guide/api/table-operations/api-record-create/
[update]: https://pleasanter.org/ja/manual/developers-guide/api/table-operations/api-record-update/
[delete]: https://pleasanter.org/ja/manual/developers-guide/api/table-operations/api-record-delete/
[cascade]: https://pleasanter.org/ja/manual/managers-guide/manage-table/editor/editor-settings/advanced-settings/general/option-list/table-management-delete-with-links/
[status]: https://github.com/Implem/Implem.Pleasanter/blob/664c833b144d9da9b6bb58e60f7e48eb01141e58/Implem.Pleasanter/Libraries/Responses/ApiResults.cs
[sitecode]: https://github.com/Implem/Implem.Pleasanter/blob/664c833b144d9da9b6bb58e60f7e48eb01141e58/Implem.Pleasanter/Models/Sites/SiteModel.cs
[settingscode]: https://github.com/Implem/Implem.Pleasanter/blob/664c833b144d9da9b6bb58e60f7e48eb01141e58/Implem.Pleasanter/Libraries/Settings/SiteSettings.cs
[columncode]: https://github.com/Implem/Implem.Pleasanter/blob/664c833b144d9da9b6bb58e60f7e48eb01141e58/Implem.Pleasanter/Libraries/Settings/Column.cs
[closest]: https://pleasanter.org/ja/manual/developers-guide/api/site-operations/api-site-get-closest-siteid/
[item]: https://pleasanter.org/ja/manual/developers-guide/json-data-layout/api-item/
[base]: https://github.com/Implem/Implem.Pleasanter/blob/664c833b144d9da9b6bb58e60f7e48eb01141e58/Implem.Pleasanter/Models/Shared/_BaseApiModel.cs
[resultcode]: https://github.com/Implem/Implem.Pleasanter/blob/664c833b144d9da9b6bb58e60f7e48eb01141e58/Implem.Pleasanter/Models/Results/ResultModel.cs
[keyvalues]: https://pleasanter.org/ja/manual/developers-guide/json-data-layout/api-view/api-view-apidatatype/
[view]: https://pleasanter.org/ja/manual/developers-guide/json-data-layout/api-view/
[searchtypes]: https://www.pleasanter.org/ja/manual/developers-guide/server-script/view/server-script-view-search-types/
[apiconfig]: https://pleasanter.org/ja/manual/setup/parameters/api-json/
[resultutil]: https://github.com/Implem/Implem.Pleasanter/blob/664c833b144d9da9b6bb58e60f7e48eb01141e58/Implem.Pleasanter/Models/Results/ResultUtilities.cs
[fmtest]: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Connectors.FileMaker/blob/a0227e376f140aa9fabc753260205e5f1dd8703a/Source/Codeer.LowCode.Blazor.Connectors.FileMaker.Test/ODataTranslatorTest.cs
[dateclear]: https://pleasanter.org/ja/manual/FAQ/features-for-developers/faq-api-set-date-to-null/
[issuecode]: https://github.com/Implem/Implem.Pleasanter/blob/664c833b144d9da9b6bb58e60f7e48eb01141e58/Implem.Pleasanter/Models/Issues/IssueModel.cs
[link]: https://www.pleasanter.org/ja/manual/users-guide/hands-on/advanced/advanced-operations-link/
[attachment]: https://pleasanter.org/ja/manual/developers-guide/api/table-operations/api-attachment-get/
[upsert]: https://pleasanter.org/ja/manual/developers-guide/api/table-operations/api-record-upsert/
[bulk]: https://pleasanter.org/ja/manual/developers-guide/api/table-operations/api-record-bulkupsert/
[extended]: https://www.pleasanter.org/ja/manual/developers-guide/extended-features/extended-sql/extended-sql-api/
