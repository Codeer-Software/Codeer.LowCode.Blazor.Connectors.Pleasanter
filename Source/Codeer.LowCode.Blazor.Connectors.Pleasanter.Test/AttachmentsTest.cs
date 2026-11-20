using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Net;
using System.Text.Json.Nodes;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>添付の列 (AttachmentsA): 読むとファイルの並びの JSON、書くと Base64 の要素を足し Deleted の要素を消す (そのレコードにある Guid だけ)。</summary>
    public class AttachmentsTest
    {
        const string G1 = "0123456789abcdef0123456789abcdef";
        const string G2 = "fedcba9876543210fedcba9876543210";
        const string Other = "11111111111111111111111111111111";
        static readonly string StoredJson = $"[{{\"Guid\":\"{G1}\",\"Name\":\"a.txt\",\"Size\":3}},{{\"Guid\":\"{G2}\",\"Name\":\"b.txt\",\"Size\":4}}]";

        FakeHarness _h = null!;

        [SetUp]
        public void SetUp()
        {
            _h = new FakeHarness(editDesign: d =>
            {
                d.Modules.Find("Customer")!.Fields.Add(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
                //添付の列に文字のフィールドを置いたもの (ダウンロードの対象にならない)
                d.Modules.Find("Customer")!.Fields.Add(new TextFieldDesign { Name = "FilesText", DbColumn = "AttachmentsA" });
            });
            var settings = _h.Fake.Sites[FakeHarness.CustomersId]["SiteSettings"]!;
            ((JsonArray)settings["Columns"]!).Add(new JsonObject { ["ColumnName"] = "AttachmentsA", ["LabelText"] = "資料" });
            ((JsonArray)settings["EditorColumnHash"]!["General"]!).Add("AttachmentsA");
            _h.Fake.Rows[FakeHarness.CustomersId] = new()
            {
                new JsonObject
                {
                    ["SiteId"] = FakeHarness.CustomersId, ["ResultId"] = 1, ["Ver"] = 1, ["Title"] = "アルファ",
                    ["AttachmentsHash"] = new JsonObject { ["AttachmentsA"] = JsonNode.Parse(StoredJson) },
                },
            };
        }

        static string Base64(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));

        Task<int> UpdateAsync(object? value)
            => _h.Accessor.ExecuteAsync(TestDesign.DataSourceName, "update \"Customers\" set \"AttachmentsA\"=@p0 where \"ResultId\"=@pid0", new() { ["@p0"] = value, ["@pid0"] = 1L });

        void Binary(string guid, long referenceId, string content, string fileName = "a.txt")
            => _h.Fake.Override(e => e.Path == $"/api/binaries/{guid}/get",
                _ => FakePleasanter.Json(HttpStatusCode.OK, new JsonObject
                {
                    ["StatusCode"] = 200,
                    ["Response"] = new JsonObject { ["ReferenceId"] = referenceId, ["Guid"] = guid, ["FileName"] = fileName, ["Base64"] = Base64(content) },
                }.ToJsonString()), times: 10);

        [Test]
        public async Task 表の定義では添付の種類で本体には添付の型の名前の文字の列()
        {
            var definition = await _h.Connection.GetTableDefinitionAsync("Customers");
            var column = definition.FindColumn("AttachmentsA")!;
            Assert.That(column.Kind, Is.EqualTo(PleasanterColumnKind.Attachments));
            Assert.That(column.HashName, Is.EqualTo("AttachmentsHash"));
            var table = definition.ToTableDefinition();
            var dbColumn = table.Columns.Single(e => e.Name == "AttachmentsA");
            Assert.That(dbColumn.RawDbTypeName, Is.EqualTo("pleasanter_attachments"));
        }

        [Test]
        public async Task 読むとファイルの並びのJSON()
        {
            var item = (await _h.IO.GetListAsync(new SearchCondition("Customer"), 0)).Items.Single();
            var value = Field<PleasanterAttachmentsFieldData>(item, "Files")!.Value!;
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(value), JsonNode.Parse(StoredJson)), Is.True);
        }

        [Test]
        public async Task 読んだままの値を書いても送らない()
        {
            Assert.That(await UpdateAsync(StoredJson), Is.EqualTo(1));
            Assert.That(await UpdateAsync(null), Is.EqualTo(1));
            Assert.That(await UpdateAsync(string.Empty), Is.EqualTo(1));
            Assert.That(_h.Fake.WriteRequests, Is.Empty);
        }

        [Test]
        public async Task 追加は名前と種類とBase64だけ送り種類が無ければoctet_stream()
        {
            var value = new JsonArray(
                JsonNode.Parse(StoredJson)![0]!.DeepClone(),
                new JsonObject { ["Name"] = "c.txt", ["ContentType"] = "text/plain", ["Base64"] = Base64("ccc"), ["Guid"] = Other, ["Size"] = 3 },
                new JsonObject { ["Name"] = "d.bin", ["Base64"] = Base64("dd") }).ToJsonString();
            await UpdateAsync(value);

            var sent = (JsonArray)_h.Fake.WriteRequests.Single().Json["AttachmentsHash"]!["AttachmentsA"]!;
            Assert.That(sent.ToJsonString(), Is.EqualTo(new JsonArray(
                new JsonObject { ["Name"] = "c.txt", ["ContentType"] = "text/plain", ["Base64"] = Base64("ccc") },
                new JsonObject { ["Name"] = "d.bin", ["ContentType"] = "application/octet-stream", ["Base64"] = Base64("dd") }).ToJsonString()));
        }

        [Test]
        public async Task 削除はそのレコードにあるGuidだけ送る()
        {
            var value = new JsonArray(
                new JsonObject { ["Guid"] = G2.ToUpperInvariant(), ["Deleted"] = true },
                new JsonObject { ["Guid"] = Other, ["Deleted"] = true }).ToJsonString();
            await UpdateAsync(value);

            var sent = (JsonArray)_h.Fake.WriteRequests.Single().Json["AttachmentsHash"]!["AttachmentsA"]!;
            Assert.That(sent.ToJsonString(), Is.EqualTo(new JsonArray(new JsonObject { ["Guid"] = G2.ToUpperInvariant(), ["Deleted"] = true }).ToJsonString()));
        }

        [Test]
        public async Task 消すものが全部ほかのレコードのGuidなら送らない()
        {
            Assert.That(await UpdateAsync(new JsonArray(new JsonObject { ["Guid"] = Other, ["Deleted"] = true }).ToJsonString()), Is.EqualTo(1));
            Assert.That(_h.Fake.WriteRequests, Is.Empty);
        }

        [Test]
        public async Task 作成では追加だけ送る()
        {
            var value = new JsonArray(
                new JsonObject { ["Name"] = "c.txt", ["ContentType"] = "text/plain", ["Base64"] = Base64("ccc") },
                new JsonObject { ["Guid"] = G1, ["Deleted"] = true }).ToJsonString();
            await _h.Accessor.InsertAsync(TestDesign.DataSourceName, "insert into \"Customers\"(\"Title\",\"AttachmentsA\") values(@p0,@p1) returning \"ResultId\"",
                new() { ["@p0"] = "新規", ["@p1"] = value });

            var body = _h.Fake.WriteRequests.Single().Json;
            Assert.That(body["AttachmentsHash"]!["AttachmentsA"]!.ToJsonString(), Is.EqualTo(new JsonArray(
                new JsonObject { ["Name"] = "c.txt", ["ContentType"] = "text/plain", ["Base64"] = Base64("ccc") }).ToJsonString()));
        }

        [TestCase("not json")]
        [TestCase("{}")]
        [TestCase("[1]")]
        [TestCase("[{\"Base64\":\"YWJj\"}]")]
        [TestCase("[{\"Name\":\"x\",\"Base64\":\"***\"}]")]
        [TestCase("[{\"Deleted\":true}]")]
        [TestCase("[{\"Guid\":\"../x\",\"Deleted\":true}]")]
        [TestCase("[{\"Guid\":\"0123456789abcdef0123456789abcdef\",\"Deleted\":true,\"Name\":\"x\",\"Base64\":\"YWJj\"}]")]
        public void 形が不正ならエラー(string value)
        {
            var e = Assert.ThrowsAsync<PleasanterException>(() => UpdateAsync(value))!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.InvalidRequest));
            Assert.That(_h.Fake.WriteRequests, Is.Empty);
        }

        [Test]
        public void 条件と並びと集計には使えない()
        {
            Assert.ThrowsAsync<PleasanterException>(() => _h.IO.GetListAsync(new SearchCondition("Customer") { Condition = Value("FilesText.Value", MatchComparison.Like, "a") }, 0));
            var sorted = new SearchCondition("Customer");
            sorted.SortConditions.Add(new SortCondition { Variable = "Files.Value" });
            Assert.ThrowsAsync<PleasanterException>(() => _h.IO.GetListAsync(sorted, 0));
            var aggregate = new AggregateCondition("Customer");
            aggregate.Groups.Add(new ValueGroup { Variable = "FilesText.Value" });
            aggregate.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            Assert.ThrowsAsync<PleasanterException>(() => _h.IO.AggregateAsync(aggregate));
        }

        // ---------------------------------------------------------------- ダウンロード

        Task<PleasanterAttachmentFile?> DownloadAsync(string? module = "Customer", string? id = "1", string? field = "Files", string? guid = G1)
            => _h.Accessor.GetAttachmentAsync(_h.IO, _h.Design, module, id, field, guid);

        [Test]
        public async Task ダウンロードはレコードの名前と中身を返す()
        {
            Binary(G1, 1, "abc", "binary-name.txt");
            var file = await DownloadAsync(guid: G1.ToUpperInvariant());
            Assert.That(file!.FileName, Is.EqualTo("a.txt"));
            Assert.That(System.Text.Encoding.UTF8.GetString(file.Content), Is.EqualTo("abc"));
            Assert.That(_h.Fake.Requests.Any(e => e.Path == $"/api/binaries/{G1}/get" && e.Json["ApiKey"]?.GetValue<string>() == FakePleasanter.ApiKey), Is.True);
        }

        [Test]
        public async Task レコードに無いGuidは見つからないで中身を読みに行かない()
        {
            Binary(Other, 2, "secret");
            Assert.That(await DownloadAsync(guid: Other), Is.Null);
            Assert.That(_h.Fake.Requests.Any(e => e.Path.StartsWith("/api/binaries/", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public async Task 中身がほかのレコードのものなら見つからない()
        {
            Binary(G1, 2, "abc");
            Assert.That(await DownloadAsync(), Is.Null);
        }

        [Test]
        public async Task プリザンターに無ければ見つからない()
        {
            _h.Fake.Override(e => e.Path.StartsWith("/api/binaries/", StringComparison.Ordinal), _ => FakePleasanter.Error(HttpStatusCode.NotFound, "Not found."), times: 10);
            Assert.That(await DownloadAsync(), Is.Null);
        }

        [TestCase("Customer", "1", "FilesText", G1, Description = "添付のフィールドでない")]
        [TestCase("Customer", "1", "Name", G1, Description = "添付の列でない")]
        [TestCase("Customer", "1", "Nothing", G1, Description = "フィールドが無い")]
        [TestCase("Nothing", "1", "Files", G1, Description = "モジュールが無い")]
        [TestCase("Customer", "x", "Files", G1, Description = "Id が数でない")]
        [TestCase("Customer", "-1", "Files", G1, Description = "Id が負")]
        [TestCase("Customer", "1", "Files", "../../api/items/1/get", Description = "Guid の形でない")]
        [TestCase(null, null, null, null, Description = "何も無い")]
        public async Task 対象が違えば見つからない(string? module, string? id, string? field, string? guid)
        {
            Binary(G1, 1, "abc");
            Assert.That(await DownloadAsync(module, id, field, guid), Is.Null);
        }

        [Test]
        public async Task プリザンターでないモジュールは見つからない()
        {
            var h = new FakeHarness(editDesign: d =>
            {
                var module = new ModuleDesign { Name = "Local", DataSourceName = "Other", DbTable = "Customers" };
                module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "ResultId" });
                module.Fields.Add(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
                ((IEditableModuleDesign)d.Modules).Add(module);
            });
            Assert.That(await h.Accessor.GetAttachmentAsync(h.IO, h.Design, "Local", "1", "Files", G1), Is.Null);
            Assert.That(h.Inner.Calls, Is.Empty);
        }

        // ---------------------------------------------------------------- 権限 (本体の ModuleDataIO の権限がダウンロードと保存に効く)

        //Customer の Files に PermissionField を付ける: 行の Name が readableName のときだけ読める・unwritableName 以外のときだけ書ける。
        //書き込みの条件は本体が SQL (行の存在) で確かめる。偽物は View の条件で絞らないので、メモリで評価する <> にしてある
        void UsePermission(string? readableName, string? unwritableName)
        {
            var rows = _h.Fake.Rows;
            _h = new FakeHarness(editDesign: d =>
            {
                var customer = d.Modules.Find("Customer")!;
                customer.Fields.Add(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
                var permission = new PermissionFieldDesign { Name = "FilesPermission", TargetFields = new() { "Files" } };
                if (readableName != null)
                {
                    permission.ReadCondition.ModuleName = "Customer";
                    permission.ReadCondition.Condition = MultiMatchCondition.And(Value("Name.Value", MatchComparison.Equal, readableName));
                }
                if (unwritableName != null)
                {
                    permission.WriteCondition.ModuleName = "Customer";
                    permission.WriteCondition.Condition = MultiMatchCondition.And(Value("Name.Value", MatchComparison.NotEqual, unwritableName));
                }
                customer.Fields.Add(permission);
            });
            var settings = _h.Fake.Sites[FakeHarness.CustomersId]["SiteSettings"]!;
            ((JsonArray)settings["Columns"]!).Add(new JsonObject { ["ColumnName"] = "AttachmentsA", ["LabelText"] = "資料" });
            ((JsonArray)settings["EditorColumnHash"]!["General"]!).Add("AttachmentsA");
            foreach (var (siteId, list) in rows) _h.Fake.Rows[siteId] = list;
        }

        [TestCase("ベータ", false, Description = "行の Name が読み取りの条件に合わない")]
        [TestCase("アルファ", true, Description = "条件に合えば読める (対照)")]
        public async Task フィールドの権限で読めない添付のフィールドのダウンロードは見つからない(string readableName, bool found)
        {
            UsePermission(readableName, null);
            Binary(G1, 1, "abc");
            var file = await DownloadAsync();
            Assert.That(file != null, Is.EqualTo(found));
            if (!found) Assert.That(_h.Fake.Requests.Any(e => e.Path.StartsWith("/api/binaries/", StringComparison.Ordinal)), Is.False, "中身を読みに行かない");
        }

        async Task<string> SubmitFilesAsync(string value)
        {
            var data = new ModuleData { Name = "Customer" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Files"] = new PleasanterAttachmentsFieldData { Value = value };
            return (await _h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Customer", Id = "1", Update = new() { data } } })).Single().ExceptionMessage;
        }

        [TestCase("アルファ", false, Description = "行の Name が書き込みの条件に合わない")]
        [TestCase("ベータ", true, Description = "条件に合えば書ける (対照)")]
        public async Task フィールドの権限で書けない添付のフィールドへの保存は拒否される(string unwritableName, bool written)
        {
            UsePermission(null, unwritableName);
            var message = await SubmitFilesAsync(new JsonArray(
                new JsonObject { ["Name"] = "c.txt", ["ContentType"] = "text/plain", ["Base64"] = Base64("ccc") },
                new JsonObject { ["Guid"] = G1, ["Deleted"] = true }).ToJsonString());
            Assert.That(string.IsNullOrEmpty(message), Is.EqualTo(written), message);
            Assert.That(_h.Fake.WriteRequests.Count, Is.EqualTo(written ? 1 : 0));
        }

        [TestCase(true, false, Description = "論理削除の行")]
        [TestCase(false, true, Description = "削除されていない行 (対照)")]
        public async Task 論理削除の行のダウンロードは見つからない(bool deleted, bool found)
        {
            var rows = _h.Fake.Rows;
            _h = new FakeHarness(editDesign: d =>
            {
                var module = new ModuleDesign { Name = "SoftCustomer", DataSourceName = TestDesign.DataSourceName, DbTable = "Customers", CanCreate = true, CanUpdate = true, CanDelete = true };
                module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "ResultId" });
                module.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Title" });
                module.Fields.Add(new BooleanFieldDesign { Name = SystemFieldNames.LogicalDelete, DbColumn = "CheckA" });
                module.Fields.Add(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
                ((IEditableModuleDesign)d.Modules).Add(module);
            });
            var settings = _h.Fake.Sites[FakeHarness.CustomersId]["SiteSettings"]!;
            ((JsonArray)settings["Columns"]!).Add(new JsonObject { ["ColumnName"] = "AttachmentsA", ["LabelText"] = "資料" });
            ((JsonArray)settings["EditorColumnHash"]!["General"]!).Add("AttachmentsA");
            foreach (var (siteId, list) in rows) _h.Fake.Rows[siteId] = list;
            _h.Fake.Rows[FakeHarness.CustomersId].Single()["CheckHash"] = new JsonObject { ["CheckA"] = deleted };
            Binary(G1, 1, "abc");

            var file = await _h.Accessor.GetAttachmentAsync(_h.IO, _h.Design, "SoftCustomer", "1", "Files", G1);
            Assert.That(file != null, Is.EqualTo(found));
        }
    }
}
