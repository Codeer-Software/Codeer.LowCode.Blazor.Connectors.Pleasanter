# Codeer.LowCode.Blazor.Connectors.Pleasanter

[Codeer.LowCode.Blazor](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Manual) のモジュールを、プリザンター (Implem.Pleasanter) のサイト (記録テーブル・期限付きテーブル) に結び付けるコネクターです。

プリザンターをデータソースの 1 つとして登録すると、DB のテーブルと同じ手順でプリザンターのサイトからモジュールを作れます。
一覧・検索・詳細・保存といったいつもの操作が、そのままプリザンターの読み書きになります。

- 一覧・検索・作成・更新・削除はプリザンターの API
- 認証は連携用の利用者の API キー
- 選択肢・リンクはデザイナで取り込み、Enum・LinkField として使える
- プリザンターのログインを使うシングルサインオンの部品つき
- プリザンター側へのプラグインの配備は不要 (シングルサインオンを使うときだけ、本人を返す拡張 SQL を 1 つ置く)

## 使い方

アプリ (サーバー) に `Codeer.LowCode.Blazor.Connectors.Pleasanter.Server`、デザイナに `Codeer.LowCode.Blazor.Connectors.Pleasanter.Designer` を追加します。

データソースにプリザンターの接続を書き (`Sites` に使うサイトを `表名:サイトID` で並べる)、

```json
{
  "Name": "Pleasanter",
  "DataSourceType": "PostgreSQL",
  "ConnectionString": "Provider=Pleasanter;Url=https://example.com/pleasanter/;ApiKey=...;Sites=Customers:1300,Projects:1200"
}
```

アプリとデザイナの `IDbAccessor` を包みます。

```csharp
//アプリ (サーバー)。サイトの定義は、配られたデザインに入っているメタ情報のファイルから読む
var pleasanter = new PleasanterDbAccessor(new DbAccessor(dataSources), PleasanterConnection.FromDataSources(dataSources),
    PleasanterMetadataFile.FromDeployedDesign(designFileDirectory));

//デザイナ (サイトの定義は、Tools > プリザンターのメタ情報を更新 でプロジェクトに保存したファイルから読む)
var pleasanter = PleasanterDesignerInitializer.CreateDbAccessor(new DbAccessor(dataSources), dataSources);
```

設定の項目・モジュールの作り方・動きと制限は [Docs/Pleasanter.md](Docs/Pleasanter.md) にあります。

コネクターを組み込んだ Web アプリとデザインのサンプルが `Source/Example` にあります。プリザンターにサンプルのサイトを作って動かす手順は [Docs/Example.md](Docs/Example.md) にあります。

## ライセンス

MIT
