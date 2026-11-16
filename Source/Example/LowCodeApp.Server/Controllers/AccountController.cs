using LowCodeApp.Server.Extras;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Codeer.LowCode.Blazor.Utils;
using Microsoft.AspNetCore.Authorization;
using LowCodeApp.Client;
using LowCodeApp.Server.Infrastructure;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Microsoft.Extensions.Caching.Distributed;
using LowCodeApp.Server.Sso;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;

namespace LowCodeApp.Server.Controllers
{
    [ApiController, AutoValidateAntiforgeryToken]
    [Route("api/account")]
    public class AccountController : ControllerBase
    {
        const string LoginPage = "/login.html";

        readonly DataService _dataService;
        readonly ExternalLoginService _externalLogins;
        readonly IDistributedCache _cache;
        readonly AuditContext _audit;

        public AccountController(DataService dataService, ExternalLoginService externalLogins, IDistributedCache cache, AuditContext audit)
        {
            _dataService = dataService;
            _externalLogins = externalLogins;
            _cache = cache;
            _audit = audit;
        }

        //プリザンターの SSO のセッションは、確かめた時刻が古ければプリザンターに確かめ直す (ログアウト済みなら 401)
        [HttpGet("current_user")]
        public async Task<IActionResult> GetCurrentUser()
        {
            if (await PleasanterSso.RevalidateAsync(HttpContext) != PleasanterSsoRevalidation.Valid) return Unauthorized();
            return Ok(new StringWrapper(LoginPrincipal.GetUserId(User)));
        }

        //antiforgery トークンの Cookie を発行する (login.html とアプリの起動時に取る)
        [HttpGet("antiforgery")]
        public IActionResult Antiforgery()
        {
            CookieAuthentication.AppendAntiforgeryTokenCookie(HttpContext);
            return NoContent();
        }

        //ログイン画面に出すもの (ID/パスワードのフォームの有無・外部 IdP のボタン)。見た目は login.html (Web) と Login.razor (MAUI) で変える
        [HttpGet("login_options")]
        public object LoginOptions()
            => new
            {
                Password = SystemConfig.Instance.AllowPasswordLogin,
                Providers = _externalLogins.Options,
                //プリザンターの SSO を使うとき、index.html はログインしていないブラウザをログイン待ち画面 (sso-login.html) へ送る
                Sso = PleasanterSso.IsEnabled,
                SsoLoginUrl = PleasanterSso.IsEnabled ? PleasanterSso.Settings.LoginUrl : null,
                SsoLogoutUrl = PleasanterSso.IsEnabled && !string.IsNullOrEmpty(PleasanterSso.Settings.LogoutUrl) ? PleasanterSso.Settings.LogoutUrl : null,
            };

        //プリザンターのログインを使うシングルサインオンの入口 (ブラウザの遷移。プリザンターのメニューから CLB を開くときなど)。
        //プリザンターでログイン済みなら CLB の Cookie を発行して returnUrl (CLB の中のパス) へ、まだならログイン待ち画面 (sso-login.html) へ送る
        [HttpGet("sso/enter"), Audit(AuditCategory.Authentication)]
        public async Task<IActionResult> SsoEnter(string? returnUrl)
        {
            if (!PleasanterSso.IsEnabled) return NotFound();
            var safe = LocalReturnUrl(returnUrl);
            if (await TrySsoSignInAsync() == SsoSignInResult.SignedIn) return Redirect(safe);
            return Redirect($"/sso-login.html?returnUrl={Uri.EscapeDataString(safe)}");
        }

        //ログイン待ち画面 (sso-login.html) と index.html が呼ぶ。プリザンターでログイン済みなら CLB の Cookie を発行して Authenticated = true を返す
        //(プリザンターのログイン画面は別のオリジンへ戻れないので、CLB の側で待つ)。
        //プリザンターでは確かめられたがユーザーモジュールに登録が無いときは NotRegistered = true (待ち画面は確かめるのをやめて知らせる)
        //待ち画面が繰り返し呼ぶので監査ログの分類は付けない (まだログインしていない結果は記録されない)。
        //プリザンターで本人が確かめられたときだけ TrySsoSignInAsync が認証の分類にする (ログインの成否が記録される)
        [HttpGet("sso/check")]
        public async Task<IActionResult> SsoCheck()
        {
            if (!PleasanterSso.IsEnabled) return NotFound();
            var result = await TrySsoSignInAsync();
            return Ok(new { Authenticated = result == SsoSignInResult.SignedIn, NotRegistered = result == SsoSignInResult.NotRegistered });
        }

        enum SsoSignInResult { NotSignedIn, SignedIn, NotRegistered }

        //届いたプリザンターの Cookie で本人を確かめ、ユーザーモジュールの行に結び付けて Cookie を発行する。
        //TenantId / UserId などはブラウザから受け取らない (プリザンターが解決した値だけを使う)
        async Task<SsoSignInResult> TrySsoSignInAsync()
        {
            var result = await PleasanterSso.VerifyAsync(HttpContext);
            if (result?.Identity == null) return SsoSignInResult.NotSignedIn;
            var identity = result.Identity;
            await _audit.StartAsync(AuditCategory.Authentication, _audit.Event.Action);
            _audit.Event.Detail = $"Provider={PleasanterSso.Provider}; LoginId={identity.LoginId}";

            var account = await FindSsoAccountAsync(identity);
            if (account == null)
            {
                _audit.Deny($"Provider={PleasanterSso.Provider}; LoginId={identity.LoginId}; user_not_registered");
                return SsoSignInResult.NotRegistered;
            }
            _audit.Event.UserId = account.UserId;
            await PleasanterSso.SignInAsync(HttpContext, account, identity);
            return SsoSignInResult.SignedIn;
        }

        //プリザンターのログイン ID を、ユーザーモジュールの外部ログイン名の列 (LoginAccountContractField の ExternalLoginName。空ならログイン名の列) で引く。
        //無くて AutoProvisionUser なら、ログイン ID をログイン名にして行を作る (パスワードは誰も知らない値。ID / パスワードのログインには使えない)。
        //同じログイン名の行が既にあれば作らない (別の人の行に入らないように)
        async Task<LoginAccount?> FindSsoAccountAsync(PleasanterIdentity identity)
        {
            var accounts = LoginAccountStore.Create(_dataService.Design.DesignData, _dataService.DbAccess);
            if (accounts == null) return null;
            var account = await accounts.FindByExternalLoginNameAsync(identity.LoginId);
            if (account != null || !PleasanterSso.Settings.AutoProvisionUser || !accounts.HasPassword) return account;
            if (await accounts.FindByLoginNameAsync(identity.LoginId) != null) return null;
            await accounts.AddAsync(identity.LoginId, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            //外部ログイン名の列がログイン名の列と別なら、作った行はこれでは引けない (その設計では事前登録にする)
            return await accounts.FindByExternalLoginNameAsync(identity.LoginId);
        }

        //CLB の中のパスだけ (別のサイトへ飛ばさない)
        static string LocalReturnUrl(string? url)
            => string.IsNullOrEmpty(url) || !url.StartsWith('/') || url.StartsWith("//") || url.StartsWith("/\\") ? "/" : url;

        [HttpPost("login"), Audit(AuditCategory.Authentication)]
        public async Task<IActionResult> Login(LoginInfo? loginInfo)
        {
            if (loginInfo == null) throw new ArgumentException(nameof(loginInfo));
            if (!SystemConfig.Instance.AllowPasswordLogin) return NotFound();
            _audit.Event.Detail = $"LoginName={loginInfo.Id}";

            //ID / パスワードの照合・ロックアウト・二要素認証は PasswordLogin。結果を HTTP と監査ログに写し、通ったら Cookie を発行する
            var login = new PasswordLogin(_dataService.Design.DesignData, _dataService.DbAccess, _cache,
                SystemConfig.Instance.LoginLockout, SystemConfig.Instance.TotpLogin, SystemConfig.Instance.EmailOtpLogin,
                message => new MailDispatcher(SystemConfig.Instance.Mail, MailSenderTable.Create).SendAsync(SystemConfig.Instance.EmailOtpLogin.MailInfraName, message));
            var result = await login.VerifyAsync(loginInfo.Id, loginInfo.Password, loginInfo.TwoFactorCode);

            if (result.IsDenied) _audit.Deny($"LoginName={loginInfo.Id}; {result.Detail}");
            else if (result.Detail != null) _audit.Event.Detail += $"; {result.Detail}";

            switch (result.Status)
            {
                case PasswordLoginStatus.NotAvailable: return NotFound();
                case PasswordLoginStatus.Locked: return StatusCode(StatusCodes.Status429TooManyRequests);
                case PasswordLoginStatus.Unauthorized: return Unauthorized();
                case PasswordLoginStatus.TwoFactor: return Ok(result.Response);   //登録情報・コードの要求・不一致: サインインしない
            }

            //監査ログのユーザー Id は Cookie を発行するときだけ入れる (二要素認証待ちの行には入れない)
            var account = result.Account!;
            _audit.Event.UserId = account.UserId;

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                LoginPrincipal.Create(account.UserId, account.DisplayName, sessionStamp: account.SessionStamp),
                new AuthenticationProperties { IsPersistent = loginInfo.IsPersistent });

            return Ok(result.Response);
        }

        //外部 IdP でログイン。IdP が本人確認した後、ExternalLoginUserResolver がユーザー行に解決して Cookie を発行する。
        //persistent = ログイン状態を保持する。mobile = ネイティブアプリ (Cookie の代わりに使い捨てチケットを返す)
        [HttpGet("login/{provider}"), Audit(AuditCategory.Authentication)]
        public IActionResult ExternalLogin(string provider, string? returnUrl, bool mobile = false, bool persistent = false)
            => _externalLogins.Challenge(this, provider, returnUrl, mobile, persistent);

        //ネイティブアプリ: システムブラウザで受け取った使い捨てチケットを認証 Cookie に交換する
        [HttpPost("login_ticket"), Audit(AuditCategory.Authentication)]
        public async Task<IActionResult> LoginTicket(LoginTicket? ticket)
        {
            var redeemed = await _externalLogins.RedeemMobileTicketAsync(ticket?.Ticket);
            if (redeemed == null) return Unauthorized();
            _audit.Event.UserId = redeemed.Value.User.UserId;
            _audit.Event.Detail = $"Provider={redeemed.Value.Provider}";

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                LoginPrincipal.Create(redeemed.Value.User.UserId, redeemed.Value.User.UserName, redeemed.Value.Provider, redeemed.Value.User.SessionStamp),
                new AuthenticationProperties { IsPersistent = true });

            return Ok();
        }

        //外部 IdP でサインインしたセッションは IdP 側のログアウトにブラウザ遷移が要るので、Cookie を残したまま遷移先 URL を返す
        [Authorize]
        [HttpPost("logout"), Audit(AuditCategory.Authentication)]
        public async Task<IActionResult> Logout(bool mobile = false)
        {
            //プリザンターの SSO: CLB からだけサインアウトしてログイン待ち画面へ (自動では入り直さない。プリザンターからのログアウトは待ち画面のリンク)
            if (!mobile && PleasanterSso.IsEnabled && PleasanterSso.IsSsoPrincipal(User))
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Ok(new LogoutResult { Redirect = "/sso-login.html?prompt=1" });
            }
            if (!mobile)
            {
                var provider = await _externalLogins.GetSignOutProviderAsync(User);
                if (provider != null) return Ok(new LogoutResult { Redirect = $"/api/account/logout/{provider}" });
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new LogoutResult());
        }

        //Cookie を破棄し IdP のセッションも終わらせてログイン画面へ戻る (GET: ブラウザ遷移)
        [HttpGet("logout/{provider}"), Audit(AuditCategory.Authentication)]
        public async Task<IActionResult> ExternalLogout(string provider)
        {
            if (Request.Headers["Sec-Fetch-Site"] == "cross-site") return Redirect("/");
            return await _externalLogins.SignOutAsync(this, provider, LoginPage);
        }
    }
}
