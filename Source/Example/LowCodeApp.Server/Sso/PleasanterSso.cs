using System.Security.Claims;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using LowCodeApp.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LowCodeApp.Server.Sso
{
    public enum PleasanterSsoStatus
    {
        /// <summary>プリザンターでログイン済み。Identity がある。</summary>
        Authenticated,
        /// <summary>プリザンターでログインしていない (Cookie が無い・期限切れ・ログアウト済み)。</summary>
        Unauthenticated,
        /// <summary>プリザンターに届かない・応答が読めない。ログイン済みとは扱わない。</summary>
        Failed,
    }

    /// <summary>プリザンターが解決した本人 (ブラウザから受け取った値ではない)。</summary>
    public sealed record PleasanterSsoIdentity(int TenantId, int UserId, string LoginId, string Name);

    public sealed record PleasanterSsoResult(PleasanterSsoStatus Status, PleasanterSsoIdentity? Identity, string? Message = null)
    {
        public static readonly PleasanterSsoResult Unauthenticated = new(PleasanterSsoStatus.Unauthenticated, null);
        public static PleasanterSsoResult Authenticated(PleasanterSsoIdentity identity) => new(PleasanterSsoStatus.Authenticated, identity);
        public static PleasanterSsoResult Failed(string message) => new(PleasanterSsoStatus.Failed, null, message);
    }

    public enum PleasanterSsoRevalidation
    {
        /// <summary>SSO のセッションでない・確かめ直す時期でない・確かめ直して有効だった。</summary>
        Valid,
        /// <summary>プリザンターでログアウト済み・別の人になっていた。CLB からもサインアウトした。</summary>
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

        /// <summary>リクエストが持つプリザンターの Cookie で本人を確かめる。</summary>
        public static async Task<PleasanterSsoResult> VerifyAsync(HttpContext ctx)
        {
            if (!IsEnabled) return PleasanterSsoResult.Unauthenticated;
            var result = await PleasanterSessionVerifier.VerifyAsync(Settings, ctx.Request.Cookies, ctx.RequestAborted);
            if (result.Status == PleasanterSsoStatus.Failed)
            {
                ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PleasanterSso).FullName!)
                    .LogWarning("Pleasanter SSO: the session could not be verified ({Message}).", result.Message);
            }
            return result;
        }

        /// <summary>CLB の認証 Cookie を発行する。ブラウザを閉じると消える Cookie (プリザンターのセッションより長く残さない)。</summary>
        public static Task SignInAsync(HttpContext ctx, LoginAccount account, PleasanterSsoIdentity identity)
            => SignInAsync(ctx, account.UserId, string.IsNullOrEmpty(account.DisplayName) ? identity.Name : account.DisplayName, account.SessionStamp, identity);

        public static bool IsSsoPrincipal(ClaimsPrincipal? user)
            => LoginPrincipal.GetProvider(user) == Provider;

        /// <summary>
        /// SSO のセッションを、確かめた時刻から RevalidateSeconds を過ぎていればプリザンターに確かめ直す。
        /// 有効なら確かめた時刻を更新し、ログアウト済み・別の人・確かめられないなら CLB からもサインアウトする。
        /// </summary>
        public static async Task<PleasanterSsoRevalidation> RevalidateAsync(HttpContext ctx)
        {
            var user = ctx.User;
            if (!IsEnabled || user.Identity?.IsAuthenticated != true || !IsSsoPrincipal(user)) return PleasanterSsoRevalidation.Valid;
            if (VerifiedAtAgeSeconds(user) <= Settings.RevalidateSeconds) return PleasanterSsoRevalidation.Valid;

            var result = await VerifyAsync(ctx);
            if (result.Status == PleasanterSsoStatus.Authenticated && result.Identity != null && Matches(result.Identity, user))
            {
                await SignInAsync(ctx, LoginPrincipal.GetUserId(user), LoginPrincipal.GetDisplayName(user), LoginPrincipal.GetSessionStamp(user), result.Identity);
                return PleasanterSsoRevalidation.Valid;
            }
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return result.Status == PleasanterSsoStatus.Failed ? PleasanterSsoRevalidation.Failed : PleasanterSsoRevalidation.Unauthenticated;
        }

        static Task SignInAsync(HttpContext ctx, string userId, string displayName, string? sessionStamp, PleasanterSsoIdentity identity)
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

        static bool Matches(PleasanterSsoIdentity identity, ClaimsPrincipal user)
            => user.FindFirst(ClaimUser)?.Value == identity.UserId.ToString()
            && user.FindFirst(ClaimTenant)?.Value == identity.TenantId.ToString();
    }
}
