using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    enum PleasanterColumnKind
    {
        /// <summary>Class / Description / Title / Body。</summary>
        String,
        /// <summary>Num・Status / Manager / Owner / Creator / Updator / Ver・キー・WorkValue など。.NET では decimal。</summary>
        Number,
        /// <summary>Date の列のうち EditorFormat が無いか Ymd のもの。.NET では DateOnly。</summary>
        Date,
        /// <summary>Date の列 (Ymd 以外)・CreatedTime / UpdatedTime / StartTime / CompletionTime。.NET では DateTime (API キーの利用者のタイムゾーンの壁時計の値)。</summary>
        DateTime,
        /// <summary>Check。</summary>
        Bool,
        /// <summary>
        /// Attachments (添付)。値はプリザンターが返すファイルの並びの JSON の文字 (<see cref="PleasanterAttachments"/>)。
        /// 書くと Base64 を持つ要素を足し、Deleted が true の要素を消す。条件・並び・集計には使えない。
        /// </summary>
        Attachments,
    }

    /// <summary>選択肢 1 つ (ChoicesText の 1 行の 値,表示)。</summary>
    sealed record PleasanterChoice(string Value, string? DisplayText);

    class PleasanterColumnDefinition
    {
        /// <summary>プリザンターの内部の列名 (モジュールの DbColumn)。</summary>
        public string Name { get; init; } = string.Empty;
        public PleasanterColumnKind Kind { get; init; }
        /// <summary>キー・Ver・Creator・Updator・CreatedTime・UpdatedTime・RemainingWorkValue (判定は <see cref="PleasanterTableDefinition.IsReadOnlyColumn"/>)。</summary>
        public bool IsReadOnly { get; init; }
        /// <summary>数値の列が null を持てるか (列の設定 Nullable)。持てない数値の列は空が 0 として返る。</summary>
        public bool IsNullable { get; init; }
        /// <summary>表示名 (列の設定 LabelText)。設定が無ければ null。</summary>
        public string? LabelText { get; init; }
        /// <summary>選択肢 (ChoicesText)。無ければ null。</summary>
        public List<PleasanterChoice>? Choices { get; init; }
        /// <summary>リンク (ChoicesText が [[サイトID]]) の相手のサイト ID。無ければ null。</summary>
        public long? LinkSiteId { get; init; }
        /// <summary>リンクの相手の表名 (接続の Sites にあるとき)。</summary>
        public string? LinkTable { get; init; }
        /// <summary>複数選択の列 (値は JSON の配列の文字)。View の条件に写さない。</summary>
        public bool MultipleSelections { get; init; }

        /// <summary>CreatedTime / UpdatedTime。プリザンターは秒より細かい値を持つが API は秒までしか返さない。</summary>
        public bool IsSystemTime => Name is "CreatedTime" or "UpdatedTime";

        /// <summary>行の JSON で入っている場所 (ClassHash など)。標準の列は null (ルート)。</summary>
        public string? HashName => PleasanterTableDefinition.GetHashName(Name);
    }

    /// <summary>
    /// 表 (プリザンターのサイト 1 つ) の定義。api/items/{siteId}/getsite の応答から作る。
    /// 列 = 標準の列 + SiteSettings の EditorColumnHash (全タブ)・GridColumns に出てくる Class / Num / Date / Description / Check / Attachments の列
    /// (API が行に入れる列。Columns は列の設定 (表示名・選択肢・Nullable など) を読むのに使う)。
    /// </summary>
    class PleasanterTableDefinition
    {
        internal const string Results = "Results";
        internal const string Issues = "Issues";

        static readonly Regex ExtendedColumnPattern = new("^(Class|Num|Date|Description|Check|Attachments)([A-Z]|[0-9]{3})$", RegexOptions.CultureInvariant);
        static readonly Regex LinkPattern = new(@"^\[\[([0-9]+)(,[^\]]*)?\]\]$", RegexOptions.CultureInvariant);

        /// <summary>表名 (接続の Sites に書いた別名)。</summary>
        public string Name { get; init; } = string.Empty;
        public long SiteId { get; init; }
        /// <summary>Results (記録テーブル) か Issues (期限付きテーブル)。</summary>
        public string ReferenceType { get; init; } = Results;
        public List<PleasanterColumnDefinition> Columns { get; } = new();
        /// <summary>
        /// Title の条件・並びが Title の値そのもので評価されるか。プリザンターは Title の条件と並びを表示用のタイトル (Items.Title) で行うので、
        /// タイトルの設定 (TitleColumns) が Title だけでないサイトでは false (Title の条件はメモリで評価・並びはメモリで並べる)。
        /// </summary>
        public bool TitleIsRaw { get; init; } = true;

        /// <summary>キーの列 (ResultId / IssueId)。</summary>
        public string KeyColumn => ReferenceType == Issues ? "IssueId" : "ResultId";

        /// <summary>キーの列 (FileMaker 版と同じ形の一覧。いつも 1 列)。</summary>
        public IReadOnlyList<string> KeyColumns => new[] { KeyColumn };

        public PleasanterColumnDefinition? FindColumn(string name) => Columns.FirstOrDefault(e => e.Name == name);

        /// <summary>サイト ID だけを替えた定義 (列は同じものを使う)。</summary>
        public PleasanterTableDefinition WithSiteId(long siteId)
        {
            var copy = new PleasanterTableDefinition { Name = Name, SiteId = siteId, ReferenceType = ReferenceType, TitleIsRaw = TitleIsRaw };
            copy.Columns.AddRange(Columns);
            return copy;
        }

        public DbTableDefinition ToTableDefinition()
        {
            var table = new DbTableDefinition { Name = Name };
            foreach (var column in Columns)
            {
                var (netType, rawType) = column.Kind switch
                {
                    PleasanterColumnKind.Number => (typeof(decimal), "numeric"),
                    PleasanterColumnKind.Date => (typeof(DateOnly), "date"),
                    //時差を持たない値 (デザイナが DateTimeField の SaveAsUtc を既定でオンにしない列型の名前にしておく)
                    PleasanterColumnKind.DateTime => (typeof(DateTime), "timestamp"),
                    PleasanterColumnKind.Bool => (typeof(bool), "boolean"),
                    //添付は文字 (JSON) の列として見せる。デザイナで添付の列と見分けられる型の名前にする
                    PleasanterColumnKind.Attachments => (typeof(string), AttachmentsRawTypeName),
                    _ => (typeof(string), "varchar"),
                };
                table.Columns.Add(new DbColumnDefinition
                {
                    Name = column.Name,
                    NetTypeFullName = netType.FullName!,
                    RawDbTypeName = rawType,
                    //全部の列を null を許す列として見せる。NOT NULL にすると、本体が空の値を型の既定値 (0・0001-01-01) に置き換えて書いてしまう
                    IsNullable = true,
                });
            }
            return table;
        }

        // ---------------------------------------------------------------- 列の種類

        /// <summary>読み取り専用の列か (判定はここ 1 か所): キー・Ver・Creator・Updator・CreatedTime・UpdatedTime・RemainingWorkValue (WorkValue と ProgressRate から計算される)。</summary>
        internal static bool IsReadOnlyColumn(string name)
            => name is "ResultId" or "IssueId" or "Ver" or "Creator" or "Updator" or "CreatedTime" or "UpdatedTime" or "RemainingWorkValue";

        /// <summary>拡張の列の、行の JSON での入れ物 (ClassHash など)。標準の列は null。</summary>
        internal static string? GetHashName(string column)
        {
            if (!ExtendedColumnPattern.IsMatch(column)) return null;
            foreach (var prefix in new[] { "Class", "Num", "Date", "Description", "Check", "Attachments" })
            {
                if (column.StartsWith(prefix, StringComparison.Ordinal)) return prefix + "Hash";
            }
            return null;
        }

        internal static bool IsExtendedColumn(string name) => ExtendedColumnPattern.IsMatch(name);

        /// <summary>添付の列 (AttachmentsA など) か。</summary>
        internal static bool IsAttachmentsColumn(string name) => ExtendedColumnPattern.IsMatch(name) && name.StartsWith("Attachments", StringComparison.Ordinal);

        /// <summary>本体の表の定義 (DbColumnDefinition.RawDbTypeName) での添付の列の型の名前。</summary>
        internal const string AttachmentsRawTypeName = "pleasanter_attachments";

        /// <summary>
        /// コメントの列。読むとプリザンターが返すコメントの JSON の文字 ([{"CommentId","CreatedTime","Creator","Body"}...]・新しい順・無ければ "[]")、
        /// 書くと空でない文字を新しいコメント 1 件として足す (空・null は送らない。前のコメントは変わらない)。
        /// 条件・並び・集計には使えない (View の条件で絞れず、値は JSON なので)。
        /// </summary>
        internal const string CommentsColumn = "Comments";

        /// <summary>View の条件 (ColumnFilterHash) に書ける列か。RemainingWorkValue は計算される列で、条件に書くとプリザンターがエラー画面へのリダイレクトを返す (実物で確認)。</summary>
        internal static bool IsFilterableColumn(string name) => name != "RemainingWorkValue";

        //標準の列 (名前・種類・null を持てるか)
        static IEnumerable<(string Name, PleasanterColumnKind Kind, bool Nullable)> StandardColumns(string referenceType)
        {
            if (referenceType == Issues)
            {
                yield return ("IssueId", PleasanterColumnKind.Number, false);
                yield return ("Title", PleasanterColumnKind.String, true);
                yield return ("Body", PleasanterColumnKind.String, true);
                yield return ("StartTime", PleasanterColumnKind.DateTime, true);
                yield return ("CompletionTime", PleasanterColumnKind.DateTime, true);
                yield return ("WorkValue", PleasanterColumnKind.Number, false);
                yield return ("ProgressRate", PleasanterColumnKind.Number, false);
                yield return ("RemainingWorkValue", PleasanterColumnKind.Number, false);
            }
            else
            {
                yield return ("ResultId", PleasanterColumnKind.Number, false);
                yield return ("Title", PleasanterColumnKind.String, true);
                yield return ("Body", PleasanterColumnKind.String, true);
            }
            yield return ("Status", PleasanterColumnKind.Number, false);
            yield return ("Manager", PleasanterColumnKind.Number, false);
            yield return ("Owner", PleasanterColumnKind.Number, false);
            yield return ("Ver", PleasanterColumnKind.Number, false);
            yield return ("Creator", PleasanterColumnKind.Number, false);
            yield return ("Updator", PleasanterColumnKind.Number, false);
            yield return ("CreatedTime", PleasanterColumnKind.DateTime, true);
            yield return ("UpdatedTime", PleasanterColumnKind.DateTime, true);
            yield return (CommentsColumn, PleasanterColumnKind.String, true);
        }

        // ---------------------------------------------------------------- getsite

        /// <summary>
        /// getsite の応答の Response.Data から定義を作る。応答の SiteId が違う・ReferenceType が Results / Issues でない (Wiki・フォルダ)・
        /// サイト統合 (IntegratedSites) のサイトは、理由つきの PleasanterException。
        /// </summary>
        /// <param name="tableName">表名 (Sites の別名)。</param>
        /// <param name="findTableName">サイト ID → 表名 (Sites にあれば)。リンクの相手の表名に使う。</param>
        internal static PleasanterTableDefinition Parse(string tableName, long siteId, JsonElement data, Func<long, string?> findTableName)
        {
            if (data.ValueKind != JsonValueKind.Object)
                throw new PleasanterException($"Pleasanter: the site settings of '{tableName}' (site {siteId.ToString(CultureInfo.InvariantCulture)}) could not be read.", PleasanterErrorKind.Unknown);
            if (!data.TryGetProperty("SiteId", out var idElement) || !idElement.TryGetInt64(out var returnedId) || returnedId != siteId)
                throw new PleasanterException($"Pleasanter returned the settings of another site for '{tableName}' (site {siteId.ToString(CultureInfo.InvariantCulture)}).", PleasanterErrorKind.Unknown);
            var referenceType = data.TryGetProperty("ReferenceType", out var rt) && rt.ValueKind == JsonValueKind.String ? rt.GetString() ?? string.Empty : string.Empty;
            if (referenceType is not (Results or Issues))
                throw new PleasanterException($"Pleasanter site {siteId.ToString(CultureInfo.InvariantCulture)} ('{tableName}') is a '{referenceType}' site. Only record tables (Results) and tables with deadlines (Issues) can be used as tables.");

            var settings = data.TryGetProperty("SiteSettings", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
            if (settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("IntegratedSites", out var integrated) &&
                integrated.ValueKind == JsonValueKind.Array && integrated.GetArrayLength() != 0)
                throw new PleasanterException($"Pleasanter site {siteId.ToString(CultureInfo.InvariantCulture)} ('{tableName}') integrates other sites (IntegratedSites). An integrated site cannot be used as a table because its rows come from several sites.");

            //列の設定 (Columns の要素)
            var columnSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var extendedNames = new List<string>();
            void AddName(string? name)
            {
                if (name != null && IsExtendedColumn(name) && !extendedNames.Contains(name)) extendedNames.Add(name);
            }
            if (settings.ValueKind == JsonValueKind.Object)
            {
                //EditorColumnHash は全部のタブ (General と _Tab-n)。セクション (_Section-n) やリンク (_Links-n) は列でないので名前の形で外れる
                if (settings.TryGetProperty("EditorColumnHash", out var editor) && editor.ValueKind == JsonValueKind.Object)
                {
                    foreach (var tab in editor.EnumerateObject().OrderBy(e => e.Name == "General" ? 0 : 1))
                    {
                        if (tab.Value.ValueKind != JsonValueKind.Array) continue;
                        foreach (var name in tab.Value.EnumerateArray()) if (name.ValueKind == JsonValueKind.String) AddName(name.GetString());
                    }
                }
                if (settings.TryGetProperty("GridColumns", out var grid) && grid.ValueKind == JsonValueKind.Array)
                {
                    foreach (var name in grid.EnumerateArray()) if (name.ValueKind == JsonValueKind.String) AddName(name.GetString());
                }
                if (settings.TryGetProperty("Columns", out var columns) && columns.ValueKind == JsonValueKind.Array)
                {
                    foreach (var column in columns.EnumerateArray())
                    {
                        if (column.ValueKind != JsonValueKind.Object || !column.TryGetProperty("ColumnName", out var n) || n.ValueKind != JsonValueKind.String) continue;
                        var name = n.GetString()!;
                        //列の設定だけにある列 (エディタにも一覧にも無い列) は API の行に入らない (SiteSettings.ReadableColumns) ので列にしない
                        columnSettings.TryAdd(name, column);
                    }
                }
            }

            var titleIsRaw = true;
            if (settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("TitleColumns", out var titleColumns) && titleColumns.ValueKind == JsonValueKind.Array)
            {
                var names = titleColumns.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()).ToList();
                titleIsRaw = names.Count == 0 || (names.Count == 1 && names[0] == "Title");
            }

            var table = new PleasanterTableDefinition { Name = tableName, SiteId = siteId, ReferenceType = referenceType, TitleIsRaw = titleIsRaw };
            foreach (var (name, kind, nullable) in StandardColumns(referenceType))
            {
                table.Columns.Add(CreateColumn(name, kind, nullable, columnSettings.GetValueOrDefault(name), findTableName));
            }
            foreach (var name in extendedNames)
            {
                var setting = columnSettings.GetValueOrDefault(name);
                var kind = GetHashName(name) switch
                {
                    "NumHash" => PleasanterColumnKind.Number,
                    "DateHash" => IsDateOnlyFormat(GetString(setting, "EditorFormat")) ? PleasanterColumnKind.Date : PleasanterColumnKind.DateTime,
                    "CheckHash" => PleasanterColumnKind.Bool,
                    "AttachmentsHash" => PleasanterColumnKind.Attachments,
                    _ => PleasanterColumnKind.String,
                };
                table.Columns.Add(CreateColumn(name, kind, false, setting, findTableName));
            }
            return table;
        }

        static bool IsDateOnlyFormat(string? editorFormat) => string.IsNullOrEmpty(editorFormat) || editorFormat == "Ymd";

        static PleasanterColumnDefinition CreateColumn(string name, PleasanterColumnKind kind, bool nullable, JsonElement setting, Func<long, string?> findTableName)
        {
            var (choices, linkSiteId) = ParseChoices(GetString(setting, "ChoicesText"));
            //数値の列が null を持てるかは列の設定 Nullable (既定は持てない = 空は 0)
            if (kind == PleasanterColumnKind.Number && setting.ValueKind == JsonValueKind.Object && setting.TryGetProperty("Nullable", out var n) &&
                n.ValueKind is JsonValueKind.True or JsonValueKind.False)
                nullable = n.GetBoolean();
            return new PleasanterColumnDefinition
            {
                Name = name,
                Kind = kind,
                IsReadOnly = IsReadOnlyColumn(name),
                IsNullable = kind != PleasanterColumnKind.Number || nullable,
                LabelText = GetString(setting, "LabelText"),
                Choices = choices,
                LinkSiteId = linkSiteId,
                LinkTable = linkSiteId == null ? null : findTableName(linkSiteId.Value),
                MultipleSelections = setting.ValueKind == JsonValueKind.Object && setting.TryGetProperty("MultipleSelections", out var m) && m.ValueKind == JsonValueKind.True,
            };
        }

        static string? GetString(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        /// <summary>
        /// ChoicesText を選択肢とリンクに分ける。1 行が 1 つ: "値,表示[,短い表示,CSS]" (カンマが無ければ値だけ)。
        /// "[[サイトID]]" (後ろに ,オプション があってもよい) の行はリンク。ほかの "[[...]]" (ユーザー・組織など) は選択肢にしない。
        /// </summary>
        internal static (List<PleasanterChoice>? Choices, long? LinkSiteId) ParseChoices(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (null, null);
            var choices = new List<PleasanterChoice>();
            long? link = null;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("[[", StringComparison.Ordinal))
                {
                    var match = LinkPattern.Match(line);
                    if (match.Success && link == null && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var siteId)) link = siteId;
                    continue;
                }
                var parts = line.Split(',');
                choices.Add(new PleasanterChoice(parts[0], parts.Length < 2 || parts[1].Length == 0 ? null : parts[1]));
            }
            return (choices.Count == 0 ? null : choices, link);
        }
    }
}
