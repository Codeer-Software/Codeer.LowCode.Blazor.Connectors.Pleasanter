using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;
using System.IO.Compression;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>
    /// 実物のサイトの設定から書いたメタ情報のファイルを App.zip に入れて配り、ファイルだけを使うコネクターで一覧・書き込みができることを確かめる。
    /// </summary>
    public class LiveMetadataFileTest
    {
        string _directory = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "clb-pleasanter-live-meta-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public async Task 実物から書いたファイルを配りファイルだけで一覧と書き込みができる()
        {
            //デザイナ: プロジェクトのサーバー専用ファイルに書く
            var project = Path.Combine(_directory, "project");
            Directory.CreateDirectory(project);
            await PleasanterMetadataFile.UpdateAsync(new PleasanterConnection(LiveFixture.CreateSettings()), project);
            var fileName = PleasanterMetadataFile.GetFileName("PL");
            var text = System.Text.Encoding.UTF8.GetString(DesignDataFileManager.ReadProjectServerFile(project, fileName)!);
            Assert.That(text, Does.Contain("\"Customers\""));
            Assert.That(text, Does.Contain("ゴールド"));
            Assert.That(text, Does.Not.Contain(LiveFixture.Url.TrimEnd('/')), "接続先を書き出さない");

            //デプロイ: プロジェクトを App.zip にして置く
            var deploy = Path.Combine(_directory, "deploy");
            Directory.CreateDirectory(deploy);
            ZipFile.CreateFromDirectory(project, Path.Combine(deploy, "App.zip"));
            var h = new LiveHarness(metadataFile: PleasanterMetadataFile.FromDeployedDesign(deploy));
            Assert.That(PleasanterMetadataFile.FromDeployedDesign(deploy).Load("PL").Select(e => e.Name), Is.EquivalentTo(new[] { "Customers", "Orders", "Tasks", "Many" }));

            //一覧 (結合先の列の条件・リンクの表示名を含む)
            var orders = await h.IO.GetListAsync(new SearchCondition("Order") { Condition = Value("Customer.Name.Value", MatchComparison.Like, "アルファ") }, 0);
            Assert.That(orders.Items.Select(Id), Is.EquivalentTo(new[] { "o1", "o2" }.Select(e => LiveFixture.Orders[e].ToString(CultureInfo.InvariantCulture))));

            //作成・更新・削除
            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = "Customer" };
            data.Fields["Id"] = tempId;
            data.Fields["Name"] = new TextFieldData { Value = "ファイルの定義で作成" };
            var added = (await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Customer", Id = tempId.Value!, Add = new() { data } } })).Single();
            Assert.That(added.ExceptionMessage, Is.Empty, added.ExceptionMessage);
            var id = added.DestinationId;

            var update = new ModuleData { Name = "Customer" };
            update.Fields["Id"] = new IdFieldData { Value = id };
            update.Fields["Code"] = new TextFieldData { Value = "F-001" };
            var updated = (await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Customer", Id = id, Update = new() { update } } })).Single();
            Assert.That(updated.ExceptionMessage, Is.Empty, updated.ExceptionMessage);
            var row = (await LiveFixture.GetRecordAsync(LiveFixture.CustomersId, long.Parse(id, CultureInfo.InvariantCulture)))!;
            Assert.That(row["ClassHash"]!["ClassA"]!.GetValue<string>(), Is.EqualTo("F-001"));

            var deleted = (await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Customer", Id = id, Delete = new() { new ModuleDeleteInfo { ModuleName = "Customer", Id = id } } } })).Single();
            Assert.That(deleted.ExceptionMessage, Is.Empty, deleted.ExceptionMessage);
            Assert.That(await LiveFixture.GetRecordAsync(LiveFixture.CustomersId, long.Parse(id, CultureInfo.InvariantCulture)), Is.Null);
        }
    }
}
