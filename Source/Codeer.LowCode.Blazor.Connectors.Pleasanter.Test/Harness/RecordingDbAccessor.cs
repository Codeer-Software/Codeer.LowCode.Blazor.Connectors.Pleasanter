using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Data;
using System.Data.Common;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness
{
    /// <summary>プリザンター以外のデータソースを受け持つ IDbAccessor の代わり。呼ばれたことを "メソッド:データソース" で記録するだけ。</summary>
    public class RecordingDbAccessor : IDbAccessor
    {
        public List<string> Calls { get; } = new();

        /// <summary>データソースを引数に取る呼び出し (トランザクションの開始・確定などを除く)。</summary>
        public List<string> DataSourceCalls => Calls.Where(e => e.Contains(':')).ToList();

        void Record(string method, string? dataSource = null) => Calls.Add(dataSource == null ? method : $"{method}:{dataSource}");

        public DataSource? GetDataSource(string dataSource)
        {
            Record(nameof(GetDataSource), dataSource);
            return new DataSource { Name = dataSource, DataSourceType = DataSourceType.SQLite, ConnectionString = "Data Source=inner.db" };
        }

        public void StartTransaction() => Record(nameof(StartTransaction));
        public Task CommitAsync() { Record(nameof(CommitAsync)); return Task.CompletedTask; }
        public Task RollbackAsync() { Record(nameof(RollbackAsync)); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Record(nameof(DisposeAsync)); return ValueTask.CompletedTask; }

        public DbConnection GetConnection(string dataSourceName)
        {
            Record(nameof(GetConnection), dataSourceName);
            throw new NotSupportedException("inner");
        }

        public IDbTransaction? GetTransaction(string dataSourceName)
        {
            Record(nameof(GetTransaction), dataSourceName);
            return null;
        }

        public Task<List<IDictionary<string, object>>> QueryAsync(string dataSourceName, string query, Dictionary<string, ParamAndRawDbTypeName> args)
        {
            Record(nameof(QueryAsync), dataSourceName);
            return Task.FromResult(new List<IDictionary<string, object>> { new Dictionary<string, object> { ["inner"] = "row" } });
        }

        public Task<int> ExecuteAsync(string dataSourceName, string query, Dictionary<string, object?> args)
        {
            Record(nameof(ExecuteAsync), dataSourceName);
            return Task.FromResult(7);
        }

        public Task<string> InsertAsync(string dataSourceName, string query, Dictionary<string, object?> args)
        {
            Record(nameof(InsertAsync), dataSourceName);
            return Task.FromResult("inner-id");
        }

        public Task<List<DbTableDefinition>?> GetCustomTableDefinitionsAsync(string dataSourceName)
        {
            Record(nameof(GetCustomTableDefinitionsAsync), dataSourceName);
            return Task.FromResult<List<DbTableDefinition>?>(new() { new DbTableDefinition { Name = "inner_table" } });
        }

        public Task<DbTableDefinition?> GetCustomTableDefinitionsAsync(string dataSourceName, string tableNames)
        {
            Record(nameof(GetCustomTableDefinitionsAsync) + "1", dataSourceName);
            return Task.FromResult<DbTableDefinition?>(new DbTableDefinition { Name = tableNames });
        }

        public object? ConvertFieldValueToDbValue(string dataSourceName, string rawDbTypeName, object? value)
        {
            Record(nameof(ConvertFieldValueToDbValue), dataSourceName);
            return "inner-converted";
        }

        public object? ConvertDbValueToFieldValue(string dataSourceName, object dbValue, Type fieldType)
        {
            Record(nameof(ConvertDbValueToFieldValue), dataSourceName);
            return "inner-converted";
        }

        public Task<DbSqlCommandResult> ExecuteSqlCommandAsync(string dataSourceName, DbSqlCommand command)
        {
            Record(nameof(ExecuteSqlCommandAsync), dataSourceName);
            return Task.FromResult(new DbSqlCommandResult { ReturnValue = 1 });
        }
    }
}
