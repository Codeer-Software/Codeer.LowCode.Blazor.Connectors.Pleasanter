using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>getsite の応答 (サイトの設定) から表の定義を作る。</summary>
    public class TableDefinitionTest
    {
        static PleasanterTableDefinition Parse(JsonObject data, long siteId = 11, Func<long, string?>? findTableName = null)
        {
            using var doc = JsonDocument.Parse(data.ToJsonString());
            return PleasanterTableDefinition.Parse("T", siteId, doc.RootElement, findTableName ?? (_ => null));
        }

        [Test]
        public void 記録テーブルの標準の列と拡張の列()
        {
            var table = Parse(TestDesign.SiteData(11, "Results", TestDesign.CustomersSettings()));

            Assert.That(table.KeyColumn, Is.EqualTo("ResultId"));
            Assert.That(table.Columns.Select(e => e.Name), Is.EqualTo(new[]
            {
                "ResultId", "Title", "Body", "Status", "Manager", "Owner", "Ver", "Creator", "Updator", "CreatedTime", "UpdatedTime", "Comments",
                "ClassA", "ClassB", "ClassC", "NumA", "NumB", "DateA", "DateB", "CheckA", "DescriptionA",
            }));
            var c = table.Columns.ToDictionary(e => e.Name);
            Assert.That(c["NumA"].Kind, Is.EqualTo(PleasanterColumnKind.Number));
            Assert.That(c["NumA"].IsNullable, Is.False);
            Assert.That(c["NumB"].IsNullable, Is.True);
            Assert.That(c["DateA"].Kind, Is.EqualTo(PleasanterColumnKind.Date));
            Assert.That(c["DateB"].Kind, Is.EqualTo(PleasanterColumnKind.DateTime));
            Assert.That(c["CheckA"].Kind, Is.EqualTo(PleasanterColumnKind.Bool));
            Assert.That(c["DescriptionA"].Kind, Is.EqualTo(PleasanterColumnKind.String));
            Assert.That(c["ClassA"].LabelText, Is.EqualTo("コード"));
            Assert.That(c["ClassB"].Choices!.Select(e => (e.Value, e.DisplayText)), Is.EqualTo(new[] { ("A", "ゴールド"), ("B", "シルバー"), ("C", "ブロンズ") }));
            Assert.That(c["ResultId"].IsReadOnly && c["Ver"].IsReadOnly && c["UpdatedTime"].IsReadOnly && c["CreatedTime"].IsReadOnly, Is.True);
            Assert.That(c["Title"].IsReadOnly || c["Status"].IsReadOnly || c["ClassA"].IsReadOnly, Is.False);
            Assert.That(table.TitleIsRaw, Is.True);
        }

        [Test]
        public void 期限付きテーブルの列とリンク()
        {
            var table = Parse(TestDesign.SiteData(13, "Issues", TestDesign.TasksSettings(11)), 13, e => e == 11 ? "Customers" : null);

            Assert.That(table.KeyColumn, Is.EqualTo("IssueId"));
            var c = table.Columns.ToDictionary(e => e.Name);
            Assert.That(c["StartTime"].Kind, Is.EqualTo(PleasanterColumnKind.DateTime));
            Assert.That(c["CompletionTime"].Kind, Is.EqualTo(PleasanterColumnKind.DateTime));
            Assert.That(c["RemainingWorkValue"].IsReadOnly, Is.True);
            Assert.That(c["WorkValue"].IsReadOnly, Is.False);
            Assert.That(c["ClassA"].LinkSiteId, Is.EqualTo(11));
            Assert.That(c["ClassA"].LinkTable, Is.EqualTo("Customers"));
            Assert.That(c["ClassA"].Choices, Is.Null);
            Assert.That(table.ToTableDefinition().Columns.All(e => e.IsNullable), Is.True);
        }

        [Test]
        public void 列の設定だけにある列とセクションは列にしない()
        {
            var settings = new JsonObject
            {
                ["Columns"] = new JsonArray(new JsonObject { ["ColumnName"] = "ClassZ", ["LabelText"] = "設定だけ" }, new JsonObject { ["ColumnName"] = "NumC" }),
                ["EditorColumnHash"] = new JsonObject
                {
                    ["General"] = new JsonArray("Title", "_Section-1", "ClassA"),
                    ["_Tab-1"] = new JsonArray("NumC", "_Links-2"),
                },
                ["GridColumns"] = new JsonArray("ResultId", "DateC"),
            };
            var table = Parse(TestDesign.SiteData(11, "Results", settings));

            var extended = table.Columns.Where(e => PleasanterTableDefinition.IsExtendedColumn(e.Name)).Select(e => e.Name);
            Assert.That(extended, Is.EqualTo(new[] { "ClassA", "NumC", "DateC" }));
        }

        [Test]
        public void タイトルを複数の列で作るサイト()
        {
            PleasanterTableDefinition WithTitle(params string[] columns)
            {
                var settings = TestDesign.CustomersSettings();
                settings["TitleColumns"] = new JsonArray(columns.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray());
                return Parse(TestDesign.SiteData(11, "Results", settings));
            }
            Assert.That(WithTitle("Title", "ClassA").TitleIsRaw, Is.False);
            Assert.That(WithTitle("ClassA").TitleIsRaw, Is.False);
            Assert.That(WithTitle("Title").TitleIsRaw, Is.True);
        }

        [Test]
        public void 複数選択の列()
        {
            var settings = TestDesign.CustomersSettings();
            settings["Columns"]!.AsArray()[1]!["MultipleSelections"] = true;
            Assert.That(Parse(TestDesign.SiteData(11, "Results", settings)).FindColumn("ClassB")!.MultipleSelections, Is.True);
        }

        [TestCase("Wikis")]
        [TestCase("Sites")]
        [TestCase("Dashboards")]
        public void 記録テーブルと期限付きテーブル以外は使えない(string referenceType)
        {
            var e = Assert.Throws<PleasanterException>(() => Parse(TestDesign.SiteData(11, referenceType, new JsonObject())))!;
            Assert.That(e.Message, Does.Contain(referenceType));
        }

        [Test]
        public void サイト統合のサイトは使えない()
        {
            var settings = TestDesign.CustomersSettings();
            settings["IntegratedSites"] = new JsonArray(12, 13);
            var e = Assert.Throws<PleasanterException>(() => Parse(TestDesign.SiteData(11, "Results", settings)))!;
            Assert.That(e.Message, Does.Contain("IntegratedSites"));
        }

        [Test]
        public void 別のサイトの設定が返ったらエラー()
            => Assert.Throws<PleasanterException>(() => Parse(TestDesign.SiteData(99, "Results", new JsonObject()), 11));

        [TestCase("A,ゴールド\nB\n[[Users]]\n[[1200,NoAddButton]]\n[[1300]]", "A=ゴールド|B=", 1200L)]
        [TestCase("  \r\n", "", null)]
        [TestCase("[[Depts]]", "", null)]
        public void 選択肢とリンクを分ける(string text, string expectedChoices, long? expectedLink)
        {
            var (choices, link) = PleasanterTableDefinition.ParseChoices(text);
            Assert.That(choices == null ? string.Empty : string.Join("|", choices.Select(e => e.Value + "=" + e.DisplayText)), Is.EqualTo(expectedChoices));
            Assert.That(link, Is.EqualTo(expectedLink));
        }
    }
}
