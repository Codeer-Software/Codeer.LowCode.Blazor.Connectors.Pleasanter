# プリザンター認証 CLB SSO — 結合テスト (Playwright)

実ブラウザ (Chromium) でプリザンターにログイン (ID/パスワード + TOTP) し、CLB が
再認証なしで同じ本人として入れること、未認証・TOTP 未完了・ログアウトが正しく拒否/失効
されることを確認する。

## 前提

- プリザンターと CLB (`Source/Example/LowCodeApp.Server`) が起動していること。CLB は
  `PleasanterSso.Enabled = true` で起動する (`Docs/Example.md` の「SSO を有効にする」)。
- プリザンターに拡張 SQL `ClbWhoAmI` (`Source/Example/Pleasanter/ExtendedSqls/ClbWhoAmI.json`) を置いてあること。
- 検証用ユーザーのログイン ID が CLB のユーザーモジュールに登録済みであること (外部ログイン名の列)。
  または CLB を `PleasanterSso.AutoProvisionUser = true` で起動する。
- 検証用ユーザーが TOTP 登録済み。**破棄可能な検証用 DB** で行う。
- Python + Playwright:

  ```
  pip install playwright
  playwright install chromium
  ```

## 実行

秘密情報はソースに書かない。すべて環境変数で渡す。

```
# 例 (PowerShell)
$env:SSO_PASSWORD    = "<テストユーザーのパスワード>"
$env:SSO_TOTP_SECRET = "<テストユーザーの TOTP 秘密鍵 (Base32)>"
python sso_e2e.py
```

`SSO_TOTP_SECRET` を渡さない場合は、DB からプリザンターの `Users.SecretKey` を読む:

```
$env:SSO_PASSWORD    = "<...>"
$env:SSO_DB_HOST     = "<db host>"
$env:SSO_DB_NAME     = "<db name>"
$env:SSO_DB_USER     = "<db user>"
$env:SSO_DB_PASSWORD = "<db password>"
python sso_e2e.py
```

| 環境変数 | 既定 | 用途 |
|---|---|---|
| `SSO_PASSWORD` | (必須) | テストユーザーのパスワード |
| `SSO_TOTP_SECRET` | — | TOTP 秘密鍵 (Base32)。無指定なら DB から取得 |
| `SSO_LOGIN` | `Administrator` | ログイン ID |
| `SSO_PLE_URL` | `http://localhost:59803` | プリザンター URL |
| `SSO_CLB_URL` | `https://localhost:7137` | CLB URL |
| `SSO_DB_HOST/PORT/NAME/USER/PASSWORD` | — | 秘密鍵を DB から読むとき (psql が要る) |
| `SSO_HEADED` | — | `1` でブラウザを表示 |

終了コード `0` が全ケース成功。

## 検証ケース

| ケース | 期待 |
|---|---|
| `unauth_extended_sql_401` | 未認証で `/api/extended/sql` → 401 |
| `clb_no_cookie_401` | Cookie 無しで CLB `current_user` → 401 |
| `pre_totp_rejected` | パスワードのみ (TOTP 未完了) → 拒否・CLB Cookie 無し |
| `full_login_enters_clb` | パスワード+TOTP 完了 → CLB に整数 Id で入場 |
| `logout_clears_clb` | ログアウトで CLB Cookie が消える (待ち画面へ) |

失効の時間内反映まで厳密に見るときは、CLB の `PleasanterSso.RevalidateSeconds` を
一時的に小さく (例 5 秒) して、ログアウト後に待ってから保護 API を叩く。
