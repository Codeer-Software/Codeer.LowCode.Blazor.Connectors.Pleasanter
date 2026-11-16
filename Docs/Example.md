# サンプルアプリ (Source/Example) を手元で動かす

`Source/Example` は、コネクターを組み込んだ Web アプリ (Cookie 認証のテンプレート) と、そのためのプリザンターの準備のスクリプトです。
プリザンターに作ったサンプルのサイト (取引先・案件・作業) につないで、一覧・検索・詳細・保存と、プリザンターのログインを使うシングルサインオン (SSO) を試せます。

| フォルダ | 内容 |
|---|---|
| `LowCodeApp.Server` / `LowCodeApp.Client` / `LowCodeApp.Client.Shared` | Web アプリ。コネクターの組み込みは `LowCodeApp.Server/Infrastructure/DataService.cs` の 2 行。SSO は `LowCodeApp.Server/Sso`・`Controllers/AccountController.cs`・`LowCodeApp.Client/wwwroot/index.html` と `sso-login.html` |
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
ログイン中の本人 (テナント・利用者の ID・ログイン ID・名前) を返すだけの SQL です。API キーでは呼べず、ブラウザでログインした利用者の Cookie で呼んだときだけ本人が返ります。

## 2. デザイナの設定を作る

(第 2 段階で書く)

## 3. ログインするユーザーの DB を作る

`C:\Codeer.LowCode.Blazor.Local\Data` のフォルダを作ってから、デザイナをビルドして (WPF なので VS の MSBuild)、`sql` コマンドで `Data/main.sql` を流します。

```
LowCodeApp.Designer.exe sql Source/Example/Design --datasource Main --file Source/Example/Data/main.sql --out sql-result.json
```

## 4. デザインを送る

(第 2 段階で書く)

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
2. ログインさせる人を、CLB のユーザー (`app_users`) に登録します。外部ログイン名の列 (`email`) にプリザンターのログイン ID を入れます。
   登録の無い人を入れるときは `AutoProvisionUser` を `true` にします (プリザンターのログイン ID をログイン名にした行を作ります。外部ログイン名の列がログイン名の列と別の設計では作った行を引けないので、事前に登録します)
3. `PleasanterSso` を設定してアプリを起動します (環境変数の例)

```
$env:PleasanterSso__Enabled = "true"
$env:PleasanterSso__InternalBaseUrl = "http://localhost:59803/"
$env:PleasanterSso__LoginUrl = "http://localhost:59803/users/login"
$env:PleasanterSso__LogoutUrl = "http://localhost:59803/users/logout"
dotnet run --project Source/Example/LowCodeApp.Server --launch-profile http
```

| 設定 | 内容 |
|---|---|
| `Enabled` | SSO を使うか (既定 `false` = ID / パスワードのログイン画面) |
| `InternalBaseUrl` | CLB のサーバーからプリザンターへ問い合わせる URL (アプリのルート) |
| `LoginUrl` | ブラウザで開くプリザンターのログイン画面の URL |
| `LogoutUrl` | ブラウザで開くプリザンターのログアウトの URL。空なら待ち画面に「プリザンターからもログアウトする」を出さない |
| `AutoProvisionUser` | 登録の無い人の行を作るか (既定 `false`) |
| `RevalidateSeconds` | CLB のセッションをプリザンターに確かめ直す間隔 (秒。既定 60) |
| `TimeoutSeconds` | プリザンターへの問い合わせのタイムアウト (秒。既定 5) |

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

(第 2 段階で書く)
