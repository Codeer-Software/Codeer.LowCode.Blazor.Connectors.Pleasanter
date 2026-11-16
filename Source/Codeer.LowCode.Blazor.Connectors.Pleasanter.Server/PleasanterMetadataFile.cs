using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>
    /// 表の定義 (メタ情報) を保存したファイル。デザイナはこのファイルから表の定義を読む (プリザンターに取りに行かない)。
    /// データソースごとに 1 つ、デザインのプロジェクトのフォルダに置く (pleasanter.&lt;データソース名&gt;.metadata.json)。接続の情報 (URL・API キー) は入らない。
    /// 列の表示名・選択肢・リンクも入れる (画面を作るときの手がかり。実行時は読まない)。
    /// </summary>
    public static class PleasanterMetadataFile
    {
        class TableData
        {
            public string Name { get; set; } = string.Empty;
            public long SiteId { get; set; }
            public string ReferenceType { get; set; } = PleasanterTableDefinition.Results;
            public string Key { get; set; } = string.Empty;
            /// <summary>Title の条件・並びが Title の値そのもので評価されるか (サイトのタイトルの設定が Title だけか)。</summary>
            public bool TitleIsRaw { get; set; } = true;
            public List<ColumnData> Columns { get; set; } = new();
        }

        class ColumnData
        {
            public string Name { get; set; } = string.Empty;
            public PleasanterColumnKind Kind { get; set; }
            public bool ReadOnly { get; set; }
            /// <summary>数値の列が空 (null) を持てるか。持てない数値の列は空が 0 になる。</summary>
            public bool Nullable { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? LabelText { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<ChoiceData>? Choices { get; set; }
            /// <summary>リンクの相手のサイト ID (ChoicesText が [[サイトID]])。</summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public long? LinkSiteId { get; set; }
            /// <summary>リンクの相手の表名 (接続の Sites にあれば)。</summary>
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? LinkTable { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool MultipleSelections { get; set; }
        }

        class ChoiceData
        {
            public string Value { get; set; } = string.Empty;
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? DisplayText { get; set; }
        }

        class FileData
        {
            public List<TableData> Tables { get; set; } = new();
        }

        static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
        };

        static readonly ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, List<PleasanterTableDefinition> Tables)> _cache = new(StringComparer.OrdinalIgnoreCase);
        static readonly ConcurrentDictionary<string, SemaphoreSlim> _updateLocks = new(StringComparer.OrdinalIgnoreCase);

        //テストで置き換えの途中の失敗を起こすための差し替え口 (一時ファイルに書いたあと、読み直す前に呼ぶ)
        internal static Action<string>? AfterTemporaryFileWritten { get; set; }

        public static string GetPath(string directory, string dataSourceName)
        {
            var name = string.Concat(dataSourceName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(directory, $"pleasanter.{name}.metadata.json");
        }

        /// <summary>
        /// 接続の Sites の全部のサイトの設定 (getsite) を読み直して、ファイルに保存する。
        /// 同じフォルダの一時ファイルに書いて読み直して確かめてから置き換える (途中で失敗しても前のファイルが残る)。同じデータソースの更新は直列にする。
        /// </summary>
        public static async Task UpdateAsync(PleasanterConnection connection, string directory)
        {
            var path = GetPath(directory, connection.Settings.Name);
            var updateLock = _updateLocks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
            await updateLock.WaitAsync();
            try
            {
                var tables = await connection.ReloadTableDefinitionsAsync();
                await SaveAsync(path, tables);
            }
            finally
            {
                updateLock.Release();
            }
        }

        static async Task SaveAsync(string path, List<PleasanterTableDefinition> tables)
        {
            var data = new FileData
            {
                Tables = tables.OrderBy(e => e.Name, StringComparer.Ordinal).Select(table => new TableData
                {
                    Name = table.Name,
                    SiteId = table.SiteId,
                    ReferenceType = table.ReferenceType,
                    Key = table.KeyColumn,
                    TitleIsRaw = table.TitleIsRaw,
                    Columns = table.Columns.Select(e => new ColumnData
                    {
                        Name = e.Name,
                        Kind = e.Kind,
                        ReadOnly = e.IsReadOnly,
                        Nullable = e.IsNullable,
                        LabelText = e.LabelText,
                        Choices = e.Choices?.Select(c => new ChoiceData { Value = c.Value, DisplayText = c.DisplayText }).ToList(),
                        LinkSiteId = e.LinkSiteId,
                        LinkTable = e.LinkTable,
                        MultipleSelections = e.MultipleSelections,
                    }).ToList(),
                }).ToList(),
            };
            var text = JsonSerializer.Serialize(data, _jsonOptions);

            var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, text);
                AfterTemporaryFileWritten?.Invoke(temporary);
                //書けた内容を読み直して、同じ定義になることを確かめてから置き換える
                var written = Parse(await File.ReadAllTextAsync(temporary), path);
                if (written.Count != tables.Count || written.Sum(e => e.Columns.Count) != tables.Sum(e => e.Columns.Count))
                    throw new PleasanterException($"The Pleasanter metadata file '{Path.GetFileName(path)}' could not be written correctly.");
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        /// <summary>保存してある定義。ファイルが無ければ null。</summary>
        internal static List<PleasanterTableDefinition>? Load(string directory, string dataSourceName)
        {
            var path = GetPath(directory, dataSourceName);
            if (!File.Exists(path)) return null;
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            if (_cache.TryGetValue(path, out var cached) && cached.LastWriteTimeUtc == lastWriteTimeUtc) return cached.Tables;
            var tables = Parse(File.ReadAllText(path), path);
            _cache[path] = (lastWriteTimeUtc, tables);
            return tables;
        }

        static List<PleasanterTableDefinition> Parse(string text, string path)
        {
            FileData? data;
            try
            {
                data = JsonSerializer.Deserialize<FileData>(text, _jsonOptions);
            }
            catch (JsonException e)
            {
                throw new PleasanterException($"The Pleasanter metadata file '{Path.GetFileName(path)}' cannot be read. Update the Pleasanter metadata again.", PleasanterErrorKind.NotSupported, e);
            }
            var tables = new List<PleasanterTableDefinition>();
            foreach (var table in data?.Tables ?? new())
            {
                if (string.IsNullOrEmpty(table.Name) || table.ReferenceType is not (PleasanterTableDefinition.Results or PleasanterTableDefinition.Issues)) continue;
                var definition = new PleasanterTableDefinition { Name = table.Name, SiteId = table.SiteId, ReferenceType = table.ReferenceType, TitleIsRaw = table.TitleIsRaw };
                foreach (var column in table.Columns.Where(e => !string.IsNullOrEmpty(e.Name)))
                {
                    definition.Columns.Add(new PleasanterColumnDefinition
                    {
                        Name = column.Name,
                        Kind = column.Kind,
                        IsReadOnly = column.ReadOnly,
                        IsNullable = column.Nullable,
                        LabelText = column.LabelText,
                        Choices = column.Choices?.Select(c => new PleasanterChoice(c.Value, c.DisplayText)).ToList(),
                        LinkSiteId = column.LinkSiteId,
                        LinkTable = column.LinkTable,
                        MultipleSelections = column.MultipleSelections,
                    });
                }
                tables.Add(definition);
            }
            return tables;
        }
    }
}
