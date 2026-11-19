using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>
    /// 本体の日付の軸 (DateGroup) の集計。本体は PostgreSQL の丸めの式 (date_trunc) を書き、コネクターはそれをメモリで同じ意味に計算する。
    /// </summary>
    public class AggregateDateBucketTest
    {
        FakeHarness _h = null!;

        [SetUp]
        public void SetUp()
        {
            _h = new FakeHarness(editDesign: d =>
            {
                //UTC で保存した日時として集計する項目 (同じ列 DateB)
                d.Modules.Find("Customer")!.Fields.Add(new DateTimeFieldDesign { Name = "LastContactUtc", DbColumn = "DateB", SaveAsUtc = true });
            });
            _h.Fake.Rows[FakeHarness.CustomersId] = new()
            {
                Customer(1, "2026-03-31T00:00:00", "2026-03-31T23:30:00"),
                //日付だけの列に時刻の入った値
                Customer(2, "2026-04-01T10:00:00", "2026-04-01T00:10:00"),
                //2026-01-04 は日曜 (週は月曜始まりなので 2025-12-29 の週)
                Customer(3, "2026-01-04T00:00:00", "2026-01-05T09:15:00"),
                Customer(4, "1899-12-30T00:00:00", "1899-12-30T00:00:00"),
            };
        }

        static JsonObject Customer(long id, string since, string last) => new()
        {
            ["SiteId"] = FakeHarness.CustomersId,
            ["ResultId"] = id,
            ["Ver"] = 1,
            ["Title"] = "C" + id,
            ["DateHash"] = new JsonObject { ["DateA"] = since, ["DateB"] = last },
        };

        async Task<List<(object? Key, decimal Count)>> AggregateAsync(string variable, DateBucket bucket, int fiscalYearStartMonth = 1, int? utcOffsetMinutes = null)
        {
            var condition = new AggregateCondition("Customer") { UtcOffsetMinutes = utcOffsetMinutes };
            condition.Groups.Add(new DateGroup { Variable = variable, Bucket = bucket, FiscalYearStartMonth = fiscalYearStartMonth });
            condition.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            var result = await _h.IO.AggregateAsync(condition);
            return result.Rows.Select(e => (e.Key(0), e.Number(0) ?? 0)).ToList();
        }

        static DateOnly D(int y, int m, int d) => new(y, m, d);

        [Test]
        public async Task 月()
            => Assert.That(await AggregateAsync("Since.Value", DateBucket.Month), Is.EqualTo(new List<(object?, decimal)>
            {
                (D(2026, 1, 1), 1), (D(2026, 3, 1), 1), (D(2026, 4, 1), 1), (null, 1),
            }));

        [Test]
        public async Task 週は月曜始まり()
            => Assert.That(await AggregateAsync("Since.Value", DateBucket.Week), Is.EqualTo(new List<(object?, decimal)>
            {
                (D(2025, 12, 29), 1), (D(2026, 3, 30), 2), (null, 1),
            }));

        [Test]
        public async Task 日付だけの列の時刻の入った値はその日()
            => Assert.That(await AggregateAsync("Since.Value", DateBucket.Day), Is.EqualTo(new List<(object?, decimal)>
            {
                (D(2026, 1, 4), 1), (D(2026, 3, 31), 1), (D(2026, 4, 1), 1), (null, 1),
            }));

        [Test]
        public async Task 四半期と年は年度の開始月で切る()
        {
            Assert.That(await AggregateAsync("Since.Value", DateBucket.Quarter, 4), Is.EqualTo(new List<(object?, decimal)>
            {
                (D(2026, 1, 1), 2), (D(2026, 4, 1), 1), (null, 1),
            }));
            Assert.That(await AggregateAsync("Since.Value", DateBucket.Year, 4), Is.EqualTo(new List<(object?, decimal)>
            {
                (D(2025, 4, 1), 2), (D(2026, 4, 1), 1), (null, 1),
            }));
            Assert.That(await AggregateAsync("Since.Value", DateBucket.Year), Is.EqualTo(new List<(object?, decimal)>
            {
                (D(2026, 1, 1), 3), (null, 1),
            }));
        }

        [Test]
        public async Task 時は日時()
            => Assert.That(await AggregateAsync("LastContact.Value", DateBucket.Hour), Is.EqualTo(new List<(object?, decimal)>
            {
                (new DateTime(2026, 1, 5, 9, 0, 0), 1), (new DateTime(2026, 3, 31, 23, 0, 0), 1), (new DateTime(2026, 4, 1, 0, 0, 0), 1), (null, 1),
            }));

        [Test]
        public async Task UTCで保存した日時は時差を足してから丸める()
            => Assert.That(await AggregateAsync("LastContactUtc.Value", DateBucket.Day, utcOffsetMinutes: 540), Is.EqualTo(new List<(object?, decimal)>
            {
                (D(2026, 1, 5), 1), (D(2026, 4, 1), 2), (null, 1),
            }));

        [Test]
        public async Task 負の時差()
            => Assert.That(await AggregateAsync("LastContactUtc.Value", DateBucket.Day, utcOffsetMinutes: -60), Is.EqualTo(new List<(object?, decimal)>
            {
                (D(2026, 1, 5), 1), (D(2026, 3, 31), 2), (null, 1),
            }));

        [Test]
        public async Task 日付の軸で選んだグループの中を別の軸で集計する()
        {
            //件数の多い上位 1 つの週 (2026-03-30 の週) の中を日でまとめる
            var within = new AggregateCondition("Customer") { LimitCount = 1 };
            within.Groups.Add(new DateGroup { Variable = "Since.Value", Bucket = DateBucket.Week });
            within.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            within.SortConditions.Add(new AggregateSort { Target = AggregateSortTarget.Measure, Index = 0, IsDescending = true });
            var condition = new AggregateCondition("Customer") { WithinGroupsOf = within };
            condition.Groups.Add(new DateGroup { Variable = "Since.Value", Bucket = DateBucket.Day });
            condition.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });

            var result = await _h.IO.AggregateAsync(condition);

            Assert.That(result.Rows.Select(e => (e.Key(0), e.Number(0))), Is.EqualTo(new[] { ((object?)D(2026, 3, 31), (decimal?)1m), (D(2026, 4, 1), 1m) }));
        }
    }
}
