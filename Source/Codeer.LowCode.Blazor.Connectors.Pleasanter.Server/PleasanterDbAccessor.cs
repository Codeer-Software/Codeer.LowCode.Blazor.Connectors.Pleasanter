using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>
    /// プリザンターをデータソースとして見せる IDbAccessor。プリザンター用のデータソースへの読み書きはプリザンターの API へ、
    /// それ以外のデータソースは包んだ IDbAccessor へそのまま渡す。
    /// プリザンターへの書き込みはトランザクションに入らない (1 件ごとに確定し、ロールバックで戻らない)。
    /// </summary>
    public class PleasanterDbAccessor : IDbAccessor
    {
        readonly IDbAccessor _inner;
        readonly Dictionary<string, PleasanterConnection> _connections;
        readonly string? _metadataDirectory;

        /// <param name="inner">プリザンター以外のデータソースを受け持つ IDbAccessor。</param>
        /// <param name="connections">プリザンターへの接続 (<see cref="PleasanterConnection.FromDataSources"/>)。</param>
        /// <param name="metadataDirectory">
        /// 指定すると、表の定義をこのフォルダに保存したファイル (<see cref="PleasanterMetadataFile"/>) から答える (プリザンターに取りに行かない)。デザイナ用。
        /// </param>
        public PleasanterDbAccessor(IDbAccessor inner, IEnumerable<PleasanterConnection> connections, string? metadataDirectory = null)
        {
            _inner = inner;
            _connections = connections.ToDictionary(e => e.Settings.Name);
            _metadataDirectory = metadataDirectory;
        }

        PleasanterConnection? Find(string dataSourceName)
            => _connections.TryGetValue(dataSourceName, out var connection) ? connection : null;

        //保存してある定義。ファイルから答えない設定なら null
        List<PleasanterTableDefinition>? LoadSavedDefinitions(PleasanterConnection connection)
            => string.IsNullOrEmpty(_metadataDirectory) ? null : PleasanterMetadataFile.Load(_metadataDirectory, connection.Settings.Name) ?? new();

        public DataSource? GetDataSource(string dataSource)
        {
            if (Find(dataSource) == null) return _inner.GetDataSource(dataSource);
            //本体には PostgreSQL として見せる (識別子を "..." で囲み、limit / offset でページングする方言)。接続の設定は返さない
            return new DataSource { Name = dataSource, DataSourceType = DataSourceType.PostgreSQL };
        }

        public void StartTransaction() => _inner.StartTransaction();
        public Task CommitAsync() => _inner.CommitAsync();
        public Task RollbackAsync() => _inner.RollbackAsync();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
        public DbTableDefinitionCache? DbTableDefinitionCache => _inner.DbTableDefinitionCache;

        public int CommandTimeoutSeconds
        {
            get => _inner.CommandTimeoutSeconds;
            set => _inner.CommandTimeoutSeconds = value;
        }

        public DbConnection GetConnection(string dataSourceName)
        {
            if (Find(dataSourceName) == null) return _inner.GetConnection(dataSourceName);
            throw new PleasanterException("A Pleasanter data source has no database connection (SQL cannot be executed on it directly).");
        }

        public IDbTransaction? GetTransaction(string dataSourceName)
            => Find(dataSourceName) == null ? _inner.GetTransaction(dataSourceName) : null;

        public object? ConvertFieldValueToDbValue(string dataSourceName, string rawDbTypeName, object? value)
            => Find(dataSourceName) == null ? _inner.ConvertFieldValueToDbValue(dataSourceName, rawDbTypeName, value) : value;

        public object? ConvertDbValueToFieldValue(string dataSourceName, object dbValue, Type fieldType)
            => Find(dataSourceName) == null ? _inner.ConvertDbValueToFieldValue(dataSourceName, dbValue, fieldType) : null;

        /// <summary>プリザンターのデータソースでは SQL・ストアドプロシージャを実行できない (ExecuteSqlField は未対応)。</summary>
        public async Task<DbSqlCommandResult> ExecuteSqlCommandAsync(string dataSourceName, DbSqlCommand command)
        {
            if (Find(dataSourceName) == null) return await _inner.ExecuteSqlCommandAsync(dataSourceName, command);
            throw new PleasanterException("SQL and stored procedures cannot be executed on a Pleasanter data source.");
        }

        // ---------------------------------------------------------------- 定義

        public async Task<List<DbTableDefinition>?> GetCustomTableDefinitionsAsync(string dataSourceName)
        {
            var connection = Find(dataSourceName);
            if (connection == null) return await _inner.GetCustomTableDefinitionsAsync(dataSourceName);
            if (LoadSavedDefinitions(connection) is { } saved) return saved.Select(e => e.ToTableDefinition()).ToList();
            return (await connection.GetTableDefinitionsAsync()).Select(e => e.ToTableDefinition()).ToList();
        }

        public async Task<DbTableDefinition?> GetCustomTableDefinitionsAsync(string dataSourceName, string tableNames)
        {
            var connection = Find(dataSourceName);
            if (connection == null) return await _inner.GetCustomTableDefinitionsAsync(dataSourceName, tableNames);
            if (LoadSavedDefinitions(connection) is { } saved)
            {
                var definition = saved.FirstOrDefault(e => e.Name == tableNames);
                if (definition == null)
                    throw new PleasanterException($"Table '{tableNames}' is not in the saved Pleasanter metadata of data source '{dataSourceName}'. Update the Pleasanter metadata.");
                return definition.ToTableDefinition();
            }
            return (await connection.GetTableDefinitionAsync(tableNames)).ToTableDefinition();
        }

        // ---------------------------------------------------------------- 読み出し

        public async Task<List<IDictionary<string, object>>> QueryAsync(string dataSourceName, string query, Dictionary<string, ParamAndRawDbTypeName> args)
        {
            var connection = Find(dataSourceName);
            if (connection == null) return await _inner.QueryAsync(dataSourceName, query, args);
            return await QueryCoreAsync(connection, query, args.ToDictionary(e => e.Key, e => e.Value.Value));
        }

        static async Task<List<IDictionary<string, object>>> QueryCoreAsync(PleasanterConnection connection, string query, Dictionary<string, object?> args)
        {
            if (SqlTranslator.IsAggregate(query))
                return await QueryExecutor.AggregateAsync(connection, await SqlTranslator.TranslateAggregateAsync(query, args, connection.GetTableDefinitionAsync));
            return await QueryExecutor.ReadAsync(connection, await SqlTranslator.TranslateSelectAsync(query, args, connection.GetTableDefinitionAsync));
        }

        // ---------------------------------------------------------------- 書き込み

        public async Task<string> InsertAsync(string dataSourceName, string query, Dictionary<string, object?> args)
        {
            var connection = Find(dataSourceName);
            if (connection == null) return await _inner.InsertAsync(dataSourceName, query, args);

            var insert = SqlTranslator.ParseInsert(query);
            if (insert.Rows.Count != 1) throw SqlTranslator.NotSupported(query);
            return await InsertRowAsync(connection, insert, insert.Rows[0], args);
        }

        public async Task<int> ExecuteAsync(string dataSourceName, string query, Dictionary<string, object?> args)
        {
            var connection = Find(dataSourceName);
            if (connection == null) return await _inner.ExecuteAsync(dataSourceName, query, args);

            if (SqlTranslator.IsUpdate(query))
            {
                var update = SqlTranslator.ParseUpdate(query);
                var definition = await GetWritableDefinitionAsync(connection, update);
                var body = CreateBody(connection, definition, update.Values, args, false);
                var id = GetId(definition, update, args);
                if (!await ExistsAsync(connection, update, args)) return 0;
                if (body.Count != 0) await connection.UpdateAsync(id, body);
                return 1;
            }
            if (SqlTranslator.IsDelete(query))
            {
                var delete = SqlTranslator.ParseDelete(query);
                var definition = await GetWritableDefinitionAsync(connection, delete);
                var id = GetId(definition, delete, args);
                if (!await ExistsAsync(connection, delete, args)) return 0;
                await connection.DeleteAsync(id);
                return 1;
            }
            if (SqlTranslator.IsInsert(query))
            {
                //一括の追加 (1 件ずつ作る。途中で失敗しても、そこまでに作った行は残る)
                var insert = SqlTranslator.ParseInsert(query);
                var created = new List<string>();
                foreach (var row in insert.Rows)
                {
                    try
                    {
                        created.Add(await InsertRowAsync(connection, insert, row, args));
                    }
                    catch (PleasanterException e) when (1 < insert.Rows.Count)
                    {
                        throw new PleasanterException($"Inserting records into '{insert.Table}' failed: {created.Count} of {insert.Rows.Count} records had been created" +
                            (created.Count == 0 ? "." : $" (ids: {string.Join(", ", created)}).") + " " + e.Message, e.Kind, e.StatusCode, e.ApiStatusCode, e);
                    }
                }
                return insert.Rows.Count;
            }
            throw SqlTranslator.NotSupported(query);
        }

        static async Task<string> InsertRowAsync(PleasanterConnection connection, InsertPlan insert, List<string> parameters, Dictionary<string, object?> args)
        {
            var definition = await connection.GetTableDefinitionAsync(insert.Table);
            if (!string.IsNullOrEmpty(insert.ReturningColumn) && insert.ReturningColumn != definition.KeyColumn) throw IdNotKey(definition);
            var body = CreateBody(connection, definition, insert.Columns.Zip(parameters).ToList(), args, true);
            var id = await connection.CreateAsync(definition, body);
            return id.ToString(CultureInfo.InvariantCulture);
        }

        static async Task<PleasanterTableDefinition> GetWritableDefinitionAsync(PleasanterConnection connection, WritePlan write)
        {
            var definition = await connection.GetTableDefinitionAsync(write.Table);
            if (write.KeyColumn != definition.KeyColumn) throw IdNotKey(definition);
            return definition;
        }

        static PleasanterException IdNotKey(PleasanterTableDefinition definition)
            => new($"A record of Pleasanter table '{definition.Name}' can only be written by its id. Bind the Id field to the column '{definition.KeyColumn}'.");

        static long GetId(PleasanterTableDefinition definition, WritePlan write, Dictionary<string, object?> args)
        {
            var value = args.TryGetValue(write.KeyParameter, out var v) ? v : null;
            if (value != null && value is not DBNull && PleasanterValues.TryConvert(PleasanterColumnKind.Number, value, out var converted) &&
                converted is decimal d && d == decimal.Truncate(d) && 0 < d && d <= long.MaxValue)
                return (long)d;
            throw new PleasanterException($"The id of the Pleasanter record to write is missing or invalid (table '{definition.Name}').", PleasanterErrorKind.InvalidRequest);
        }

        //書き込みの前に、サイトの URL + ID の条件 (+ SQL に付いていたほかの条件 = 楽観ロック・論理削除など) で対象の行を数えて確かめる。
        //レコードの URL (api/items/{id}/update) は別のサイトの行でも通るので、いつも確かめる。確かめてから書くまでの間に別の更新が入ることは防げない
        static async Task<bool> ExistsAsync(PleasanterConnection connection, WritePlan write, Dictionary<string, object?> args)
        {
            var rows = await QueryCoreAsync(connection, $"select count(*) from \"{write.Table}\" where {write.WhereSql}", args);
            var count = rows.FirstOrDefault()?.Values.FirstOrDefault();
            return count != null && 0 < Convert.ToDecimal(count, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 要求の本文 (標準の列はそのまま、拡張の列は ClassHash などの中)。読み取り専用の列は、作成で値が null なら送らず、値があればエラー。更新の対象に入っていればエラー。
        /// 作成では値が null の列は送らない (プリザンターの既定値になる)。
        /// 更新で値が null の列は、プリザンターが空として受け付ける値を送る (null を送ると無視される・400 になる列があるため):
        /// 文字 → ""・数値 → null (null を持てない数値の列は 0 になる)・日付 → 空の日付 (EmptyDate)・チェック → false・Status / Manager / Owner など null を持てない標準の数値 → 0。
        /// </summary>
        static JsonObject CreateBody(PleasanterConnection connection, PleasanterTableDefinition definition, IEnumerable<(string Column, string Parameter)> values,
            Dictionary<string, object?> args, bool isInsert)
        {
            var body = new JsonObject();
            foreach (var (name, parameter) in values)
            {
                var column = definition.FindColumn(name) ?? throw new PleasanterException($"Table '{definition.Name}' has no column '{name}'.");
                if (!args.TryGetValue(parameter, out var value)) throw new PleasanterException($"The value of parameter '{parameter}' is missing.");
                if (value is DBNull) value = null;
                if (column.IsReadOnly)
                {
                    if (isInsert && value == null) continue;
                    throw new PleasanterException($"Column '{name}' of Pleasanter table '{definition.Name}' is read-only (id, version, creator, updater, created / updated time or remaining work value) and cannot be written.");
                }
                if (isInsert && value == null) continue;
                var node = ToJson(connection, definition, column, value);
                if (column.HashName is { } hashName)
                {
                    if (body[hashName] is not JsonObject hash)
                    {
                        hash = new JsonObject();
                        body[hashName] = hash;
                    }
                    hash[column.Name] = node;
                }
                else
                {
                    body[column.Name] = node;
                }
            }
            return body;
        }

        static JsonNode? ToJson(PleasanterConnection connection, PleasanterTableDefinition definition, PleasanterColumnDefinition column, object? value)
        {
            if (value == null)
            {
                return column.Kind switch
                {
                    PleasanterColumnKind.String => JsonValue.Create(string.Empty),
                    PleasanterColumnKind.Number => column.HashName == null && !column.IsNullable ? JsonValue.Create(0m) : null,
                    PleasanterColumnKind.Date or PleasanterColumnKind.DateTime => JsonValue.Create(Format(connection.Settings.EmptyDate)),
                    PleasanterColumnKind.Bool => JsonValue.Create(false),
                    _ => null,
                };
            }
            if (!PleasanterValues.TryConvert(column.Kind, value, out var converted))
                throw new PleasanterException($"The value '{Convert.ToString(value, CultureInfo.InvariantCulture)}' cannot be written to column '{column.Name}' of Pleasanter table '{definition.Name}' ({column.Kind}).", PleasanterErrorKind.InvalidRequest);
            return converted switch
            {
                decimal d => JsonValue.Create(d),
                DateTime dt => JsonValue.Create(Format(dt)),
                bool b => JsonValue.Create(b),
                _ => JsonValue.Create((string)converted),
            };
        }

        //秒まで (プリザンターは秒より細かい値を返さない)
        static string Format(DateTime value) => value.ToString(PleasanterValues.DateTimeFormat, CultureInfo.InvariantCulture);
    }
}
