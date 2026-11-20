using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
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
        readonly PleasanterMetadataFile? _metadataFile;

        /// <param name="inner">プリザンター以外のデータソースを受け持つ IDbAccessor。</param>
        /// <param name="connections">プリザンターへの接続 (<see cref="PleasanterConnection.FromDataSources"/>)。</param>
        /// <param name="metadataFile">
        /// 表の定義の出所。指定すると表の定義をこのファイルから読み、プリザンターに取りに行かない (ファイルに無い表は無い)。
        /// デザイナは <see cref="PleasanterMetadataFile.FromProject"/>、Web アプリは <see cref="PleasanterMetadataFile.FromDeployedDesign"/>。
        /// null なら表ごとに最初の 1 回プリザンターのサイトの設定 (getsite) を読む。
        /// </param>
        public PleasanterDbAccessor(IDbAccessor inner, IEnumerable<PleasanterConnection> connections, PleasanterMetadataFile? metadataFile = null)
        {
            _inner = inner;
            _connections = connections.ToDictionary(e => e.Settings.Name);
            _metadataFile = metadataFile;
        }

        PleasanterConnection? Find(string dataSourceName)
            => _connections.TryGetValue(dataSourceName, out var connection) ? connection : null;

        //表の定義。ファイルから読むときは、サイト ID を接続文字列の Sites のものにする (ファイルのサイト ID は書き出した環境のもの)
        Task<PleasanterTableDefinition> GetDefinitionAsync(PleasanterConnection connection, string table)
        {
            if (_metadataFile == null) return connection.GetTableDefinitionAsync(table);
            if (!connection.Settings.Sites.TryGetValue(table, out var siteId))
                throw new PleasanterException($"Pleasanter data source '{connection.Settings.Name}': table '{table}' is not in Sites of the connection string.", PleasanterErrorKind.NotFound);
            var definition = _metadataFile.Load(connection.Settings.Name).FirstOrDefault(e => e.Name == table) ?? throw _metadataFile.TableNotFound(connection.Settings.Name, table);
            return Task.FromResult(definition.WithSiteId(siteId));
        }

        Func<string, Task<PleasanterTableDefinition>> DefinitionResolver(PleasanterConnection connection) => table => GetDefinitionAsync(connection, table);

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

        /// <summary>
        /// プリザンターのデータソースでは、ストアドプロシージャ (CommandType = StoredProcedure) をプリザンターの拡張 SQL (api/extended/sql) として実行する。
        /// CommandText が拡張 SQL の名前 (Name)。入力のパラメータ (Input / InputOutput) を Params に入れる (名前の先頭の @ は外す)。
        /// 結果は最初の表の最初の行: Scalar の ReturnValue は先頭の列の値、NonQuery の ReturnValue は -1 (件数は分からない)、Reader は null。
        /// 出力のパラメータ (Output / InputOutput / ReturnValue) は同じ名前の列の値 (無ければ null)。拡張 SQL は書き込みとして扱う (やり直さない)。
        /// SQL 文は実行できない。
        /// </summary>
        public async Task<DbSqlCommandResult> ExecuteSqlCommandAsync(string dataSourceName, DbSqlCommand command)
        {
            var connection = Find(dataSourceName);
            if (connection == null) return await _inner.ExecuteSqlCommandAsync(dataSourceName, command);
            if (command.CommandType != ExecuteSqlCommandType.StoredProcedure)
                throw new PleasanterException("SQL cannot be executed on a Pleasanter data source. To run an extended SQL of Pleasanter, set the command type to StoredProcedure and the name of the extended SQL as the text.");

            var name = command.CommandText.Trim();
            var parameters = new JsonObject();
            foreach (var parameter in command.Parameters.Where(e => e.Direction is ParameterDirection.Input or ParameterDirection.InputOutput))
                parameters[ParameterName(parameter.Name)] = PleasanterValues.ToExtendedSqlParameter(parameter.Value);

            var row = await connection.ExecuteExtendedSqlAsync(name, parameters);
            object? Column(string column) => row?.FirstOrDefault(e => e.Name == column).Value;
            var result = new DbSqlCommandResult
            {
                ReturnValue = command.MethodType switch
                {
                    ExecuteSqlMethodType.Scalar => row?.FirstOrDefault().Value,
                    ExecuteSqlMethodType.NonQuery => -1,
                    _ => null,
                },
            };
            foreach (var parameter in command.Parameters)
                result.ParameterValues.TryAdd(parameter.Name, parameter.Direction == ParameterDirection.Input ? parameter.Value : Column(ParameterName(parameter.Name)));
            return result;
        }

        static string ParameterName(string name) => name.TrimStart('@', ':');

        // ---------------------------------------------------------------- 定義

        public async Task<List<DbTableDefinition>?> GetCustomTableDefinitionsAsync(string dataSourceName)
        {
            var connection = Find(dataSourceName);
            if (connection == null) return await _inner.GetCustomTableDefinitionsAsync(dataSourceName);
            //ファイルから読むときは、ファイルにあって接続文字列の Sites にもある表
            if (_metadataFile != null)
                return _metadataFile.Load(connection.Settings.Name).Where(e => connection.Settings.Sites.ContainsKey(e.Name)).Select(e => e.ToTableDefinition()).ToList();
            return (await connection.GetTableDefinitionsAsync()).Select(e => e.ToTableDefinition()).ToList();
        }

        public async Task<DbTableDefinition?> GetCustomTableDefinitionsAsync(string dataSourceName, string tableNames)
        {
            var connection = Find(dataSourceName);
            if (connection == null) return await _inner.GetCustomTableDefinitionsAsync(dataSourceName, tableNames);
            return (await GetDefinitionAsync(connection, tableNames)).ToTableDefinition();
        }

        // ---------------------------------------------------------------- 読み出し

        public async Task<List<IDictionary<string, object>>> QueryAsync(string dataSourceName, string query, Dictionary<string, ParamAndRawDbTypeName> args)
        {
            var connection = Find(dataSourceName);
            if (connection == null) return await _inner.QueryAsync(dataSourceName, query, args);
            return await QueryCoreAsync(connection, query, args.ToDictionary(e => e.Key, e => e.Value.Value));
        }

        async Task<List<IDictionary<string, object>>> QueryCoreAsync(PleasanterConnection connection, string query, Dictionary<string, object?> args)
        {
            if (SqlTranslator.IsAggregate(query))
                return await QueryExecutor.AggregateAsync(connection, await SqlTranslator.TranslateAggregateAsync(query, args, DefinitionResolver(connection)));
            return await QueryExecutor.ReadAsync(connection, await SqlTranslator.TranslateSelectAsync(query, args, DefinitionResolver(connection)));
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
                var body = CreateBody(connection, definition, update.Values, args, false, out var attachments);
                var id = GetId(definition, update, args);
                if (attachments.Any(e => e.Value.Deletions.Count != 0))
                {
                    //消す添付は、このレコードのその列に今あるものだけ送る (プリザンターは別のレコードの添付の Guid でも消してしまう)
                    var stored = await ReadStoredAttachmentsAsync(connection, update, args, attachments.Where(e => e.Value.Deletions.Count != 0).Select(e => e.Key).ToList());
                    if (stored == null) return 0;
                    foreach (var (column, change) in attachments)
                    {
                        if (!stored.TryGetValue(column, out var guids)) continue;
                        change.Deletions.RemoveAll(e => !guids.Contains(e, StringComparer.OrdinalIgnoreCase));
                    }
                }
                else if (!await ExistsAsync(connection, update, args)) return 0;
                AddAttachments(body, attachments);
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

        async Task<string> InsertRowAsync(PleasanterConnection connection, InsertPlan insert, List<string> parameters, Dictionary<string, object?> args)
        {
            var definition = await GetDefinitionAsync(connection, insert.Table);
            if (!string.IsNullOrEmpty(insert.ReturningColumn) && insert.ReturningColumn != definition.KeyColumn) throw IdNotKey(definition);
            var body = CreateBody(connection, definition, insert.Columns.Zip(parameters).ToList(), args, true, out var attachments);
            //作成では消す添付は無い (足すものだけ送る)
            foreach (var change in attachments.Values) change.Deletions.Clear();
            AddAttachments(body, attachments);
            var id = await connection.CreateAsync(definition, body);
            return id.ToString(CultureInfo.InvariantCulture);
        }

        async Task<PleasanterTableDefinition> GetWritableDefinitionAsync(PleasanterConnection connection, WritePlan write)
        {
            var definition = await GetDefinitionAsync(connection, write.Table);
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
        async Task<bool> ExistsAsync(PleasanterConnection connection, WritePlan write, Dictionary<string, object?> args)
        {
            var rows = await QueryCoreAsync(connection, $"select count(*) from \"{write.Table}\" where {write.WhereSql}", args);
            var count = rows.FirstOrDefault()?.Values.FirstOrDefault();
            return count != null && 0 < Convert.ToDecimal(count, CultureInfo.InvariantCulture);
        }

        //ExistsAsync と同じ条件で行を読み、添付の列に今あるファイルの Guid を返す。行が無ければ null
        async Task<Dictionary<string, List<string>>?> ReadStoredAttachmentsAsync(PleasanterConnection connection, WritePlan write, Dictionary<string, object?> args, List<string> columns)
        {
            var select = string.Join(", ", columns.Select(e => $"\"{e}\""));
            var rows = await QueryCoreAsync(connection, $"select {select} from \"{write.Table}\" where {write.WhereSql}", args);
            var row = rows.FirstOrDefault();
            if (row == null) return null;
            return columns.ToDictionary(e => e, e => PleasanterAttachments.ReadGuids(row.TryGetValue(e, out var value) ? value as string : null));
        }

        //添付の変更を本文の AttachmentsHash に入れる (足すものも消すものも無い列は送らない)
        static void AddAttachments(JsonObject body, Dictionary<string, PleasanterAttachments.Change> attachments)
        {
            foreach (var (column, change) in attachments)
            {
                if (change.IsEmpty) continue;
                if (body["AttachmentsHash"] is not JsonObject hash)
                {
                    hash = new JsonObject();
                    body["AttachmentsHash"] = hash;
                }
                hash[column] = PleasanterAttachments.ToRequest(change.Additions, change.Deletions);
            }
        }

        /// <summary>
        /// 要求の本文 (標準の列はそのまま、拡張の列は ClassHash などの中)。読み取り専用の列は、作成ではキー以外は値があっても送らず、キーに値があればエラー。更新の対象に入っていればエラー。
        /// 作成では値が null の列は送らない (プリザンターの既定値になる)。
        /// 更新で値が null の列は、プリザンターが空として受け付ける値を送る (null を送ると無視される・400 になる列があるため):
        /// 文字 → ""・数値 → null (null を持てない数値の列は 0 になる)・日付 → 空の日付 (EmptyDate)・チェック → false・Status / Manager / Owner など null を持てない標準の数値 → 0。
        /// 添付の列は本文に入れず attachments に分ける (消すものは送る前にレコードの今の値で絞るため)。
        /// </summary>
        static JsonObject CreateBody(PleasanterConnection connection, PleasanterTableDefinition definition, IEnumerable<(string Column, string Parameter)> values,
            Dictionary<string, object?> args, bool isInsert, out Dictionary<string, PleasanterAttachments.Change> attachments)
        {
            var body = new JsonObject();
            attachments = new Dictionary<string, PleasanterAttachments.Change>(StringComparer.Ordinal);
            foreach (var (name, parameter) in values)
            {
                var column = definition.FindColumn(name) ?? throw new PleasanterException($"Table '{definition.Name}' has no column '{name}'.");
                if (!args.TryGetValue(parameter, out var value)) throw new PleasanterException($"The value of parameter '{parameter}' is missing.");
                if (value is DBNull) value = null;
                if (column.Kind == PleasanterColumnKind.Attachments)
                {
                    attachments[column.Name] = PleasanterAttachments.ParseChange(definition.Name, column.Name, value);
                    continue;
                }
                if (column.IsReadOnly)
                {
                    //作成では、プリザンターが自分で決める列 (版・作成者・更新者・作成日時・更新日時・残作業量) の値は送らない
                    //(レコードのコピーは、これらを表示しているフィールドの値も写して作成に乗せる)。キーの指定だけはエラー
                    if (isInsert && (value == null || !string.Equals(column.Name, definition.KeyColumn, StringComparison.Ordinal))) continue;
                    throw new PleasanterException($"Column '{name}' of Pleasanter table '{definition.Name}' is read-only (id, version, creator, updater, created / updated time or remaining work value) and cannot be written.");
                }
                if (isInsert && value == null) continue;
                //コメントは足すだけ (空は送らない。空を送っても消えない)
                if (column.Name == PleasanterTableDefinition.CommentsColumn && string.IsNullOrEmpty(value as string)) continue;
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

        // ---------------------------------------------------------------- 添付のダウンロード

        /// <summary>添付のフィールド (クライアント用パッケージの PleasanterAttachmentsFieldDesign) の型の名前。このパッケージはクライアント用パッケージを参照しないので名前で見る。</summary>
        internal const string AttachmentsFieldDesignTypeFullName = "Codeer.LowCode.Blazor.Connectors.Pleasanter.PleasanterAttachmentsFieldDesign";

        /// <summary>
        /// 添付のフィールドのファイル 1 つの中身を、現在の利用者の権限で読む (ホストのダウンロードの Controller から呼ぶ)。
        /// 手順: モジュールがこのアクセサのプリザンターのデータソースで、フィールドが添付のフィールド (添付の列) であることを確かめる →
        /// io の通常の読み出し (モジュール・行・フィールドの権限が効く) でそのレコードのそのフィールドを読む → その値に guid のファイルがあることを確かめる →
        /// プリザンターから中身を読む (そのレコードの添付であることも確かめる)。
        /// どれかが満たせなければ null (理由は区別しない。失敗でも例外を投げない)。
        /// 連携用の API キーはテナント内のどの添付も読めるので、guid だけで中身を返す口をホストに作らない。
        /// </summary>
        /// <param name="io">現在の利用者の ModuleDataIO。</param>
        /// <param name="designData">io と同じデザイン。</param>
        public async Task<PleasanterAttachmentFile?> GetAttachmentAsync(ModuleDataIO io, DesignData designData, string? moduleName, string? id, string? fieldName, string? guid)
        {
            try
            {
                if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(fieldName) || !PleasanterAttachments.IsGuid(guid)) return null;
                if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var recordId) || recordId <= 0) return null;

                var module = designData.Modules.Find(moduleName);
                if (module == null || string.IsNullOrEmpty(module.DataSourceName)) return null;
                var connection = Find(module.DataSourceName);
                if (connection == null) return null;
                var field = module.Fields.FirstOrDefault(e => e.Name == fieldName);
                if (field?.TypeFullName != AttachmentsFieldDesignTypeFullName) return null;
                //型は名前で見たので、列の名前は公開のプロパティから読む
                if (field.GetType().GetProperty("DbColumn")?.GetValue(field) is not string dbColumn) return null;
                var definition = await GetDefinitionAsync(connection, module.DbTable);
                if (definition.FindColumn(dbColumn)?.Kind != PleasanterColumnKind.Attachments) return null;
                var idField = module.Fields.OfType<IdFieldDesign>().FirstOrDefault();
                if (idField == null || idField.DbColumn != definition.KeyColumn) return null;

                var condition = new SearchCondition(moduleName)
                {
                    Condition = MultiMatchCondition.And(new FieldValueMatchCondition
                    {
                        SearchTargetVariable = idField.Name + ".Value",
                        Comparison = MatchComparison.Equal,
                        Value = MultiTypeValue.Create(recordId.ToString(CultureInfo.InvariantCulture)),
                    }),
                };
                condition.SelectFields.Add(fieldName);
                var rows = (await io.GetListAsync(condition, 0)).Items;
                if (rows.Count != 1 || !rows[0].Fields.TryGetValue(fieldName, out var data) || data is not ValueFieldDataBase<string> value) return null;
                var file = PleasanterAttachments.ReadFiles(value.Value).FirstOrDefault(e => string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase));
                if (file.Guid == null) return null;

                var binary = await connection.GetBinaryAsync(file.Guid);
                if (binary.ReferenceId != recordId) return null;
                return new PleasanterAttachmentFile(string.IsNullOrEmpty(file.Name) ? binary.FileName : file.Name, binary.Content);
            }
            catch (Exception)
            {
                return null;
            }
        }

        //秒まで (プリザンターは秒より細かい値を返さない)
        static string Format(DateTime value) => value.ToString(PleasanterValues.DateTimeFormat, CultureInfo.InvariantCulture);
    }
}
