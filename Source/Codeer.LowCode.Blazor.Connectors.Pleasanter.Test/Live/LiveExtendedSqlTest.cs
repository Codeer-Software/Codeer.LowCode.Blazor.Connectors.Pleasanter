using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Data;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>
    /// 実物のプリザンターの拡張 SQL をストアドプロシージャとして実行する。プリザンター側に ClbConnectorTestEcho
    /// (Text をそのまま・Number に 1 を足した NumberPlusOne・利用者の UserId を返す) と ClbWhoAmI (本人の TenantId / UserId を返す) を
    /// API から使える (Api: true) ように置いておく。無ければ Ignore。
    /// </summary>
    public class LiveExtendedSqlTest
    {
        static async Task<DbSqlCommandResult> ExecuteAsync(DbSqlCommand command)
        {
            try
            {
                return await new LiveHarness().Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, command);
            }
            catch (PleasanterException e) when (e.StatusCode is { } status && 300 <= (int)status && (int)status < 400)
            {
                Assert.Ignore($"The extended SQL '{command.CommandText}' is not usable on this Pleasanter: {e.Message}");
                throw;
            }
        }

        [Test]
        public async Task 拡張SQLにパラメータを渡し結果の列を受け取る()
        {
            var command = new DbSqlCommand { CommandText = "ClbConnectorTestEcho", CommandType = ExecuteSqlCommandType.StoredProcedure, MethodType = ExecuteSqlMethodType.NonQuery };
            command.Parameters.Add(new DbSqlCommandParameter { Name = "@Text", Value = "abc 'テスト'" });
            command.Parameters.Add(new DbSqlCommandParameter { Name = "@Number", Value = 41.5m });
            command.Parameters.Add(new DbSqlCommandParameter { Name = "@NumberPlusOne", Direction = ParameterDirection.Output });
            command.Parameters.Add(new DbSqlCommandParameter { Name = "@UserId", Direction = ParameterDirection.Output });

            var result = await ExecuteAsync(command);

            Assert.That(result.ReturnValue, Is.EqualTo(-1));
            Assert.That(result.ParameterValues["@Text"], Is.EqualTo("abc 'テスト'"));
            Assert.That(result.ParameterValues["@NumberPlusOne"], Is.EqualTo(42.5m));
            Assert.That(result.ParameterValues["@UserId"], Is.EqualTo(LiveFixture.ApiUserId));
        }

        [Test]
        public async Task 拡張SQLのScalarは最初の列()
        {
            var command = new DbSqlCommand { CommandText = "ClbWhoAmI", CommandType = ExecuteSqlCommandType.StoredProcedure, MethodType = ExecuteSqlMethodType.Scalar };
            command.Parameters.Add(new DbSqlCommandParameter { Name = "UserId", Direction = ParameterDirection.Output });

            var result = await ExecuteAsync(command);

            Assert.That(result.ReturnValue, Is.Not.Null);
            Assert.That(result.ParameterValues["UserId"], Is.EqualTo(LiveFixture.ApiUserId));
        }

        [Test]
        public void 無い拡張SQLは結果不明のエラー()
        {
            var command = new DbSqlCommand { CommandText = "ClbNoSuchExtendedSql", CommandType = ExecuteSqlCommandType.StoredProcedure, MethodType = ExecuteSqlMethodType.NonQuery };
            var e = Assert.ThrowsAsync<PleasanterException>(() => new LiveHarness().Accessor.ExecuteSqlCommandAsync(TestDesign.DataSourceName, command))!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.WriteResultUnknown));
        }
    }
}
