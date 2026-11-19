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
  SSO_CLB_ADMIN_LOGIN / SSO_CLB_ADMIN_PASSWORD   CLB の管理者 (既定 admin / admin。ID / パスワードのログインに使う)
  SSO_CLB_WRONG_TENANT_URL   PleasanterSso.TenantId を違う値にして起動した CLB の URL (指定したときだけテナント照合を確かめる)
  SSO_DB_HOST/PORT/NAME/USER/PASSWORD   SSO_TOTP_SECRET 未指定時のみ (psql で秘密鍵を取得)

前提: プリザンターと CLB が起動済み。CLB は PleasanterSso.Enabled=true・TenantId はテスト用ユーザーのテナント。
      テスト用ユーザーは TOTP 登録済み・CLB のユーザーに登録済みで管理者ではない。DB は破棄可能な検証用。

実行:
  pip install playwright && playwright install chromium
  SSO_PASSWORD=... SSO_TOTP_SECRET=... python sso_e2e.py
終了コード 0 = 全ケース成功。
"""
import os, re, sys, time, base64, hmac, hashlib, struct, json, subprocess
from urllib.parse import quote
from playwright.sync_api import sync_playwright

PLE = os.environ.get("SSO_PLE_URL", "http://localhost:59803").rstrip("/")
CLB = os.environ.get("SSO_CLB_URL", "https://localhost:7137").rstrip("/")
LOGIN = os.environ.get("SSO_LOGIN", "Administrator")
PW = os.environ.get("SSO_PASSWORD")
ADMIN_LOGIN = os.environ.get("SSO_CLB_ADMIN_LOGIN", "admin")
ADMIN_PW = os.environ.get("SSO_CLB_ADMIN_PASSWORD", "admin")
WRONG_TENANT = (os.environ.get("SSO_CLB_WRONG_TENANT_URL") or "").rstrip("/")
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

# CLB の画面の中から antiforgery トークン付きで POST する (応答は base64。一覧の応答は MessagePack)
POST_JSON = """async ({path, body})=>{
    await fetch('/api/account/antiforgery',{credentials:'include',cache:'no-store'});
    const t=document.cookie.split('; ').find(x=>x.startsWith('X-ANTIFORGERY-TOKEN='))?.split('=')[1]||'';
    const r=await fetch(path,{method:'POST',credentials:'include',
      headers:{'Content-Type':'application/json','X-ANTIFORGERY-TOKEN':t},body:JSON.stringify(body)});
    const b=new Uint8Array(await r.arrayBuffer()); let s=''; for (const x of b) s+=String.fromCharCode(x);
    return {status:r.status, body:btoa(s)};
}"""

EVIL_RETURN_URLS = ["/\\evil.example", "/\t/evil.example", "//evil.example", "https://evil.example/"]


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


def clb_current_user(ctx, base=CLB):
    r = ctx.request.get(f"{base}/api/account/current_user")
    return r.status, r.text()[:60]


def clb_user_id(body):
    m = re.search(r'"value"\s*:\s*"(\d+)"', body or "")
    return m.group(1) if m else None


def clb_anonymous(status, body):
    # 未ログインの current_user は 200 で空の Id を返す (ログイン済みなら整数の Id。プリザンターでログアウト済みと分かれば 401)
    return status == 401 or (status == 200 and re.search(r'"value"\s*:\s*""', body or "") is not None)


def sso_enter_location(ctx, return_url):
    r = ctx.request.get(f"{CLB}/api/account/sso/enter?returnUrl={quote(return_url, safe='')}", max_redirects=0)
    return r.status, r.headers.get("location", "")


def post(pg, path, body):
    r = pg.evaluate(POST_JSON, {"path": path, "body": body})
    return r["status"], base64.b64decode(r["body"])


def list_request(module):
    return [{"Condition": {"ModuleName": module}, "PageIndex": 0}]


def field(type_name, value):
    return {"Value": value, "TypeFullName": f"Codeer.LowCode.Blazor.Repository.Data.{type_name}"}


def submit_request(module, fields, add=False):
    data = {"Name": module, "Fields": fields}
    return [{"ModuleName": module, "Add": [data] if add else [], "Update": [] if add else [data]}]


def submit_denied(status, body):
    # 拒否は HTTP のエラーか、結果の ExceptionMessage
    if status != 200:
        return True
    try:
        return any((x.get("exceptionMessage") or x.get("ExceptionMessage")) for x in json.loads(body))
    except Exception:
        return False


def msgpack_has_str(body, s):
    # MessagePack の短い文字列 (fixstr) としてその値が入っているか
    b = s.encode("utf-8")
    return len(b) < 32 and bytes([0xA0 | len(b)]) + b in body


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

    # 2. Cookie 無し: CLB current_user は未ログイン
    c = b.new_context(ignore_https_errors=True)
    st, body = clb_current_user(c)
    check("clb_no_cookie_anonymous", clb_anonymous(st, body), f"status={st} body={body}")

    # 2b. 待ち画面への入口の returnUrl: CLB の外を指す値は "/" に落ちる (プリザンターでログインしていないので待ち画面へ)
    bad = [(u, sso_enter_location(c, u)) for u in EVIL_RETURN_URLS]
    ok = all(st == 302 and loc == "/sso-login.html?returnUrl=%2F" for _, (st, loc) in bad)
    st, loc = sso_enter_location(c, "/Customer?x=1")
    ok = ok and st == 302 and loc == "/sso-login.html?returnUrl=%2FCustomer%3Fx%3D1"
    check("sso_enter_return_url_local_only", ok, f"evil={[x[1] for x in bad]} local={st} {loc}"); c.close()

    # 3. パスワードのみ・TOTP 未完了: 拡張SQL 401 かつ CLB 認証 Cookie 無し
    c = b.new_context(ignore_https_errors=True); pg = c.new_page()
    pg.goto(f"{PLE}/users/login", wait_until="domcontentloaded")
    pg.fill("#Users_LoginId", LOGIN); pg.fill("#Users_Password", PW)
    pg.locator("#Login").first.click(); pg.wait_for_timeout(2500)
    st = pg.evaluate(EXTSQL, PLE)
    pg.goto(f"{CLB}/", wait_until="domcontentloaded"); pg.wait_for_timeout(2500)
    st2, body2 = clb_current_user(c)
    has_clb = any(x["name"] == ".CLB.Auth" for x in c.cookies())
    check("pre_totp_rejected", st == 401 and clb_anonymous(st2, body2) and not has_clb,
          f"extsql={st} clb_current_user={st2} {body2} clb_cookie={has_clb}"); c.close()

    # 3b. テナント照合: TenantId が違う CLB には、プリザンターでログイン済みでも入れない (待ち画面は「登録されていません」)
    if WRONG_TENANT:
        c = b.new_context(ignore_https_errors=True); pg = c.new_page()
        pleasanter_login(pg)
        r = c.request.get(f"{WRONG_TENANT}/api/account/sso/check")
        j = r.json() if r.status == 200 else {}
        has_clb = any(x["name"] == ".CLB.Auth" for x in c.cookies())
        pg.goto(f"{WRONG_TENANT}/sso-login.html", wait_until="domcontentloaded"); pg.wait_for_timeout(2500)
        shown = "登録されていません" in pg.inner_text("#status")
        check("wrong_tenant_rejected",
              r.status == 200 and not (j.get("authenticated") or j.get("Authenticated"))
              and (j.get("notRegistered") or j.get("NotRegistered")) and not has_clb and shown,
              f"status={r.status} body={j} clb_cookie={has_clb} message_shown={shown}"); c.close()
    else:
        print("SKIP wrong_tenant_rejected - SSO_CLB_WRONG_TENANT_URL is not set", flush=True)

    # 4. パスワード+TOTP 完了 -> CLB に自動ログイン (current_user が整数 Id)
    c = b.new_context(ignore_https_errors=True); pg = c.new_page()
    pleasanter_login(pg)
    pg.goto(f"{CLB}/", wait_until="domcontentloaded"); pg.wait_for_timeout(4000)
    st, body = clb_current_user(c)
    sso_user_id = clb_user_id(body)
    check("full_login_enters_clb", st == 200 and sso_user_id is not None,
          f"status={st} body={body}")

    # 4b. ログイン済みで returnUrl が CLB の外を指すとき: 入口は "/" へ、待ち画面も CLB の中に戻る
    enter = [sso_enter_location(c, u) for u in EVIL_RETURN_URLS]
    pages = []
    for u in EVIL_RETURN_URLS:
        p2 = c.new_page()
        p2.goto(f"{CLB}/sso-login.html?returnUrl={quote(u, safe='')}", wait_until="domcontentloaded")
        p2.wait_for_timeout(2500)
        pages.append(p2.url); p2.close()
    check("signed_in_return_url_local_only",
          all(st == 302 and loc == "/" for st, loc in enter) and all(x.startswith(CLB + "/") for x in pages),
          f"enter={enter} pages={pages}")

    # 4c. ユーザーの表: 一般の利用者は自分の行だけ読める・ユーザーの管理 (UserAdmin) は読めない・どちらも書けない
    st_own, own = post(pg, "/api/module_data/list", list_request("AppUser"))
    st_adm, _ = post(pg, "/api/module_data/list", list_request("UserAdmin"))
    w1 = post(pg, "/api/module_data", submit_request("AppUser", {
        "Id": field("IdFieldData", sso_user_id), "管理者": field("BooleanFieldData", True)}))
    w2 = post(pg, "/api/module_data", submit_request("UserAdmin", {
        "Id": field("IdFieldData", sso_user_id), "管理者": field("BooleanFieldData", True)}))
    w3 = post(pg, "/api/module_data", submit_request("UserAdmin", {
        "ユーザー識別名": field("TextFieldData", "sso_e2e_must_not_exist")}, add=True))
    check("sso_user_reads_own_row_only",
          st_own == 200 and not msgpack_has_str(own, ADMIN_LOGIN) and st_adm != 200
          and all(submit_denied(*w) for w in (w1, w2, w3)),
          f"AppUser list={st_own} admin_row_leaked={msgpack_has_str(own, ADMIN_LOGIN)} UserAdmin list={st_adm} "
          f"writes={[w[0] for w in (w1, w2, w3)]} denied={[submit_denied(*w) for w in (w1, w2, w3)]}")

    # 5. CLB のログアウトで CLB の Cookie が消える (待ち画面へ)
    #    プリザンターでのログアウトの反映は RevalidateSeconds の後 (README)
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

    # 6. SSO を有効にしたままの ID / パスワードのログイン (/login.html)。管理者はユーザーの管理を読み書きできる
    c = b.new_context(ignore_https_errors=True); pg = c.new_page()
    pg.goto(f"{CLB}/login.html", wait_until="domcontentloaded")
    pg.fill("#Id", ADMIN_LOGIN); pg.fill("#Password", ADMIN_PW)
    pg.click("#LoginButton"); pg.wait_for_timeout(4000)
    st, body = clb_current_user(c)
    admin_id = clb_user_id(body)
    check("password_login_with_sso_enabled", st == 200 and admin_id is not None, f"status={st} body={body} url={pg.url}")
    if admin_id:
        st_list, rows = post(pg, "/api/module_data/list", list_request("UserAdmin"))
        w = post(pg, "/api/module_data", submit_request("UserAdmin", {
            "Id": field("IdFieldData", admin_id), "ユーザー識別名": field("TextFieldData", ADMIN_LOGIN)}))
        check("admin_manages_users", st_list == 200 and msgpack_has_str(rows, ADMIN_LOGIN) and not submit_denied(*w),
              f"UserAdmin list={st_list} write={w[0]} denied={submit_denied(*w)}")
    c.close()

    b.close()

print("\n=== RESULT ===")
print(json.dumps(results, ensure_ascii=False, indent=2))
failed = [k for k, v in results.items() if not v["pass"]]
if failed:
    print("FAILED:", failed)
    sys.exit(1)
print("ALL PASS")
