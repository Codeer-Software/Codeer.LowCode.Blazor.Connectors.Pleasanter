using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Text.Json.Nodes;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>
    /// 本体の ModuleDataIO が組む SQL が、どんな一覧の要求 (View) になるかを偽物のプリザンターで確かめる。
    /// 偽物は条件で絞らない (View を書けば絞られる前提)。メモリで評価する道では、偽物が返した行をコネクターが絞る。
    /// </summary>
    public class ViewMappingTest
    {
        FakeHarness _h = null!;

        [SetUp]
        public void SetUp()
        {
            _h = new FakeHarness();
            _h.Fake.Rows[FakeHarness.CustomersId] = new()
            {
                Customer(1, "アルファ", "K-001", 10m, "2026-01-15T00:00:00", true),
                Customer(2, "ベータ", "K-002", 0m, "1899-12-30T00:00:00", false),
                Customer(3, "ガンマ", null, 5.5m, null, null),
            };
        }

        static JsonObject Customer(long id, string title, string? code, decimal credit, string? since, bool? active)
        {
            var row = new JsonObject
            {
                ["SiteId"] = FakeHarness.CustomersId,
                ["ResultId"] = id,
                ["Ver"] = 1,
                ["Title"] = title,
                ["Body"] = string.Empty,
                ["Status"] = 100,
                ["Manager"] = 0,
                ["UpdatedTime"] = "2026-11-01T10:00:00",
                ["ClassHash"] = new JsonObject { ["ClassA"] = code ?? string.Empty, ["ClassB"] = string.Empty },
                ["NumHash"] = new JsonObject { ["NumA"] = credit },
                ["DateHash"] = new JsonObject { ["DateA"] = since ?? "1899-12-30T00:00:00", ["DateB"] = "1899-12-30T00:00:00" },
                ["DescriptionHash"] = new JsonObject { ["DescriptionA"] = string.Empty },
            };
            if (active != null) row["CheckHash"] = new JsonObject { ["CheckA"] = active };
            return row;
        }

        async Task<List<ModuleData>> ListAsync(string module, MatchConditionBase? condition, params SortCondition[] sorts)
        {
            var search = new SearchCondition(module) { Condition = condition };
            search.SortConditions.AddRange(sorts);
            return (await _h.IO.GetListAsync(search, 0)).Items;
        }

        /// <summary>行を読む要求 (件数だけの要求 = PageSize 1 を除く)。</summary>
        List<RecordedRequest> RowRequests(long siteId) => _h.Fake.GetRequests(siteId).Where(e => e.Json["PageSize"]?.GetValue<int>() != 1).ToList();

        [Test]
        public async Task 文字の一致はExactMatchMultiple()
        {
            var items = await ListAsync("Customer", Value("Code.Value", MatchComparison.Equal, "K-001"));

            var request = RowRequests(FakeHarness.CustomersId).Single();
            Assert.That(request.Filter, Is.EqualTo(new Dictionary<string, string> { ["ClassA"] = "[\"K-001\"]" }));
            Assert.That(request.SearchTypes, Is.EqualTo(new Dictionary<string, string> { ["ClassA"] = "ExactMatchMultiple" }));
            //並びの指定が無ければ本体が Id の降順を付ける (キーの列が並びにあるのでキーの昇順は足さない)
            Assert.That(request.Sorter, Is.EqualTo(new[] { "ResultId desc" }));
            //偽物は絞らないので 3 行返る (正確な条件ならコネクターは絞り直さない)
            Assert.That(items, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task 部分一致はPartialMatchMultiple()
        {
            await ListAsync("Customer", Value("Memo.Value", MatchComparison.Like, "abc"));
            var request = RowRequests(FakeHarness.CustomersId).Single();
            Assert.That(request.Filter, Is.EqualTo(new Dictionary<string, string> { ["DescriptionA"] = "[\"abc\"]" }));
            Assert.That(request.SearchTypes["DescriptionA"], Is.EqualTo("PartialMatchMultiple"));
        }

        [Test]
        public async Task 数値と日付の範囲()
        {
            await ListAsync("Customer", MultiMatchCondition.And(
                Value("Credit.Value", MatchComparison.GreaterThanOrEqual, 5.5m),
                Value("Since.Value", MatchComparison.LessThanOrEqual, new DateOnly(2026, 2, 1))));
            var filter = RowRequests(FakeHarness.CustomersId).Single().Filter;
            Assert.That(filter.Keys, Is.EquivalentTo(new[] { "and_1", "and_2" }));
            //and_n の値は入れ子の JSON の文字 ({"列":"値の並びの JSON"})
            var leaves = filter.Values.Select(e => JsonNode.Parse(e)!.AsObject().Single()).ToDictionary(e => e.Key, e => e.Value!.GetValue<string>());
            Assert.That(leaves, Is.EqualTo(new Dictionary<string, string> { ["NumA"] = "[\"5.5,\"]", ["DateA"] = "[\",2026-02-01T23:59:59\"]" }));
        }

        [Test]
        public async Task 同じ列の一致のorは1つの葉()
        {
            await ListAsync("Customer", MultiMatchCondition.Or(Value("Code.Value", MatchComparison.Equal, "K-001"), Value("Code.Value", MatchComparison.Equal, "K-002")));
            Assert.That(RowRequests(FakeHarness.CustomersId).Single().Filter, Is.EqualTo(new Dictionary<string, string> { ["ClassA"] = "[\"K-001\",\"K-002\"]" }));
        }

        [Test]
        public async Task 否定は送らずに全部読んでメモリで絞る()
        {
            var items = await ListAsync("Customer", Not(Value("Code.Value", MatchComparison.Equal, "K-001")));

            Assert.That(RowRequests(FakeHarness.CustomersId).Single().Filter, Is.Empty);
            //SQL の 3 値の論理: Code が空 (null) の行は not (= 'K-001') に合わない
            Assert.That(items.Select(Id), Is.EqualTo(new[] { "2" }));
        }

        [Test]
        public async Task 並びの最後にキーの昇順を足す()
        {
            await ListAsync("Customer", null, new SortCondition { Variable = "Credit.Value" });
            Assert.That(RowRequests(FakeHarness.CustomersId).Single().Sorter, Is.EqualTo(new[] { "NumA asc", "ResultId asc" }));
        }

        [Test]
        public async Task 未満は以下で送りメモリで絞る()
        {
            var items = await ListAsync("Customer", Value("Credit.Value", MatchComparison.LessThan, 5.5m));
            Assert.That(RowRequests(FakeHarness.CustomersId).Single().Filter, Is.EqualTo(new Dictionary<string, string> { ["NumA"] = "[\",5.5\"]" }));
            Assert.That(items.Select(Id), Is.EquivalentTo(new[] { "2" }));
        }

        [Test]
        public async Task 計算される列の条件はViewに書かない()
        {
            _h.Fake.Rows[FakeHarness.TasksId] = new()
            {
                new JsonObject { ["IssueId"] = 7, ["Title"] = "a", ["WorkValue"] = 8, ["ProgressRate"] = 50, ["RemainingWorkValue"] = 4 },
                new JsonObject { ["IssueId"] = 8, ["Title"] = "b", ["WorkValue"] = 8, ["ProgressRate"] = 0, ["RemainingWorkValue"] = 8 },
            };
            var items = await ListAsync("Task", Value("Remaining.Value", MatchComparison.Equal, 4m));
            Assert.That(RowRequests(FakeHarness.TasksId).Single().Filter, Is.Empty);
            Assert.That(items.Select(Id), Is.EqualTo(new[] { "7" }));
        }

        [Test]
        public async Task 表示用のタイトルがTitleでないサイトのTitleの条件はメモリで評価する()
        {
            var settings = TestDesign.CustomersSettings();
            settings["TitleColumns"] = new JsonArray("Title", "ClassA");
            _h.Fake.Sites[FakeHarness.CustomersId] = TestDesign.SiteData(FakeHarness.CustomersId, "Results", settings);

            var items = await ListAsync("Customer", Value("Name.Value", MatchComparison.Equal, "ベータ"));
            Assert.That(RowRequests(FakeHarness.CustomersId).Single().Filter, Is.Empty);
            Assert.That(items.Select(Id), Is.EqualTo(new[] { "2" }));
        }

        [Test]
        public async Task 空の日付とチェックの無い行を読む()
        {
            var items = await ListAsync("Customer", null, new SortCondition { Variable = "Id.Value" });
            var byId = items.ToDictionary(Id);
            Assert.That(Field<DateFieldData>(byId["1"], "Since")!.Value, Is.EqualTo(new DateOnly(2026, 1, 15)));
            Assert.That(Field<DateFieldData>(byId["2"], "Since")?.Value, Is.Null, "空の日付 (1899-12-30) は null");
            Assert.That(Field<DateTimeFieldData>(byId["1"], "LastContact")?.Value, Is.Null);
            Assert.That(Field<BooleanFieldData>(byId["1"], "Active")!.Value, Is.True);
            Assert.That(Field<BooleanFieldData>(byId["3"], "Active")?.Value, Is.False, "チェックの無い行は false");
            Assert.That(Field<TextFieldData>(byId["3"], "Code")?.Value, Is.Null, "空の文字は null");
            Assert.That(Field<NumberFieldData>(byId["3"], "Credit")!.Value, Is.EqualTo(5.5m));
            Assert.That(Field<NumberFieldData>(byId["1"], "Status")!.Value, Is.EqualTo(100m));
        }

        [Test]
        public async Task メモリで評価するときは全部のページを読む()
        {
            _h.Fake.Rows[FakeHarness.ManyId] = Enumerable.Range(0, 450).Select(i => new JsonObject { ["ResultId"] = 1000 + i, ["Title"] = "m" + i, ["NumHash"] = new JsonObject { ["NumA"] = i } }).ToList();

            var items = await ListAsync("Many", Value("No.Value", MatchComparison.LessThan, 5m));

            Assert.That(RowRequests(FakeHarness.ManyId).Select(e => e.Json["Offset"]!.GetValue<int>()), Is.EqualTo(new[] { 0, 200, 400 }));
            Assert.That(items, Has.Count.EqualTo(5));
        }

        [Test]
        public void 上限を超える行は黙って切り捨てない()
        {
            var h = new FakeHarness(e => e.MaxRows = 100);
            h.Fake.Rows[FakeHarness.ManyId] = Enumerable.Range(0, 450).Select(i => new JsonObject { ["ResultId"] = 1000 + i, ["Title"] = "m" + i, ["NumHash"] = new JsonObject { ["NumA"] = i } }).ToList();
            var e = Assert.ThrowsAsync<PleasanterException>(() => h.IO.GetListAsync(new SearchCondition("Many") { Condition = Value("No.Value", MatchComparison.LessThan, 5m) }, 0))!;
            Assert.That(e.Message, Does.Contain("MaxRows"));
        }

        [Test]
        public async Task 更新はサイトのURLで行を確かめてから空の値を種類ごとに書く()
        {
            var update = new ModuleData { Name = "Customer" };
            update.Fields["Id"] = new IdFieldData { Value = "1" };
            update.Fields["Body"] = new TextFieldData { Value = null };
            update.Fields["Code"] = new TextFieldData { Value = null };
            update.Fields["Credit"] = new NumberFieldData { Value = null };
            update.Fields["Status"] = new NumberFieldData { Value = null };
            update.Fields["Since"] = new DateFieldData { Value = null };
            update.Fields["Active"] = new BooleanFieldData { Value = null };
            _h.Fake.Override(e => e.Action == "get", _ => FakePleasanter.Json(System.Net.HttpStatusCode.OK,
                "{\"StatusCode\":200,\"Response\":{\"Offset\":0,\"PageSize\":1,\"TotalCount\":1,\"Data\":[]}}"));

            var result = (await _h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Customer", Id = "1", Update = new() { update } } })).Single();

            Assert.That(result.ExceptionMessage, Is.Empty, result.ExceptionMessage);
            var check = _h.Fake.GetRequests(FakeHarness.CustomersId).Single();
            Assert.That(check.Filter, Is.EqualTo(new Dictionary<string, string> { ["ResultId"] = "[\"1,1\"]" }));
            var write = _h.Fake.WriteRequests.Single();
            Assert.That(write.Path, Is.EqualTo("/api/items/1/update"));
            var body = write.Json;
            Assert.That(body["Body"]!.GetValue<string>(), Is.EqualTo(string.Empty));
            Assert.That(body["Status"]!.GetValue<decimal>(), Is.EqualTo(0m));
            Assert.That(body["ClassHash"]!["ClassA"]!.GetValue<string>(), Is.EqualTo(string.Empty));
            Assert.That(body["NumHash"]!.AsObject().ContainsKey("NumA") && body["NumHash"]!["NumA"] == null, Is.True);
            Assert.That(body["DateHash"]!["DateA"]!.GetValue<string>(), Is.EqualTo("1899-12-30T00:00:00"));
            Assert.That(body["CheckHash"]!["CheckA"]!.GetValue<bool>(), Is.False);
        }

        [Test]
        public async Task サイトに無い行は更新も削除も送らない()
        {
            _h.Fake.Override(e => e.Action == "get", _ => FakePleasanter.Json(System.Net.HttpStatusCode.OK,
                "{\"StatusCode\":200,\"Response\":{\"Offset\":0,\"PageSize\":1,\"TotalCount\":0,\"Data\":[]}}"), 2);
            var update = await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "update \"Customers\" set \"Title\"=@p0 where \"ResultId\"=@pid0", new() { ["@p0"] = "x", ["@pid0"] = 5L });
            var delete = await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "delete from \"Customers\" where \"ResultId\"=@pid0", new() { ["@pid0"] = 5L });
            Assert.That((update, delete), Is.EqualTo((0, 0)));
            Assert.That(_h.Fake.WriteRequests, Is.Empty);
        }

        [Test]
        public async Task 作成は拡張の列を入れ物に入れて送り作った行のIdを返す()
        {
            var id = await _h.Accessor.InsertAsync(TestDesign.DataSourceName,
                "insert into \"Customers\"(\"Title\",\"ClassA\",\"NumA\",\"CheckA\") values(@p0,@p1,@p2,@p3) returning \"ResultId\"",
                new() { ["@p0"] = "新規", ["@p1"] = "K-9", ["@p2"] = 3m, ["@p3"] = true });

            Assert.That(id?.ToString(), Is.EqualTo("1001"));
            var body = _h.Fake.WriteRequests.Single().Json;
            Assert.That(body["Title"]!.GetValue<string>(), Is.EqualTo("新規"));
            Assert.That(body["ClassHash"]!["ClassA"]!.GetValue<string>(), Is.EqualTo("K-9"));
            Assert.That(body["NumHash"]!["NumA"]!.GetValue<decimal>(), Is.EqualTo(3m));
            Assert.That(body["CheckHash"]!["CheckA"]!.GetValue<bool>(), Is.True);
        }

        [Test]
        public async Task 作成ではキー以外の読み取り専用の列に値があっても送らない()
        {
            //レコードのコピーは作成日時などを表示しているフィールドの値も写して作成に乗せる。プリザンターが自分で決める列なので送らない
            await _h.Accessor.InsertAsync(TestDesign.DataSourceName,
                "insert into \"Customers\"(\"Title\",\"Ver\",\"Creator\",\"Updator\",\"CreatedTime\",\"UpdatedTime\") values(@p0,@p1,@p2,@p3,@p4,@p5) returning \"ResultId\"",
                new() { ["@p0"] = "コピー", ["@p1"] = 3m, ["@p2"] = 7m, ["@p3"] = 8m, ["@p4"] = new DateTime(2020, 1, 2, 3, 4, 5), ["@p5"] = new DateTime(2021, 1, 2, 3, 4, 5) });
            await _h.Accessor.InsertAsync(TestDesign.DataSourceName,
                "insert into \"Tasks\"(\"Title\",\"WorkValue\",\"ProgressRate\",\"RemainingWorkValue\") values(@p0,@p1,@p2,@p3) returning \"IssueId\"",
                new() { ["@p0"] = "コピー", ["@p1"] = 10m, ["@p2"] = 20m, ["@p3"] = 99m });

            var bodies = _h.Fake.WriteRequests.Select(e => e.Json).ToList();
            Assert.That(bodies, Has.Count.EqualTo(2));
            Assert.That(bodies[0]["Title"]!.GetValue<string>(), Is.EqualTo("コピー"));
            foreach (var name in new[] { "Ver", "Creator", "Updator", "CreatedTime", "UpdatedTime" }) Assert.That(bodies[0].ContainsKey(name), Is.False, name);
            Assert.That(bodies[1]["WorkValue"]!.GetValue<decimal>(), Is.EqualTo(10m));
            Assert.That(bodies[1].ContainsKey("RemainingWorkValue"), Is.False);
        }

        [TestCase("Customers", "ResultId")]
        [TestCase("Tasks", "IssueId")]
        public void 作成でキーに値があればエラー(string table, string key)
        {
            var e = Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.InsertAsync(TestDesign.DataSourceName,
                $"insert into \"{table}\"(\"Title\",\"{key}\") values(@p0,@p1) returning \"{key}\"", new() { ["@p0"] = "x", ["@p1"] = 5L }))!;
            Assert.That(e.Message, Does.Contain("read-only"));
            Assert.That(_h.Fake.WriteRequests, Is.Empty);
        }

        [TestCase("Customers", "ResultId", "Ver", 5)]
        [TestCase("Customers", "ResultId", "Creator", 5)]
        [TestCase("Customers", "ResultId", "Updator", 5)]
        [TestCase("Customers", "ResultId", "CreatedTime", null)]
        [TestCase("Customers", "ResultId", "UpdatedTime", null)]
        [TestCase("Customers", "ResultId", "ResultId", 5)]
        [TestCase("Tasks", "IssueId", "RemainingWorkValue", 5)]
        public void 更新で読み取り専用の列を書くとエラー(string table, string key, string column, object? value)
        {
            //値が null (空にする) でもエラー
            var e = Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.ExecuteAsync(TestDesign.DataSourceName,
                $"update \"{table}\" set \"{column}\"=@p0 where \"{key}\"=@pid0", new() { ["@p0"] = value, ["@pid0"] = 1L }))!;
            Assert.That(e.Message, Does.Contain("read-only"));
            Assert.That(_h.Fake.WriteRequests, Is.Empty);
        }

        [TestCase("select \"ResultId\" from \"Customers\" union select \"ResultId\" from \"Orders\"")]
        [TestCase("select \"ResultId\" from \"Customers\" where \"Title\" like @p0 escape '\\'")]
        [TestCase("select \"ResultId\" from \"Customers\" where lower(\"Title\") = @p0")]
        [TestCase("select * from \"Customers\" c inner join \"Orders\" o on c.\"ResultId\" = o.\"ClassA\"")]
        public void 読み替えられないSQLはエラー(string sql)
            => Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.QueryAsync(TestDesign.DataSourceName, sql, new() { ["@p0"] = new ParamAndRawDbTypeName { Value = "%a" } }));

        [Test]
        public void SQLの直接実行はできない()
            => Assert.That(Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, new DbSqlCommand { CommandText = "delete from \"Customers\"" }))!.Message,
                Does.Contain("StoredProcedure"));
    }
}
