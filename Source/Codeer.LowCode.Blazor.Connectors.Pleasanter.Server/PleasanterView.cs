using System.Globalization;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>プリザンターの一覧の要求の View に書く条件 (ColumnFilterHash / ColumnFilterSearchTypes)。</summary>
    sealed class ViewFilter
    {
        public Dictionary<string, string> ColumnFilterHash { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> ColumnFilterSearchTypes { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// View の条件が SQL の条件と同じ行を返すか。false なら View は「広めの条件」(SQL の条件に合う行を全部含む) で、
        /// 読んだ行を <see cref="PleasanterView.Evaluate"/> で絞る。
        /// </summary>
        public bool IsExact { get; set; } = true;
    }

    /// <summary>
    /// 条件の構文木 → プリザンターの View の条件と、読んだ行のメモリでの評価。
    /// 正確に写せる葉だけを View に書き、写せない葉は「真」に緩める (and の中なら外れる・or の中にあれば or 全体が真)。
    /// 緩めたら <see cref="ViewFilter.IsExact"/> が false になり、SQL の条件の全体をメモリで評価する (SQL の 3 値の論理)。
    /// </summary>
    static class PleasanterView
    {
        internal const string ExactMatchMultiple = "ExactMatchMultiple";
        internal const string PartialMatchMultiple = "PartialMatchMultiple";

        /// <summary>集めた値との一致を View に書く値の数の上限 (超えたら緩めてメモリで評価する)。</summary>
        internal const int MaxViewValues = 500;

        //View の条件の中間の形
        abstract record VNode;
        //SearchType は文字の列の一致 (ExactMatchMultiple) / 部分一致 (PartialMatchMultiple)。空の判定 (["\t"]) だけの葉は null (どちらでもよい)
        sealed record VLeaf(string Column, string? SearchType, List<string>? Values, string? Text) : VNode;
        sealed record VAnd(List<VNode> Children) : VNode;
        sealed record VOr(List<VNode> Children) : VNode;

        /// <summary>条件を View に写す。filter が null なら条件なし (正確)。</summary>
        internal static ViewFilter Build(PleasanterTableDefinition definition, FilterNode? filter, DateTime emptyDate)
        {
            var result = new ViewFilter();
            if (filter == null) return result;
            var context = new BuildContext(definition, emptyDate);
            context.CollectUsage(filter);
            var (view, exact) = context.Map(filter);
            result.IsExact = exact;
            if (view == null) return result;
            var counter = 0;
            foreach (var (key, value) in Render(view, ref counter)) result.ColumnFilterHash[key] = value;
            //文字の列には検索の種類を必ず書く (書かないと列の既定の種類になる)
            foreach (var leaf in Leaves(view))
            {
                if (definition.FindColumn(leaf.Column)?.Kind != PleasanterColumnKind.String) continue;
                if (leaf.SearchType != null) result.ColumnFilterSearchTypes[leaf.Column] = leaf.SearchType;
                else result.ColumnFilterSearchTypes.TryAdd(leaf.Column, ExactMatchMultiple);
            }
            return result;
        }

        static IEnumerable<VLeaf> Leaves(VNode node) => node switch
        {
            VLeaf leaf => new[] { leaf },
            VAnd and => and.Children.SelectMany(Leaves),
            VOr or => or.Children.SelectMany(Leaves),
            _ => Array.Empty<VLeaf>(),
        };

        //and の子は and_n、or は or_n の中に子ごとの and_n (同じ列を 2 回書けるように、子は必ず 1 段包む)
        static Dictionary<string, string> Render(VNode node, ref int counter)
        {
            var hash = new Dictionary<string, string>(StringComparer.Ordinal);
            switch (node)
            {
                case VLeaf leaf:
                    hash[leaf.Column] = leaf.Text ?? JsonSerializer.Serialize(leaf.Values);
                    break;
                case VAnd and:
                    foreach (var child in and.Children)
                    {
                        var name = "and_" + (++counter).ToString(CultureInfo.InvariantCulture);
                        hash[name] = JsonSerializer.Serialize(Render(child, ref counter));
                    }
                    break;
                case VOr or:
                    {
                        var inner = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var child in or.Children)
                        {
                            var name = "and_" + (++counter).ToString(CultureInfo.InvariantCulture);
                            inner[name] = JsonSerializer.Serialize(Render(child, ref counter));
                        }
                        hash["or_" + (++counter).ToString(CultureInfo.InvariantCulture)] = JsonSerializer.Serialize(inner);
                        break;
                    }
            }
            return hash;
        }

        sealed class BuildContext
        {
            readonly PleasanterTableDefinition _definition;
            readonly DateTime _emptyDate;
            //一致 (ExactMatchMultiple) で使う文字の列。部分一致と両方に使う列は、部分一致の葉を緩める (検索の種類は列ごとに 1 つ)
            readonly HashSet<string> _exactColumns = new(StringComparer.Ordinal);

            public BuildContext(PleasanterTableDefinition definition, DateTime emptyDate)
            {
                _definition = definition;
                _emptyDate = emptyDate;
            }

            public void CollectUsage(FilterNode node)
            {
                switch (node)
                {
                    case PredicateNode { Operator: PredicateOperator.Equal } p:
                        _exactColumns.Add(p.Column);
                        break;
                    case ValuesNode v:
                        _exactColumns.Add(v.Column);
                        break;
                    case NotNode not:
                        CollectUsage(not.Child);
                        break;
                    case AndNode and:
                        foreach (var child in and.Children) CollectUsage(child);
                        break;
                    case OrNode or:
                        foreach (var child in or.Children) CollectUsage(child);
                        break;
                }
            }

            /// <summary>View の条件 (null = 絞らない) と、それが正確か。</summary>
            public (VNode? View, bool Exact) Map(FilterNode node)
            {
                switch (node)
                {
                    case ConstNode constant:
                        //偽は View に書けない (一番外の偽は要求を送らない。中の偽は畳んで消えている)
                        return (null, constant.Value);
                    case PredicateNode predicate:
                        return MapPredicate(predicate);
                    case ValuesNode values:
                        return MapValues(values);
                    case NotNode:
                        //否定フィルタはサイトの設定次第で黙って無視されるので使わない
                        return (null, false);
                    case AndNode and:
                        {
                            var views = new List<VNode>();
                            var exact = true;
                            foreach (var child in and.Children)
                            {
                                var (view, childExact) = Map(child);
                                exact &= childExact;
                                if (view != null) views.Add(view);
                            }
                            return (views.Count == 0 ? null : views.Count == 1 ? views[0] : new VAnd(views), exact);
                        }
                    case OrNode or:
                        {
                            var views = new List<VNode>();
                            var exact = true;
                            foreach (var child in or.Children)
                            {
                                var (view, childExact) = Map(child);
                                //絞らない子があれば or 全体が絞らない (正確に真の子なら正確に真)
                                if (view == null) return (null, childExact);
                                exact &= childExact;
                                views.Add(view);
                            }
                            return (Merge(views) ?? new VOr(views), exact);
                        }
                    default:
                        return (null, false);
                }
            }

            //同じ列・同じ検索の種類の葉の or は 1 つの葉 (値の並びは or)
            static VNode? Merge(List<VNode> views)
            {
                if (views.Count == 1) return views[0];
                if (!views.All(e => e is VLeaf { Values: not null })) return null;
                var leaves = views.Cast<VLeaf>().ToList();
                var column = leaves[0].Column;
                if (leaves.Any(e => e.Column != column)) return null;
                var types = leaves.Select(e => e.SearchType).Where(e => e != null).Distinct().ToList();
                if (1 < types.Count) return null;
                return new VLeaf(column, types.FirstOrDefault(), leaves.SelectMany(e => e.Values!).Distinct().ToList(), null);
            }

            (VNode? View, bool Exact) MapPredicate(PredicateNode predicate)
            {
                var column = _definition.FindColumn(predicate.Column);
                if (column == null || column.MultipleSelections || !PleasanterTableDefinition.IsFilterableColumn(column.Name)) return (null, false);
                var op = predicate.Operator;
                switch (column.Kind)
                {
                    case PleasanterColumnKind.String:
                        {
                            //Title の条件はプリザンターでは表示用のタイトルで評価される
                            if (column.Name == "Title" && (!_definition.TitleIsRaw || op == PredicateOperator.IsNull)) return (null, false);
                            if (op == PredicateOperator.IsNull) return (new VLeaf(column.Name, null, new() { "\t" }, null), true);
                            if (predicate.Value == null || !PleasanterValues.TryConvert(column.Kind, predicate.Value, out var converted)) return (null, false);
                            var text = (string)converted;
                            if (text.Length == 0 || text == "\t") return (null, false);
                            if (op == PredicateOperator.Equal) return (new VLeaf(column.Name, ExactMatchMultiple, new() { text }, null), true);
                            if (op == PredicateOperator.Like && !_exactColumns.Contains(column.Name)) return (new VLeaf(column.Name, PartialMatchMultiple, new() { text }, null), true);
                            return (null, false);
                        }
                    case PleasanterColumnKind.Number:
                        {
                            if (op == PredicateOperator.IsNull)
                                return column.IsNullable ? (new VLeaf(column.Name, null, new() { "\t" }, null), true) : (null, false);
                            if (predicate.Value == null || !PleasanterValues.TryConvert(column.Kind, predicate.Value, out var converted)) return (null, false);
                            var number = PleasanterValues.FormatNumber((decimal)converted);
                            return op switch
                            {
                                PredicateOperator.Equal => (Range(column, number, number), true),
                                PredicateOperator.LessThanOrEqual => (Range(column, string.Empty, number), true),
                                PredicateOperator.GreaterThanOrEqual => (Range(column, number, string.Empty), true),
                                PredicateOperator.LessThan => (Range(column, string.Empty, number), false),
                                PredicateOperator.GreaterThan => (Range(column, number, string.Empty), false),
                                _ => (null, false),
                            };
                        }
                    case PleasanterColumnKind.Date:
                    case PleasanterColumnKind.DateTime:
                        {
                            if (op == PredicateOperator.IsNull) return (new VLeaf(column.Name, null, new() { "\t" }, null), true);
                            if (predicate.Value == null || !PleasanterValues.TryConvert(column.Kind, predicate.Value, out var converted)) return (null, false);
                            var value = (DateTime)converted;
                            if (value == _emptyDate) return (null, false);
                            var whole = value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
                            //CreatedTime / UpdatedTime は秒より細かい値を持つ (API は秒までを返す)・秒より細かい値の比較は、前後 1 秒に広げて送りメモリで評価する
                            if (column.IsSystemTime || whole != value)
                            {
                                var from = Format(whole.AddSeconds(-1));
                                var to = Format(whole.AddSeconds(1));
                                return op switch
                                {
                                    PredicateOperator.Equal => (Range(column, from, to), false),
                                    PredicateOperator.LessThanOrEqual or PredicateOperator.LessThan => (Range(column, string.Empty, to), false),
                                    PredicateOperator.GreaterThanOrEqual or PredicateOperator.GreaterThan => (Range(column, from, string.Empty), false),
                                    _ => (null, false),
                                };
                            }
                            var text = Format(value);
                            return op switch
                            {
                                PredicateOperator.Equal => (Range(column, text, text), true),
                                PredicateOperator.LessThanOrEqual => (Range(column, string.Empty, text), true),
                                PredicateOperator.GreaterThanOrEqual => (Range(column, text, string.Empty), true),
                                PredicateOperator.LessThan => (Range(column, string.Empty, text), false),
                                PredicateOperator.GreaterThan => (Range(column, text, string.Empty), false),
                                _ => (null, false),
                            };
                        }
                    case PleasanterColumnKind.Bool:
                        {
                            if (op != PredicateOperator.Equal || predicate.Value == null || !PleasanterValues.TryConvert(column.Kind, predicate.Value, out var converted)) return (null, false);
                            //"false" は null の行も含む (読むと false なのでメモリの評価と同じ)
                            return (new VLeaf(column.Name, null, null, (bool)converted ? "true" : "false"), true);
                        }
                }
                return (null, false);
            }

            static VLeaf Range(PleasanterColumnDefinition column, string from, string to) => new(column.Name, null, new() { from + "," + to }, null);

            static string Format(DateTime value) => value.ToString(PleasanterValues.DateTimeFormat, CultureInfo.InvariantCulture);

            (VNode? View, bool Exact) MapValues(ValuesNode values)
            {
                var column = _definition.FindColumn(values.Column);
                if (column == null || column.MultipleSelections || column.Kind == PleasanterColumnKind.Bool || !PleasanterTableDefinition.IsFilterableColumn(column.Name)) return (null, false);
                if (column.Name == "Title" && !_definition.TitleIsRaw) return (null, false);
                if (MaxViewValues < values.Values.Count) return (null, false);
                var items = new List<string>();
                foreach (var value in values.Values)
                {
                    //列の種類で表せない値はどの行とも一致しない
                    if (!PleasanterValues.TryConvert(column.Kind, value, out var converted)) continue;
                    switch (column.Kind)
                    {
                        case PleasanterColumnKind.String:
                            {
                                var text = (string)converted;
                                if (text.Length == 0 || text == "\t") return (null, false);
                                items.Add(text);
                                break;
                            }
                        case PleasanterColumnKind.Number:
                            {
                                var number = PleasanterValues.FormatNumber((decimal)converted);
                                items.Add(number + "," + number);
                                break;
                            }
                        default:
                            {
                                var date = (DateTime)converted;
                                if (column.IsSystemTime || date == _emptyDate || date.Ticks % TimeSpan.TicksPerSecond != 0) return (null, false);
                                var text = Format(date);
                                items.Add(text + "," + text);
                                break;
                            }
                    }
                }
                //一致する値が無い (偽) は View に書けない
                if (items.Count == 0) return (null, false);
                return (new VLeaf(column.Name, column.Kind == PleasanterColumnKind.String ? ExactMatchMultiple : null, items.Distinct().ToList(), null), true);
            }
        }

        // ---------------------------------------------------------------- メモリでの評価

        /// <summary>
        /// 読んだ行 (<see cref="PleasanterValues.ToValues"/> の結果) で条件を評価する。SQL の 3 値の論理 (null = unknown)。
        /// 値の扱いは View と揃える: 文字の "" と空の日付は null (読んだ時点で null)。文字の一致は大文字・小文字を区別し、部分一致は区別しない
        /// (プリザンター (PostgreSQL) は = と ilike)。
        /// </summary>
        internal static bool? Evaluate(FilterNode node, PleasanterTableDefinition definition, Dictionary<string, object?> row)
        {
            switch (node)
            {
                case ConstNode constant:
                    return constant.Value;
                case NotNode not:
                    return !Evaluate(not.Child, definition, row);
                case AndNode and:
                    {
                        bool? result = true;
                        foreach (var child in and.Children)
                        {
                            var value = Evaluate(child, definition, row);
                            if (value == false) return false;
                            if (value == null) result = null;
                        }
                        return result;
                    }
                case OrNode or:
                    {
                        bool? result = false;
                        foreach (var child in or.Children)
                        {
                            var value = Evaluate(child, definition, row);
                            if (value == true) return true;
                            if (value == null) result = null;
                        }
                        return result;
                    }
                case ValuesNode values:
                    return values.Contains(PleasanterValues.ToComparable(row.GetValueOrDefault(values.Column)));
                case PredicateNode predicate:
                    return EvaluatePredicate(predicate, definition, row);
                default:
                    throw new InvalidOperationException("Unexpected condition node: " + node.GetType().Name);
            }
        }

        static bool? EvaluatePredicate(PredicateNode predicate, PleasanterTableDefinition definition, Dictionary<string, object?> row)
        {
            var column = definition.FindColumn(predicate.Column)!;
            var value = row.GetValueOrDefault(predicate.Column);
            switch (predicate.Operator)
            {
                case PredicateOperator.IsNull:
                    return value == null;
                case PredicateOperator.IsNotNull:
                    return value != null;
            }
            if (value == null || predicate.Value == null) return null;
            if (predicate.Operator == PredicateOperator.Like)
            {
                var text = value as string ?? PleasanterValues.KeyText(value);
                var pattern = predicate.Value as string ?? PleasanterValues.KeyText(predicate.Value);
                return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            }
            //列の種類で表せない値との比較は unknown にする (DB なら型のエラー)
            if (!PleasanterValues.TryConvert(column.Kind, predicate.Value, out var right)) return null;
            var compare = PleasanterValues.Compare(value, right);
            return predicate.Operator switch
            {
                PredicateOperator.Equal => compare == 0,
                PredicateOperator.NotEqual => compare != 0,
                PredicateOperator.LessThan => compare < 0,
                PredicateOperator.LessThanOrEqual => compare <= 0,
                PredicateOperator.GreaterThan => compare > 0,
                PredicateOperator.GreaterThanOrEqual => compare >= 0,
                _ => null,
            };
        }
    }
}
