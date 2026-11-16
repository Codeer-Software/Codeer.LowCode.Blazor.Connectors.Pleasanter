using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>
    /// 実物のプリザンター + 本物のコネクター + 本体の ModuleDataIO で、定義・一覧・検索・ページング・結合・一覧をまたぐ条件を確かめる。
    /// 期待値は入れた行 (<see cref="LiveFixture"/>) から書く。
    /// </summary>
    public class LiveReadTest
    {
        LiveHarness _h = null!;

        [SetUp]
        public void SetUp() => _h = new LiveHarness();

        static string C(string key) => LiveFixture.Customers[key].ToString(CultureInfo.InvariantCulture);
        static string O(string key) => LiveFixture.Orders[key].ToString(CultureInfo.InvariantCulture);

        async Task<List<string>> IdsAsync(string module, MatchConditionBase? condition, params SortCondition[] sorts)
        {
            var search = new SearchCondition(module) { Condition = condition };
            search.SortConditions.AddRange(sorts);
            return (await _h.IO.GetListAsync(search, 0)).Items.Select(Id).ToList();
        }

        async Task<HashSet<string>> CustomerKeysAsync(MatchConditionBase condition)
        {
            var ids = await IdsAsync("Customer", condition);
            return ids.Select(id => LiveFixture.Customers.Single(e => e.Value.ToString(CultureInfo.InvariantCulture) == id).Key).ToHashSet();
        }

        // ---------------------------------------------------------------- 定義

        [Test]
        public async Task 定義はgetsiteから作り標準の列とエディタの列が入る()
        {
            var customers = await _h.Connection.GetTableDefinitionAsync("Customers");
            Assert.That(customers.KeyColumn, Is.EqualTo("ResultId"));
            Assert.That(customers.Columns.Select(e => e.Name), Is.SupersetOf(new[] { "ResultId", "Title", "Body", "Status", "Manager", "Owner", "Ver", "CreatedTime", "UpdatedTime", "ClassA", "ClassB", "NumA", "NumB", "DateA", "DateB", "CheckA", "DescriptionA" }));
            Assert.That(customers.FindColumn("NumB")!.IsNullable, Is.True);
            Assert.That(customers.FindColumn("NumA")!.IsNullable, Is.False);
            Assert.That(customers.FindColumn("DateA")!.Kind, Is.EqualTo(PleasanterColumnKind.Date));
            Assert.That(customers.FindColumn("DateB")!.Kind, Is.EqualTo(PleasanterColumnKind.DateTime));
            Assert.That(customers.FindColumn("ClassB")!.Choices!.Select(e => e.Value + "=" + e.DisplayText), Is.EqualTo(new[] { "A=ゴールド", "B=シルバー", "C=ブロンズ" }));
            Assert.That(customers.FindColumn("NumA")!.LabelText, Is.EqualTo("与信"));
            var orders = await _h.Connection.GetTableDefinitionAsync("Orders");
            Assert.That(orders.FindColumn("ClassA")!.LinkSiteId, Is.EqualTo(LiveFixture.CustomersId));
            Assert.That(orders.FindColumn("ClassA")!.LinkTable, Is.EqualTo("Customers"));
            var tasks = await _h.Connection.GetTableDefinitionAsync("Tasks");
            Assert.That(tasks.KeyColumn, Is.EqualTo("IssueId"));
            Assert.That(tasks.Columns.Select(e => e.Name), Is.SupersetOf(new[] { "StartTime", "CompletionTime", "WorkValue", "ProgressRate", "RemainingWorkValue" }));

            var tables = (await _h.Accessor.GetCustomTableDefinitionsAsync(TestDesign.DataSourceName))!;
            Assert.That(tables.Select(e => e.Name), Is.EquivalentTo(new[] { "Customers", "Orders", "Tasks", "Many" }));
            Assert.That(tables.SelectMany(e => e.Columns).Select(e => e.IsNullable), Is.All.True);
            Assert.That(_h.Inner.DataSourceCalls, Is.Empty);
        }

        // ---------------------------------------------------------------- 値

        [Test]
        public async Task 値は列の型で読め空は空になる()
        {
            var items = (await _h.IO.GetListAsync(new SearchCondition("Customer"), 0)).Items.ToDictionary(Id);
            var c1 = items[C("c1")];
            Assert.That(Field<TextFieldData>(c1, "Name")!.Value, Is.EqualTo("株式会社アルファ"));
            Assert.That(Field<TextFieldData>(c1, "Code")!.Value, Is.EqualTo("K-001"));
            Assert.That(Field<NumberFieldData>(c1, "Credit")!.Value, Is.EqualTo(500000m));
            Assert.That(Field<NumberFieldData>(c1, "Quantity")!.Value, Is.EqualTo(10m));
            Assert.That(Field<DateFieldData>(c1, "Since")!.Value, Is.EqualTo(new DateOnly(2026, 1, 15)));
            Assert.That(Field<DateTimeFieldData>(c1, "LastContact")!.Value, Is.EqualTo(new DateTime(2026, 11, 1, 9, 30, 15)));
            Assert.That(Field<BooleanFieldData>(c1, "Active")!.Value, Is.True);
            Assert.That(Field<NumberFieldData>(c1, "Status")!.Value, Is.EqualTo(100m));

            //空の値: 文字 "" → null・null を持てない数値は 0・持てる数値は null・空の日付 → null・チェックは false
            var c4 = items[C("c4")];
            Assert.That(Field<TextFieldData>(c4, "Code")?.Value, Is.Null);
            Assert.That(Field<NumberFieldData>(c4, "Credit")?.Value, Is.EqualTo(0m));
            Assert.That(Field<NumberFieldData>(c4, "Quantity")?.Value, Is.Null);
            Assert.That(Field<DateFieldData>(c4, "Since")?.Value, Is.Null);
            Assert.That(Field<DateTimeFieldData>(c4, "LastContact")?.Value, Is.Null);
            Assert.That(Field<BooleanFieldData>(c4, "Active")?.Value, Is.False);
            Assert.That(Field<TextFieldData>(items[C("c5")], "Code")?.Value, Is.Null);
        }

        [Test]
        public async Task 既定の並びはIdの降順()
        {
            var ids = await IdsAsync("Customer", null);
            Assert.That(ids, Is.EqualTo(LiveFixture.Customers.Values.OrderByDescending(e => e).Select(e => e.ToString(CultureInfo.InvariantCulture))));
        }

        // ---------------------------------------------------------------- 検索 (期待値を書けるもの)

        [Test]
        public async Task 文字の一致は大文字小文字を区別し部分一致は区別しない()
        {
            Assert.That(await CustomerKeysAsync(Value("Code.Value", MatchComparison.Equal, "K-001")), Is.EquivalentTo(new[] { "c1", "c8" }));
            Assert.That(await CustomerKeysAsync(Value("Code.Value", MatchComparison.Equal, "k-001")), Is.Empty);
            Assert.That(await CustomerKeysAsync(Value("Code.Value", MatchComparison.Like, "k-00")), Is.EquivalentTo(new[] { "c1", "c2", "c3", "c6", "c7", "c8" }));
            Assert.That(await CustomerKeysAsync(Value("Memo.Value", MatchComparison.Like, "%_")), Is.EquivalentTo(new[] { "c1" }));
            Assert.That(await CustomerKeysAsync(Value("Name.Value", MatchComparison.Like, "O'Hara &")), Is.EquivalentTo(new[] { "c6" }));
        }

        [Test]
        public async Task 空の文字は空として扱う()
        {
            Assert.That(await CustomerKeysAsync(Value("Code.Value", MatchComparison.Equal, null)), Is.EquivalentTo(new[] { "c4", "c5" }));
            Assert.That(await CustomerKeysAsync(Not(Value("Code.Value", MatchComparison.Equal, null))), Is.EquivalentTo(new[] { "c1", "c2", "c3", "c6", "c7", "c8" }));
            Assert.That(await CustomerKeysAsync(Value("Memo.Value", MatchComparison.Equal, null)), Is.EquivalentTo(new[] { "c3", "c4", "c7" }));
        }

        [Test]
        public async Task 数値の比較()
        {
            Assert.That(await CustomerKeysAsync(Value("Credit.Value", MatchComparison.Equal, 10.5m)), Is.EquivalentTo(new[] { "c5", "c8" }));
            Assert.That(await CustomerKeysAsync(Value("Credit.Value", MatchComparison.Equal, 0m)), Is.EquivalentTo(new[] { "c2", "c4" }));
            Assert.That(await CustomerKeysAsync(Value("Credit.Value", MatchComparison.LessThan, 0m)), Is.EquivalentTo(new[] { "c3" }));
            Assert.That(await CustomerKeysAsync(Value("Credit.Value", MatchComparison.GreaterThan, 10.5m)), Is.EquivalentTo(new[] { "c1", "c6", "c7" }));
            Assert.That(await CustomerKeysAsync(Value("Quantity.Value", MatchComparison.Equal, null)), Is.EquivalentTo(new[] { "c3", "c4", "c7" }));
            Assert.That(await CustomerKeysAsync(Value("Quantity.Value", MatchComparison.LessThan, 5m)), Is.EquivalentTo(new[] { "c2", "c6" }));
            Assert.That(await CustomerKeysAsync(Value("Status.Value", MatchComparison.In, new List<decimal> { 200m, 900m })), Is.EquivalentTo(new[] { "c2", "c3", "c5" }));
        }

        [Test]
        public async Task 日付と日時とチェックの比較()
        {
            Assert.That(await CustomerKeysAsync(Value("Since.Value", MatchComparison.Equal, new DateOnly(2026, 1, 15))), Is.EquivalentTo(new[] { "c1", "c5" }));
            Assert.That(await CustomerKeysAsync(Value("Since.Value", MatchComparison.LessThan, new DateOnly(2026, 1, 15))), Is.EquivalentTo(new[] { "c6" }));
            Assert.That(await CustomerKeysAsync(Value("Since.Value", MatchComparison.Equal, null)), Is.EquivalentTo(new[] { "c4", "c7" }));
            Assert.That(await CustomerKeysAsync(Value("LastContact.Value", MatchComparison.Equal, new DateTime(2026, 11, 1, 9, 30, 15))), Is.EquivalentTo(new[] { "c1", "c8" }));
            Assert.That(await CustomerKeysAsync(Value("LastContact.Value", MatchComparison.GreaterThan, new DateTime(2026, 11, 1, 9, 30, 15))), Is.EquivalentTo(new[] { "c2", "c5" }));
            Assert.That(await CustomerKeysAsync(Value("Active.Value", MatchComparison.Equal, true)), Is.EquivalentTo(new[] { "c1", "c3", "c6", "c8" }));
            Assert.That(await CustomerKeysAsync(Value("Active.Value", MatchComparison.Equal, false)), Is.EquivalentTo(new[] { "c2", "c4", "c5", "c7" }));
        }

        [Test]
        public async Task 期限付きテーブルの期限と作業量()
        {
            var due = await IdsAsync("Task", Value("Due.Value", MatchComparison.Equal, new DateTime(2026, 11, 20)));
            Assert.That(due, Is.EqualTo(new[] { LiveFixture.Tasks["t1"].ToString(CultureInfo.InvariantCulture) }));
            var remaining = await IdsAsync("Task", Value("Remaining.Value", MatchComparison.Equal, 4m));
            Assert.That(remaining, Is.EquivalentTo(new[] { LiveFixture.Tasks["t1"], LiveFixture.Tasks["t2"] }.Select(e => e.ToString(CultureInfo.InvariantCulture))));
            //残りの作業量 (計算される列) の条件は View に書かず (書くとプリザンターがエラー)、メモリで評価する。並びはプリザンターで並べる: t3 (0) → t1 (4) / t2 (4) はキーの昇順
            var sorted = await IdsAsync("Task", null, new SortCondition { Variable = "Remaining.Value" }, new SortCondition { Variable = "Id.Value" });
            Assert.That(sorted, Is.EqualTo(new[] { "t3", "t1", "t2" }.Select(e => LiveFixture.Tasks[e].ToString(CultureInfo.InvariantCulture))));
        }

        // ---------------------------------------------------------------- ページング・件数

        [Test]
        public async Task 一度に返る上限を超えるページと件数()
        {
            var all = await _h.IO.GetListAsync(new SearchCondition("Many") { LimitCount = 300 }, 0);
            Assert.That(all.Items, Has.Count.EqualTo(LiveFixture.ManyCount));
            Assert.That(all.Items.Select(e => Field<NumberFieldData>(e, "No")!.Value), Is.EqualTo(Enumerable.Range(0, LiveFixture.ManyCount).Reverse().Select(e => (decimal?)e)));

            var page = await _h.IO.GetListAsync(new SearchCondition("Many") { LimitCount = 50 }, 2);
            Assert.That(page.Items.Select(e => Field<NumberFieldData>(e, "No")!.Value), Is.EqualTo(Enumerable.Range(0, LiveFixture.ManyCount).Reverse().Skip(100).Take(50).Select(e => (decimal?)e)));

            //メモリで評価する条件 (<>) でも同じ並びとページ
            var notEqual = await _h.IO.GetListAsync(new SearchCondition("Many") { LimitCount = 50, Condition = Value("No.Value", MatchComparison.NotEqual, 204m) }, 0);
            Assert.That(notEqual.Items.Select(e => Field<NumberFieldData>(e, "No")!.Value), Is.EqualTo(Enumerable.Range(0, 204).Reverse().Take(50).Select(e => (decimal?)e)));
        }

        [Test]
        public void MaxRowsを超える読み出しはエラー()
        {
            var h = new LiveHarness(edit: e => e.MaxRows = 100);
            var e = Assert.ThrowsAsync<PleasanterException>(() => h.IO.GetListAsync(new SearchCondition("Many") { LimitCount = 300, Condition = Value("No.Value", MatchComparison.NotEqual, 1m) }, 0))!;
            Assert.That(e.Message, Does.Contain("MaxRows"));
        }

        // ---------------------------------------------------------------- 結合・一覧をまたぐ条件

        [Test]
        public async Task リンクの表示名を結合で読み結合先の列で絞り並べる()
        {
            var items = (await _h.IO.GetListAsync(new SearchCondition("Order"), 0)).Items.ToDictionary(Id);
            Assert.That(Field<LinkFieldData>(items[O("o1")], "Customer")!.DisplayText, Is.EqualTo("株式会社アルファ"));
            Assert.That(Field<LinkFieldData>(items[O("o6")], "Customer")!.DisplayText, Is.Null.Or.Empty);

            var ids = await IdsAsync("Order", Value("Customer.Name.Value", MatchComparison.Like, "株式会社"));
            Assert.That(ids, Is.EquivalentTo(new[] { O("o1"), O("o2") }));
            var none = await IdsAsync("Order", Value("Customer.Name.Value", MatchComparison.Equal, null));
            Assert.That(none, Is.EquivalentTo(new[] { O("o5"), O("o6") }));

            var sorted = await IdsAsync("Order", Value("Amount.Value", MatchComparison.GreaterThanOrEqual, 1000m), new SortCondition { Variable = "Customer.Name.Value" });
            //メモリで並べる (文字は序数の順)。同じ名前はキーの昇順
            Assert.That(sorted, Is.EqualTo(new[] { O("o7"), O("o9"), O("o4"), O("o1"), O("o2") }));
        }

        [Test]
        public async Task 一覧をまたぐ条件()
        {
            Assert.That(await CustomerKeysAsync(Value("Orders.Amount.Value", MatchComparison.GreaterThan, 1000m)), Is.EquivalentTo(new[] { "c1", "c3", "c6", "c8" }));
            Assert.That(await CustomerKeysAsync(Not(Value("Orders.Amount.Value", MatchComparison.GreaterThan, 1000m))), Is.EquivalentTo(new[] { "c2", "c4", "c5", "c7" }));
            Assert.That(await CustomerKeysAsync(Value("Orders.Kind.Value", MatchComparison.Equal, "特急")), Is.EquivalentTo(new[] { "c1", "c8" }));
        }

        [Test]
        public async Task リンクの値で探す()
        {
            var ids = await IdsAsync("Order", Value("Customer.Value", MatchComparison.Equal, C("c6")));
            Assert.That(ids, Is.EquivalentTo(new[] { O("o7"), O("o8") }));
            var inIds = await IdsAsync("Order", Value("Customer.Value", MatchComparison.In, new List<string> { C("c2"), C("c3") }));
            Assert.That(inIds, Is.EquivalentTo(new[] { O("o3"), O("o4") }));
        }
    }
}
