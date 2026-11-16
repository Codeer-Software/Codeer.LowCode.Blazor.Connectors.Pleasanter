namespace LowCodeApp.Server.Sso
{
    /// <summary>
    /// SSO のセッションの確かめ直し。/api/ (/api/account/ を除く。current_user は AccountController が確かめ直す) の入口で、
    /// プリザンターでのログアウト・失効を反映する。確かめられなければ CLB からもサインアウトして 401 (プリザンターに届かないときは 503) を返す。
    /// 認証の後に置く。
    /// </summary>
    public sealed class PleasanterSsoRevalidationMiddleware
    {
        readonly RequestDelegate _next;

        public PleasanterSsoRevalidationMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext ctx)
        {
            if (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/account"))
            {
                var revalidation = await PleasanterSso.RevalidateAsync(ctx);
                if (revalidation != PleasanterSsoRevalidation.Valid)
                {
                    ctx.Response.StatusCode = revalidation == PleasanterSsoRevalidation.Failed
                        ? StatusCodes.Status503ServiceUnavailable
                        : StatusCodes.Status401Unauthorized;
                    return;
                }
            }
            await _next(ctx);
        }
    }
}
