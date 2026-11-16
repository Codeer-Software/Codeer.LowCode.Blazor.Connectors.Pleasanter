using System.Net;
using System.Text;
using System.Text.Json;

namespace LowCodeApp.Server.Sso
{
    /// <summary>
    /// プリザンターに「この Cookie の持ち主は誰か」を問い合わせる所 (ホストの中でここだけ)。
    /// 利用者の Cookie を要求ごとのヘッダに書いて拡張 SQL (ClbWhoAmI) を呼び、プリザンターが解決した本人を返す。API キーは送らない。
    /// 失敗は例外にせず Failed で返す (呼ぶ側は成功と扱わない)。
    /// </summary>
    public static class PleasanterSessionVerifier
    {
        //転送するプリザンターの Cookie の名前 (前方一致。分割された Cookie の ...C1 / C2 も拾う)。CLB 自身の Cookie は送らない
        static readonly string[] _forwardCookiePrefixes = [".AspNetCore.Cookies", "Pleasanter_SessionGuid", ".AspNetCore.Session"];
        const string ExtendedSqlName = "ClbWhoAmI";

        //Cookie を自動で持たない (利用者どうしで混ざらない)・リダイレクトに従わない (ログイン画面への転送を成功と取り違えない)
        static readonly HttpClient _http = new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false });

        public static async Task<PleasanterSsoResult> VerifyAsync(PleasanterSsoSettings settings, IEnumerable<KeyValuePair<string, string>> cookies, CancellationToken ct)
        {
            var cookieHeader = BuildCookieHeader(cookies);
            if (string.IsNullOrEmpty(cookieHeader)) return PleasanterSsoResult.Unauthenticated;

            var url = settings.InternalBaseUrl.TrimEnd('/') + "/api/extended/sql";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { ApiVersion = 1.1, Name = ExtendedSqlName }), Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (response.StatusCode == HttpStatusCode.Unauthorized) return PleasanterSsoResult.Unauthenticated;
                if (response.StatusCode != HttpStatusCode.OK) return PleasanterSsoResult.Failed($"HTTP {(int)response.StatusCode}");
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) return PleasanterSsoResult.Failed($"Content-Type {mediaType}");

                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                return Read(doc.RootElement);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return PleasanterSsoResult.Failed("timeout");
            }
            catch (Exception ex)
            {
                return PleasanterSsoResult.Failed(ex.GetType().Name);
            }
        }

        //応答: { StatusCode, Response: { Data: { "<表>": [ { TenantId, UserId, LoginId, Name } ] } } }。表の名前は問わず最初の行を読む
        static PleasanterSsoResult Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object || !TryGetInt(root, "StatusCode", out var statusCode)) return PleasanterSsoResult.Failed("no StatusCode");
            if (statusCode == 401) return PleasanterSsoResult.Unauthenticated;
            if (statusCode != 200) return PleasanterSsoResult.Failed($"StatusCode {statusCode}");

            if (!root.TryGetProperty("Response", out var response) || response.ValueKind != JsonValueKind.Object
                || !response.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object)
                return PleasanterSsoResult.Failed("no Response.Data");

            var row = data.EnumerateObject()
                .Where(e => e.Value.ValueKind == JsonValueKind.Array)
                .SelectMany(e => e.Value.EnumerateArray())
                .FirstOrDefault();
            if (row.ValueKind != JsonValueKind.Object) return PleasanterSsoResult.Failed("no row");
            if (!TryGetInt(row, "TenantId", out var tenantId) || !TryGetInt(row, "UserId", out var userId)) return PleasanterSsoResult.Failed("no TenantId / UserId");
            //本人が解決されていない (0)
            if (userId <= 0) return PleasanterSsoResult.Unauthenticated;

            return PleasanterSsoResult.Authenticated(new PleasanterSsoIdentity(tenantId, userId, GetString(row, "LoginId"), GetString(row, "Name")));
        }

        static string BuildCookieHeader(IEnumerable<KeyValuePair<string, string>> cookies)
            => string.Join("; ", cookies
                .Where(c => _forwardCookiePrefixes.Any(p => c.Key.StartsWith(p, StringComparison.Ordinal)))
                .Select(c => $"{c.Key}={c.Value}"));

        static bool TryGetInt(JsonElement obj, string name, out int value)
        {
            value = 0;
            if (!obj.TryGetProperty(name, out var e)) return false;
            if (e.ValueKind == JsonValueKind.Number) return e.TryGetInt32(out value);
            return e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out value);
        }

        static string GetString(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : string.Empty;
    }
}
