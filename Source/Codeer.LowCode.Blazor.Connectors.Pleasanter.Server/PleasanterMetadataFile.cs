using Codeer.LowCode.Blazor.DesignLogic;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>
    /// 表の定義 (メタ情報) を保存したファイル (pleasanter.&lt;データソース名&gt;.metadata.json)。デザインのプロジェクトのサーバー専用ファイルに置き、
    /// デザインと一緒に配られる (App.zip に入る)。デザイナも Web アプリもこのファイルから表の定義を読む (プリザンターに取りに行かない)。
    /// 接続の情報 (URL・API キー) は入らない。列の表示名・選択肢・リンクも入れる (画面を作るときの手がかり。実行時は読まない)。
    /// サイト ID は実行時の接続文字列の Sites のものを使う (ファイルのサイト ID は書き出したときの環境のもの)。
    /// </summary>
    public sealed class PleasanterMetadataFile
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

        static readonly ConcurrentDictionary<string, SemaphoreSlim> _updateLocks = new(StringComparer.OrdinalIgnoreCase);

        //読んだ定義 (場所とデータソースごと)。中身 (プロジェクト) か App.zip の更新日時と大きさ (配られたデザイン) が変わるまで使い回す。
        //値は差し替えるだけで中を変えないので、読む側はロックなしで使える。同じ場所を同時に読み直すのは 1 つだけ (Entry のロック)
        sealed class Entry
        {
            public object Sync { get; } = new();
            public (object Stamp, List<PleasanterTableDefinition> Tables)? Loaded { get; set; }
        }

        static readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);

        readonly string _directory;
        readonly bool _isDeployedDesign;

        PleasanterMetadataFile(string directory, bool isDeployedDesign)
        {
            _directory = Path.GetFullPath(directory);
            _isDeployedDesign = isDeployedDesign;
        }

        /// <summary>デザインのプロジェクトのファイル (デザイナ用)。</summary>
        /// <param name="projectDirectory">デザインのプロジェクトのフォルダ。</param>
        public static PleasanterMetadataFile FromProject(string projectDirectory) => new(projectDirectory, false);

        /// <summary>配られたデザイン (App.zip) の中のファイル (Web アプリ用)。App.zip が差し替わると次の読み出しから新しい内容になる。</summary>
        /// <param name="designFileDirectory">App.zip のあるフォルダ。</param>
        public static PleasanterMetadataFile FromDeployedDesign(string designFileDirectory) => new(designFileDirectory, true);

        internal static string GetFileName(string dataSourceName)
            => $"pleasanter.{string.Concat(dataSourceName.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_'))}.metadata.json";

        /// <summary>
        /// 接続の Sites の全部のサイトの設定 (getsite) を読み直して、プロジェクトのサーバー専用ファイルに保存する (次のデプロイでデザインと一緒に配られる)。
        /// 読めなければファイルを変えない。書き終えてから置き換える (途中で失敗しても前のファイルが残る)。同じファイルの更新は直列にする。
        /// </summary>
        /// <param name="connection">プリザンターへの接続。</param>
        /// <param name="projectDirectory">デザインのプロジェクトのフォルダ。</param>
        public static async Task UpdateAsync(PleasanterConnection connection, string projectDirectory)
        {
            var fileName = GetFileName(connection.Settings.Name);
            var updateLock = _updateLocks.GetOrAdd(Path.Combine(Path.GetFullPath(projectDirectory), fileName), _ => new SemaphoreSlim(1, 1));
            await updateLock.WaitAsync();
            try
            {
                var tables = await connection.ReloadTableDefinitionsAsync();
                var bytes = Serialize(tables);
                //書く内容を読み直して、同じ定義になることを確かめてから書く
                var written = Parse(bytes, fileName);
                if (written.Count != tables.Count || written.Sum(e => e.Columns.Count) != tables.Sum(e => e.Columns.Count))
                    throw new PleasanterException($"The Pleasanter metadata file '{fileName}' could not be written correctly.");
                DesignDataFileManager.WriteProjectServerFile(projectDirectory, fileName, bytes);
            }
            finally
            {
                updateLock.Release();
            }
        }

        static byte[] Serialize(List<PleasanterTableDefinition> tables)
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
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, _jsonOptions));
        }

        /// <summary>このファイルの表の定義 (サイト ID は書き出したときのもの)。ファイルが無ければ空。</summary>
        internal List<PleasanterTableDefinition> Load(string dataSourceName)
        {
            var fileName = GetFileName(dataSourceName);
            var entry = _cache.GetOrAdd((_isDeployedDesign ? "zip|" : "project|") + _directory + "|" + fileName, _ => new Entry());
            lock (entry.Sync)
            {
                if (_isDeployedDesign)
                {
                    //App.zip の更新日時と大きさで差し替えを知る (読む前に取る。読む間に差し替わっても、次の読み出しで日時が違うので読み直す)
                    var stamp = ZipStamp();
                    if (entry.Loaded is { } loaded && loaded.Stamp.Equals(stamp)) return loaded.Tables;
                    using var stream = DesignDataFileManager.GetServerFile(_directory, fileName);
                    var tables = stream == null ? new List<PleasanterTableDefinition>() : Parse(stream.ToArray(), fileName);
                    entry.Loaded = (stamp, tables);
                    return tables;
                }
                else
                {
                    //プロジェクトのファイルは中身で比べる (小さいファイルなので読むのは毎回。解釈は変わったときだけ)
                    var bytes = DesignDataFileManager.ReadProjectServerFile(_directory, fileName);
                    var content = bytes == null ? string.Empty : Convert.ToBase64String(bytes);
                    if (entry.Loaded is { } loaded && loaded.Stamp.Equals(content)) return loaded.Tables;
                    var tables = bytes == null ? new List<PleasanterTableDefinition>() : Parse(bytes, fileName);
                    entry.Loaded = (content, tables);
                    return tables;
                }
            }
        }

        //App.zip の名前は本体の配置の決まり (DesignDataFileManager が読む <フォルダ>/App.zip)
        (bool Exists, DateTime LastWriteTimeUtc, long Length) ZipStamp()
        {
            var zip = new FileInfo(Path.Combine(_directory, "App.zip"));
            return zip.Exists ? (true, zip.LastWriteTimeUtc, zip.Length) : (false, default, 0);
        }

        /// <summary>表がファイルに無いときのエラー (どこを直せばよいかを入れる)。</summary>
        internal PleasanterException TableNotFound(string dataSourceName, string table)
            => new(_isDeployedDesign
                ? $"Table '{table}' is not in the Pleasanter metadata ({GetFileName(dataSourceName)}) of the deployed design. Update the Pleasanter metadata in the designer and deploy the design again."
                : $"Table '{table}' is not in the Pleasanter metadata ({GetFileName(dataSourceName)}) of the design project. Update the Pleasanter metadata (Tools menu of the designer).",
                PleasanterErrorKind.NotFound);

        static List<PleasanterTableDefinition> Parse(byte[] bytes, string fileName)
        {
            FileData? data;
            try
            {
                data = JsonSerializer.Deserialize<FileData>(bytes, _jsonOptions);
            }
            catch (JsonException e)
            {
                throw new PleasanterException($"The Pleasanter metadata file '{fileName}' cannot be read. Update the Pleasanter metadata again.", PleasanterErrorKind.NotSupported, e);
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
