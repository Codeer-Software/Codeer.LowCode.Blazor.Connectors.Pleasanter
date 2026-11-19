using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;
using System.Text.Json.Nodes;
using static Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness.TestUtil;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>実物のプリザンターのコメント: 書くと 1 件足され、読むとコメントの JSON の文字。行はテストの中で作って消す。</summary>
    public class LiveCommentsTest
    {
        static LiveHarness Harness() => new(editDesign: d => d.Modules.Find("Customer")!.Fields.Add(new TextFieldDesign { Name = "Comments", DbColumn = "Comments" }));

        [Test]
        public async Task 書くとコメントが足され読むとJSONの文字()
        {
            var h = Harness();
            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = "Customer" };
            data.Fields["Id"] = tempId;
            data.Fields["Name"] = new TextFieldData { Value = "コメントのテスト" };
            data.Fields["Comments"] = new TextFieldData { Value = "最初のコメント" };
            var added = (await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Customer", Id = tempId.Value!, Add = new() { data } } })).Single();
            Assert.That(added.ExceptionMessage, Is.Empty, added.ExceptionMessage);
            var id = added.DestinationId;
            try
            {
                var update = new ModuleData { Name = "Customer" };
                update.Fields["Id"] = new IdFieldData { Value = id };
                update.Fields["Comments"] = new TextFieldData { Value = "二つ目のコメント" };
                var updated = (await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Customer", Id = id, Update = new() { update } } })).Single();
                Assert.That(updated.ExceptionMessage, Is.Empty, updated.ExceptionMessage);

                //空にしても消えない
                var empty = new ModuleData { Name = "Customer" };
                empty.Fields["Id"] = new IdFieldData { Value = id };
                empty.Fields["Comments"] = new TextFieldData { Value = null };
                var emptied = (await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Customer", Id = id, Update = new() { empty } } })).Single();
                Assert.That(emptied.ExceptionMessage, Is.Empty, emptied.ExceptionMessage);

                var read = (await h.IO.GetListAsync(new SearchCondition("Customer") { Condition = Value("Id.Value", MatchComparison.Equal, id) }, 0)).Items.Single();
                var comments = JsonNode.Parse(Field<TextFieldData>(read, "Comments")!.Value!)!.AsArray();
                Assert.That(comments.Select(e => e!["Body"]!.GetValue<string>()), Is.EqualTo(new[] { "二つ目のコメント", "最初のコメント" }));
            }
            finally
            {
                await h.IO.SubmitWithTransactionAsync(new() { new ModuleSubmitData { ModuleName = "Customer", Id = id, Delete = new() { new ModuleDeleteInfo { ModuleName = "Customer", Id = id } } } });
            }
        }

        [Test]
        public void 条件には使えない()
            => Assert.ThrowsAsync<PleasanterException>(() => Harness().IO.GetListAsync(new SearchCondition("Customer") { Condition = Value("Comments.Value", MatchComparison.Like, "x") }, 0));
    }
}
