using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>
    /// 実物のプリザンターを相手にしたテストの準備と後片付け。この名前空間のテストの前にテスト用のサイトを作り (createsite)、行を入れ、最後にサイトを消す (deletesite)。
    /// 接続先は環境変数 PLEASANTER_TEST_URL (既定 http://localhost:59803/)。API キーは PLEASANTER_TEST_APIKEY_FILE (キーを書いたファイルのパス) か
    /// PLEASANTER_TEST_APIKEY。どちらも無ければこの名前空間のテストは Ignore。キーはどこにも書き出さない。
    /// </summary>
    [SetUpFixture]
    public class LiveFixture
    {
        public static string Url { get; private set; } = string.Empty;
        static string _apiKey = string.Empty;
        static HttpClient? _http;

        public static long CustomersId { get; private set; }
        public static long OrdersId { get; private set; }
        public static long TasksId { get; private set; }
        public static long ManyId { get; private set; }

        /// <summary>入れた顧客 (キー c1..c8 → ID)。</summary>
        public static Dictionary<string, long> Customers { get; } = new();
        /// <summary>入れた注文 (o1..o9 → ID)。</summary>
        public static Dictionary<string, long> Orders { get; } = new();
        /// <summary>入れた作業 (t1..t3 → ID)。</summary>
        public static Dictionary<string, long> Tasks { get; } = new();

        public const int ManyCount = 205;

        static readonly List<long> _createdSites = new();

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            Url = Environment.GetEnvironmentVariable("PLEASANTER_TEST_URL") is { Length: > 0 } url ? (url.EndsWith('/') ? url : url + "/") : "http://localhost:59803/";
            var keyFile = Environment.GetEnvironmentVariable("PLEASANTER_TEST_APIKEY_FILE");
            if (!string.IsNullOrEmpty(keyFile)) _apiKey = File.ReadAllText(keyFile).Trim();
            else _apiKey = Environment.GetEnvironmentVariable("PLEASANTER_TEST_APIKEY")?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(_apiKey)) Assert.Ignore("PLEASANTER_TEST_APIKEY_FILE / PLEASANTER_TEST_APIKEY is not set. The tests against a real Pleasanter are skipped.");

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var suffix = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            try
            {
                CustomersId = await CreateSiteAsync("CLBコネクタテスト_顧客_" + suffix, "Results", TestDesign.CustomersSettings());
                OrdersId = await CreateSiteAsync("CLBコネクタテスト_注文_" + suffix, "Results", TestDesign.OrdersSettings(CustomersId));
                TasksId = await CreateSiteAsync("CLBコネクタテスト_作業_" + suffix, "Issues", TestDesign.TasksSettings(CustomersId));
                ManyId = await CreateSiteAsync("CLBコネクタテスト_大量_" + suffix, "Results", TestDesign.ManySettings());
                await SeedAsync();
            }
            catch
            {
                await TearDownAsync();
                throw;
            }
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            if (_http == null) return;
            foreach (var siteId in _createdSites.ToList())
            {
                try
                {
                    await CallAsync($"api/items/{siteId}/deletesite", new JsonObject());
                    _createdSites.Remove(siteId);
                }
                catch (Exception e)
                {
                    TestContext.Progress.WriteLine($"Could not delete the test site {siteId}: {e.Message}");
                }
            }
            _http.Dispose();
            _http = null;
        }

        /// <summary>このテストのプリザンターへの接続の設定 (表: Customers / Orders / Tasks / Many)。</summary>
        internal static PleasanterConnectionSettings CreateSettings(Action<PleasanterConnectionSettings>? edit = null)
        {
            var settings = new PleasanterConnectionSettings
            {
                Name = TestDesign.DataSourceName,
                Url = Url,
                ApiKey = _apiKey,
                Sites = new() { ["Customers"] = CustomersId, ["Orders"] = OrdersId, ["Tasks"] = TasksId, ["Many"] = ManyId },
            };
            edit?.Invoke(settings);
            return settings;
        }

        /// <summary>API を直接呼ぶ (準備・確かめ用)。成功しなければ例外。</summary>
        public static async Task<JsonObject> CallAsync(string path, JsonObject body)
        {
            body["ApiVersion"] = 1.1m;
            body["ApiKey"] = _apiKey;
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http!.PostAsync(Url + path, content);
            var text = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(text) as JsonObject;
            if (!response.IsSuccessStatusCode || json?["StatusCode"]?.GetValue<int>() != 200)
                throw new InvalidOperationException($"{path}: HTTP {(int)response.StatusCode} {json?["Message"]}");
            return json!;
        }

        /// <summary>1 行を API で直接読む (確かめ用)。無ければ null。</summary>
        public static async Task<JsonObject?> GetRecordAsync(long siteId, long id)
        {
            var filter = new JsonObject { [IdColumn(siteId)] = $"[\"{id},{id}\"]" };
            var json = await CallAsync($"api/items/{siteId}/get", new JsonObject { ["View"] = new JsonObject { ["ColumnFilterHash"] = filter } });
            return json["Response"]!["Data"]!.AsArray().FirstOrDefault() as JsonObject;
        }

        static string IdColumn(long siteId) => siteId == TasksId ? "IssueId" : "ResultId";

        static async Task<long> CreateSiteAsync(string title, string referenceType, JsonObject settings)
        {
            var json = await CallAsync("api/items/0/createsite", new JsonObject { ["Title"] = title, ["ReferenceType"] = referenceType, ["SiteSettings"] = settings });
            var id = json["Id"]!.GetValue<long>();
            _createdSites.Add(id);
            return id;
        }

        static async Task<long> CreateAsync(long siteId, JsonObject body)
            => (await CallAsync($"api/items/{siteId}/create", body))["Id"]!.GetValue<long>();

        static JsonObject Hash(params (string Name, JsonNode? Value)[] values)
        {
            var hash = new JsonObject();
            foreach (var (name, value) in values) hash[name] = value;
            return hash;
        }

        static async Task SeedAsync()
        {
            async Task Customer(string key, string title, string? code = null, string? rank = null, decimal? credit = null, decimal? quantity = null,
                string? since = null, string? lastContact = null, bool? active = null, string? memo = null, int? status = null)
            {
                var body = new JsonObject { ["Title"] = title };
                if (status != null) body["Status"] = status;
                var classes = new List<(string, JsonNode?)>();
                if (code != null) classes.Add(("ClassA", code));
                if (rank != null) classes.Add(("ClassB", rank));
                if (classes.Count != 0) body["ClassHash"] = Hash(classes.ToArray());
                var nums = new List<(string, JsonNode?)>();
                if (credit != null) nums.Add(("NumA", credit));
                if (quantity != null) nums.Add(("NumB", quantity));
                if (nums.Count != 0) body["NumHash"] = Hash(nums.ToArray());
                var dates = new List<(string, JsonNode?)>();
                if (since != null) dates.Add(("DateA", since));
                if (lastContact != null) dates.Add(("DateB", lastContact));
                if (dates.Count != 0) body["DateHash"] = Hash(dates.ToArray());
                if (active != null) body["CheckHash"] = Hash(("CheckA", active));
                if (memo != null) body["DescriptionHash"] = Hash(("DescriptionA", memo));
                Customers[key] = await CreateAsync(CustomersId, body);
            }

            await Customer("c1", "株式会社アルファ", "K-001", "A", 500000m, 10m, "2026-01-15T00:00:00", "2026-11-01T09:30:15", true, "優良 100%_達成", 100);
            await Customer("c2", "ベータ商事", "K-002", "B", 0m, 0m, "2026-02-01T00:00:00", "2026-11-02T23:59:59", false, "abc", 200);
            await Customer("c3", "ガンマ工業", "k-003", "C", -3.25m, null, "2026-03-10T00:00:00", null, true, null, 200);
            await Customer("c4", "デルタ");
            await Customer("c5", "Epsilon Ltd", "", "A", 10.5m, 5m, "2026-01-15T00:00:00", "2026-11-03T00:00:00", false, "ABC def", 900);
            await Customer("c6", "O'Hara & Co", "K-006", "B", 1234567.89m, 3m, "2025-12-31T00:00:00", "2026-10-31T12:00:00", true, "x", 100);
            await Customer("c7", "株式会社イプシロン", "K-007", null, 600000m, null, null, null, false, "", 300);
            await Customer("c8", "zeta", "K-001", "C", 10.5m, 7m, "2026-05-05T00:00:00", "2026-11-01T09:30:15", true, "ZETA", 100);

            async Task Order(string key, string title, string? customer, string? kind, decimal? amount, string? date)
            {
                var body = new JsonObject { ["Title"] = title };
                var classes = new List<(string, JsonNode?)>();
                if (customer != null) classes.Add(("ClassA", customer.StartsWith('c') ? Customers[customer].ToString(CultureInfo.InvariantCulture) : customer));
                if (kind != null) classes.Add(("ClassB", kind));
                if (classes.Count != 0) body["ClassHash"] = Hash(classes.ToArray());
                if (amount != null) body["NumHash"] = Hash(("NumA", amount));
                if (date != null) body["DateHash"] = Hash(("DateA", date));
                Orders[key] = await CreateAsync(OrdersId, body);
            }

            await Order("o1", "注文1", "c1", "通常", 1000m, "2026-11-01T00:00:00");
            await Order("o2", "注文2", "c1", "特急", 2500.5m, "2026-11-02T00:00:00");
            await Order("o3", "注文3", "c2", "通常", 0m, "2026-11-03T00:00:00");
            await Order("o4", "注文4", "c3", null, 15000m, "2026-10-01T00:00:00");
            await Order("o5", "注文5", null, "通常", 300m, null);
            await Order("o6", "注文6", "99999999", "特急", 700m, "2026-11-05T00:00:00");
            await Order("o7", "注文7", "c6", "通常", 12000m, "2026-11-01T00:00:00");
            await Order("o8", "注文8", "c6", "通常", 50m, "2026-09-30T00:00:00");
            await Order("o9", "注文9", "c8", "特急", 99999m, "2026-11-10T00:00:00");

            Tasks["t1"] = await CreateAsync(TasksId, new JsonObject
            {
                ["Title"] = "作業1", ["StartTime"] = "2026-11-10T09:00:00", ["CompletionTime"] = "2026-11-20T00:00:00", ["WorkValue"] = 8, ["ProgressRate"] = 50,
                ["ClassHash"] = Hash(("ClassA", Customers["c1"].ToString(CultureInfo.InvariantCulture))), ["NumHash"] = Hash(("NumA", 3)),
            });
            Tasks["t2"] = await CreateAsync(TasksId, new JsonObject
            {
                ["Title"] = "作業2", ["StartTime"] = "2026-11-01T00:00:00", ["CompletionTime"] = "2026-12-01T00:00:00", ["WorkValue"] = 4, ["ProgressRate"] = 0,
                ["ClassHash"] = Hash(("ClassA", Customers["c2"].ToString(CultureInfo.InvariantCulture))),
            });
            Tasks["t3"] = await CreateAsync(TasksId, new JsonObject { ["Title"] = "作業3", ["StartTime"] = "2026-11-05T00:00:00", ["CompletionTime"] = "2026-11-06T00:00:00" });

            for (var i = 0; i < ManyCount; i++)
                await CreateAsync(ManyId, new JsonObject { ["Title"] = "m" + i.ToString("000", CultureInfo.InvariantCulture), ["NumHash"] = Hash(("NumA", i)) });
        }
    }

    /// <summary>実物のプリザンター + 本物のコネクター + 本体の ModuleDataIO。</summary>
    public sealed class LiveHarness
    {
        public RecordingDbAccessor Inner { get; } = new();
        internal PleasanterConnection Connection { get; }
        public PleasanterDbAccessor Accessor { get; }
        public DesignData Design { get; }
        public ModuleDataIO IO { get; }

        /// <param name="forceMemory">条件を View に書かずに全部読んでメモリで評価する (差分のテスト用)。</param>
        public LiveHarness(bool forceMemory = false, Action<PleasanterConnectionSettings>? edit = null, Action<DesignData>? editDesign = null)
        {
            //接続ごとに定義のキャッシュを持つので、テストごとに新しい接続 (FromDataSources の共有の接続は使わない)
            Connection = new PleasanterConnection(LiveFixture.CreateSettings(edit)) { ForceMemoryEvaluation = forceMemory };
            Accessor = new PleasanterDbAccessor(Inner, new[] { Connection });
            Design = TestDesign.CreateDesign(editDesign);
            IO = new ModuleDataIO(Design, new TestDesign.Auth(), Accessor, null!);
        }
    }
}
