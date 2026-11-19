using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Text.Json.Nodes;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>コメントの列 (Comments): 読むと JSON の文字、書くと 1 件足す。条件・並び・集計には使えない。</summary>
    public class CommentsTest
    {
        const string CommentsJson = "[{\"CommentId\":2,\"CreatedTime\":\"2026-11-19T10:00:00\",\"Creator\":1,\"Body\":\"二つ目\"},{\"CommentId\":1,\"CreatedTime\":\"2026-11-18T10:00:00\",\"Creator\":1,\"Body\":\"一つ目\"}]";

        FakeHarness _h = null!;

        [SetUp]
        public void SetUp()
        {
            _h = new FakeHarness(editDesign: d => d.Modules.Find("Customer")!.Fields.Add(new TextFieldDesign { Name = "Comments", DbColumn = "Comments" }));
            _h.Fake.Rows[FakeHarness.CustomersId] = new()
            {
                new JsonObject { ["SiteId"] = FakeHarness.CustomersId, ["ResultId"] = 1, ["Ver"] = 1, ["Title"] = "アルファ", ["Comments"] = CommentsJson },
                new JsonObject { ["SiteId"] = FakeHarness.CustomersId, ["ResultId"] = 2, ["Ver"] = 1, ["Title"] = "ベータ", ["Comments"] = "[]" },
            };
        }

        [Test]
        public void 表の定義にいつもある()
            => Assert.That(_h.Connection.GetTableDefinitionAsync("Customers").Result.FindColumn("Comments")!.Kind, Is.EqualTo(PleasanterColumnKind.String));

        [Test]
        public async Task 読むとプリザンターが返すJSONの文字()
        {
            var items = (await _h.IO.GetListAsync(new SearchCondition("Customer"), 0)).Items.OrderBy(Id).ToList();
            Assert.That(Field<TextFieldData>(items[0], "Comments")!.Value, Is.EqualTo(CommentsJson));
            Assert.That(Field<TextFieldData>(items[1], "Comments")!.Value, Is.EqualTo("[]"));
        }

        [Test]
        public async Task 作成で空でない文字を送り空は送らない()
        {
            await _h.Accessor.InsertAsync(TestDesign.DataSourceName, "insert into \"Customers\"(\"Title\",\"Comments\") values(@p0,@p1) returning \"ResultId\"",
                new() { ["@p0"] = "新規", ["@p1"] = "最初のコメント" });
            await _h.Accessor.InsertAsync(TestDesign.DataSourceName, "insert into \"Customers\"(\"Title\",\"Comments\") values(@p0,@p1) returning \"ResultId\"",
                new() { ["@p0"] = "新規2", ["@p1"] = string.Empty });

            var bodies = _h.Fake.WriteRequests.Select(e => e.Json).ToList();
            Assert.That(bodies[0]["Comments"]!.GetValue<string>(), Is.EqualTo("最初のコメント"));
            Assert.That(bodies[1].ContainsKey("Comments"), Is.False);
        }

        [Test]
        public async Task 更新で空でない文字を送り空やnullだけなら更新しない()
        {
            await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "update \"Customers\" set \"Comments\"=@p0 where \"ResultId\"=@pid0", new() { ["@p0"] = "追記", ["@pid0"] = 1L });
            await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "update \"Customers\" set \"Comments\"=@p0 where \"ResultId\"=@pid0", new() { ["@p0"] = null, ["@pid0"] = 1L });
            await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "update \"Customers\" set \"Comments\"=@p0 where \"ResultId\"=@pid0", new() { ["@p0"] = string.Empty, ["@pid0"] = 1L });

            var update = _h.Fake.WriteRequests.Single();
            Assert.That(update.Action, Is.EqualTo("update"));
            Assert.That(update.Json["Comments"]!.GetValue<string>(), Is.EqualTo("追記"));
        }

        [Test]
        public void 条件と並びと集計には使えない()
        {
            Assert.ThrowsAsync<PleasanterException>(() => _h.IO.GetListAsync(new SearchCondition("Customer") { Condition = Value("Comments.Value", MatchComparison.Like, "一つ目") }, 0));
            var sorted = new SearchCondition("Customer");
            sorted.SortConditions.Add(new SortCondition { Variable = "Comments.Value" });
            Assert.ThrowsAsync<PleasanterException>(() => _h.IO.GetListAsync(sorted, 0));
            var aggregate = new AggregateCondition("Customer");
            aggregate.Groups.Add(new ValueGroup { Variable = "Comments.Value" });
            aggregate.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            Assert.ThrowsAsync<PleasanterException>(() => _h.IO.AggregateAsync(aggregate));
        }
    }
}
