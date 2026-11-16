using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>表の定義の保存ファイル (デザイナがプリザンターに取りに行かずに表の定義を答えるためのもの)。</summary>
    public class MetadataFileTest
    {
        string _directory = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "clb-pleasanter-meta-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void ファイル名はデータソース名から決まる()
            => Assert.That(Path.GetFileName(PleasanterMetadataFile.GetPath(_directory, "PL")), Is.EqualTo("pleasanter.PL.metadata.json"));

        [Test]
        public async Task 書き出した定義を読み戻すと同じ()
        {
            var h = new FakeHarness();
            await PleasanterMetadataFile.UpdateAsync(h.Connection, _directory);

            var loaded = PleasanterMetadataFile.Load(_directory, "PL")!.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
            var original = (await h.Connection.GetTableDefinitionsAsync()).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
            Assert.That(loaded.Select(e => e.Name), Is.EqualTo(original.Select(e => e.Name)));
            foreach (var (a, b) in loaded.Zip(original))
            {
                Assert.That((a.SiteId, a.ReferenceType, a.KeyColumn, a.TitleIsRaw), Is.EqualTo((b.SiteId, b.ReferenceType, b.KeyColumn, b.TitleIsRaw)));
                Assert.That(a.Columns.Select(Describe), Is.EqualTo(b.Columns.Select(Describe)), a.Name);
            }
        }

        static string Describe(PleasanterColumnDefinition c)
            => $"{c.Name}|{c.Kind}|{c.IsReadOnly}|{c.IsNullable}|{c.LabelText}|{c.LinkSiteId}|{c.LinkTable}|{c.MultipleSelections}|" +
               string.Join(";", c.Choices?.Select(e => e.Value + "=" + e.DisplayText) ?? Array.Empty<string>());

        [Test]
        public async Task ファイルに接続先とAPIキーを書かない()
        {
            var h = new FakeHarness();
            await PleasanterMetadataFile.UpdateAsync(h.Connection, _directory);
            var text = await File.ReadAllTextAsync(PleasanterMetadataFile.GetPath(_directory, "PL"));
            Assert.That(text, Does.Not.Contain(FakePleasanter.ApiKey));
            Assert.That(text, Does.Not.Contain("example.com"));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1), "一時ファイルを残さない");
        }

        [Test]
        public async Task ファイルを指定したアクセサはプリザンターに取りに行かない()
        {
            var h = new FakeHarness();
            await PleasanterMetadataFile.UpdateAsync(h.Connection, _directory);

            var designer = new FakeHarness();
            var accessor = new PleasanterDbAccessor(designer.Inner, new[] { designer.Connection }, _directory);
            var tables = await accessor.GetCustomTableDefinitionsAsync("PL");
            var orders = await accessor.GetCustomTableDefinitionsAsync("PL", "Orders");

            Assert.That(tables!.Select(e => e.Name), Is.EquivalentTo(new[] { "Customers", "Orders", "Tasks", "Many" }));
            Assert.That(orders!.Columns.Select(e => e.Name), Does.Contain("ClassA"));
            Assert.That(designer.Fake.Requests, Is.Empty);
            Assert.ThrowsAsync<PleasanterException>(() => accessor.GetCustomTableDefinitionsAsync("PL", "Nothing"));
        }

        [Test]
        public async Task ファイルが無ければ表は無い()
        {
            var h = new FakeHarness();
            var accessor = new PleasanterDbAccessor(h.Inner, new[] { h.Connection }, _directory);
            Assert.That(await accessor.GetCustomTableDefinitionsAsync("PL"), Is.Empty);
            Assert.That(h.Fake.Requests, Is.Empty);
        }

        [Test]
        public void 定義を読めなければファイルを変えない()
        {
            var path = PleasanterMetadataFile.GetPath(_directory, "PL");
            File.WriteAllText(path, "{\"Tables\":[]}");
            var h = new FakeHarness();
            h.Fake.Override(e => e.Action == "getsite", _ => FakePleasanter.Error(System.Net.HttpStatusCode.Unauthorized, "Unauthorized"), 10);
            Assert.ThrowsAsync<PleasanterException>(() => PleasanterMetadataFile.UpdateAsync(h.Connection, _directory));
            Assert.That(File.ReadAllText(path), Is.EqualTo("{\"Tables\":[]}"));
        }
    }
}
