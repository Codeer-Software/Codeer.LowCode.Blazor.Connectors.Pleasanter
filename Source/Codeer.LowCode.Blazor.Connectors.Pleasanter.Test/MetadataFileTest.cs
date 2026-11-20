using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Match;
using System.IO.Compression;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>
    /// 表の定義の保存ファイル。デザイナがプロジェクトのサーバー専用ファイルに書き、デザインと一緒に配られ (App.zip)、
    /// デザイナも Web アプリもそこから表の定義を読む (プリザンターに取りに行かない)。
    /// </summary>
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

        static string FileName => PleasanterMetadataFile.GetFileName("PL");

        [Test]
        public void ファイル名はデータソース名から決まる()
            => Assert.That(FileName, Is.EqualTo("pleasanter.PL.metadata.json"));

        [Test]
        public async Task プロジェクトのサーバー専用ファイルに書き読み戻すと同じ()
        {
            var h = new FakeHarness();
            await PleasanterMetadataFile.UpdateAsync(h.Connection, _directory);

            Assert.That(DesignDataFileManager.ReadProjectServerFile(_directory, FileName), Is.Not.Null, "サーバー専用ファイルとして書く");
            Assert.That(File.Exists(Path.Combine(_directory, FileName)), Is.False, "プロジェクトの直下には書かない");

            var loaded = PleasanterMetadataFile.FromProject(_directory).Load("PL").OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
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
            var text = System.Text.Encoding.UTF8.GetString(DesignDataFileManager.ReadProjectServerFile(_directory, FileName)!);
            Assert.That(text, Does.Not.Contain(FakePleasanter.ApiKey));
            Assert.That(text, Does.Not.Contain("example.com"));
        }

        [Test]
        public async Task プロジェクトのファイルを指定したアクセサはプリザンターに取りに行かない()
        {
            var h = new FakeHarness();
            await PleasanterMetadataFile.UpdateAsync(h.Connection, _directory);

            var designer = new FakeHarness();
            var accessor = new PleasanterDbAccessor(designer.Inner, new[] { designer.Connection }, PleasanterMetadataFile.FromProject(_directory));
            var tables = await accessor.GetCustomTableDefinitionsAsync("PL");
            var orders = await accessor.GetCustomTableDefinitionsAsync("PL", "Orders");

            Assert.That(tables!.Select(e => e.Name), Is.EquivalentTo(new[] { "Customers", "Orders", "Tasks", "Many" }));
            Assert.That(orders!.Columns.Select(e => e.Name), Does.Contain("ClassA"));
            Assert.That(designer.Fake.Requests, Is.Empty);
            Assert.That(Assert.ThrowsAsync<PleasanterException>(() => accessor.GetCustomTableDefinitionsAsync("PL", "Nothing"))!.Kind, Is.EqualTo(PleasanterErrorKind.NotFound));
        }

        [Test]
        public async Task プロジェクトにファイルが無ければ表は無い()
        {
            var h = new FakeHarness();
            var accessor = new PleasanterDbAccessor(h.Inner, new[] { h.Connection }, PleasanterMetadataFile.FromProject(_directory));
            Assert.That(await accessor.GetCustomTableDefinitionsAsync("PL"), Is.Empty);
            Assert.That(h.Fake.Requests, Is.Empty);
        }

        [Test]
        public async Task プロジェクトのファイルを書き換えると次の読み出しから新しい内容()
        {
            var h = new FakeHarness();
            var file = PleasanterMetadataFile.FromProject(_directory);
            Assert.That(file.Load("PL"), Is.Empty);
            await PleasanterMetadataFile.UpdateAsync(h.Connection, _directory);
            Assert.That(file.Load("PL").Select(e => e.Name), Is.EquivalentTo(new[] { "Customers", "Orders", "Tasks", "Many" }));
        }

        [Test]
        public void 定義を読めなければファイルを変えない()
        {
            DesignDataFileManager.WriteProjectServerFile(_directory, FileName, System.Text.Encoding.UTF8.GetBytes("{\"Tables\":[]}"));
            var h = new FakeHarness();
            h.Fake.Override(e => e.Action == "getsite", _ => FakePleasanter.Error(System.Net.HttpStatusCode.Unauthorized, "Unauthorized"), 10);
            Assert.ThrowsAsync<PleasanterException>(() => PleasanterMetadataFile.UpdateAsync(h.Connection, _directory));
            Assert.That(System.Text.Encoding.UTF8.GetString(DesignDataFileManager.ReadProjectServerFile(_directory, FileName)!), Is.EqualTo("{\"Tables\":[]}"));
        }

        //今のフォルダをテスト用のフォルダにして呼ぶ (元のフォルダには何も書かない)
        void InCurrentDirectory(string directory, Action action)
        {
            var original = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(directory);
            try
            {
                action();
            }
            finally
            {
                Directory.SetCurrentDirectory(original);
            }
        }

        [Test]
        public async Task プロジェクトのフォルダが空なら今のフォルダ()
        {
            var project = await WriteProjectAsync();
            InCurrentDirectory(project, () =>
                Assert.That(PleasanterMetadataFile.FromProject(string.Empty).Load("PL").Select(e => e.Name), Is.EquivalentTo(new[] { "Customers", "Orders", "Tasks", "Many" })));
        }

        [Test]
        public async Task 配られたデザインのフォルダが空なら今のフォルダ()
        {
            var deploy = Deploy(await WriteProjectAsync(), new DateTime(2026, 1, 1, 10, 0, 0));
            InCurrentDirectory(deploy, () =>
                Assert.That(PleasanterMetadataFile.FromDeployedDesign(string.Empty).Load("PL").Select(e => e.Name), Is.EquivalentTo(new[] { "Customers", "Orders", "Tasks", "Many" })));
        }

        [TestCase("{\"Tables\":null}", "")]
        [TestCase("{\"Tables\":[null]}", "")]
        [TestCase("{\"Tables\":[null,{\"Name\":\"Customers\",\"ReferenceType\":\"Results\",\"Columns\":null}]}", "Customers:")]
        [TestCase("{\"Tables\":[{\"Name\":\"Customers\",\"ReferenceType\":\"Results\",\"Columns\":[null,{\"Name\":\"Title\",\"Kind\":\"String\"},{\"Name\":null},{}]}]}", "Customers:Title")]
        [TestCase("{\"Tables\":[{\"Name\":\"\",\"ReferenceType\":\"Results\"},{\"Name\":\"Wiki\",\"ReferenceType\":\"Wikis\"}]}", "")]
        public void 手で直したファイルの無い値は無いものとして読む(string json, string expected)
        {
            DesignDataFileManager.WriteProjectServerFile(_directory, FileName, System.Text.Encoding.UTF8.GetBytes(json));
            var tables = PleasanterMetadataFile.FromProject(_directory).Load("PL");
            Assert.That(string.Join(";", tables.Select(e => e.Name + ":" + string.Join(",", e.Columns.Select(c => c.Name)))), Is.EqualTo(expected));
        }

        // ---------------------------------------------------------------- 配られたデザイン (App.zip)

        //プロジェクトのフォルダを App.zip にして deploy に置く (デザイナのデプロイと同じく、サーバー専用ファイルは ServerFiles/ の下に入る)
        string Deploy(string project, DateTime lastWriteTime)
        {
            var deploy = Path.Combine(_directory, "deploy");
            Directory.CreateDirectory(deploy);
            var temporary = Path.Combine(_directory, "App.zip.tmp");
            if (File.Exists(temporary)) File.Delete(temporary);
            ZipFile.CreateFromDirectory(project, temporary);
            var zip = Path.Combine(deploy, "App.zip");
            File.Move(temporary, zip, true);
            File.SetLastWriteTime(zip, lastWriteTime);
            return deploy;
        }

        async Task<string> WriteProjectAsync(Action<PleasanterConnectionSettings>? edit = null)
        {
            var project = Path.Combine(_directory, "project");
            if (Directory.Exists(project)) Directory.Delete(project, true);
            Directory.CreateDirectory(project);
            var h = new FakeHarness(edit);
            await PleasanterMetadataFile.UpdateAsync(h.Connection, project);
            return project;
        }

        [Test]
        public async Task 配られたデザインのファイルから読み実行時にサイトの設定を取りに行かない()
        {
            var deploy = Deploy(await WriteProjectAsync(), new DateTime(2026, 1, 1, 10, 0, 0));

            var h = new FakeHarness(editAccessor: (inner, connection) => new PleasanterDbAccessor(inner, new[] { connection }, PleasanterMetadataFile.FromDeployedDesign(deploy)));
            h.Fake.Rows[FakeHarness.CustomersId] = new() { new System.Text.Json.Nodes.JsonObject { ["ResultId"] = 1, ["Title"] = "アルファ" } };
            var list = await h.IO.GetListAsync(new SearchCondition("Customer") { Condition = Value("Name.Value", MatchComparison.Like, "アル") }, 0);

            Assert.That(list.Items.Select(Id), Is.EqualTo(new[] { "1" }));
            Assert.That(h.Fake.Requests.Where(e => e.Action == "getsite"), Is.Empty);
        }

        [Test]
        public async Task 配られたデザインに無い表はエラーで実行時にサイトの設定を取りに行かない()
        {
            var project = Path.Combine(_directory, "project");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "app.clprj"), "{}");
            var deploy = Deploy(project, new DateTime(2026, 1, 1, 10, 0, 0));

            var h = new FakeHarness(editAccessor: (inner, connection) => new PleasanterDbAccessor(inner, new[] { connection }, PleasanterMetadataFile.FromDeployedDesign(deploy)));
            var e = Assert.ThrowsAsync<PleasanterException>(() => h.IO.GetListAsync(new SearchCondition("Customer"), 0))!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.NotFound));
            Assert.That(e.Message, Does.Contain("deploy the design again"));
            Assert.That(h.Fake.Requests, Is.Empty);
        }

        [Test]
        public async Task AppZipが無ければ表は無い()
        {
            var h = new FakeHarness(editAccessor: (inner, connection) => new PleasanterDbAccessor(inner, new[] { connection }, PleasanterMetadataFile.FromDeployedDesign(_directory)));
            Assert.That(await h.Accessor.GetCustomTableDefinitionsAsync("PL"), Is.Empty);
            Assert.That(h.Fake.Requests, Is.Empty);
        }

        [Test]
        public async Task AppZipが差し替わると読み直す()
        {
            //初めは Many の無い定義 (接続の Sites から Many を外して書き出す)
            var deploy = Deploy(await WriteProjectAsync(s => s.Sites.Remove("Many")), new DateTime(2026, 1, 1, 10, 0, 0));
            var file = PleasanterMetadataFile.FromDeployedDesign(deploy);
            Assert.That(file.Load("PL").Select(e => e.Name), Is.EquivalentTo(new[] { "Customers", "Orders", "Tasks" }));

            //同じ App.zip のままなら読み直さない (開かずに同じものを返す)
            var first = file.Load("PL");
            Assert.That(PleasanterMetadataFile.FromDeployedDesign(deploy).Load("PL"), Is.SameAs(first));

            Deploy(await WriteProjectAsync(), new DateTime(2026, 1, 1, 11, 0, 0));
            Assert.That(file.Load("PL").Select(e => e.Name), Is.EquivalentTo(new[] { "Customers", "Orders", "Tasks", "Many" }));
        }

        [Test]
        public async Task ファイルのサイトIDでなく接続文字列のSitesのサイトIDを使う()
        {
            var deploy = Deploy(await WriteProjectAsync(), new DateTime(2026, 1, 1, 10, 0, 0));
            //本番では同じ表が別のサイト ID
            var h = new FakeHarness(edit: s => s.Sites["Customers"] = 99,
                editAccessor: (inner, connection) => new PleasanterDbAccessor(inner, new[] { connection }, PleasanterMetadataFile.FromDeployedDesign(deploy)));
            await h.IO.GetListAsync(new SearchCondition("Customer"), 0);
            Assert.That(h.Fake.Requests.Where(e => e.Action == "get").Select(e => e.Id).Distinct(), Is.EqualTo(new[] { 99L }));
        }
    }
}
