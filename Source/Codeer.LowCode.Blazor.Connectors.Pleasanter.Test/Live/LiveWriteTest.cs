using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;
using System.Text.Json.Nodes;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>
    /// 実物のプリザンター + 本物のコネクター + 本体の ModuleDataIO で、作成・更新・削除・空にする値・読み取り専用の列・楽観ロック・別のサイトの ID を確かめる。
    /// 結果は API で直接読み直して確かめる。行はテストの中で作る (入れてある行は変えない)。
    /// </summary>
    public class LiveWriteTest
    {
        LiveHarness _h = null!;

        [SetUp]
        public void SetUp() => _h = new LiveHarness();

        Task<List<ModuleSubmitResult>> SubmitAsync(ModuleSubmitData data) => _h.IO.SubmitWithTransactionAsync(new() { data });

        async Task<ModuleSubmitResult> AddAsync(string module, Action<ModuleData> fill)
        {
            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = module };
            data.Fields["Id"] = tempId;
            fill(data);
            return (await SubmitAsync(new ModuleSubmitData { ModuleName = module, Id = tempId.Value!, Add = new() { data } })).Single();
        }

        async Task<ModuleSubmitResult> UpdateAsync(string module, string id, Action<ModuleData> fill)
        {
            var data = new ModuleData { Name = module };
            data.Fields["Id"] = new IdFieldData { Value = id };
            fill(data);
            return (await SubmitAsync(new ModuleSubmitData { ModuleName = module, Id = id, Update = new() { data } })).Single();
        }

        async Task<ModuleSubmitResult> DeleteAsync(string module, string id, OptimisticLockingFieldData? locking = null)
            => (await SubmitAsync(new ModuleSubmitData { ModuleName = module, Id = id, Delete = new() { new ModuleDeleteInfo { ModuleName = module, Id = id, OptimisticLockingFieldData = locking } } })).Single();

        static void AssertSucceeded(ModuleSubmitResult result) => Assert.That(result.ExceptionMessage, Is.Empty, result.ExceptionMessage);

        static string Hash(JsonObject row, string hash, string name) => row[hash]?[name]?.ToJsonString() ?? "(none)";

        [Test]
        public async Task 作成した行の値とIdがプリザンターの行と一致する()
        {
            var result = await AddAsync("Customer", e =>
            {
                e.Fields["Name"] = new TextFieldData { Value = "新規 'テスト'" };
                e.Fields["Code"] = new TextFieldData { Value = "N-001" };
                e.Fields["Credit"] = new NumberFieldData { Value = 1234.5m };
                e.Fields["Since"] = new DateFieldData { Value = new DateOnly(2026, 11, 16) };
                e.Fields["LastContact"] = new DateTimeFieldData { Value = new DateTime(2026, 11, 16, 8, 45, 30) };
                e.Fields["Active"] = new BooleanFieldData { Value = true };
            });

            AssertSucceeded(result);
            var id = long.Parse(result.DestinationId, CultureInfo.InvariantCulture);
            var row = (await LiveFixture.GetRecordAsync(LiveFixture.CustomersId, id))!;
            Assert.That(row["Title"]!.GetValue<string>(), Is.EqualTo("新規 'テスト'"));
            Assert.That(Hash(row, "ClassHash", "ClassA"), Is.EqualTo("\"N-001\""));
            Assert.That(row["NumHash"]!["NumA"]!.GetValue<decimal>(), Is.EqualTo(1234.5m));
            Assert.That(Hash(row, "DateHash", "DateA"), Is.EqualTo("\"2026-11-16T00:00:00\""));
            Assert.That(Hash(row, "DateHash", "DateB"), Is.EqualTo("\"2026-11-16T08:45:30\""));
            Assert.That(Hash(row, "CheckHash", "CheckA"), Is.EqualTo("true"));
            await DeleteAsync("Customer", result.DestinationId);
        }

        [Test]
        public async Task 更新で空にした値はプリザンターの空になり読み直すと空()
        {
            var created = await AddAsync("Customer", e =>
            {
                e.Fields["Name"] = new TextFieldData { Value = "空にする" };
                e.Fields["Body"] = new TextFieldData { Value = "本文" };
                e.Fields["Code"] = new TextFieldData { Value = "E-001" };
                e.Fields["Credit"] = new NumberFieldData { Value = 5m };
                e.Fields["Quantity"] = new NumberFieldData { Value = 6m };
                e.Fields["Since"] = new DateFieldData { Value = new DateOnly(2026, 1, 1) };
                e.Fields["LastContact"] = new DateTimeFieldData { Value = new DateTime(2026, 1, 1, 10, 0, 0) };
                e.Fields["Active"] = new BooleanFieldData { Value = true };
                e.Fields["Memo"] = new TextFieldData { Value = "メモ" };
                e.Fields["Status"] = new NumberFieldData { Value = 200m };
            });
            AssertSucceeded(created);
            var id = created.DestinationId;

            var result = await UpdateAsync("Customer", id, e =>
            {
                e.Fields["Body"] = new TextFieldData { Value = null };
                e.Fields["Code"] = new TextFieldData { Value = null };
                e.Fields["Credit"] = new NumberFieldData { Value = null };
                e.Fields["Quantity"] = new NumberFieldData { Value = null };
                e.Fields["Since"] = new DateFieldData { Value = null };
                e.Fields["LastContact"] = new DateTimeFieldData { Value = null };
                e.Fields["Active"] = new BooleanFieldData { Value = null };
                e.Fields["Memo"] = new TextFieldData { Value = null };
                e.Fields["Status"] = new NumberFieldData { Value = null };
            });
            AssertSucceeded(result);

            var read = (await _h.IO.GetListAsync(new SearchCondition("Customer") { Condition = Value("Id.Value", MatchComparison.Equal, id) }, 0)).Items.Single();
            Assert.That(Field<TextFieldData>(read, "Body")?.Value, Is.Null);
            Assert.That(Field<TextFieldData>(read, "Code")?.Value, Is.Null);
            Assert.That(Field<NumberFieldData>(read, "Credit")?.Value, Is.EqualTo(0m));
            Assert.That(Field<NumberFieldData>(read, "Quantity")?.Value, Is.Null);
            Assert.That(Field<DateFieldData>(read, "Since")?.Value, Is.Null);
            Assert.That(Field<DateTimeFieldData>(read, "LastContact")?.Value, Is.Null);
            Assert.That(Field<BooleanFieldData>(read, "Active")?.Value, Is.False);
            Assert.That(Field<TextFieldData>(read, "Memo")?.Value, Is.Null);
            Assert.That(Field<NumberFieldData>(read, "Status")?.Value, Is.EqualTo(0m));
            //空にした日付は空の条件で見つかる
            var nullSince = await _h.IO.GetListAsync(new SearchCondition("Customer") { Condition = And(Value("Id.Value", MatchComparison.Equal, id), Value("Since.Value", MatchComparison.Equal, null)) }, 0);
            Assert.That(nullSince.Items, Has.Count.EqualTo(1));
            await DeleteAsync("Customer", id);
        }

        static MatchConditionBase And(params MatchConditionBase[] children) => MultiMatchCondition.And(children);

        [Test]
        public async Task 読み取り専用の列は更新できず作成ではキーだけ指定できない()
        {
            var e = Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.ExecuteAsync(TestDesign.DataSourceName,
                "update \"Customers\" set \"Ver\"=@p0 where \"ResultId\"=@pid0", new() { ["@p0"] = 5m, ["@pid0"] = LiveFixture.Customers["c1"] }))!;
            Assert.That(e.Message, Does.Contain("read-only"));
            var updateTime = Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.ExecuteAsync(TestDesign.DataSourceName,
                "update \"Customers\" set \"CreatedTime\"=@p0 where \"ResultId\"=@pid0", new() { ["@p0"] = new DateTime(2020, 1, 1), ["@pid0"] = LiveFixture.Customers["c1"] }))!;
            Assert.That(updateTime.Message, Does.Contain("read-only"));
            var insert = Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.InsertAsync(TestDesign.DataSourceName,
                "insert into \"Customers\"(\"Title\",\"ResultId\") values(@p0,@p1) returning \"ResultId\"", new() { ["@p0"] = "x", ["@p1"] = 999999L }))!;
            Assert.That(insert.Message, Does.Contain("read-only"));
            var c1 = (await LiveFixture.GetRecordAsync(LiveFixture.CustomersId, LiveFixture.Customers["c1"]))!;
            Assert.That(c1["CreatedTime"]!.GetValue<string>(), Does.Not.StartWith("2020-01-01"));
        }

        [Test]
        public async Task 作成ではキー以外の読み取り専用の列の値を送らずプリザンターが決める()
        {
            var old = new DateTime(2020, 1, 2, 3, 4, 5);
            var id = await _h.Accessor.InsertAsync(TestDesign.DataSourceName,
                "insert into \"Customers\"(\"Title\",\"Ver\",\"Creator\",\"Updator\",\"CreatedTime\",\"UpdatedTime\") values(@p0,@p1,@p2,@p3,@p4,@p5) returning \"ResultId\"",
                new() { ["@p0"] = "読み取り専用の値つき", ["@p1"] = 9m, ["@p2"] = 999999m, ["@p3"] = 999999m, ["@p4"] = old, ["@p5"] = old });
            var resultId = long.Parse(id!.ToString()!, CultureInfo.InvariantCulture);
            var row = (await LiveFixture.GetRecordAsync(LiveFixture.CustomersId, resultId))!;
            Assert.That(row["Title"]!.GetValue<string>(), Is.EqualTo("読み取り専用の値つき"));
            Assert.That(row["Ver"]!.GetValue<decimal>(), Is.EqualTo(1m));
            Assert.That(row["Creator"]!.GetValue<decimal>(), Is.EqualTo((decimal)LiveFixture.ApiUserId));
            Assert.That(row["Updator"]!.GetValue<decimal>(), Is.EqualTo((decimal)LiveFixture.ApiUserId));
            Assert.That(row["CreatedTime"]!.GetValue<string>(), Does.Not.StartWith("2020-01-02"));
            Assert.That(row["UpdatedTime"]!.GetValue<string>(), Does.Not.StartWith("2020-01-02"));
            AssertSucceeded(await DeleteAsync("Customer", resultId.ToString(CultureInfo.InvariantCulture)));

            //期限付きテーブルの残作業量は作業量と進捗率から計算される
            var taskId = await _h.Accessor.InsertAsync(TestDesign.DataSourceName,
                "insert into \"Tasks\"(\"Title\",\"WorkValue\",\"ProgressRate\",\"RemainingWorkValue\") values(@p0,@p1,@p2,@p3) returning \"IssueId\"",
                new() { ["@p0"] = "残作業量つき", ["@p1"] = 10m, ["@p2"] = 20m, ["@p3"] = 99m });
            var task = (await LiveFixture.GetRecordAsync(LiveFixture.TasksId, long.Parse(taskId!.ToString()!, CultureInfo.InvariantCulture)))!;
            Assert.That(task["RemainingWorkValue"]!.GetValue<decimal>(), Is.EqualTo(8m));
            AssertSucceeded(await DeleteAsync("Task", taskId.ToString()!));
        }

        [Test]
        public async Task 作成日時を表示するフィールドの値を写した作成も保存できる()
        {
            //レコードのコピー (CopyModuleButtonField) と同じく、更新日時のフィールド (Updated = UpdatedTime) に値を入れて作成する
            var result = await AddAsync("Customer", e =>
            {
                e.Fields["Name"] = new TextFieldData { Value = "コピー" };
                e.Fields["Updated"] = new DateTimeFieldData { Value = new DateTime(2020, 1, 2, 3, 4, 5) };
            });
            AssertSucceeded(result);
            var read = (await _h.IO.GetListAsync(new SearchCondition("Customer") { Condition = Value("Id.Value", MatchComparison.Equal, result.DestinationId) }, 0)).Items.Single();
            Assert.That(Field<DateTimeFieldData>(read, "Updated")!.Value, Is.GreaterThan(new DateTime(2025, 1, 1)));
            AssertSucceeded(await DeleteAsync("Customer", result.DestinationId));
        }

        [Test]
        public async Task 削除した行は読めず無い行の更新と削除は0件()
        {
            var created = await AddAsync("Customer", e => e.Fields["Name"] = new TextFieldData { Value = "消す" });
            AssertSucceeded(created);
            AssertSucceeded(await DeleteAsync("Customer", created.DestinationId));
            Assert.That(await LiveFixture.GetRecordAsync(LiveFixture.CustomersId, long.Parse(created.DestinationId, CultureInfo.InvariantCulture)), Is.Null);

            var update = await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "update \"Customers\" set \"Title\"=@p0 where \"ResultId\"=@pid0",
                new() { ["@p0"] = "x", ["@pid0"] = long.Parse(created.DestinationId, CultureInfo.InvariantCulture) });
            var delete = await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "delete from \"Customers\" where \"ResultId\"=@pid0",
                new() { ["@pid0"] = long.Parse(created.DestinationId, CultureInfo.InvariantCulture) });
            Assert.That((update, delete), Is.EqualTo((0, 0)));
        }

        [Test]
        public async Task 別のサイトの行のIdでは書き込まない()
        {
            //注文のサイトの行の ID を顧客の表として更新・削除しても、顧客のサイトの行ではないので 0 件 (レコードの URL は別のサイトの行でも通るため、先に確かめる)
            var orderId = LiveFixture.Orders["o1"];
            var update = await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "update \"Customers\" set \"Title\"=@p0 where \"ResultId\"=@pid0", new() { ["@p0"] = "壊す", ["@pid0"] = orderId });
            var delete = await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "delete from \"Customers\" where \"ResultId\"=@pid0", new() { ["@pid0"] = orderId });

            Assert.That((update, delete), Is.EqualTo((0, 0)));
            var row = (await LiveFixture.GetRecordAsync(LiveFixture.OrdersId, orderId))!;
            Assert.That(row["Title"]!.GetValue<string>(), Is.EqualTo("注文1"));
        }

        [Test]
        public async Task 更新日時の楽観ロックは読んだ時の更新日時でだけ更新できる()
        {
            var created = await AddAsync("Customer", e => e.Fields["Name"] = new TextFieldData { Value = "ロック" });
            AssertSucceeded(created);
            var id = created.DestinationId;
            var read = (await _h.IO.GetListAsync(new SearchCondition("CustomerLocked") { Condition = Value("Id.Value", MatchComparison.Equal, id) }, 0)).Items.Single();
            var locking = Field<OptimisticLockingFieldData>(read, SystemFieldNames.OptimisticLocking)!;
            Assert.That(locking.GetValue(), Is.InstanceOf<DateTime>());

            var ok = await UpdateAsync("CustomerLocked", id, e =>
            {
                e.Fields["Name"] = new TextFieldData { Value = "ロック 1" };
                e.Fields[SystemFieldNames.OptimisticLocking] = locking;
            });
            AssertSucceeded(ok);
            //同じ (古い) 更新日時でもう一度 → 失敗 (プリザンターの更新日時が変わっている)
            await Task.Delay(1100);
            var stale = await UpdateAsync("CustomerLocked", id, e =>
            {
                e.Fields["Name"] = new TextFieldData { Value = "ロック 2" };
                e.Fields[SystemFieldNames.OptimisticLocking] = locking;
            });
            Assert.That(stale.ExceptionMessage, Is.Not.Empty);
            var row = (await LiveFixture.GetRecordAsync(LiveFixture.CustomersId, long.Parse(id, CultureInfo.InvariantCulture)))!;
            Assert.That(row["Title"]!.GetValue<string>(), Is.EqualTo("ロック 1"));
            await DeleteAsync("Customer", id);
        }

        [Test]
        public async Task 期限付きテーブルの作成と更新()
        {
            var created = await AddAsync("Task", e =>
            {
                e.Fields["Title"] = new TextFieldData { Value = "新しい作業" };
                e.Fields["Start"] = new DateTimeFieldData { Value = new DateTime(2026, 12, 1, 9, 0, 0) };
                e.Fields["Due"] = new DateTimeFieldData { Value = new DateTime(2026, 12, 10) };
                e.Fields["Work"] = new NumberFieldData { Value = 10m };
                e.Fields["Progress"] = new NumberFieldData { Value = 20m };
                e.Fields["Customer"] = new LinkFieldData { Value = LiveFixture.Customers["c2"].ToString(CultureInfo.InvariantCulture) };
            });
            AssertSucceeded(created);
            var read = (await _h.IO.GetListAsync(new SearchCondition("Task") { Condition = Value("Id.Value", MatchComparison.Equal, created.DestinationId) }, 0)).Items.Single();
            Assert.That(Field<DateTimeFieldData>(read, "Due")!.Value, Is.EqualTo(new DateTime(2026, 12, 10)));
            Assert.That(Field<NumberFieldData>(read, "Remaining")!.Value, Is.EqualTo(8m));
            Assert.That(Field<LinkFieldData>(read, "Customer")!.DisplayText, Is.EqualTo("ベータ商事"));

            AssertSucceeded(await UpdateAsync("Task", created.DestinationId, e => e.Fields["Progress"] = new NumberFieldData { Value = 50m }));
            read = (await _h.IO.GetListAsync(new SearchCondition("Task") { Condition = Value("Id.Value", MatchComparison.Equal, created.DestinationId) }, 0)).Items.Single();
            Assert.That(Field<NumberFieldData>(read, "Remaining")!.Value, Is.EqualTo(5m));
            AssertSucceeded(await DeleteAsync("Task", created.DestinationId));
        }

        [Test]
        public async Task 一括の追加は1件ずつ作る()
        {
            var count = await _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "insert into \"Orders\" (\"Title\",\"NumA\") values (@p0,@p1),(@p2,@p3)",
                new() { ["@p0"] = "一括1", ["@p1"] = 1m, ["@p2"] = "一括2", ["@p3"] = 2m });
            Assert.That(count, Is.EqualTo(2));
            var created = (await _h.IO.GetListAsync(new SearchCondition("Order") { Condition = Value("Note.Value", MatchComparison.Like, "一括") }, 0)).Items;
            Assert.That(created, Has.Count.EqualTo(2));
            foreach (var item in created) AssertSucceeded(await DeleteAsync("Order", Id(item)));
        }
    }
}
