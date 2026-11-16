namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>
    /// 読み出しの計画 (<see cref="ReadPlan"/> / <see cref="AggregatePlan"/>) をプリザンターの一覧の要求 (api/items/{siteId}/get) で実行する。
    /// 条件が View に正確に写せればページ・件数・並びをプリザンターに任せる。写せなければ広めの条件で全部読み (MaxRows まで)、メモリで評価・並べ・ページを切る。
    /// 別の表を読んで値を集める条件 (結合先の列の条件・一覧をまたぐ条件) は、値を集めてから主表の条件 (値のどれかに一致) に読み替える。
    /// どの読み方でも MaxRows を超えたらエラーにする (黙って切り捨てない)。
    /// </summary>
    static class QueryExecutor
    {
        /// <summary>プリザンターが 1 回の要求で返す行数の上限 (既定の設定)。</summary>
        internal const int ServerPageSize = 200;

        // ---------------------------------------------------------------- 一覧・件数

        internal static async Task<List<IDictionary<string, object>>> ReadAsync(PleasanterConnection connection, ReadPlan read)
        {
            var maxRows = connection.Settings.MaxRows;
            var joins = NeededJoins(read.Joins, read.MainAlias, read.Columns.Select(e => e.Alias).Concat(read.Order.Select(e => e.Alias)));
            var filter = read.IsEmpty ? new ConstNode(false) : read.Where == null ? null : await ResolveAsync(connection, read.Where, read.Definition);
            if (filter is ConstNode constant)
            {
                if (!constant.Value) return read.IsCount ? CountResult(0) : new();
                filter = null;
            }
            var view = CreateView(connection, read.Definition, filter);
            var (sorter, serverSort) = CreateSorter(read.Definition, read.MainAlias, read.Order);

            if (read.IsCount)
            {
                if (view.IsExact) return CountResult(await connection.ReadCountAsync(read.Definition, view));
                return CountResult((await ReadFilteredAsync(connection, read.Definition, filter!, view, sorter, maxRows)).Count);
            }
            if (read.Limit == 0) return new();

            List<Dictionary<string, object?>> mainRows;
            Dictionary<string, List<Dictionary<string, object?>?>> rowsByAlias;
            if (view.IsExact && serverSort)
            {
                //ページ・並びともプリザンター
                mainRows = await ReadRangeAsync(connection, read.Definition, view, sorter, read.Offset, read.Limit, maxRows);
                rowsByAlias = await JoinAsync(connection, read.MainAlias, joins, mainRows);
            }
            else
            {
                //条件に合う行を全部読み (MaxRows まで)、メモリで絞り・並べ・ページを切る
                mainRows = await ReadFilteredAsync(connection, read.Definition, filter, view, sorter, maxRows);
                if (!serverSort)
                {
                    rowsByAlias = await JoinAsync(connection, read.MainAlias, joins, mainRows);
                    var order = Page(SortOrder(read, rowsByAlias, mainRows.Count), read.Offset, read.Limit);
                    mainRows = order.Select(i => mainRows[i]).ToList();
                    rowsByAlias = rowsByAlias.ToDictionary(e => e.Key, e => order.Select(i => e.Value[i]).ToList());
                }
                else
                {
                    //並びはプリザンターの並びのまま (絞るだけなので順は保たれる)
                    mainRows = mainRows.Skip(read.Offset).Take(read.Limit ?? int.MaxValue).ToList();
                    rowsByAlias = await JoinAsync(connection, read.MainAlias, joins, mainRows);
                }
            }

            var result = new List<IDictionary<string, object>>();
            for (var i = 0; i < mainRows.Count; i++)
            {
                var row = new Dictionary<string, object>();
                foreach (var column in read.Columns)
                {
                    object? value = null;
                    rowsByAlias[column.Alias][i]?.TryGetValue(column.Column, out value);
                    row[column.Key] = value!;
                }
                result.Add(row);
            }
            return result;
        }

        static List<IDictionary<string, object>> CountResult(long count) => new() { new Dictionary<string, object> { ["count"] = count } };

        /// <summary>View の条件。差分のテスト用にメモリの道を強いるときは、条件を送らずに全部読んでメモリで評価する。</summary>
        static ViewFilter CreateView(PleasanterConnection connection, PleasanterTableDefinition definition, FilterNode? filter)
        {
            if (filter != null && connection.ForceMemoryEvaluation) return new ViewFilter { IsExact = false };
            return PleasanterView.Build(definition, filter, connection.Settings.EmptyDate);
        }

        /// <summary>
        /// 並び (ColumnSorterHash)。最後にキーの列の昇順を必ず足す (順序を決めるため。足さないとプリザンターが更新日時の降順を足す)。
        /// プリザンターで並べられない並び (結合先の列・リンクの列 (参照先のタイトルで並ぶ)・表示用のタイトルが Title でないサイトの Title) があれば serverSort = false。
        /// </summary>
        internal static (List<KeyValuePair<string, string>> Sorter, bool ServerSort) CreateSorter(PleasanterTableDefinition definition, string mainAlias, List<OrderItem> order)
        {
            var sorter = new List<KeyValuePair<string, string>>();
            var serverSort = true;
            foreach (var item in order)
            {
                var column = item.Alias == mainAlias ? definition.FindColumn(item.Column) : null;
                if (column == null || column.LinkSiteId != null || (column.Name == "Title" && !definition.TitleIsRaw))
                {
                    serverSort = false;
                    continue;
                }
                if (sorter.Any(e => e.Key == column.Name)) continue;
                sorter.Add(new(column.Name, item.Descending ? "desc" : "asc"));
            }
            if (!sorter.Any(e => e.Key == definition.KeyColumn)) sorter.Add(new(definition.KeyColumn, "asc"));
            return (sorter, serverSort);
        }

        //主表の行に結合先の行を付ける (別名 → 行ごとの値。結合先が無い行は null)
        static async Task<Dictionary<string, List<Dictionary<string, object?>?>>> JoinAsync(PleasanterConnection connection, string mainAlias, List<JoinPlan> joins, List<Dictionary<string, object?>> mainRows)
        {
            var rowsByAlias = new Dictionary<string, List<Dictionary<string, object?>?>> { [mainAlias] = mainRows.Cast<Dictionary<string, object?>?>().ToList() };
            foreach (var join in joins) rowsByAlias[join.Alias] = await ReadJoinAsync(connection, join, rowsByAlias[join.SourceAlias]);
            return rowsByAlias;
        }

        //使う別名とその結合元をたどった結合だけ (from の順のまま)。条件だけに使う結合は読まない (条件は値を集めて読み替え済み)
        static List<JoinPlan> NeededJoins(List<JoinPlan> joins, string mainAlias, IEnumerable<string> aliases)
        {
            var needed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var alias in aliases)
            {
                for (var a = alias; a != mainAlias && needed.Add(a);) a = joins.First(e => e.Alias == a).SourceAlias;
            }
            return joins.Where(e => needed.Contains(e.Alias)).ToList();
        }

        static List<int> Page(List<int> order, int offset, int? limit)
            => order.Skip(offset).Take(limit ?? int.MaxValue).ToList();

        //メモリでの並び。並びの項目のあとにキーの列。空の値はプリザンター (PostgreSQL) と同じ向き: 文字は昇順で先 (空の文字は '')、ほかは昇順で後
        static List<int> SortOrder(ReadPlan read, Dictionary<string, List<Dictionary<string, object?>?>> rowsByAlias, int count)
        {
            var keys = read.Order.Select(e => (e.Alias, e.Column, e.Descending)).ToList();
            if (!keys.Any(e => e.Alias == read.MainAlias && e.Column == read.Definition.KeyColumn)) keys.Add((read.MainAlias, read.Definition.KeyColumn, false));
            var definitions = new Dictionary<string, PleasanterTableDefinition>(StringComparer.Ordinal) { [read.MainAlias] = read.Definition };
            foreach (var join in read.Joins) definitions[join.Alias] = join.Definition;
            var nullsFirst = keys.Select(e => definitions[e.Alias].FindColumn(e.Column)?.Kind == PleasanterColumnKind.String).ToList();
            object? Value(int row, string alias, string column)
            {
                object? value = null;
                rowsByAlias[alias][row]?.TryGetValue(column, out value);
                return value;
            }
            var order = Enumerable.Range(0, count).ToList();
            order.Sort((a, b) =>
            {
                for (var k = 0; k < keys.Count; k++)
                {
                    var (alias, column, descending) = keys[k];
                    var x = Value(a, alias, column);
                    var y = Value(b, alias, column);
                    int compare;
                    if (x == null) compare = y == null ? 0 : nullsFirst[k] ? -1 : 1;
                    else if (y == null) compare = nullsFirst[k] ? 1 : -1;
                    else compare = PleasanterValues.Compare(x, y);
                    if (compare != 0) return descending ? -compare : compare;
                }
                return a.CompareTo(b);
            });
            return order;
        }

        // ---------------------------------------------------------------- 条件の読み替え

        /// <summary>
        /// 条件の中の「別の表を読んで集める値との一致」(<see cref="InSetNode"/>) を、値を集めて <see cref="ValuesNode"/> にし、畳み直す。
        /// 集めた値が無ければ偽。table はこの条件で絞る表 (集めた値をその列の種類の値にする)。
        /// </summary>
        internal static async Task<FilterNode> ResolveAsync(PleasanterConnection connection, FilterNode node, PleasanterTableDefinition table)
        {
            var cache = new Dictionary<KeySource, List<object>>(ReferenceEqualityComparer.Instance);
            return SqlTranslator.Fold(await ResolveCoreAsync(connection, SqlTranslator.Fold(node), table, cache));
        }

        static async Task<FilterNode> ResolveCoreAsync(PleasanterConnection connection, FilterNode node, PleasanterTableDefinition table, Dictionary<KeySource, List<object>> cache)
        {
            switch (node)
            {
                case InSetNode inSet:
                    {
                        if (!cache.TryGetValue(inSet.Source, out var values))
                        {
                            values = await ReadKeysAsync(connection, inSet.Source);
                            cache[inSet.Source] = values;
                        }
                        var kind = table.FindColumn(inSet.Column)!.Kind;
                        var converted = new List<object>();
                        foreach (var value in values)
                        {
                            //列の種類で表せない値 (数値の列に数字でない文字など) はその列のどの行とも一致しない
                            if (PleasanterValues.TryConvert(kind, value, out var c)) converted.Add(c);
                        }
                        return converted.Count == 0 ? new ConstNode(false) : new ValuesNode(inSet.Column, converted);
                    }
                case NotNode not:
                    return new NotNode(await ResolveCoreAsync(connection, not.Child, table, cache));
                case AndNode and:
                    {
                        var children = new List<FilterNode>();
                        foreach (var child in and.Children) children.Add(await ResolveCoreAsync(connection, child, table, cache));
                        return new AndNode(children);
                    }
                case OrNode or:
                    {
                        var children = new List<FilterNode>();
                        foreach (var child in or.Children) children.Add(await ResolveCoreAsync(connection, child, table, cache));
                        return new OrNode(children);
                    }
                default:
                    return node;
            }
        }

        //別の表を条件で読んで、1 つの列の値を集める (重ならない・null は除く)。読む行は MaxRows まで
        static async Task<List<object>> ReadKeysAsync(PleasanterConnection connection, KeySource source)
        {
            var filter = await ResolveAsync(connection, source.Filter, source.Definition);
            if (filter is ConstNode { Value: false }) return new();
            var rows = await ReadFilteredAsync(connection, source.Definition, filter is ConstNode ? null : filter, connection.Settings.MaxRows);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var values = new List<object>();
            foreach (var row in rows)
            {
                if (!row.TryGetValue(source.Column, out var value) || value == null) continue;
                if (seen.Add(PleasanterValues.KeyText(PleasanterValues.ToComparable(value)!))) values.Add(value);
            }
            return values;
        }

        /// <summary>条件に合う行を全部読む (MaxRows まで)。並びはキーの昇順。</summary>
        internal static Task<List<Dictionary<string, object?>>> ReadFilteredAsync(PleasanterConnection connection, PleasanterTableDefinition definition, FilterNode? filter, int maxRows)
        {
            var (sorter, _) = CreateSorter(definition, string.Empty, new());
            return ReadFilteredAsync(connection, definition, filter, CreateView(connection, definition, filter), sorter, maxRows);
        }

        //View で読み、View が広めの条件ならメモリで評価して絞る (順は読んだ順のまま)
        static async Task<List<Dictionary<string, object?>>> ReadFilteredAsync(PleasanterConnection connection, PleasanterTableDefinition definition, FilterNode? filter,
            ViewFilter view, List<KeyValuePair<string, string>> sorter, int maxRows)
        {
            var rows = await ReadRangeAsync(connection, definition, view, sorter, 0, null, maxRows);
            if (view.IsExact || filter == null) return rows;
            return rows.Where(e => PleasanterView.Evaluate(filter, definition, e) == true).ToList();
        }

        /// <summary>
        /// offset から limit 行 (null なら最後まで) を読む。1 回の要求の上限 (200 行) を超える分は、受け取った行数だけ Offset を進めて続きを読む。
        /// 最後まで読むときは、条件に合う件数 (TotalCount) が maxRows を超えたらエラー。同じキーの行は 1 度だけ (読む間に行が動いたとき)。
        /// </summary>
        static async Task<List<Dictionary<string, object?>>> ReadRangeAsync(PleasanterConnection connection, PleasanterTableDefinition definition, ViewFilter view,
            List<KeyValuePair<string, string>> sorter, int offset, int? limit, int maxRows)
        {
            var rows = new List<Dictionary<string, object?>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var position = offset;
            while (limit == null || rows.Count < limit)
            {
                var pageSize = limit == null ? ServerPageSize : Math.Min(ServerPageSize, limit.Value - rows.Count);
                var page = await connection.ReadPageAsync(definition, view, sorter, position, pageSize);
                if (limit == null && maxRows < page.TotalCount - offset) throw TooManyRows(maxRows);
                foreach (var element in page.Rows)
                {
                    var values = PleasanterValues.ToValues(definition, element, connection.Settings.EmptyDate);
                    if (seen.Add(PleasanterValues.KeyText(values[definition.KeyColumn] ?? string.Empty))) rows.Add(values);
                }
                if (limit == null && maxRows < rows.Count) throw TooManyRows(maxRows);
                position += page.Rows.Count;
                if (page.Rows.Count == 0 || page.TotalCount <= position) break;
            }
            return limit == null ? rows : rows.Take(limit.Value).ToList();
        }

        static PleasanterException TooManyRows(int maxRows)
            => new($"The query returned more than {maxRows} rows (MaxRows of the Pleasanter data source). Narrow the condition or use paging.", PleasanterErrorKind.InvalidRequest);

        //結合先の行: 結合元の値を集めて 50 個ずつ (値のどれかに一致) 読み、値で突き合わせる (同じ値の行が複数あれば最初の 1 行)。
        //読む行は合わせて MaxRows まで
        static async Task<List<Dictionary<string, object?>?>> ReadJoinAsync(PleasanterConnection connection, JoinPlan join, List<Dictionary<string, object?>?> sourceRows)
        {
            var maxRows = connection.Settings.MaxRows;
            var destination = join.Definition.FindColumn(join.DestinationColumn)!;
            var keys = new List<object>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in sourceRows)
            {
                if (row == null || !row.TryGetValue(join.SourceColumn, out var value) || value == null) continue;
                if (!PleasanterValues.TryConvert(destination.Kind, value, out var converted)) continue;
                if (seen.Add(PleasanterValues.KeyText(converted))) keys.Add(converted);
            }

            var read = 0;
            var found = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
            for (var start = 0; start < keys.Count; start += SqlTranslator.JoinBatchSize)
            {
                var filter = new ValuesNode(join.DestinationColumn, keys.Skip(start).Take(SqlTranslator.JoinBatchSize).ToList());
                var rows = await ReadFilteredAsync(connection, join.Definition, filter, maxRows - read);
                read += rows.Count;
                foreach (var values in rows)
                {
                    if (!values.TryGetValue(join.DestinationColumn, out var value) || value == null) continue;
                    found.TryAdd(PleasanterValues.KeyText(PleasanterValues.ToComparable(value)!), values);
                }
            }

            return sourceRows.Select(row =>
            {
                if (row == null || !row.TryGetValue(join.SourceColumn, out var value) || value == null) return null;
                if (!PleasanterValues.TryConvert(destination.Kind, value, out var converted)) return null;
                return found.TryGetValue(PleasanterValues.KeyText(converted), out var joined) ? joined : null;
            }).ToList();
        }

        // ---------------------------------------------------------------- 集計

        //集計の 1 単位 (1 行)
        sealed class Member
        {
            public Dictionary<string, Dictionary<string, object?>?> Rows { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// 集計を実行する。プリザンターの API に集計は無いので、条件に合う行を全部読み (MaxRows まで)、結合先を読んで突き合わせ、メモリで集計する。
        /// 意味は SQL と同じ (件数は行数・それ以外は空の値を除いて集計し、値が 1 つも無ければ null・軸の空の値は 1 つのグループ)。
        /// </summary>
        internal static async Task<List<IDictionary<string, object>>> AggregateAsync(PleasanterConnection connection, AggregatePlan plan)
        {
            var maxRows = connection.Settings.MaxRows;
            var filter = plan.IsEmpty ? new ConstNode(false) : plan.Where == null ? null : await ResolveAsync(connection, plan.Where, plan.Definition);
            var isEmpty = filter is ConstNode { Value: false };
            if (filter is ConstNode) filter = null;

            //行を選ぶ定義 (WithinGroupsOf): 先にその集計を実行して、選ばれた軸の値の組を集める。1 行も無ければどの行も選ばれない
            HashSet<string>? withinKeys = null;
            var withinColumns = plan.Within?.On.Select(e => e.Outer).ToList() ?? new();
            if (plan.Within != null && !isEmpty)
            {
                var selected = await AggregateAsync(connection, plan.Within.Plan);
                if (selected.Count == 0) isEmpty = true;
                withinKeys = selected.Select(row => TupleKey(plan.Within.On.Select(e => row.TryGetValue(e.InnerAlias, out var value) ? value : null))).ToHashSet(StringComparer.Ordinal);
            }

            var joinedAliases = plan.Groups.Concat(plan.Measures.Where(e => e.Column != null).Select(e => e.Column!)).Concat(withinColumns)
                .Where(e => e.Alias != plan.MainAlias).Select(e => e.Alias);
            var members = new List<Member>();
            if (!isEmpty)
            {
                foreach (var row in await ReadFilteredAsync(connection, plan.Definition, filter, maxRows))
                {
                    var member = new Member();
                    member.Rows[plan.MainAlias] = row;
                    members.Add(member);
                }
                //結合先 (結合元の値で読み、突き合わせる)
                foreach (var join in NeededJoins(plan.Joins, plan.MainAlias, joinedAliases))
                {
                    var joined = await ReadJoinAsync(connection, join, members.Select(e => e.Rows.GetValueOrDefault(join.SourceAlias)).ToList());
                    for (var i = 0; i < members.Count; i++) members[i].Rows[join.Alias] = joined[i];
                }
                //行を選ぶ定義に合う行だけ残す
                if (withinKeys != null && plan.Within!.On.Count != 0)
                    members = members.Where(e => withinKeys.Contains(TupleKey(withinColumns.Select(c => ValueOf(e, c))))).ToList();
            }

            //軸でまとめる (軸なしは 1 グループ。行が無くても 1 行返す)
            var groups = new List<List<Member>>();
            if (plan.Groups.Count == 0)
            {
                groups.Add(members);
            }
            else
            {
                var index = new Dictionary<string, List<Member>>(StringComparer.Ordinal);
                foreach (var member in members)
                {
                    var key = TupleKey(plan.Groups.Select(e => ValueOf(member, e)));
                    if (!index.TryGetValue(key, out var list))
                    {
                        list = new();
                        index[key] = list;
                        groups.Add(list);
                    }
                    list.Add(member);
                }
            }

            var results = new List<Dictionary<string, object?>>();
            foreach (var group in groups)
            {
                var measures = plan.Measures.ToDictionary(e => e, e => Compute(e, group));
                if (!plan.Having.All(e => IsMatch(measures[e.Measure], e.Operator, e.Value))) continue;
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                var first = group.FirstOrDefault();
                foreach (var item in plan.Items)
                {
                    row[item.Alias] = item.Expression switch
                    {
                        AggregateColumn column => first == null ? null : ValueOf(first, column),
                        AggregateNullFlag flag => (first == null ? null : ValueOf(first, flag.Column)) == null ? 1L : 0L,
                        AggregateRank rank => Rank(rank, first == null ? null : ValueOf(first, rank.Column)),
                        AggregateFunctionCall call => measures[call],
                        _ => 1L,
                    };
                }
                results.Add(row);
            }

            if (plan.IsGroupCount) return CountResult(results.Count);

            //並び (PostgreSQL と同じく空の値は昇順で後・降順で先)。同じ順位は元の順のまま
            var ordered = results.Select((row, i) => (row, i)).ToList();
            ordered.Sort((a, b) =>
            {
                foreach (var (alias, descending) in plan.OrderBy)
                {
                    var x = a.row.GetValueOrDefault(alias);
                    var y = b.row.GetValueOrDefault(alias);
                    var compare = x == null ? (y == null ? 0 : 1) : y == null ? -1 : PleasanterValues.Compare(x, y);
                    if (compare != 0) return descending ? -compare : compare;
                }
                return a.i.CompareTo(b.i);
            });
            return ordered.Skip(plan.Offset).Take(plan.Limit ?? int.MaxValue)
                .Select(e => (IDictionary<string, object>)e.row.ToDictionary(x => x.Key, x => x.Value!)).ToList();
        }

        //値の組の突き合わせの文字 (空の値は空の値と一致する)
        static string TupleKey(IEnumerable<object?> values)
            => string.Join("\u0001", values.Select(e => e is { } value ? "v" + PleasanterValues.KeyText(PleasanterValues.ToComparable(value)!) : "\u0000"));

        static object? ValueOf(Member member, AggregateColumn column)
        {
            object? value = null;
            member.Rows.GetValueOrDefault(column.Alias)?.TryGetValue(column.Column, out value);
            return value;
        }

        static long Rank(AggregateRank rank, object? value)
        {
            if (value == null) return rank.Else;
            var text = PleasanterValues.KeyText(value);
            foreach (var (code, order) in rank.Codes)
            {
                if (code == text) return order;
            }
            return rank.Else;
        }

        //SQL と同じ意味: 件数は行数、それ以外は空の値を除いて集計し、値が 1 つも無ければ null
        static object? Compute(AggregateFunctionCall call, List<Member> members)
        {
            if (call.Column == null) return (long)members.Count;
            var values = members.Select(e => ValueOf(e, call.Column)).Where(e => e != null).Select(e => e!).ToList();
            switch (call.Function)
            {
                case "count":
                    return (long)values.Count;
                case "countdistinct":
                    return (long)values.Select(e => PleasanterValues.KeyText(PleasanterValues.ToComparable(e)!)).Distinct(StringComparer.Ordinal).Count();
                case "sum":
                    return values.Count == 0 ? null : values.Sum(PleasanterValues.ToDecimal);
                case "avg":
                    return values.Count == 0 ? null : values.Sum(PleasanterValues.ToDecimal) / values.Count;
                case "min":
                    return values.Count == 0 ? null : values.Aggregate((a, b) => PleasanterValues.Compare(a, b) <= 0 ? a : b);
                case "max":
                    return values.Count == 0 ? null : values.Aggregate((a, b) => PleasanterValues.Compare(a, b) >= 0 ? a : b);
                default:
                    throw new InvalidOperationException();
            }
        }

        static bool IsMatch(object? value, string op, decimal right)
        {
            if (value == null) return false;
            var left = PleasanterValues.ToDecimal(value);
            return op switch
            {
                "=" => left == right,
                "<>" => left != right,
                "<" => left < right,
                "<=" => left <= right,
                ">" => left > right,
                ">=" => left >= right,
                _ => throw new InvalidOperationException(),
            };
        }
    }
}
