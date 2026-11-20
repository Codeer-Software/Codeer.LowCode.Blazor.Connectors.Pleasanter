using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>
    /// 実物のプリザンターの添付 (AttachmentsA / AttachmentsB): 追加・削除・読んだままの書き戻し・ほかのレコードの Guid・ダウンロードの判定。
    /// 添付の列を持つサイトをこのクラスで作って消す。
    /// </summary>
    public class LiveAttachmentsTest
    {
        long _siteId;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            var settings = new JsonObject
            {
                ["ReferenceType"] = "Results",
                ["Columns"] = new JsonArray(
                    new JsonObject { ["ColumnName"] = "AttachmentsA", ["LabelText"] = "資料" },
                    new JsonObject { ["ColumnName"] = "AttachmentsB", ["LabelText"] = "別の資料" }),
                ["EditorColumnHash"] = new JsonObject { ["General"] = new JsonArray("Title", "AttachmentsA", "AttachmentsB") },
            };
            var title = "CLBコネクタテスト_添付_" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var json = await LiveFixture.CallAsync("api/items/0/createsite", new JsonObject { ["Title"] = title, ["ReferenceType"] = "Results", ["SiteSettings"] = settings });
            _siteId = json["Id"]!.GetValue<long>();
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            if (_siteId != 0) await LiveFixture.CallAsync($"api/items/{_siteId}/deletesite", new JsonObject());
        }

        /// <param name="readableTitle">指定すると、Doc の行の閲覧条件を「タイトルがこれ」にする。</param>
        LiveHarness Harness(string? readableTitle = null) => new(edit: s => s.Sites["Docs"] = _siteId, editDesign: d =>
        {
            var doc = new ModuleDesign { Name = "Doc", DataSourceName = TestDesign.DataSourceName, DbTable = "Docs", CanCreate = true, CanUpdate = true, CanDelete = true };
            doc.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "ResultId" });
            doc.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Title" });
            doc.Fields.Add(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
            doc.Fields.Add(new PleasanterAttachmentsFieldDesign { Name = "Files2", DbColumn = "AttachmentsB" });
            doc.Fields.Add(new TextFieldDesign { Name = "FilesText", DbColumn = "AttachmentsA" });
            if (readableTitle != null)
            {
                doc.DataReadCondition.ModuleName = "Doc";
                doc.DataReadCondition.Condition = Value("Name.Value", MatchComparison.Equal, readableTitle);
            }
            ((IEditableModuleDesign)d.Modules).Add(doc);

            var local = new ModuleDesign { Name = "Local", DataSourceName = "Other", DbTable = "Docs" };
            local.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "ResultId" });
            local.Fields.Add(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
            ((IEditableModuleDesign)d.Modules).Add(local);
        });

        static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        static JsonObject Add(string name, string content) => new() { ["Name"] = name, ["ContentType"] = "text/plain", ["Base64"] = Base64(content) };

        static JsonObject Delete(string guid) => new() { ["Guid"] = guid, ["Deleted"] = true };

        static async Task<string> CreateAsync(LiveHarness h, string title, params JsonObject[] files)
        {
            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = "Doc" };
            data.Fields["Id"] = tempId;
            data.Fields["Name"] = new TextFieldData { Value = title };
            data.Fields["Files"] = new PleasanterAttachmentsFieldData { Value = new JsonArray(files.Cast<JsonNode>().ToArray()).ToJsonString() };
            var added = (await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Doc", Id = tempId.Value!, Add = new() { data } } })).Single();
            Assert.That(added.ExceptionMessage, Is.Empty, added.ExceptionMessage);
            return added.DestinationId;
        }

        static async Task<string> UpdateAsync(LiveHarness h, string id, string field, string value)
        {
            var data = new ModuleData { Name = "Doc" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields[field] = new PleasanterAttachmentsFieldData { Value = value };
            var updated = (await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Doc", Id = id, Update = new() { data } } })).Single();
            return updated.ExceptionMessage;
        }

        static async Task DeleteRecordAsync(LiveHarness h, string id)
            => await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Doc", Id = id, Delete = new() { new ModuleDeleteInfo { ModuleName = "Doc", Id = id } } } });

        static async Task<string> ReadAsync(LiveHarness h, string id, string field = "Files")
        {
            var read = (await h.IO.GetListAsync(new SearchCondition("Doc") { Condition = Value("Id.Value", MatchComparison.Equal, id) }, 0)).Items.Single();
            return Field<PleasanterAttachmentsFieldData>(read, field)!.Value!;
        }

        static List<(string Guid, string Name)> Files(string value)
            => JsonNode.Parse(value)!.AsArray().Select(e => (e!["Guid"]!.GetValue<string>(), e["Name"]!.GetValue<string>())).ToList();

        /// <summary>API キーで中身を直接読む。無ければ null。</summary>
        static async Task<string?> BinaryAsync(string guid)
        {
            try
            {
                var json = await LiveFixture.CallAsync($"api/binaries/{guid}/get", new JsonObject());
                return Encoding.UTF8.GetString(Convert.FromBase64String(json["Response"]!["Base64"]!.GetValue<string>()));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        [Test]
        public async Task 作成で複数足し更新で足して消し読んだままの書き戻しでは変わらない()
        {
            var h = Harness();
            var id = await CreateAsync(h, "添付の基本", Add("a.txt", "aaa"), Add("b.txt", "bbbb"));
            try
            {
                var created = Files(await ReadAsync(h, id));
                Assert.That(created.Select(e => e.Name), Is.EquivalentTo(new[] { "a.txt", "b.txt" }));

                var a = created.Single(e => e.Name == "a.txt").Guid;
                var value = JsonNode.Parse(await ReadAsync(h, id))!.AsArray();
                value.Add(Add("c.txt", "c"));
                value.Add(Delete(a));
                Assert.That(await UpdateAsync(h, id, "Files", value.ToJsonString()), Is.Empty);
                var updated = Files(await ReadAsync(h, id));
                Assert.That(updated.Select(e => e.Name), Is.EquivalentTo(new[] { "b.txt", "c.txt" }));
                Assert.That(await BinaryAsync(a), Is.Null);

                var before = await ReadAsync(h, id);
                Assert.That(await UpdateAsync(h, id, "Files", before), Is.Empty);
                Assert.That(JsonNode.DeepEquals(JsonNode.Parse(await ReadAsync(h, id)), JsonNode.Parse(before)), Is.True);
            }
            finally
            {
                await DeleteRecordAsync(h, id);
            }
        }

        [Test]
        public async Task ほかのレコードのGuidでは消えない()
        {
            var h = Harness();
            var victim = await CreateAsync(h, "消されない側", Add("keep.txt", "keep"));
            var attacker = await CreateAsync(h, "消そうとする側");
            try
            {
                var guid = Files(await ReadAsync(h, victim)).Single().Guid;
                //プリザンターは追加と一緒に送ると別のレコードの添付でも消すので、追加と一緒に送る
                var value = new JsonArray(Add("x.txt", "x"), Delete(guid)).ToJsonString();
                Assert.That(await UpdateAsync(h, attacker, "Files", value), Is.Empty);

                Assert.That(Files(await ReadAsync(h, victim)).Select(e => e.Guid), Is.EqualTo(new[] { guid }));
                Assert.That(await BinaryAsync(guid), Is.EqualTo("keep"));
                Assert.That(Files(await ReadAsync(h, attacker)).Select(e => e.Name), Is.EqualTo(new[] { "x.txt" }));
            }
            finally
            {
                await DeleteRecordAsync(h, victim);
                await DeleteRecordAsync(h, attacker);
            }
        }

        [Test]
        public async Task ダウンロードはレコードとフィールドの添付だけ中身を返す()
        {
            var h = Harness();
            var readable = await CreateAsync(h, "読める", Add("資料.txt", "中身です"));
            var other = await CreateAsync(h, "読めない", Add("secret.txt", "secret"));
            try
            {
                var guid = Files(await ReadAsync(h, readable)).Single().Guid;
                var otherGuid = Files(await ReadAsync(h, other)).Single().Guid;

                var file = await h.Accessor.GetAttachmentAsync(h.IO, h.Design, "Doc", readable, "Files", guid);
                Assert.That(file!.FileName, Is.EqualTo("資料.txt"));
                Assert.That(Encoding.UTF8.GetString(file.Content), Is.EqualTo("中身です"));

                Assert.That(await h.Accessor.GetAttachmentAsync(h.IO, h.Design, "Doc", readable, "Files", "0123456789abcdef0123456789abcdef"), Is.Null, "無い Guid");
                Assert.That(await h.Accessor.GetAttachmentAsync(h.IO, h.Design, "Doc", readable, "Files", otherGuid), Is.Null, "ほかのレコードの Guid");
                Assert.That(await h.Accessor.GetAttachmentAsync(h.IO, h.Design, "Doc", readable, "Files2", guid), Is.Null, "ほかのフィールド");
                Assert.That(await h.Accessor.GetAttachmentAsync(h.IO, h.Design, "Doc", readable, "FilesText", guid), Is.Null, "添付のフィールドでない");
                Assert.That(await h.Accessor.GetAttachmentAsync(h.IO, h.Design, "Local", readable, "Files", guid), Is.Null, "プリザンターでないモジュール");

                var limited = Harness(readableTitle: "読める");
                Assert.That(await limited.Accessor.GetAttachmentAsync(limited.IO, limited.Design, "Doc", readable, "Files", guid), Is.Not.Null, "読めるレコード");
                Assert.That(await limited.Accessor.GetAttachmentAsync(limited.IO, limited.Design, "Doc", other, "Files", otherGuid), Is.Null, "読めないレコード");
            }
            finally
            {
                await DeleteRecordAsync(h, readable);
                await DeleteRecordAsync(h, other);
            }
        }

        [Test]
        public void 条件には使えない()
            => Assert.ThrowsAsync<PleasanterException>(() => Harness().IO.GetListAsync(new SearchCondition("Doc") { Condition = Value("FilesText.Value", MatchComparison.Like, "a") }, 0));
    }
}
