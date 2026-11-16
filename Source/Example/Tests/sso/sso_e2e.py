"""
プリザンター認証を使った CLB SSO の結合テスト (Playwright / 実ブラウザ)。

秘密情報はソースに書かない。すべて環境変数で渡す。

必須:
  SSO_PASSWORD        テスト用ユーザーのパスワード
  SSO_TOTP_SECRET     テスト用ユーザーの TOTP 秘密鍵 (Base32)。
                      未指定なら SSO_DB_* からプリザンターの Users.SecretKey を読む。
任意:
  SSO_PLE_URL         プリザンターの URL           (既定 http://localhost:59803)
  SSO_CLB_URL         CLB の URL                   (既定 https://localhost:7137)
  SSO_LOGIN           ログイン ID                  (既定 Administrator)
  SSO_DB_HOST/PORT/NAME/USER/PASSWORD   SSO_TOTP_SECRET 未指定時のみ (psql で秘密鍵を取得)

前提: プリザンターと CLB が起動済み。CLB は PleasanterSso.Enabled=true。
      テスト用ユーザーは TOTP 登録済み。DB は破棄可能な検証用。

実行:
  pip install playwright && playwright install chromium
  SSO_PASSWORD=... SSO_TOTP_SECRET=... python sso_e2e.py
終了コード 0 = 全ケース成功。
"""
import os, re, sys, time, base64, hmac, hashlib, struct, json, subprocess
from playwright.sync_api import sync_playwright

PLE = os.environ.get("SSO_PLE_URL", "http://localhost:59803").rstrip("/")
CLB = os.environ.get("SSO_CLB_URL", "https://localhost:7137").rstrip("/")
LOGIN = os.environ.get("SSO_LOGIN", "Administrator")
PW = os.environ.get("SSO_PASSWORD")
if not PW:
    sys.exit("SSO_PASSWORD is required")


def totp_secret():
    s = os.environ.get("SSO_TOTP_SECRET")
    if s:
        return s
    # fall back to reading Users.SecretKey via psql
    host = os.environ.get("SSO_DB_HOST"); db = os.environ.get("SSO_DB_NAME")
    user = os.environ.get("SSO_DB_USER"); pw = os.environ.get("SSO_DB_PASSWORD")
    port = os.environ.get("SSO_DB_PORT", "5432")
    if not all([host, db, user, pw]):
        sys.exit("Provide SSO_TOTP_SECRET, or SSO_DB_HOST/NAME/USER/PASSWORD")
    env = dict(os.environ, PGPASSWORD=pw, PGSSLMODE=os.environ.get("SSO_DB_SSLMODE", "require"))
    q = 'select "SecretKey" from "Users" where lower("LoginId")=lower(%s) limit 1;'
    out = subprocess.check_output(
        ["psql", "-h", host, "-p", port, "-U", user, "-d", db, "-Atc",
         q.replace("%s", "'" + LOGIN.replace("'", "''") + "'")], env=env, text=True)
    return out.strip()


def totp(secret, t=None):
    s = re.sub(r"[^A-Za-z0-9]", "", secret).upper()
    s += "=" * ((8 - len(s) % 8) % 8)
    key = base64.b32decode(s)
    if t is None:
        t = int(time.time())
    h = hmac.new(key, struct.pack(">Q", t // 30), hashlib.sha1).digest()
    o = h[-1] & 0x0F
    return str((struct.unpack(">I", h[o:o + 4])[0] & 0x7FFFFFFF) % 1000000).zfill(6)


SECRET = totp_secret()
EXTSQL = ("""async (o)=>{const r=await fetch(o+'/api/extended/sql',{method:'POST',credentials:'include',"""
          """headers:{'Content-Type':'application/json'},body:JSON.stringify({ApiVersion:1.1,Name:'ClbWhoAmI'})});"""
          """return r.status;}""")


def pleasanter_login(pg):
    pg.goto(f"{PLE}/users/login", wait_until="domcontentloaded")
    pg.fill("#Users_LoginId", LOGIN); pg.fill("#Users_Password", PW)
    pg.locator("#Login").first.click(); pg.wait_for_timeout(2500)
    code = totp(SECRET)
    if pg.locator("#FirstTotpAuthenticationCode").count() > 0:
        pg.click("#FirstTotpAuthenticationCode"); pg.keyboard.type(code, delay=60)
    elif pg.locator("#SecondaryAuthenticationCode").count() > 0:
        pg.fill("#SecondaryAuthenticationCode", code)
    pg.wait_for_timeout(400)
    if pg.locator("#SecondaryAuthentication").count() > 0 and pg.locator("#SecondaryAuthentication").first.is_visible():
        pg.locator("#SecondaryAuthentication").first.click()
    else:
        pg.keyboard.press("Enter")
    pg.wait_for_timeout(3000)


def clb_current_user(ctx):
    r = ctx.request.get(f"{CLB}/api/account/current_user")
    return r.status, r.text()[:60]


results = {}


def check(name, ok, detail=""):
    results[name] = {"pass": bool(ok), "detail": detail}
    print(("PASS" if ok else "FAIL"), name, "-", detail, flush=True)


with sync_playwright() as p:
    b = p.chromium.launch(headless=os.environ.get("SSO_HEADED") != "1")

    # 1. 未認証: 拡張SQL は 401
    c = b.new_context(ignore_https_errors=True); pg = c.new_page()
    pg.goto(f"{PLE}/users/login", wait_until="domcontentloaded")
    st = pg.evaluate(EXTSQL, PLE)
    check("unauth_extended_sql_401", st == 401, f"status={st}"); c.close()

    # 2. Cookie 無し: CLB current_user は 401
    c = b.new_context(ignore_https_errors=True)
    st, _ = clb_current_user(c)
    check("clb_no_cookie_401", st == 401, f"status={st}"); c.close()

    # 3. パスワードのみ・TOTP 未完了: 拡張SQL 401 かつ CLB 認証 Cookie 無し
    c = b.new_context(ignore_https_errors=True); pg = c.new_page()
    pg.goto(f"{PLE}/users/login", wait_until="domcontentloaded")
    pg.fill("#Users_LoginId", LOGIN); pg.fill("#Users_Password", PW)
    pg.locator("#Login").first.click(); pg.wait_for_timeout(2500)
    st = pg.evaluate(EXTSQL, PLE)
    pg.goto(f"{CLB}/", wait_until="domcontentloaded"); pg.wait_for_timeout(2500)
    st2, _ = clb_current_user(c)
    has_clb = any(x["name"] == ".CLB.Auth" for x in c.cookies())
    check("pre_totp_rejected", st == 401 and st2 == 401 and not has_clb,
          f"extsql={st} clb_current_user={st2} clb_cookie={has_clb}"); c.close()

    # 4. パスワード+TOTP 完了 -> CLB に自動ログイン (current_user が整数 Id)
    c = b.new_context(ignore_https_errors=True); pg = c.new_page()
    pleasanter_login(pg)
    pg.goto(f"{CLB}/", wait_until="domcontentloaded"); pg.wait_for_timeout(4000)
    st, body = clb_current_user(c)
    m = re.search(r'"value"\s*:\s*"(\d+)"', body or "")
    check("full_login_enters_clb", st == 200 and m is not None,
          f"status={st} body={body}")

    # 5. プリザンターでログアウト -> 一定時間内に CLB も失効
    #    (再検証間隔を短くしておくと速い。既定 60 秒なので、ここでは待ち画面へ戻ることのみ確認)
    lo = pg.evaluate("""async()=>{
        await fetch('/api/account/antiforgery',{credentials:'include',cache:'no-store'});
        const t=document.cookie.split('; ').find(x=>x.startsWith('X-ANTIFORGERY-TOKEN='))?.split('=')[1]||'';
        const r=await fetch('/api/account/logout',{method:'POST',credentials:'include',
          headers:{'Content-Type':'application/json','X-ANTIFORGERY-TOKEN':t},body:JSON.stringify('')});
        let j=null; try{j=await r.json()}catch(e){}
        return {status:r.status, redirect:(j&&(j.redirect||j.Redirect))||null};
    }""")
    clb_gone = not any(x["name"] == ".CLB.Auth" for x in c.cookies())
    check("logout_clears_clb", lo.get("status") == 200 and clb_gone,
          f"logout={lo} clb_cookie_gone={clb_gone}"); c.close()

    b.close()

print("\n=== RESULT ===")
print(json.dumps(results, ensure_ascii=False, indent=2))
failed = [k for k, v in results.items() if not v["pass"]]
if failed:
    print("FAILED:", failed)
    sys.exit(1)
print("ALL PASS")
