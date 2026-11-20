using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>添付のフィールド (クライアント用パッケージ)。後半は本物の Module に載せて読み直さない保存の後の状態を確かめる。</summary>
    public class AttachmentsFieldTest
    {
        [Test]
        public void サーバーが名前で見る型の名前と一致する()
            => Assert.That(typeof(PleasanterAttachmentsFieldDesign).FullName, Is.EqualTo(PleasanterDbAccessor.AttachmentsFieldDesignTypeFullName));

        [Test]
        public void デザインとデータはJSONで型ごと往復する()
        {
            PleasanterFieldsInitializer.Initialize();
            var design = new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA", MaxFileSizeMB = 3 };
            var restored = (PleasanterAttachmentsFieldDesign)JsonConverterEx.DeserializeObject<FieldDesignBase>(JsonConverterEx.SerializeObject(design, typeof(FieldDesignBase)))!;
            Assert.That((restored.DbColumn, restored.MaxFileSizeMB), Is.EqualTo(("AttachmentsA", 3)));

            var data = new PleasanterAttachmentsFieldData { Value = "[]" };
            var restoredData = (PleasanterAttachmentsFieldData)JsonConverterEx.DeserializeObject<FieldDataBase>(JsonConverterEx.SerializeObject(data, typeof(FieldDataBase)))!;
            Assert.That(restoredData.Value, Is.EqualTo("[]"));
        }

        [Test]
        public void 既定の上限は10MBで検索の部品を持たない()
        {
            var design = new PleasanterAttachmentsFieldDesign();
            Assert.That(design.MaxFileSizeMB, Is.EqualTo(10));
            Assert.That(design.GetSearchWebComponentTypeFullName(), Is.Empty);
            Assert.That(design.GetSearchControlTypeFullName(), Is.Empty);
        }

        [Test]
        public async Task 読んだ値は変更なしでファイルの並びになる()
        {
            var field = new PleasanterAttachmentsField(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
            await field.InitializeDataAsync(new PleasanterAttachmentsFieldData { Value = "[{\"Guid\":\"0123456789abcdef0123456789abcdef\",\"Name\":\"a.txt\",\"Size\":3}]" });
            Assert.That(field.IsModified, Is.False);
            Assert.That(field.GetSubmitData().FieldData, Is.Null);
            Assert.That(field.Entries.Select(e => (e.Name, e.Size, e.IsAdded, e.Deleted)), Is.EqualTo(new[] { ("a.txt", (long?)3, false, false) }));
        }

        [Test]
        public void レコードのコピーでは写さない()
            => Assert.That(((IConfirmCopyFieldDesign)new PleasanterAttachmentsFieldDesign()).NeedCopy, Is.False);

        [Test]
        public async Task 消す印を付けられるのはGuidの分かる読んだファイルだけ()
        {
            var field = new PleasanterAttachmentsField(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
            //読んだファイル (Guid あり)・読み直さない保存の後の足したファイル (名前と大きさだけ)・足したファイル (Base64)
            await field.InitializeDataAsync(new PleasanterAttachmentsFieldData
            {
                Value = "[{\"Guid\":\"0123456789abcdef0123456789abcdef\",\"Name\":\"a.txt\",\"Size\":3},{\"Name\":\"b.txt\",\"Size\":4},{\"Name\":\"c.txt\",\"Base64\":\"YWJj\"}]",
            });
            Assert.That(field.Entries.Select(PleasanterAttachmentsField.CanDelete), Is.EqualTo(new[] { true, false, false }));
        }

        [Test]
        public async Task Guidの分からないファイルに消す印を付けようとしても変わらない()
        {
            var field = new PleasanterAttachmentsField(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
            await field.InitializeDataAsync(new PleasanterAttachmentsFieldData { Value = "[{\"Name\":\"b.txt\",\"Size\":4}]" });
            await field.SetDeletedAsync(field.Entries.Single(), true);
            Assert.That(field.Entries.Single().Deleted, Is.False);
            Assert.That(field.IsModified, Is.False);
            Assert.That(field.GetSubmitData().FieldData, Is.Null);
        }

        //---- 読み直さない保存 (自動保存) の後の状態。本物の Module に載せ、自動保存の送信の間に画面の操作をする ----

        const string GuidA = "0123456789abcdef0123456789abcdef";

        static DesignData CreateDesign()
        {
            var design = new DesignData();
            var module = new ModuleDesign { Name = "Project", DataSourceName = "Pleasanter", DbTable = "Projects" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "ResultId" });
            module.Fields.Add(new PleasanterAttachmentsFieldDesign { Name = "Files", DbColumn = "AttachmentsA" });
            module.Fields.Add(new AutoSubmitFieldDesign { Name = "Auto", DelayMilliseconds = 0 });
            ((IEditableModuleDesign)design.Modules).Add(module);
            return design;
        }

        //読んだファイル a.txt (Guid A) が 1 つあるレコード
        static async Task<(FieldHarness Harness, PleasanterAttachmentsField Field, AutoSubmitField Auto)> CreateAsync()
        {
            var harness = new FieldHarness(CreateDesign());
            var data = new ModuleData { Name = "Project" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Files"] = new PleasanterAttachmentsFieldData { Value = $"[{{\"Guid\":\"{GuidA}\",\"Name\":\"a.txt\",\"Size\":3}}]" };
            var module = await harness.CreateModuleAsync(data);
            return (harness, module.GetField<PleasanterAttachmentsField>("Files")!, module.GetField<AutoSubmitField>("Auto")!);
        }

        static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

        static string B64(string text) => Convert.ToBase64String(Bytes(text));

        //送った値 (添付のフィールドの JSON)
        static string SentValue(FieldHarness harness, int index)
            => ((PleasanterAttachmentsFieldData)harness.Submitted[index].Single().Update.Single().Fields["Files"]).Value!;

        static string? SubmitValue(PleasanterAttachmentsField field)
            => (field.GetSubmitData().FieldData as PleasanterAttachmentsFieldData)?.Value;

        [Test]
        public async Task 送った値と今の値が同じなら送った追加と削除が保存済みになる()
        {
            var (harness, field, auto) = await CreateAsync();
            Assert.That(await field.AddFileAsync("x.txt", "text/plain", Bytes("xx")), Is.True);
            await field.SetDeletedAsync(field.Entries[0], true);

            await FieldHarness.AutoSubmitAsync(auto);

            Assert.That(harness.Submitted, Has.Count.EqualTo(1));
            Assert.That(SentValue(harness, 0), Does.Contain(B64("xx")).And.Contain(GuidA));
            //消したファイルは外れ、足したファイルは名前と大きさだけ (Guid なし = 読み直すまで消せない・リンクなし)
            Assert.That(field.Entries.Select(e => (e.Name, e.Size, e.IsAdded, e.Deleted, e.Guid)), Is.EqualTo(new[] { ("x.txt", (long?)2, false, false, (string?)null) }));
            Assert.That(field.IsModified, Is.False);
            Assert.That(field.GetSubmitData().FieldData, Is.Null);
        }

        [Test]
        public async Task 送信の間に足したファイルだけが変更として残り先のファイルは二度送らない()
        {
            var (harness, field, auto) = await CreateAsync();
            await field.AddFileAsync("x.txt", "text/plain", Bytes("xx"));
            harness.DuringSubmit = async () =>
            {
                harness.DuringSubmit = null;
                await field.AddFileAsync("y.txt", "text/plain", Bytes("yyy"));
            };

            await FieldHarness.AutoSubmitAsync(auto);

            Assert.That(SentValue(harness, 0), Does.Contain(B64("xx")).And.Not.Contain(B64("yyy")));
            Assert.That(field.Entries.Select(e => (e.Name, e.IsAdded)), Is.EqualTo(new[] { ("a.txt", false), ("x.txt", false), ("y.txt", true) }));
            Assert.That(field.IsModified, Is.True);
            Assert.That(SubmitValue(field), Does.Contain(B64("yyy")).And.Not.Contain(B64("xx")));

            //次の保存で送るのは後のファイルだけ
            await FieldHarness.AutoSubmitAsync(auto);
            Assert.That(harness.Submitted, Has.Count.EqualTo(2));
            Assert.That(SentValue(harness, 1), Does.Contain(B64("yyy")).And.Not.Contain(B64("xx")));
            Assert.That(field.Entries.Select(e => (e.Name, e.IsAdded)), Is.EqualTo(new[] { ("a.txt", false), ("x.txt", false), ("y.txt", false) }));
            Assert.That(field.IsModified, Is.False);
        }

        [Test]
        public async Task 送信の間に付けた消す印は変更として残る()
        {
            var (harness, field, auto) = await CreateAsync();
            await field.AddFileAsync("x.txt", "text/plain", Bytes("xx"));
            harness.DuringSubmit = async () =>
            {
                harness.DuringSubmit = null;
                await field.SetDeletedAsync(field.Entries[0], true);
            };

            await FieldHarness.AutoSubmitAsync(auto);

            Assert.That(SentValue(harness, 0), Does.Not.Contain("Deleted"));
            Assert.That(field.Entries.Select(e => (e.Name, e.IsAdded, e.Deleted)), Is.EqualTo(new[] { ("a.txt", false, true), ("x.txt", false, false) }));
            Assert.That(field.IsModified, Is.True);
            var value = SubmitValue(field);
            Assert.That(value, Does.Contain(GuidA).And.Contain("\"Deleted\":true").And.Not.Contain(B64("xx")));

            //印を外せば変更なし (保存済みの状態には消す印が無い)
            await field.SetDeletedAsync(field.Entries[0], false);
            Assert.That(field.IsModified, Is.False);
        }

        [Test]
        public async Task 同じ名前と中身のファイルを2つ足して1つだけ送っていたら1つだけ保存済みになる()
        {
            var (harness, field, auto) = await CreateAsync();
            await field.AddFileAsync("x.txt", "text/plain", Bytes("xx"));
            harness.DuringSubmit = async () =>
            {
                harness.DuringSubmit = null;
                await field.AddFileAsync("x.txt", "text/plain", Bytes("xx"));
            };

            await FieldHarness.AutoSubmitAsync(auto);

            Assert.That(field.Entries.Select(e => (e.Name, e.IsAdded)), Is.EqualTo(new[] { ("a.txt", false), ("x.txt", false), ("x.txt", true) }));
            Assert.That(field.IsModified, Is.True);
            var value = SubmitValue(field)!;
            Assert.That(value.Split(B64("xx")).Length - 1, Is.EqualTo(1), "送るのは残った 1 つだけ");
        }

        [Test]
        public async Task 送信の間に取り消した足すファイルは名前だけの保存済みの行になる()
        {
            var (harness, field, auto) = await CreateAsync();
            await field.AddFileAsync("x.txt", "text/plain", Bytes("xx"));
            harness.DuringSubmit = async () =>
            {
                harness.DuringSubmit = null;
                await field.RemoveAddedAsync(field.Entries.Single(e => e.IsAdded));
            };

            await FieldHarness.AutoSubmitAsync(auto);

            //サーバーには付いたので、画面にも名前だけの行 (中身なし・Guid なし) として出す
            Assert.That(SentValue(harness, 0), Does.Contain(B64("xx")));
            Assert.That(field.Entries.Select(e => (e.Name, e.IsAdded, e.Deleted, e.Guid)),
                Is.EqualTo(new[] { ("a.txt", false, false, (string?)GuidA), ("x.txt", false, false, null) }));
            Assert.That(field.IsModified, Is.False);
            Assert.That(field.GetSubmitData().FieldData, Is.Null);
        }

        [Test]
        public async Task データありで初期化した後にデータなしで読み直すと空になり変更なし()
        {
            //ファイルのあるレコードを開き、最後の 1 つに消す印を付ける
            var (_, field, _) = await CreateAsync();
            await field.SetDeletedAsync(field.Entries.Single(), true);
            Assert.That(field.IsModified, Is.True);

            //読み直したレコードに列のキーが無い (値が null の列) と、本体は InitializeDataAsync(null) を呼ぶ
            await field.InitializeDataAsync(null);

            Assert.That(field.Entries, Is.Empty);
            Assert.That(field.IsModified, Is.False);
            Assert.That(field.GetSubmitData().FieldData, Is.Null);
        }

        [Test]
        public async Task 送った値に追加も削除も無ければ何も変えない()
        {
            var (harness, field, auto) = await CreateAsync();
            //読んだファイルだけの値 (名前を変えた) = 変更はあるが送る追加・削除は無い
            var renamed = $"[{{\"Guid\":\"{GuidA}\",\"Name\":\"b.txt\",\"Size\":3}}]";
            await field.SetDataAsync(new PleasanterAttachmentsFieldData { Value = renamed });
            Assert.That(field.IsModified, Is.True);
            harness.DuringSubmit = async () =>
            {
                harness.DuringSubmit = null;
                await field.AddFileAsync("y.txt", "text/plain", Bytes("yyy"));
            };

            await FieldHarness.AutoSubmitAsync(auto);

            Assert.That(SentValue(harness, 0), Is.EqualTo(renamed));
            Assert.That(field.Entries.Select(e => (e.Name, e.IsAdded, e.Guid)), Is.EqualTo(new[] { ("b.txt", false, (string?)GuidA), ("y.txt", true, null) }));
            Assert.That(field.IsModified, Is.True);
            Assert.That(SubmitValue(field), Does.Contain(B64("yyy")));
        }
    }
}
