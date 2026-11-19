using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Text.Json.Nodes;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>
    /// 日付だけの列 (Ymd) の条件と、プリザンターが読み替える値 "Own" の条件。
    /// 日付だけの列は時刻の入った値も持てる (API で時刻を送ると残る) が、読むと日付に丸めるので、条件も日単位で View に書く。
    /// </summary>
    public class DateOnlyAndOwnTest
    {
        FakeHarness _h = null!;

        [SetUp]
        public void SetUp()
        {
            //Customer に、日付だけの列 DateA を日時で比べるフィールドを足す (本体が日時の値で比べる場合)
            _h = new FakeHarness(editDesign: d => d.Modules.Find("Customer")!.Fields.Add(new DateTimeFieldDesign { Name = "SinceAt", DbColumn = "DateA" }));
            _h.Fake.Rows[FakeHarness.CustomersId] = new()
            {
                Customer(1, "2026-03-09T23:59:59", "K-001"),
                Customer(2, "2026-03-10T00:00:00", "Own"),
                Customer(3, "2026-03-10T15:45:00", "1"),
                Customer(4, "2026-03-11T00:00:00", null),
            };
        }

        static JsonObject Customer(long id, string since, string? code) => new()
        {
            ["SiteId"] = FakeHarness.CustomersId,
            ["ResultId"] = id,
            ["Ver"] = 1,
            ["Title"] = "C" + id,
            ["ClassHash"] = new JsonObject { ["ClassA"] = code ?? string.Empty },
            ["DateHash"] = new JsonObject { ["DateA"] = since, ["DateB"] = "1899-12-30T00:00:00" },
        };

        async Task<(List<string> Ids, Dictionary<string, string> Filter)> ListAsync(MatchConditionBase condition, bool forceMemory = false)
        {
            _h.Fake.Requests.Clear();
            _h.Connection.ForceMemoryEvaluation = forceMemory;
            var items = (await _h.IO.GetListAsync(new SearchCondition("Customer") { Condition = condition }, 0)).Items;
            var request = _h.Fake.GetRequests(FakeHarness.CustomersId).Where(e => e.Json["PageSize"]?.GetValue<int>() != 1).Single();
            return (items.Select(Id).OrderBy(e => e, StringComparer.Ordinal).ToList(), request.Filter);
        }

        static readonly DateOnly Day = new(2026, 3, 10);

        [TestCase(MatchComparison.Equal, "[\"2026-03-10T00:00:00,2026-03-10T23:59:59\"]")]
        [TestCase(MatchComparison.LessThanOrEqual, "[\",2026-03-10T23:59:59\"]")]
        [TestCase(MatchComparison.LessThan, "[\",2026-03-09T23:59:59\"]")]
        [TestCase(MatchComparison.GreaterThanOrEqual, "[\"2026-03-10T00:00:00,\"]")]
        [TestCase(MatchComparison.GreaterThan, "[\"2026-03-11T00:00:00,\"]")]
        public async Task 日付だけの列の日付の条件は日単位の範囲(MatchComparison comparison, string expected)
        {
            var (_, filter) = await ListAsync(Value("Since.Value", comparison, Day));
            Assert.That(filter, Is.EqualTo(new Dictionary<string, string> { ["DateA"] = expected }));
        }

        [TestCase(MatchComparison.Equal, new[] { "2", "3" })]
        [TestCase(MatchComparison.LessThanOrEqual, new[] { "1", "2", "3" })]
        [TestCase(MatchComparison.LessThan, new[] { "1" })]
        [TestCase(MatchComparison.GreaterThanOrEqual, new[] { "2", "3", "4" })]
        [TestCase(MatchComparison.GreaterThan, new[] { "4" })]
        public async Task メモリで評価しても時刻の入った値はその日として比べる(MatchComparison comparison, string[] expected)
        {
            var (ids, filter) = await ListAsync(Value("Since.Value", comparison, Day), forceMemory: true);
            Assert.That(filter, Is.Empty);
            Assert.That(ids, Is.EqualTo(expected));
        }

        [Test]
        public async Task 日付だけの列の値の集合はそれぞれの日の範囲()
        {
            var (_, filter) = await ListAsync(Value("Since.Value", MatchComparison.In, new List<object?> { Day, new DateOnly(2026, 3, 11) }));
            Assert.That(filter, Is.EqualTo(new Dictionary<string, string> { ["DateA"] = "[\"2026-03-10T00:00:00,2026-03-10T23:59:59\",\"2026-03-11T00:00:00,2026-03-11T23:59:59\"]" }));
        }

        [Test]
        public async Task 日時のフィールドで比べても本体が日付にしてから渡す()
        {
            //本体は列の型 (日付) に合わせて条件の値を日付にする。時刻は落ちてその日の一致になる
            var condition = Value("SinceAt.Value", MatchComparison.Equal, new DateTime(2026, 3, 10, 15, 45, 0));
            var (_, filter) = await ListAsync(condition);
            Assert.That(filter, Is.EqualTo(new Dictionary<string, string> { ["DateA"] = "[\"2026-03-10T00:00:00,2026-03-10T23:59:59\"]" }));
            var (ids, _) = await ListAsync(condition, forceMemory: true);
            Assert.That(ids, Is.EqualTo(new[] { "2", "3" }));
        }

        async Task<(List<string> Ids, Dictionary<string, string> Filter)> QueryAsync(string op, object value, bool forceMemory)
        {
            _h.Fake.Requests.Clear();
            _h.Connection.ForceMemoryEvaluation = forceMemory;
            var rows = await _h.Accessor.QueryAsync(TestDesign.DataSourceName, $"select \"ResultId\" from \"Customers\" where \"DateA\" {op} @p0",
                new() { ["@p0"] = new ParamAndRawDbTypeName { Value = value } });
            var request = _h.Fake.GetRequests(FakeHarness.CustomersId).Where(e => e.Json["PageSize"]?.GetValue<int>() != 1).Single();
            return (rows.Select(e => Convert.ToString(e["ResultId"], System.Globalization.CultureInfo.InvariantCulture)!).OrderBy(e => e, StringComparer.Ordinal).ToList(), request.Filter);
        }

        //SQL に日時の値が直接来たとき (本体を通さない呼び出し)。日付の列の値はその日の 0:00 として比べる (PostgreSQL の date と timestamp の比較と同じ)
        [TestCase("<=", "[\",2026-03-10T23:59:59\"]", new[] { "1", "2", "3" })]
        [TestCase("<", "[\",2026-03-10T23:59:59\"]", new[] { "1", "2", "3" })]
        [TestCase(">=", "[\"2026-03-11T00:00:00,\"]", new[] { "4" })]
        [TestCase(">", "[\"2026-03-11T00:00:00,\"]", new[] { "4" })]
        //一致はどの行とも合わない。View にはその日の範囲を書き (多めに読む)、メモリで絞り直す
        [TestCase("=", "[\"2026-03-10T00:00:00,2026-03-10T23:59:59\"]", new string[0])]
        public async Task 日付だけの列を時刻のある日時で比べる(string op, string expectedFilter, string[] expected)
        {
            var value = new DateTime(2026, 3, 10, 12, 0, 0);
            var (_, filter) = await QueryAsync(op, value, false);
            Assert.That(filter, Is.EqualTo(new Dictionary<string, string> { ["DateA"] = expectedFilter }));
            var (ids, _) = await QueryAsync(op, value, true);
            Assert.That(ids, Is.EqualTo(expected));
        }

        [Test]
        public async Task Ownの一致はViewに書かず文字のOwnとして比べる()
        {
            var (ids, filter) = await ListAsync(Value("Code.Value", MatchComparison.Equal, "Own"));
            Assert.That(filter, Is.Empty);
            Assert.That(ids, Is.EqualTo(new[] { "2" }));
        }

        [Test]
        public async Task Ownを含む値の集合もViewに書かない()
        {
            var (ids, filter) = await ListAsync(Value("Code.Value", MatchComparison.In, new List<object?> { "Own", "K-001" }));
            Assert.That(filter, Is.Empty);
            Assert.That(ids, Is.EqualTo(new[] { "1", "2" }));
        }

        [Test]
        public async Task Ownを含む部分一致もViewに書かない()
        {
            var (ids, filter) = await ListAsync(Value("Code.Value", MatchComparison.Like, "Own"));
            Assert.That(filter, Is.Empty);
            Assert.That(ids, Is.EqualTo(new[] { "2" }));
        }

        [Test]
        public async Task Ownと違う値はViewに書く()
        {
            var (_, filter) = await ListAsync(Value("Code.Value", MatchComparison.Equal, "own"));
            Assert.That(filter, Is.EqualTo(new Dictionary<string, string> { ["ClassA"] = "[\"own\"]" }));
        }
    }
}
