using System.Security.Claims;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using LowCodeApp.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LowCodeApp.Server.Sso
{
    public enum PleasanterSsoRevalidation
    {
        /// <summary>SSO のセッションでない・確かめ直す時期でない・確かめ直して有効だった。</summary>
        Valid,
        /// <summary>プリザンターでログアウト済み・別の人になっていた・入れないテナントだった。CLB からもサインアウトした。</summary>
        Unauthenticated,
        /// <summary>プリザンターに確かめられなかった。CLB からもサインアウトした (確かめられないまま延長しない)。</summary>
        Failed,
    }

    /// <summary>
    /// プリザンターのログインを使うシングルサインオンの、Cookie の発行と確かめ直し。
    /// Cookie はパスワードのログインと同じ形 (LoginPrincipal。ユーザー Id はユーザーモジュールの行の Id) に、
    /// ログインの方法 "Pleasanter" と、確かめ直しの突き合わせ用にプリザンターの TenantId / UserId・確かめた時刻を足す。
    /// </summary>
    public static class PleasanterSso
    {
        //Cookie に入れるログインの方法の名前 (外部 IdP の名前と重ならないもの)
        public const string Provider = "Pleasanter";
        const string ClaimTenant = "pleasanter_tenant";
        const string ClaimUser = "pleasanter_user";
        const string ClaimVerifiedAt = "pleasanter_verified_at";

        public static PleasanterSsoSettings Settings => SystemConfig.Instance.PleasanterSso;

        public static bool IsEnabled => Settings.IsEnabled;

        /// <summary>
        /// リクエストが持つプリザンターの Cookie で本人を確かめる。問い合わせ先はプリザンターのデータソースの接続文字列の Url
        /// (DataService と同じ接続。データソースは PleasanterSso.DataSourceName、空なら最初のプリザンターのデータソース)。
        /// 確かめられない (Failed) ときはログに残す。null はプリザンターのデータソースが無い・TenantId が未設定 (確かめられないのと同じに扱う)。
        /// 本人のテナントが TenantId と同じかは呼ぶ側が IsAllowedTenant で見る (違えば入れない)。
        /// </summary>
        public static async Task<PleasanterSessionResult?> VerifyAsync(HttpContext ctx)
        {
            if (!IsEnabled) return null;
            var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PleasanterSso).FullName!);
            //テナントを照合せずに通さない
            if (Settings.TenantId == null)
            {
                logger.LogWarning("Pleasanter SSO: PleasanterSso.TenantId is not set. Nobody can sign in with SSO.");
                return null;
            }
            var connection = PleasanterConnection.FromDataSources(SystemConfig.Instance.DataSources)
                .FirstOrDefault(e => string.IsNullOrEmpty(Settings.DataSourceName) || e.Settings.Name == Settings.DataSourceName);
            if (connection == null)
            {
                logger.LogWarning("Pleasanter SSO: there is no Pleasanter data source to verify the session.");
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Settings.TimeoutSeconds)));
            var result = await connection.VerifySessionAsync(ctx.Request.Cookies, cancellationToken: timeout.Token);
            if (result.Status == PleasanterSessionStatus.Failed)
                logger.LogWarning("Pleasanter SSO: the session could not be verified ({Message}).", result.Message);
            return result;
        }

        /// <summary>CLB の認証 Cookie を発行する。ブラウザを閉じると消える Cookie (プリザンターのセッションより長く残さない)。</summary>
        public static Task SignInAsync(HttpContext ctx, LoginAccount account, PleasanterIdentity identity)
            => SignInAsync(ctx, account.UserId, string.IsNullOrEmpty(account.DisplayName) ? identity.Name : account.DisplayName, account.SessionStamp, identity);

        /// <summary>本人のテナントが PleasanterSso.TenantId と同じか。</summary>
        public static bool IsAllowedTenant(PleasanterIdentity identity)
            => Settings.TenantId == identity.TenantId;

        public static bool IsSsoPrincipal(ClaimsPrincipal? user)
            => LoginPrincipal.GetProvider(user) == Provider;

        /// <summary>
        /// SSO のセッションを、確かめた時刻から RevalidateSeconds を過ぎていればプリザンターに確かめ直す。
        /// 有効なら確かめた時刻を更新し、ログアウト済み・別の人・入れないテナント・確かめられないなら CLB からもサインアウトする。
        /// </summary>
        public static async Task<PleasanterSsoRevalidation> RevalidateAsync(HttpContext ctx)
        {
            var user = ctx.User;
            if (!IsEnabled || user.Identity?.IsAuthenticated != true || !IsSsoPrincipal(user)) return PleasanterSsoRevalidation.Valid;
            if (VerifiedAtAgeSeconds(user) <= Settings.RevalidateSeconds) return PleasanterSsoRevalidation.Valid;

            var result = await VerifyAsync(ctx);
            if (result?.Identity != null && IsAllowedTenant(result.Identity) && Matches(result.Identity, user))
            {
                await SignInAsync(ctx, LoginPrincipal.GetUserId(user), LoginPrincipal.GetDisplayName(user), LoginPrincipal.GetSessionStamp(user), result.Identity);
                return PleasanterSsoRevalidation.Valid;
            }
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return result == null || result.Status == PleasanterSessionStatus.Failed ? PleasanterSsoRevalidation.Failed : PleasanterSsoRevalidation.Unauthenticated;
        }

        static Task SignInAsync(HttpContext ctx, string userId, string displayName, string? sessionStamp, PleasanterIdentity identity)
        {
            var principal = LoginPrincipal.Create(userId, displayName, Provider, sessionStamp);
            var claims = (ClaimsIdentity)principal.Identity!;
            claims.AddClaim(new Claim(ClaimTenant, identity.TenantId.ToString()));
            claims.AddClaim(new Claim(ClaimUser, identity.UserId.ToString()));
            claims.AddClaim(new Claim(ClaimVerifiedAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()));
            return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });
        }

        static long VerifiedAtAgeSeconds(ClaimsPrincipal user)
            => long.TryParse(user.FindFirst(ClaimVerifiedAt)?.Value, out var at) ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() - at : long.MaxValue;

        static bool Matches(PleasanterIdentity identity, ClaimsPrincipal user)
            => user.FindFirst(ClaimUser)?.Value == identity.UserId.ToString()
            && user.FindFirst(ClaimTenant)?.Value == identity.TenantId.ToString();
    }
}
