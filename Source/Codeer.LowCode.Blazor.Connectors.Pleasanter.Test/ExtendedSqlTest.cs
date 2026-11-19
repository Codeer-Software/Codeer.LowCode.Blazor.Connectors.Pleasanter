using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Data;
using System.Net;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>ExecuteSqlField のストアドプロシージャ → プリザンターの拡張 SQL (api/extended/sql)。</summary>
    public class ExtendedSqlTest
    {
        FakeHarness _h = null!;

        [SetUp]
        public void SetUp() => _h = new FakeHarness();

        void Respond(JsonObject row)
            => _h.Fake.Override(e => e.Path == "/api/extended/sql", _ => FakePleasanter.Json(HttpStatusCode.OK, new JsonObject
            {
                ["Id"] = 0,
                ["StatusCode"] = 200,
                ["Response"] = new JsonObject { ["Data"] = new JsonObject { ["Table"] = new JsonArray(row) } },
            }.ToJsonString()));

        static DbSqlCommand Command(ExecuteSqlMethodType method, params DbSqlCommandParameter[] parameters)
        {
            var command = new DbSqlCommand { CommandText = " ClbConnectorTestEcho ", CommandType = ExecuteSqlCommandType.StoredProcedure, MethodType = method };
            command.Parameters.AddRange(parameters);
            return command;
        }

        [Test]
        public async Task 名前とパラメータとAPIキーを送り最初の行を返す()
        {
            Respond(new JsonObject { ["Text"] = "abc", ["NumberPlusOne"] = 42.5m, ["UserId"] = 1 });
            var result = await _h.Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, Command(ExecuteSqlMethodType.NonQuery,
                new DbSqlCommandParameter { Name = "@Text", Value = "abc" },
                new DbSqlCommandParameter { Name = "Number", Value = 41.5m },
                new DbSqlCommandParameter { Name = "Day", Value = new DateTime(2026, 11, 2, 10, 30, 0) },
                new DbSqlCommandParameter { Name = "Flag", Value = true },
                new DbSqlCommandParameter { Name = "Nothing", Value = null },
                new DbSqlCommandParameter { Name = "NumberPlusOne", Direction = ParameterDirection.Output },
                new DbSqlCommandParameter { Name = "@UserId", Direction = ParameterDirection.InputOutput, Value = 9 },
                new DbSqlCommandParameter { Name = "Missing", Direction = ParameterDirection.ReturnValue }));

            var request = _h.Fake.Requests.Single();
            Assert.That(request.Url, Is.EqualTo(FakePleasanter.Url + "api/extended/sql"));
            var body = request.Json;
            Assert.That(body["ApiKey"]!.GetValue<string>(), Is.EqualTo(FakePleasanter.ApiKey));
            Assert.That(body["Name"]!.GetValue<string>(), Is.EqualTo("ClbConnectorTestEcho"));
            Assert.That(body["Params"]!.ToJsonString(), Is.EqualTo("{\"Text\":\"abc\",\"Number\":41.5,\"Day\":\"2026-11-02T10:30:00\",\"Flag\":true,\"Nothing\":null,\"UserId\":9}"));

            Assert.That(result.ReturnValue, Is.EqualTo(-1));
            Assert.That(result.ParameterValues["@Text"], Is.EqualTo("abc"));
            Assert.That(result.ParameterValues["NumberPlusOne"], Is.EqualTo(42.5m));
            Assert.That(result.ParameterValues["@UserId"], Is.EqualTo(1L));
            Assert.That(result.ParameterValues["Missing"], Is.Null);
        }

        [Test]
        public async Task Scalarは最初の列の値でReaderはnull()
        {
            Respond(new JsonObject { ["UserId"] = 1, ["Name"] = "x" });
            Assert.That((await _h.Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, Command(ExecuteSqlMethodType.Scalar))).ReturnValue, Is.EqualTo(1L));
            Respond(new JsonObject { ["UserId"] = 1 });
            Assert.That((await _h.Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, Command(ExecuteSqlMethodType.Reader))).ReturnValue, Is.Null);
        }

        [Test]
        public async Task 行が無ければScalarはnull()
        {
            _h.Fake.Override(e => e.Path == "/api/extended/sql", _ => FakePleasanter.Json(HttpStatusCode.OK,
                "{\"Id\":0,\"StatusCode\":200,\"Response\":{\"Data\":{\"Table\":[]}}}"));
            Assert.That((await _h.Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, Command(ExecuteSqlMethodType.Scalar))).ReturnValue, Is.Null);
        }

        [Test]
        public void リダイレクトは結果不明でやり直さない()
        {
            _h.Fake.Override(e => e.Path == "/api/extended/sql", _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri(FakePleasanter.Url + "errors/badrequest");
                return response;
            }, 10);
            var e = Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, Command(ExecuteSqlMethodType.NonQuery)))!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.WriteResultUnknown));
            Assert.That(e.Message, Does.Contain("ClbConnectorTestEcho"));
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void サーバーの障害でもやり直さない()
        {
            _h.Fake.Override(e => e.Path == "/api/extended/sql", _ => FakePleasanter.Error(HttpStatusCode.ServiceUnavailable, "busy"), 10);
            Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, Command(ExecuteSqlMethodType.NonQuery)));
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void SQL文は実行しない()
        {
            var command = new DbSqlCommand { CommandText = "select 1", CommandType = ExecuteSqlCommandType.Sql };
            Assert.ThrowsAsync<PleasanterException>(() => _h.Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, command));
            Assert.That(_h.Fake.Requests, Is.Empty);
        }

        [Test]
        public async Task プリザンター以外のデータソースは内側に渡す()
        {
            var command = new DbSqlCommand { CommandText = "select 1", CommandType = ExecuteSqlCommandType.Sql };
            await _h.Accessor.ExecuteSqlCommandAsync("Main", command);
            Assert.That(_h.Fake.Requests, Is.Empty);
        }
    }
}
