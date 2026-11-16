# サンプルアプリ (Source/Example) を手元で動かす

`Source/Example` は、コネクターを組み込んだ Web アプリ (Cookie 認証のテンプレート) と、そのためのプリザンターの準備のスクリプトです。
プリザンターに作ったサンプルのサイト (取引先・案件・作業) につないで、一覧・検索・詳細・保存と、プリザンターのログインを使うシングルサインオン (SSO) を試せます。

| フォルダ | 内容 |
|---|---|
| `LowCodeApp.Server` / `LowCodeApp.Client` / `LowCodeApp.Client.Shared` | Web アプリ。コネクターの組み込みは `LowCodeApp.Server/Infrastructure/DataService.cs` の 2 行。SSO は `LowCodeApp.Server/Sso`・`Controllers/AccountController.cs`・`LowCodeApp.Client/wwwroot/index.html` と `sso-login.html` |
| `Design` | サンプルのデザイン (取引先・案件・作業・取引先別の案件金額・ユーザー) |
| `Data/main.sql` | ログインするユーザーの表と、最初のユーザー (admin / admin) |
| `Data/setup-pleasanter.ps1` | プリザンターにサンプルのサイトとデータを作るスクリプト |
| `Pleasanter/ExtendedSqls/ClbWhoAmI.json` | SSO のためにプリザンターに置く拡張 SQL |
| `Tests/sso` | SSO の結合テスト (Playwright。実物のプリザンターにブラウザでログインする) |

.NET 10 の SDK と PowerShell 7 が要ります。CLB のライセンスを登録したマシンで動かします。
サイトの作成のスクリプトは、プリザンター 1.5.9.1 で確かめています。以下のパス・ポートは例です。変えるときは、それぞれの設定を同じ値にそろえます。

## 1. プリザンターを準備する

### サンプルのサイトを作る

テナントの管理者など、最上位にサイトを作れる利用者の API キーを用意します (プリザンターの「API の設定」で作る)。
キーは 1 行だけのファイルに置き、リポジトリの外に置きます。

```
pwsh Source/Example/Data/setup-pleasanter.ps1 -Url http://localhost:59803/ -ApiKeyFile C:\secrets\pleasanter-api-key.txt
```

最上位に 3 つのサイトができ、架空のデータが入ります。

| サイト | 種類 | 項目 |
|---|---|---|
| `CLB見本_取引先` | 記録テーブル | 会社名・業種 (選択肢)・電話番号・備考 (5 件) |
| `CLB見本_案件` | 記録テーブル | 案件名・取引先 (取引先へのリンク)・区分 (選択肢)・状況・金額・受注日・確認済・メモ (12 件) |
| `CLB見本_作業` | 期限付きテーブル | 作業名・案件 (案件へのリンク)・担当者 (選択肢)・状況・開始・完了・作業量・進捗率 (20 件) |

終わりに、接続文字列に書く値が出ます。

```
Sites=Customers:215,Projects:216,Tasks:217
```

- サイトには「サイト名」`CLB見本_Customers` / `CLB見本_Projects` / `CLB見本_Tasks` が付きます。同じサイト名のサイトが既にあると、何も作らずに既存のサイトの ID を出します
- 作り直すときは `-Recreate` を付けます。既存のサイトを (中のレコードごと) 消してから作ります。サイト ID は変わるので、接続文字列の `Sites` も直します
- `-ApiKeyFile` の代わりに `-ApiKey` でキーを渡すこともできます (コマンドの履歴に残ります)

### 拡張 SQL を置く (SSO を使うとき)

`Source/Example/Pleasanter/ExtendedSqls/ClbWhoAmI.json` を、プリザンターの `App_Data/Parameters/ExtendedSqls/` にコピーして、プリザンターを再起動します。
ログイン中の本人 (テナント・利用者の ID・ログイン ID・名前) を返すだけの SQL です。呼んだ人自身の情報だけが返ります (このアプリは、ブラウザでログインした利用者の Cookie で呼びます)。

## 2. デザイナの設定を作る

`Source/Example/Design/designer.settings.Development.json` を作ります (リポジトリには入れません)。`Sites` は手順 1 で出た値にします。

```json
{
  "ConnectionStrings": {
    "Main": "Data Source=C:/Codeer.LowCode.Blazor.Local/Data/pleasanter_example.db;",
    "Pleasanter": "Provider=Pleasanter;Url=http://localhost:59803/;ApiKey=<API キー>;Sites=Customers:215,Projects:216,Tasks:217"
  },
  "CurrentDeployInfoName": "local",
  "DeployInfo": {
    "local": {
      "DeployMethod": "FileSystem",
      "AppName": "App",
      "Directory": "C:/Codeer.LowCode.Blazor.Local/Designs/Pleasanter"
    }
  }
}
```

デザイナはプリザンターの表の定義を `Design/pleasanter.Pleasanter.metadata.json` から読みます (プリザンターには取りに行きません)。
このファイルはサンプルのサイトから書き出したものです。手順 1 でサイトを作り直した・サイト ID が違うとき・プリザンターで項目や選択肢を変えたときは、
デザイナで Tools > プリザンターのメタ情報を更新 を実行してファイルを書き直します。このときは接続文字列の `ApiKey` に本物のキーが要ります (定義をファイルから読むだけなら要りません)。

接続文字列に API キーを書くので、このファイルはリポジトリの外に出さないでください (`.gitignore` に入れてあります)。

## 3. ログインするユーザーの DB を作る

`C:\Codeer.LowCode.Blazor.Local\Data` のフォルダを作ってから、デザイナをビルドして (WPF なので VS の MSBuild)、`sql` コマンドで `Data/main.sql` を流します。

```
LowCodeApp.Designer.exe sql Source/Example/Design --datasource Main --file Source/Example/Data/main.sql --out sql-result.json
```

## 4. デザインを送る

デザイナで `Source/Example/Design/app.clprj` を開き、デプロイします。`C:\Codeer.LowCode.Blazor.Local\Designs\Pleasanter\App.zip` ができます。
アプリの `appsettings.Development.json` の `DesignFileDirectory` がこのフォルダを指しています。

## 5. アプリを起動する

プリザンターの接続文字列は `appsettings` に書かず、環境変数で渡します (PowerShell の例。`Sites` は手順 1 で出た値)。

```
$key = (Get-Content -Raw C:\secrets\pleasanter-api-key.txt).Trim()
$env:ConnectionStrings__Pleasanter = "Provider=Pleasanter;Url=http://localhost:59803/;ApiKey=$key;Sites=Customers:215,Projects:216,Tasks:217"
dotnet run --project Source/Example/LowCodeApp.Server --launch-profile http
```

`http://localhost:5085` を開き、admin / admin でログインします。

- データの読み書きは、接続文字列の API キーの利用者で行います。利用者ごとの読み書きの制限は CLB のモジュールの権限で決めます
- DB とデザインの置き場所は `LowCodeApp.Server/appsettings.Development.json` (`ConnectionStrings:Main`・`DesignFileDirectory`) です。環境変数 `ConnectionStrings__Main`・`DesignFileDirectory` でも上書きできます

## SSO を有効にする

プリザンターにログインしているブラウザなら、CLB のログイン画面を出さずに同じ人として入れるようにします。

1. 手順 1 の拡張 SQL を置きます
2. ログインさせる人を、CLB のユーザー (`app_users`) に登録します。admin でログインして「ユーザー」の画面で行を作り、メールの欄 (外部ログイン名の列 `email`) にプリザンターのログイン ID を入れます。
   登録の無い人がプリザンターでログインすると、待ち画面に「このアプリの利用者として登録されていません」と出ます。
   登録の無い人を入れるときは `AutoProvisionUser` を `true` にします (プリザンターのログイン ID をログイン名にした行を作ります。外部ログイン名の列がログイン名の列と別の設計では作った行を引けないので、事前に登録します)
3. `PleasanterSso` を設定してアプリを起動します (環境変数の例。手順 5 のプリザンターの接続文字列も渡します)

```
$env:PleasanterSso__Enabled = "true"
$env:PleasanterSso__LoginUrl = "http://localhost:59803/users/login"
$env:PleasanterSso__LogoutUrl = "http://localhost:59803/users/logout"
dotnet run --project Source/Example/LowCodeApp.Server --launch-profile http
```

| 設定 | 内容 |
|---|---|
| `Enabled` | SSO を使うか (既定 `false` = ID / パスワードのログイン画面) |
| `DataSourceName` | 本人を問い合わせるプリザンターのデータソースの名前。問い合わせ先はその接続文字列の `Url`。空なら最初のプリザンターのデータソース |
| `LoginUrl` | ブラウザで開くプリザンターのログイン画面の URL |
| `LogoutUrl` | ブラウザで開くプリザンターのログアウトの URL。空なら待ち画面に「プリザンターからもログアウトする」を出さない |
| `AutoProvisionUser` | 登録の無い人の行を作るか (既定 `false`) |
| `RevalidateSeconds` | CLB のセッションをプリザンターに確かめ直す間隔 (秒。既定 60) |
| `TimeoutSeconds` | 本人の問い合わせのタイムアウト (秒。既定 5)。データの読み書きのタイムアウトは接続文字列の `TimeoutSeconds` |

動き:

- ログインしていないブラウザで CLB を開くと、CLB のサーバーがブラウザのプリザンターの Cookie をプリザンター (拡張 SQL `ClbWhoAmI`) に送って本人を確かめます。ログイン済みならそのまま入れます
- プリザンターでログインしていなければ、CLB の待ち画面 (`sso-login.html`) が出ます。「プリザンターでログイン」でプリザンターのログイン画面が別のタブで開き、ログインが済むと CLB に戻ります
- 入ったあとも `RevalidateSeconds` ごとにプリザンターに確かめ直し、プリザンターでログアウトしていれば CLB からもサインアウトします (API は 401。プリザンターに届かないときは 503)
- CLB のログアウトは CLB からだけ抜けて待ち画面に戻ります。プリザンターからも抜けるときは待ち画面の「プリザンターからもログアウトする」
- ID / パスワードのログインも使えます (`/login.html` を開く。`AllowPasswordLogin` が `true` のとき)

条件:

- ブラウザがプリザンターの Cookie を CLB にも送ること。同じホスト名で動かします (ポートは違ってよい)。Cookie のパス・ドメインの設定によっては届きません
- CLB の認証 Cookie の名前は `.CLB.Auth` にしてあります (テンプレートの既定の `.AspNetCore.Cookies` はプリザンターの Cookie と同じ名前で、同じホストでは上書きし合うため)

結合テストは [Tests/sso/README.md](../Source/Example/Tests/sso/README.md) にあります。

## モジュール

| モジュール | 表 | 内容 |
|---|---|---|
| `Customer` (取引先) | `Customers` | 一覧・検索 (会社名・業種)・詳細。業種は Enum `業種` の SelectField。詳細にその取引先の案件の一覧 |
| `Project` (案件) | `Projects` | 一覧 (受注日の降順・50 件ずつ)・検索 (案件名・取引先名・区分・状況・金額の範囲・受注日の範囲)・詳細。取引先は LinkField (選択のダイアログ)。状況はプリザンターでは数値の項目で、Enum `案件状況` (値が数字) の SelectField で名前を見せる。作成日時は表示だけ。詳細でその案件の作業を一覧で足す・直す・消す |
| `WorkTask` (作業) | `Tasks` | 一覧 (10 件ずつ)・検索 (作業名・案件・担当者・開始の範囲)・詳細。期限付きテーブルの開始・完了・作業量・進捗率。残作業量はプリザンターが計算するので表示だけ |
| `AmountByCustomer` (取引先別の案件金額) | — | 案件の金額を取引先 × 区分で合計するクロス集計 (CrossTabField とスクリプト)。プリザンターに集計の API は無いので、コネクターが案件を全部読んで集計します |
| `AppUser` (ユーザー) | `app_users` (Main) | ログインするユーザー |
| `Home` | — | ログイン後の最初の画面 |

- 取引先・案件・作業は UpdatedTime の楽観ロック付きです (画面を開いたあとにプリザンターで更新されると、保存が止まります)
- 状況・担当者などの選択肢は、メタ情報のファイルの選択肢を Enum に写したものです。プリザンターで選択肢を変えたときは、メタ情報を更新して Enum を直します
