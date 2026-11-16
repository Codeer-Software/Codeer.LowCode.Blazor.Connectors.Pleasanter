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
        public async Task 表の定義ファイルを実物から書き出せる()
        {
            var connection = new PleasanterConnection(LiveFixture.CreateSettings());
            var directory = Path.Combine(Path.GetTempPath(), "clb-pleasanter-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                await PleasanterMetadataFile.UpdateAsync(connection, directory);
                var path = PleasanterMetadataFile.GetPath(directory, "PL");
                Assert.That(File.Exists(path), Is.True);
                var text = await File.ReadAllTextAsync(path);
                Assert.That(text, Does.Contain("\"Customers\""));
                Assert.That(text, Does.Contain("ゴールド"));
                Assert.That(text, Does.Not.Contain(LiveFixture.Url.TrimEnd('/')), "接続先を書き出さない");
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
