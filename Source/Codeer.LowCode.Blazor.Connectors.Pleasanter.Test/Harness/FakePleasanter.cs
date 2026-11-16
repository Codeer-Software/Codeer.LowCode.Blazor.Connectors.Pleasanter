using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness
{
    /// <summary>記録した 1 つの要求。</summary>
    public class RecordedRequest
    {
        public string Url { get; init; } = string.Empty;
        /// <summary>パス (/api/items/{id}/{action} など)。</summary>
        public string Path { get; init; } = string.Empty;
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Body { get; init; } = string.Empty;

        public JsonObject Json => string.IsNullOrEmpty(Body) ? new JsonObject() : JsonNode.Parse(Body)!.AsObject();

        /// <summary>/api/items/{id}/{action} の id (それ以外は 0)。</summary>
        public long Id => Segments.Length == 4 && Segments[0] == "api" && Segments[1] == "items" && long.TryParse(Segments[2], out var id) ? id : 0;
        /// <summary>/api/items/{id}/{action} の action。</summary>
        public string Action => Segments.Length == 4 && Segments[0] == "api" && Segments[1] == "items" ? Segments[3] : string.Empty;

        string[] Segments => Path.Trim('/').Split('/');

        public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

        /// <summary>一覧の要求の View (無ければ空)。</summary>
        public JsonObject View => Json["View"] as JsonObject ?? new JsonObject();

        /// <summary>View.ColumnFilterHash (無ければ空)。値は文字 (入れ子の JSON の文字を含む)。</summary>
        public Dictionary<string, string> Filter => (View["ColumnFilterHash"] as JsonObject)?.ToDictionary(e => e.Key, e => e.Value!.GetValue<string>()) ?? new();

        /// <summary>View.ColumnFilterSearchTypes (無ければ空)。</summary>
        public Dictionary<string, string> SearchTypes => (View["ColumnFilterSearchTypes"] as JsonObject)?.ToDictionary(e => e.Key, e => e.Value!.GetValue<string>()) ?? new();

        /// <summary>View.ColumnSorterHash を "列 asc" の並びにしたもの。</summary>
        public List<string> Sorter => (View["ColumnSorterHash"] as JsonObject)?.Select(e => e.Key + " " + e.Value!.GetValue<string>()).ToList() ?? new();
    }

    /// <summary>
    /// プリザンターの API の偽物 (HttpMessageHandler)。実物の代わりではなく、送った要求の形とエラーの扱いを確かめるためのもの
    /// (実物の挙動は実物のプリザンターを相手にしたテストで確かめる)。要求を記録し、getsite・一覧 (条件で絞らない。Offset / PageSize だけ効かせる)・書き込みに応答する。
    /// </summary>
    public class FakePleasanter : HttpMessageHandler
    {
        public const string Url = "https://pleasanter.example.com/pleasanter/";
        public const string ApiKey = "fake-api-key";

        readonly object _lock = new();
        readonly List<(Func<RecordedRequest, bool> Match, Func<RecordedRequest, HttpResponseMessage> Respond, int Remaining)> _overrides = new();
        long _created = 1000;

        public List<RecordedRequest> Requests { get; } = new();

        /// <summary>サイト ID → getsite の Response.Data。</summary>
        public Dictionary<long, JsonObject> Sites { get; } = new();

        /// <summary>サイト ID → 行 (get で返す)。</summary>
        public Dictionary<long, List<JsonObject>> Rows { get; } = new();

        /// <summary>1 回の要求で返す行数の上限 (実物の既定は 200)。</summary>
        public int MaxPageSize { get; set; } = 200;

        public List<RecordedRequest> GetRequests(long siteId) => Requests.Where(e => e.Action == "get" && e.Id == siteId).ToList();
        public List<RecordedRequest> WriteRequests => Requests.Where(e => e.Action is "create" or "update" or "delete").ToList();

        /// <summary>条件に合う要求に、times 回だけ別の応答を返す。</summary>
        public void Override(Func<RecordedRequest, bool> match, Func<RecordedRequest, HttpResponseMessage> respond, int times = 1)
        {
            lock (_lock) _overrides.Add((match, respond, times));
        }

        /// <summary>条件に合う要求で、times 回だけ例外を投げる (通信の失敗)。</summary>
        public void Throw(Func<RecordedRequest, bool> match, Func<Exception> exception, int times = 1)
            => Override(match, _ => throw exception(), times);

        public static HttpResponseMessage Json(HttpStatusCode code, string body)
            => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        /// <summary>プリザンターのエラーの形 ({"Id":0,"StatusCode":n,"Message":"..."})。</summary>
        public static HttpResponseMessage Error(HttpStatusCode code, string message)
            => Json(code, new JsonObject { ["Id"] = 0, ["StatusCode"] = (int)code, ["Message"] = message }.ToJsonString());

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var recorded = new RecordedRequest
            {
                Url = request.RequestUri!.AbsoluteUri,
                Path = request.RequestUri!.AbsolutePath.StartsWith("/pleasanter/") ? request.RequestUri!.AbsolutePath["/pleasanter".Length..] : request.RequestUri!.AbsolutePath,
                Body = body,
            };
            foreach (var h in request.Headers) recorded.Headers[h.Key] = string.Join(",", h.Value);
            if (request.Content != null) foreach (var h in request.Content.Headers) recorded.Headers[h.Key] = string.Join(",", h.Value);

            Func<RecordedRequest, HttpResponseMessage>? respond = null;
            lock (_lock)
            {
                Requests.Add(recorded);
                for (var i = 0; i < _overrides.Count; i++)
                {
                    var o = _overrides[i];
                    if (!o.Match(recorded)) continue;
                    respond = o.Respond;
                    if (o.Remaining <= 1) _overrides.RemoveAt(i);
                    else _overrides[i] = (o.Match, o.Respond, o.Remaining - 1);
                    break;
                }
            }
            if (respond != null) return respond(recorded);
            return Default(recorded);
        }

        HttpResponseMessage Default(RecordedRequest request)
        {
            switch (request.Action)
            {
                case "getsite":
                    return Sites.TryGetValue(request.Id, out var site)
                        ? Json(HttpStatusCode.OK, new JsonObject { ["StatusCode"] = 200, ["Response"] = new JsonObject { ["Data"] = site.DeepClone() } }.ToJsonString())
                        : Error(HttpStatusCode.NotFound, "Not found.");
                case "get":
                    {
                        var rows = Rows.TryGetValue(request.Id, out var list) ? list : new();
                        var json = request.Json;
                        var offset = json["Offset"]?.GetValue<int>() ?? 0;
                        var pageSize = json["PageSize"]?.GetValue<int>() ?? MaxPageSize;
                        if (pageSize <= 0 || MaxPageSize < pageSize) pageSize = MaxPageSize;
                        var data = new JsonArray();
                        foreach (var row in rows.Skip(offset).Take(pageSize)) data.Add(row.DeepClone());
                        return Json(HttpStatusCode.OK, new JsonObject
                        {
                            ["StatusCode"] = 200,
                            ["Response"] = new JsonObject { ["Offset"] = offset, ["PageSize"] = pageSize, ["TotalCount"] = rows.Count, ["Data"] = data },
                        }.ToJsonString());
                    }
                case "create":
                    return Json(HttpStatusCode.OK, new JsonObject { ["Id"] = Interlocked.Increment(ref _created), ["StatusCode"] = 200, ["Message"] = "created" }.ToJsonString());
                case "update":
                case "delete":
                    return Json(HttpStatusCode.OK, new JsonObject { ["Id"] = request.Id, ["StatusCode"] = 200, ["Message"] = "ok" }.ToJsonString());
            }
            return Error(HttpStatusCode.NotFound, "Not found.");
        }
    }
}
