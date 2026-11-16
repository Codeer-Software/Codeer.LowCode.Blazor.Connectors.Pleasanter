using Codeer.LowCode.Blazor.SystemSettings;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>
    /// プリザンター 1 つへの接続 (API キー 1 つ)。HttpClient と表の定義を持つので、アプリの中で使い回す
    /// (<see cref="FromDataSources"/> は同じ設定に同じ接続を返す)。
    /// </summary>
    public sealed class PleasanterConnection
    {
        /// <summary>ブラウザのセッションの確認で転送する Cookie の名前 (前方一致。分割された Cookie の ...C1 / C2 も拾う) の既定。</summary>
        public static readonly IReadOnlyList<string> DefaultSessionCookiePrefixes = new[] { ".AspNetCore.Cookies", "Pleasanter_SessionGuid", ".AspNetCore.Session" };

        /// <summary>ブラウザのセッションの確認に使うプリザンターの拡張 SQL の名前 (App_Data/Parameters/ExtendedSqls に置くもの) の既定。</summary>
        public const string DefaultWhoAmISqlName = "ClbWhoAmI";

        static readonly ConcurrentDictionary<string, PleasanterConnection> _shared = new();

        readonly HttpClient _http;
        readonly HttpMessageHandler? _handler;
        readonly SemaphoreSlim _definitionLock = new(1, 1);
        readonly Dictionary<string, PleasanterTableDefinition> _definitions = new(StringComparer.Ordinal);
        HttpClient? _sessionHttp;

        public PleasanterConnectionSettings Settings { get; }

        //テストで待ち時間を無くすための差し替え口
        internal Func<TimeSpan, Task> Delay { get; set; } = Task.Delay;

        /// <summary>条件を View に書かずに全部読んでメモリで評価する (View の道とメモリの道の差分のテスト用)。</summary>
        internal bool ForceMemoryEvaluation { get; set; }

        public PleasanterConnection(PleasanterConnectionSettings settings)
            : this(settings, null)
        {
        }

        /// <param name="handler">HTTP の送信を差し替えるとき (テスト)。null なら既定。</param>
        internal PleasanterConnection(PleasanterConnectionSettings settings, HttpMessageHandler? handler)
        {
            settings.Validate();
            Settings = settings;
            _handler = handler;
            //リダイレクトには従わない (本文の API キーが別の宛先へ送られるため)。接続は数分で張り直す (DNS の変更に追従する)
            _http = handler == null
                ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
                : new HttpClient(handler, false);
            _http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        }

        /// <summary>
        /// データソースの中からプリザンター用 (ConnectionString が Provider=Pleasanter) のものを接続にする。同じ設定には同じ接続を返す。
        /// </summary>
        public static List<PleasanterConnection> FromDataSources(IEnumerable<DataSource> dataSources)
        {
            var list = new List<PleasanterConnection>();
            foreach (var dataSource in dataSources)
            {
                var settings = PleasanterConnectionSettings.FromDataSource(dataSource);
                if (settings == null) continue;
                list.Add(_shared.GetOrAdd(settings.GetIdentityKey(), _ => new PleasanterConnection(settings)));
            }
            return list;
        }

        // ---------------------------------------------------------------- 表の定義

        /// <summary>表 (Sites の別名) の定義。getsite は表ごとに最初の 1 回だけ読んで接続が生きている間持つ (サイトの設定を変えたらアプリを再起動する)。</summary>
        internal async Task<PleasanterTableDefinition> GetTableDefinitionAsync(string table)
        {
            if (!Settings.Sites.TryGetValue(table, out var siteId))
                throw new PleasanterException($"Pleasanter data source '{Settings.Name}': table '{table}' is not in Sites of the connection string.", PleasanterErrorKind.NotFound);
            lock (_definitions)
            {
                if (_definitions.TryGetValue(table, out var cached)) return cached;
            }
            await _definitionLock.WaitAsync();
            try
            {
                lock (_definitions)
                {
                    if (_definitions.TryGetValue(table, out var cached)) return cached;
                }
                var definition = await ReadSiteAsync(table, siteId);
                lock (_definitions) _definitions[table] = definition;
                return definition;
            }
            finally
            {
                _definitionLock.Release();
            }
        }

        /// <summary>Sites の全部の表の定義 (持っている定義を捨てて読み直す)。</summary>
        internal async Task<List<PleasanterTableDefinition>> ReloadTableDefinitionsAsync()
        {
            await _definitionLock.WaitAsync();
            try
            {
                var list = new List<PleasanterTableDefinition>();
                foreach (var (table, siteId) in Settings.Sites) list.Add(await ReadSiteAsync(table, siteId));
                lock (_definitions)
                {
                    _definitions.Clear();
                    foreach (var definition in list) _definitions[definition.Name] = definition;
                }
                return list;
            }
            finally
            {
                _definitionLock.Release();
            }
        }

        /// <summary>Sites の全部の表の定義 (持っていればそれを使う)。</summary>
        internal async Task<List<PleasanterTableDefinition>> GetTableDefinitionsAsync()
        {
            var list = new List<PleasanterTableDefinition>();
            foreach (var table in Settings.Sites.Keys) list.Add(await GetTableDefinitionAsync(table));
            return list;
        }

        async Task<PleasanterTableDefinition> ReadSiteAsync(string table, long siteId)
        {
            using var json = await SendAsync(SiteUrl(siteId, "getsite"), null, true);
            if (!json.RootElement.TryGetProperty("Response", out var response) || response.ValueKind != JsonValueKind.Object ||
                !response.TryGetProperty("Data", out var data))
                throw new PleasanterException($"Pleasanter: the response of getsite for '{table}' has no Response.Data.", PleasanterErrorKind.Unknown);
            return PleasanterTableDefinition.Parse(table, siteId, data, Settings.FindTableName);
        }

        string SiteUrl(long id, string action) => Settings.Url + "api/items/" + id.ToString(CultureInfo.InvariantCulture) + "/" + action;

        // ---------------------------------------------------------------- 読み出し

        internal sealed class ItemsPage
        {
            public List<JsonElement> Rows { get; } = new();
            public long TotalCount { get; set; }
        }

        /// <summary>サイトの一覧を 1 ページ読む (api/items/{siteId}/get)。</summary>
        internal async Task<ItemsPage> ReadPageAsync(PleasanterTableDefinition definition, ViewFilter view, IEnumerable<KeyValuePair<string, string>> sorter, int offset, int pageSize)
        {
            var body = new JsonObject
            {
                ["Offset"] = offset,
                ["PageSize"] = pageSize,
                ["View"] = CreateView(view, sorter),
            };
            using var json = await SendAsync(SiteUrl(definition.SiteId, "get"), body, true);
            var page = new ItemsPage();
            if (!json.RootElement.TryGetProperty("Response", out var response) || response.ValueKind != JsonValueKind.Object)
                throw new PleasanterException("Pleasanter: the response of get has no Response.", PleasanterErrorKind.Unknown);
            if (response.TryGetProperty("Data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in data.EnumerateArray()) if (row.ValueKind == JsonValueKind.Object) page.Rows.Add(row.Clone());
            }
            if (!response.TryGetProperty("TotalCount", out var total) || !total.TryGetInt64(out var count))
                throw new PleasanterException("Pleasanter: the response of get has no TotalCount.", PleasanterErrorKind.Unknown);
            page.TotalCount = count;
            return page;
        }

        /// <summary>条件に合う件数 (PageSize=1 の要求の TotalCount)。</summary>
        internal async Task<long> ReadCountAsync(PleasanterTableDefinition definition, ViewFilter view)
            => (await ReadPageAsync(definition, view, new[] { new KeyValuePair<string, string>(definition.KeyColumn, "asc") }, 0, 1)).TotalCount;

        static JsonObject CreateView(ViewFilter view, IEnumerable<KeyValuePair<string, string>> sorter)
        {
            var result = new JsonObject();
            if (view.ColumnFilterHash.Count != 0)
            {
                var hash = new JsonObject();
                foreach (var (key, value) in view.ColumnFilterHash) hash[key] = value;
                result["ColumnFilterHash"] = hash;
            }
            if (view.ColumnFilterSearchTypes.Count != 0)
            {
                var types = new JsonObject();
                foreach (var (key, value) in view.ColumnFilterSearchTypes) types[key] = value;
                result["ColumnFilterSearchTypes"] = types;
            }
            var sort = new JsonObject();
            foreach (var (key, value) in sorter) sort[key] = value;
            if (sort.Count != 0) result["ColumnSorterHash"] = sort;
            return result;
        }

        // ---------------------------------------------------------------- 書き込み

        /// <summary>行を作る (api/items/{siteId}/create)。作った行の ID を返す。再送しない。</summary>
        internal async Task<long> CreateAsync(PleasanterTableDefinition definition, JsonObject body)
        {
            using var json = await SendAsync(SiteUrl(definition.SiteId, "create"), body, false);
            if (json.RootElement.TryGetProperty("Id", out var id) && id.TryGetInt64(out var value) && 0 < value) return value;
            throw new PleasanterException($"Pleasanter created a record in '{definition.Name}' but did not return its id. Check the records in Pleasanter before retrying.",
                PleasanterErrorKind.WriteResultUnknown, HttpStatusCode.OK, 200);
        }

        /// <summary>行を更新する (api/items/{id}/update)。対象はあらかじめサイトの URL で読んで確かめておく (レコードの URL は別のサイトの行でも通る)。</summary>
        internal async Task UpdateAsync(long id, JsonObject body)
        {
            using var _ = await SendAsync(SiteUrl(id, "update"), body, false);
        }

        /// <summary>行を削除する (api/items/{id}/delete)。対象はあらかじめサイトの URL で読んで確かめておく。</summary>
        internal async Task DeleteAsync(long id)
        {
            using var _ = await SendAsync(SiteUrl(id, "delete"), null, false);
        }

        // ---------------------------------------------------------------- ブラウザのセッションの確認

        /// <summary>
        /// ブラウザが持つプリザンターの認証 Cookie をプリザンターの拡張 SQL (POST {Url}api/extended/sql) に転送し、プリザンターが解決した本人を返す
        /// (プリザンターのログインを使ったシングルサインオンの確認)。失敗しても例外を投げずに結果で返す。
        /// API キーは送らない。Cookie は要求ごとにヘッダに書き (Cookie を持たない別の HttpClient で送る)、どこにも保存しない。1 回だけ送る (やり直さない)。
        /// プリザンター側に、本人の TenantId / UserId (/ LoginId / Name) を返す拡張 SQL (既定の名前 <see cref="DefaultWhoAmISqlName"/>) を置いておく。
        /// </summary>
        /// <param name="cookies">ブラウザから受け取った Cookie (名前と値)。cookieNamePrefixes に前方一致するものだけを転送する。</param>
        /// <param name="cookieNamePrefixes">転送する Cookie の名前 (前方一致)。null なら <see cref="DefaultSessionCookiePrefixes"/>。</param>
        /// <param name="extendedSqlName">拡張 SQL の名前。</param>
        public async Task<PleasanterSessionResult> VerifySessionAsync(IEnumerable<KeyValuePair<string, string>> cookies, IEnumerable<string>? cookieNamePrefixes = null,
            string extendedSqlName = DefaultWhoAmISqlName, CancellationToken cancellationToken = default)
        {
            var prefixes = (cookieNamePrefixes ?? DefaultSessionCookiePrefixes).Where(e => !string.IsNullOrEmpty(e)).ToList();
            var header = new StringBuilder();
            foreach (var (name, value) in cookies)
            {
                if (string.IsNullOrEmpty(name) || !prefixes.Any(e => name.StartsWith(e, StringComparison.Ordinal))) continue;
                //ヘッダを壊す (別の Cookie・ヘッダを差し込む) 名前と値は送らない
                if (HasHeaderBreak(name) || name.Contains('=') || HasHeaderBreak(value ?? string.Empty)) continue;
                if (header.Length != 0) header.Append("; ");
                header.Append(name).Append('=').Append(value);
            }
            //転送する Cookie が無ければプリザンターに問い合わせない
            if (header.Length == 0) return PleasanterSessionResult.Unauthenticated("No Pleasanter session cookie.");
            if (string.IsNullOrWhiteSpace(extendedSqlName)) return PleasanterSessionResult.Failed("The name of the extended SQL is empty.");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Settings.Url + "api/extended/sql");
                var body = new JsonObject { ["ApiVersion"] = 1.1m, ["Name"] = extendedSqlName };
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Cookie", header.ToString());
                using var response = await GetSessionHttp().SendAsync(request, cancellationToken);

                //リダイレクト (ログイン画面へ)・HTML は認証済みにしない。JSON の StatusCode で決める
                if (response.StatusCode == HttpStatusCode.Unauthorized) return PleasanterSessionResult.Unauthenticated("Pleasanter returned HTTP 401.");
                if (!response.IsSuccessStatusCode)
                    return PleasanterSessionResult.Failed("Pleasanter returned HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ".");
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) return PleasanterSessionResult.Failed("The response of Pleasanter is not JSON.");

                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !TryGetInt(root, "StatusCode", out var statusCode))
                    return PleasanterSessionResult.Failed("The response of Pleasanter has no StatusCode.");
                if (statusCode == 401) return PleasanterSessionResult.Unauthenticated("Pleasanter returned StatusCode 401.");
                if (statusCode != 200)
                    return PleasanterSessionResult.Failed("Pleasanter returned StatusCode " + statusCode.ToString(CultureInfo.InvariantCulture) + ".");

                //拡張 SQL の応答: { Response: { Data: { "<表名>": [ { TenantId, UserId, LoginId, Name } ] } } } の最初の表の最初の行
                if (!root.TryGetProperty("Response", out var resp) || resp.ValueKind != JsonValueKind.Object ||
                    !resp.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object)
                    return PleasanterSessionResult.Failed("The response of the extended SQL has no Response.Data.");
                JsonElement? row = null;
                foreach (var table in data.EnumerateObject())
                {
                    if (table.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var r in table.Value.EnumerateArray())
                    {
                        row = r;
                        break;
                    }
                    break;
                }
                if (row is not { ValueKind: JsonValueKind.Object } found)
                    return PleasanterSessionResult.Failed("The extended SQL returned no row.");
                if (!TryGetInt(found, "TenantId", out var tenantId) || !TryGetInt(found, "UserId", out var userId))
                    return PleasanterSessionResult.Failed("The extended SQL did not return TenantId and UserId.");
                //本人が解決できていない (UserId が 0 など) は認証済みにしない
                if (userId <= 0) return PleasanterSessionResult.Unauthenticated("Pleasanter did not resolve the user.");
                return PleasanterSessionResult.Authenticated(new PleasanterIdentity
                {
                    TenantId = tenantId,
                    UserId = userId,
                    LoginId = GetString(found, "LoginId"),
                    Name = GetString(found, "Name"),
                });
            }
            catch (Exception e)
            {
                //例外のメッセージにはホスト名が入ることがあるので型名だけ
                return PleasanterSessionResult.Failed("Could not check the session with Pleasanter: " + e.GetType().Name);
            }
        }

        static bool HasHeaderBreak(string text) => text.IndexOfAny(new[] { ';', '\r', '\n' }) >= 0;

        static bool TryGetInt(JsonElement element, string name, out int value)
        {
            value = 0;
            if (!element.TryGetProperty(name, out var e)) return false;
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out value)) return true;
            return e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        static string GetString(JsonElement element, string name)
            => element.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : string.Empty;

        //利用者の Cookie で送る HttpClient (Cookie を持たない・リダイレクトに従わない)。連携用の _http とは別
        HttpClient GetSessionHttp()
        {
            if (_sessionHttp != null) return _sessionHttp;
            var http = _handler == null
                ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
                : new HttpClient(_handler, false);
            http.Timeout = TimeSpan.FromSeconds(Settings.TimeoutSeconds);
            return Interlocked.CompareExchange(ref _sessionHttp, http, null) ?? http;
        }

        // ---------------------------------------------------------------- 送信

        const int MaxAttempts = 4;

        /// <summary>
        /// POST で送る (本文に ApiVersion 1.1 と ApiKey を足す)。成功は「HTTP 200 かつ JSON の StatusCode が 200」。
        /// 読み出し (isRead) は 502 / 503 / 504・通信の失敗を 3 回までやり直す (429 は日次の上限のこともあるのでやり直さない)。
        /// 書き込みはやり直さず、送ったあと応答を受け取れなかった・5xx・想定外の応答なら「結果不明」。リダイレクト (3xx) には従わずエラー。
        /// </summary>
        internal async Task<JsonDocument> SendAsync(string url, JsonObject? body, bool isRead)
        {
            var payload = body == null ? new JsonObject() : (JsonObject)body.DeepClone();
            payload["ApiVersion"] = 1.1m;
            payload["ApiKey"] = Settings.ApiKey;
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            for (var attempt = 1; ; attempt++)
            {
                HttpResponseMessage response;
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = new ByteArrayContent(bytes);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                    request.Headers.Accept.ParseAdd("application/json");
                    try
                    {
                        response = await _http.SendAsync(request);
                    }
                    catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException)
                    {
                        if (!isRead)
                            throw new PleasanterException("The request was sent to Pleasanter but no response was received. Whether it was processed is unknown. Check the record in Pleasanter before retrying.",
                                PleasanterErrorKind.WriteResultUnknown, e);
                        if (attempt < MaxAttempts)
                        {
                            await Delay(GetBackoff(attempt));
                            continue;
                        }
                        //例外のメッセージにはホスト名が入ることがあるので、詳細は InnerException だけに残す
                        throw new PleasanterException("Could not reach Pleasanter (network error or timeout).", PleasanterErrorKind.ServiceUnavailable, e);
                    }
                }

                using (response)
                {
                    var status = (int)response.StatusCode;
                    if (300 <= status && status < 400)
                        throw new PleasanterException($"Pleasanter: the server returned a redirect (HTTP {status}). Redirects are not followed and the request was not processed. Check that Url is the root URL of Pleasanter.",
                            PleasanterErrorKind.InvalidRequest, response.StatusCode, null);

                    var isTemporary = response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
                    if (isRead && isTemporary && attempt < MaxAttempts)
                    {
                        await Delay(GetBackoff(attempt));
                        continue;
                    }

                    string text;
                    try
                    {
                        text = await response.Content.ReadAsStringAsync();
                    }
                    catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException)
                    {
                        if (!isRead) throw new PleasanterException("The response of Pleasanter could not be read. Whether the write was processed is unknown. Check the record in Pleasanter before retrying.", PleasanterErrorKind.WriteResultUnknown, e);
                        throw new PleasanterException("The response of Pleasanter could not be read.", PleasanterErrorKind.ServiceUnavailable, e);
                    }

                    JsonDocument? json = null;
                    try
                    {
                        json = JsonDocument.Parse(text);
                        if (json.RootElement.ValueKind != JsonValueKind.Object)
                        {
                            json.Dispose();
                            json = null;
                        }
                    }
                    catch (JsonException)
                    {
                    }
                    int? apiStatus = json != null && TryGetInt(json.RootElement, "StatusCode", out var s) ? s : null;
                    var message = json != null && json.RootElement.TryGetProperty("Message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? string.Empty : string.Empty;

                    if (response.StatusCode == HttpStatusCode.OK && apiStatus == 200) return json!;
                    json?.Dispose();

                    //HTTP が成功でも JSON の StatusCode が 200 でなければ JSON の方で分類する
                    var code = response.IsSuccessStatusCode && apiStatus != null ? apiStatus.Value : status;
                    var kind = code switch
                    {
                        401 => PleasanterErrorKind.Authentication,
                        403 => PleasanterErrorKind.Forbidden,
                        404 => PleasanterErrorKind.NotFound,
                        429 => PleasanterErrorKind.ServiceUnavailable,
                        _ when 500 <= code => PleasanterErrorKind.ServiceUnavailable,
                        _ when 400 <= code => PleasanterErrorKind.InvalidRequest,
                        _ => PleasanterErrorKind.Unknown,
                    };
                    var detail = string.IsNullOrEmpty(message) ? "HTTP " + status.ToString(CultureInfo.InvariantCulture) : message + " (HTTP " + status.ToString(CultureInfo.InvariantCulture) + ")";
                    if (json == null && response.IsSuccessStatusCode) detail = "the response is not a JSON object (HTTP " + status.ToString(CultureInfo.InvariantCulture) + ")";
                    //書き込みを送ったあとの 5xx・想定外の応答 (JSON でない・成功でも失敗でもない) は結果不明
                    if (!isRead && (500 <= code || kind == PleasanterErrorKind.Unknown || (response.IsSuccessStatusCode && apiStatus == null)))
                        throw new PleasanterException("Pleasanter: " + detail + ". Whether the write was processed is unknown. Check the record in Pleasanter before retrying.",
                            PleasanterErrorKind.WriteResultUnknown, response.StatusCode, apiStatus);
                    throw new PleasanterException("Pleasanter: " + detail + ".", kind, response.StatusCode, apiStatus);
                }
            }
        }

        static TimeSpan GetBackoff(int attempt)
            => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
    }
}
