using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>
    /// 同じデータで「View に写す道」と「条件を送らずに全部読んでメモリで評価する道」の結果 (行と順) が同じになるかを、条件の語彙ごとに確かめる。
    /// View の意味 (プリザンターの SQL) とメモリの評価 (SQL の 3 値の論理) の食い違いを見つける。
    /// </summary>
    public class LiveDifferentialTest
    {
        public record Case(string Label, string Module, Func<MatchConditionBase> Condition)
        {
            public override string ToString() => Label;
        }

        static string C(string key) => LiveFixture.Customers[key].ToString(CultureInfo.InvariantCulture);

        static MatchConditionBase And(params MatchConditionBase[] children) => MultiMatchCondition.And(children);
        static MatchConditionBase Or(params MatchConditionBase[] children) => MultiMatchCondition.Or(children);

        static IEnumerable<Case> Cases()
        {
            Case Customer(string label, Func<MatchConditionBase> condition) => new(label, "Customer", condition);

            //文字
            yield return Customer("code = K-001", () => Value("Code.Value", MatchComparison.Equal, "K-001"));
            yield return Customer("code = k-001 (大文字小文字)", () => Value("Code.Value", MatchComparison.Equal, "k-001"));
            yield return Customer("code in", () => Value("Code.Value", MatchComparison.In, new List<string> { "K-001", "K-002", "k-003" }));
            yield return Customer("code like K-0", () => Value("Code.Value", MatchComparison.Like, "K-0"));
            yield return Customer("code like k-0", () => Value("Code.Value", MatchComparison.Like, "k-0"));
            yield return Customer("code is null", () => Value("Code.Value", MatchComparison.Equal, null));
            yield return Customer("code is not null", () => Not(Value("Code.Value", MatchComparison.Equal, null)));
            yield return Customer("code <> K-001", () => Value("Code.Value", MatchComparison.NotEqual, "K-001"));
            yield return Customer("code not in", () => Not(Value("Code.Value", MatchComparison.In, new List<string> { "K-001", "K-002" })));
            yield return Customer("not code like K", () => Not(Value("Code.Value", MatchComparison.Like, "K")));
            yield return Customer("code < K-002", () => Value("Code.Value", MatchComparison.LessThan, "K-002"));
            yield return Customer("code = and like same column", () => And(Value("Code.Value", MatchComparison.Like, "K"), Value("Code.Value", MatchComparison.Equal, "K-001")));
            yield return Customer("name = アルファ", () => Value("Name.Value", MatchComparison.Equal, "株式会社アルファ"));
            yield return Customer("name like 株式会社", () => Value("Name.Value", MatchComparison.Like, "株式会社"));
            yield return Customer("name like '", () => Value("Name.Value", MatchComparison.Like, "'"));
            yield return Customer("memo like %_", () => Value("Memo.Value", MatchComparison.Like, "%_"));
            yield return Customer("memo like abc", () => Value("Memo.Value", MatchComparison.Like, "abc"));
            yield return Customer("rank = A", () => Value("Rank.Value", MatchComparison.Equal, "A"));
            yield return Customer("rank is null", () => Value("Rank.Value", MatchComparison.Equal, null));
            //プリザンターがユーザーの列の条件で読み替える値 "Own" (c1 は文字の Own、c2 は API キーの利用者の ID)
            yield return Customer("person = Own", () => Value("Person.Value", MatchComparison.Equal, "Own"));
            yield return Customer("person in (Own, 利用者)", () => Value("Person.Value", MatchComparison.In, new List<string> { "Own", LiveFixture.ApiUserId.ToString(CultureInfo.InvariantCulture) }));
            yield return Customer("person like Own", () => Value("Person.Value", MatchComparison.Like, "Own"));
            yield return Customer("person = 利用者", () => Value("Person.Value", MatchComparison.Equal, LiveFixture.ApiUserId.ToString(CultureInfo.InvariantCulture)));
            yield return Customer("person = Own or code = K-006", () => Or(Value("Person.Value", MatchComparison.Equal, "Own"), Value("Code.Value", MatchComparison.Equal, "K-006")));
            //数値
            yield return Customer("credit = 10.5", () => Value("Credit.Value", MatchComparison.Equal, 10.5m));
            yield return Customer("credit = 0", () => Value("Credit.Value", MatchComparison.Equal, 0m));
            yield return Customer("credit < 0", () => Value("Credit.Value", MatchComparison.LessThan, 0m));
            yield return Customer("credit > 10.5", () => Value("Credit.Value", MatchComparison.GreaterThan, 10.5m));
            yield return Customer("credit <= 10.5", () => Value("Credit.Value", MatchComparison.LessThanOrEqual, 10.5m));
            yield return Customer("credit >= 600000", () => Value("Credit.Value", MatchComparison.GreaterThanOrEqual, 600000m));
            yield return Customer("credit <> 0", () => Value("Credit.Value", MatchComparison.NotEqual, 0m));
            yield return Customer("credit in", () => Value("Credit.Value", MatchComparison.In, new List<decimal> { 10.5m, -3.25m }));
            yield return Customer("credit is null", () => Value("Credit.Value", MatchComparison.Equal, null));
            yield return Customer("quantity is null", () => Value("Quantity.Value", MatchComparison.Equal, null));
            yield return Customer("quantity is not null", () => Not(Value("Quantity.Value", MatchComparison.Equal, null)));
            yield return Customer("quantity = 0", () => Value("Quantity.Value", MatchComparison.Equal, 0m));
            yield return Customer("quantity >= 5", () => Value("Quantity.Value", MatchComparison.GreaterThanOrEqual, 5m));
            yield return Customer("quantity < 5", () => Value("Quantity.Value", MatchComparison.LessThan, 5m));
            yield return Customer("not quantity < 5", () => Not(Value("Quantity.Value", MatchComparison.LessThan, 5m)));
            yield return Customer("status = 200", () => Value("Status.Value", MatchComparison.Equal, 200m));
            yield return Customer("status in", () => Value("Status.Value", MatchComparison.In, new List<decimal> { 100m, 900m }));
            yield return Customer("manager = 0", () => Value("Manager.Value", MatchComparison.Equal, 0m));
            //日付・日時
            yield return Customer("since = 1/15", () => Value("Since.Value", MatchComparison.Equal, new DateOnly(2026, 1, 15)));
            yield return Customer("since >= 2/1", () => Value("Since.Value", MatchComparison.GreaterThanOrEqual, new DateOnly(2026, 2, 1)));
            yield return Customer("since < 2/1", () => Value("Since.Value", MatchComparison.LessThan, new DateOnly(2026, 2, 1)));
            yield return Customer("since is null", () => Value("Since.Value", MatchComparison.Equal, null));
            yield return Customer("since is not null", () => Not(Value("Since.Value", MatchComparison.Equal, null)));
            yield return Customer("since <> 1/15", () => Value("Since.Value", MatchComparison.NotEqual, new DateOnly(2026, 1, 15)));
            //日付だけの列に時刻の入った値 (c3 は 3/10 15:45)
            yield return Customer("since = 3/10 (時刻あり)", () => Value("Since.Value", MatchComparison.Equal, new DateOnly(2026, 3, 10)));
            yield return Customer("since <= 3/10 (時刻あり)", () => Value("Since.Value", MatchComparison.LessThanOrEqual, new DateOnly(2026, 3, 10)));
            yield return Customer("since < 3/10 (時刻あり)", () => Value("Since.Value", MatchComparison.LessThan, new DateOnly(2026, 3, 10)));
            yield return Customer("since >= 3/10 (時刻あり)", () => Value("Since.Value", MatchComparison.GreaterThanOrEqual, new DateOnly(2026, 3, 10)));
            yield return Customer("since > 3/10 (時刻あり)", () => Value("Since.Value", MatchComparison.GreaterThan, new DateOnly(2026, 3, 10)));
            yield return Customer("since > 3/9", () => Value("Since.Value", MatchComparison.GreaterThan, new DateOnly(2026, 3, 9)));
            yield return Customer("since in (3/10, 1/15)", () => Value("Since.Value", MatchComparison.In, new List<DateOnly> { new(2026, 3, 10), new(2026, 1, 15) }));
            yield return Customer("since <> 3/10 (時刻あり)", () => Value("Since.Value", MatchComparison.NotEqual, new DateOnly(2026, 3, 10)));
            yield return Customer("last = 11/1 9:30:15", () => Value("LastContact.Value", MatchComparison.Equal, new DateTime(2026, 11, 1, 9, 30, 15)));
            yield return Customer("last <= 11/2 23:59:59", () => Value("LastContact.Value", MatchComparison.LessThanOrEqual, new DateTime(2026, 11, 2, 23, 59, 59)));
            yield return Customer("last > 11/1 9:30:15", () => Value("LastContact.Value", MatchComparison.GreaterThan, new DateTime(2026, 11, 1, 9, 30, 15)));
            yield return Customer("last = 小数秒", () => Value("LastContact.Value", MatchComparison.Equal, new DateTime(2026, 11, 1, 9, 30, 15, 500)));
            yield return Customer("updated >= 昨日", () => Value("Updated.Value", MatchComparison.GreaterThanOrEqual, DateTime.Now.AddDays(-1)));
            yield return Customer("updated <= 明日", () => Value("Updated.Value", MatchComparison.LessThanOrEqual, DateTime.Now.AddDays(1)));
            //チェック
            yield return Customer("active = true", () => Value("Active.Value", MatchComparison.Equal, true));
            yield return Customer("active = false", () => Value("Active.Value", MatchComparison.Equal, false));
            yield return Customer("active <> true", () => Value("Active.Value", MatchComparison.NotEqual, true));
            //入れ子
            yield return Customer("(code = K-001 or credit < 0) and active", () => And(Or(Value("Code.Value", MatchComparison.Equal, "K-001"), Value("Credit.Value", MatchComparison.LessThan, 0m)), Value("Active.Value", MatchComparison.Equal, true)));
            yield return Customer("or of ands", () => Or(And(Value("Rank.Value", MatchComparison.Equal, "A"), Value("Status.Value", MatchComparison.Equal, 900m)), And(Value("Rank.Value", MatchComparison.Equal, "B"), Value("Active.Value", MatchComparison.Equal, true))));
            yield return Customer("not (code = K-001 and active)", () => Not(And(Value("Code.Value", MatchComparison.Equal, "K-001"), Value("Active.Value", MatchComparison.Equal, true))));
            yield return Customer("(rank = A or rank is null) and not credit = 0", () => And(Or(Value("Rank.Value", MatchComparison.Equal, "A"), Value("Rank.Value", MatchComparison.Equal, null)), Not(Value("Credit.Value", MatchComparison.Equal, 0m))));
            yield return Customer("same column range and", () => And(Value("Credit.Value", MatchComparison.GreaterThanOrEqual, 0m), Value("Credit.Value", MatchComparison.LessThanOrEqual, 600000m)));
            yield return Customer("or with inexact child", () => Or(Value("Code.Value", MatchComparison.Equal, "K-002"), Value("Credit.Value", MatchComparison.NotEqual, 0m)));
            //一覧をまたぐ条件
            yield return Customer("orders.amount > 1000", () => Value("Orders.Amount.Value", MatchComparison.GreaterThan, 1000m));
            yield return Customer("not orders.amount > 1000", () => Not(Value("Orders.Amount.Value", MatchComparison.GreaterThan, 1000m)));
            yield return Customer("orders.kind = 特急", () => Value("Orders.Kind.Value", MatchComparison.Equal, "特急"));
            //結合先の列
            yield return new Case("order customer.name like 株式会社", "Order", () => Value("Customer.Name.Value", MatchComparison.Like, "株式会社"));
            yield return new Case("order customer.name = zeta", "Order", () => Value("Customer.Name.Value", MatchComparison.Equal, "zeta"));
            yield return new Case("order customer.name is null", "Order", () => Value("Customer.Name.Value", MatchComparison.Equal, null));
            yield return new Case("order not customer.name like 株式会社", "Order", () => Not(Value("Customer.Name.Value", MatchComparison.Like, "株式会社")));
            yield return new Case("order customer = c1", "Order", () => Value("Customer.Value", MatchComparison.Equal, C("c1")));
            yield return new Case("order customer is null", "Order", () => Value("Customer.Value", MatchComparison.Equal, null));
            yield return new Case("order kind is null or amount = 0", "Order", () => Or(Value("Kind.Value", MatchComparison.Equal, null), Value("Amount.Value", MatchComparison.Equal, 0m)));
            yield return new Case("order date is null", "Order", () => Value("OrderDate.Value", MatchComparison.Equal, null));
            //期限付きテーブル
            yield return new Case("task due = 11/20", "Task", () => Value("Due.Value", MatchComparison.Equal, new DateTime(2026, 11, 20)));
            yield return new Case("task due >= 11/15", "Task", () => Value("Due.Value", MatchComparison.GreaterThanOrEqual, new DateTime(2026, 11, 15)));
            yield return new Case("task start < 11/5", "Task", () => Value("Start.Value", MatchComparison.LessThan, new DateTime(2026, 11, 5)));
            yield return new Case("task work >= 4", "Task", () => Value("Work.Value", MatchComparison.GreaterThanOrEqual, 4m));
            yield return new Case("task remaining = 4", "Task", () => Value("Remaining.Value", MatchComparison.Equal, 4m));
            yield return new Case("task progress < 50", "Task", () => Value("Progress.Value", MatchComparison.LessThan, 50m));
        }

        static async Task<List<string>> IdsAsync(LiveHarness h, Case c)
            => (await h.IO.GetListAsync(new SearchCondition(c.Module) { Condition = c.Condition() }, 0)).Items.Select(Id).ToList();

        [TestCaseSource(nameof(Cases))]
        public async Task Viewの道とメモリの道が同じ行を同じ順で返す(Case c)
        {
            var view = await IdsAsync(new LiveHarness(), c);
            var memory = await IdsAsync(new LiveHarness(forceMemory: true), c);
            Assert.That(view, Is.EqualTo(memory), c.Label);
        }

        //SQL に日時の値が直接来たとき (本体は条件の値を列の型の日付にしてから渡すので、本体を通さない呼び出し)
        [TestCase("=")]
        [TestCase("<=")]
        [TestCase("<")]
        [TestCase(">=")]
        [TestCase(">")]
        public async Task 日付だけの列を時刻のある日時で比べてもViewの道とメモリの道が同じ(string op)
        {
            async Task<List<string>> QueryAsync(LiveHarness h, DateTime value)
                => (await h.Accessor.QueryAsync(TestDesign.DataSourceName, $"select \"ResultId\" from \"Customers\" where \"DateA\" {op} @p0 order by \"ResultId\"",
                        new() { ["@p0"] = new Codeer.LowCode.Blazor.DataIO.Db.ParamAndRawDbTypeName { Value = value } }))
                    .Select(e => Convert.ToString(e["ResultId"], CultureInfo.InvariantCulture)!).ToList();
            foreach (var value in new[] { new DateTime(2026, 3, 10, 12, 0, 0), new DateTime(2026, 3, 10, 15, 45, 0), new DateTime(2026, 3, 10, 23, 0, 0), new DateTime(2026, 3, 10) })
            {
                var view = await QueryAsync(new LiveHarness(), value);
                var memory = await QueryAsync(new LiveHarness(forceMemory: true), value);
                Assert.That(view, Is.EqualTo(memory), $"{op} {value:s}");
            }
        }

        [Test]
        public async Task 件数とページもViewの道とメモリの道で同じ()
        {
            foreach (var c in Cases().Where(e => e.Module == "Customer"))
            {
                var search = new SearchCondition("Customer") { Condition = c.Condition(), LimitCount = 2 };
                var view = await new LiveHarness().IO.GetListAsync(search, 1);
                var memory = await new LiveHarness(forceMemory: true).IO.GetListAsync(search, 1);
                Assert.That(view.Items.Select(Id), Is.EqualTo(memory.Items.Select(Id)), c.Label);
                Assert.That(view.TotalCount, Is.EqualTo(memory.TotalCount), c.Label);
            }
        }
    }
}
