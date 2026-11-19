using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>実物のプリザンターの行を本体の集計で数える (集計はコネクターのメモリで行う)。</summary>
    public class LiveAggregateTest
    {
        [Test]
        public async Task リンクの軸で件数と合計を集計し表示名を付ける()
        {
            var h = new LiveHarness();
            var condition = new AggregateCondition("Order");
            condition.Groups.Add(new ValueGroup { Variable = "Customer.Value" });
            condition.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            condition.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Amount.Value" });

            var result = await h.IO.AggregateAsync(condition);

            var rows = result.Rows.ToDictionary(e => e.Key(0)?.ToString() ?? "(null)");
            string C(string key) => LiveFixture.Customers[key].ToString(CultureInfo.InvariantCulture);
            Assert.That(rows[C("c1")].Number(0), Is.EqualTo(2));
            Assert.That(rows[C("c1")].Number(1), Is.EqualTo(3500.5m));
            Assert.That(rows[C("c1")].Keys[0].DisplayText, Is.EqualTo("株式会社アルファ"));
            Assert.That(rows[C("c6")].Number(1), Is.EqualTo(12050m));
            Assert.That(rows["(null)"].Number(0), Is.EqualTo(1));
            Assert.That(rows["99999999"].Number(0), Is.EqualTo(1));
            Assert.That(result.TotalCount, Is.EqualTo(9));
        }

        [Test]
        public async Task 軸なしの集計と条件つきの集計()
        {
            var h = new LiveHarness();
            var condition = new AggregateCondition("Customer") { Condition = Value("Active.Value", MatchComparison.Equal, true) };
            condition.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            condition.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Credit.Value" });
            condition.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Max, Variable = "Quantity.Value" });

            var result = await h.IO.AggregateAsync(condition);

            //有効 = c1 / c3 / c6 / c8
            var row = result.Rows.Single();
            Assert.That(row.Number(0), Is.EqualTo(4));
            Assert.That(row.Number(1), Is.EqualTo(500000m - 3.25m + 1234567.89m + 10.5m));
            Assert.That(row.Number(2), Is.EqualTo(10m));
        }

        [Test]
        public async Task 日付の軸で集計する()
        {
            var h = new LiveHarness();
            var byMonth = new AggregateCondition("Order");
            byMonth.Groups.Add(new DateGroup { Variable = "OrderDate.Value", Bucket = DateBucket.Month });
            byMonth.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            byMonth.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Amount.Value" });
            var months = (await h.IO.AggregateAsync(byMonth)).Rows.Select(e => (e.Key(0), e.Number(0), e.Number(1))).ToList();
            Assert.That(months, Is.EqualTo(new List<(object?, decimal?, decimal?)>
            {
                (new DateOnly(2026, 9, 1), 1m, 50m),
                (new DateOnly(2026, 10, 1), 1m, 15000m),
                (new DateOnly(2026, 11, 1), 6m, 1000m + 2500.5m + 0m + 700m + 12000m + 99999m),
                (null, 1m, 300m),
            }));

            //c3 の登録日は 3/10 15:45 (日付だけの列に時刻の入った値)。年度は 4 月始まり
            var byQuarter = new AggregateCondition("Customer");
            byQuarter.Groups.Add(new DateGroup { Variable = "Since.Value", Bucket = DateBucket.Quarter, FiscalYearStartMonth = 4 });
            byQuarter.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            var quarters = (await h.IO.AggregateAsync(byQuarter)).Rows.Select(e => (e.Key(0), e.Number(0))).ToList();
            Assert.That(quarters, Is.EqualTo(new List<(object?, decimal?)>
            {
                (new DateOnly(2025, 10, 1), 1m), (new DateOnly(2026, 1, 1), 4m), (new DateOnly(2026, 4, 1), 1m), (null, 2m),
            }));

            var byDay = new AggregateCondition("Customer") { Condition = Value("Since.Value", MatchComparison.Equal, new DateOnly(2026, 3, 10)) };
            byDay.Groups.Add(new DateGroup { Variable = "Since.Value", Bucket = DateBucket.Day });
            byDay.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            Assert.That((await h.IO.AggregateAsync(byDay)).Rows.Select(e => (e.Key(0), e.Number(0))), Is.EqualTo(new[] { ((object?)new DateOnly(2026, 3, 10), (decimal?)1m) }));
        }
    }
}
