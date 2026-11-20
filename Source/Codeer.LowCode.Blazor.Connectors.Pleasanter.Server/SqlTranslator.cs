using System.Globalization;
using System.Text;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>
    /// 本体 (Codeer.LowCode.Blazor) が PostgreSQL 向けに組み立てた SQL を、読み出し・集計・書き込みの計画に読む。
    /// 本体が組み立てる形だけを受け付け、それ以外は拒否する (条件を落として続けない)。
    /// (FileMaker 版の ODataTranslator のパーサを移植したもの。条件を何に写すかは <see cref="PleasanterView"/>)
    /// </summary>
    static class SqlTranslator
    {
        /// <summary>結合先を読むときに 1 回の要求に入れる値の数。</summary>
        internal const int JoinBatchSize = 50;

        // ---------------------------------------------------------------- 字句

        internal static List<SqlToken> Tokenize(string sql)
        {
            var tokens = new List<SqlToken>();
            var i = 0;
            while (i < sql.Length)
            {
                var c = sql[i];
                if (char.IsWhiteSpace(c))
                {
                    while (i < sql.Length && char.IsWhiteSpace(sql[i])) i++;
                    tokens.Add(new(SqlTokenKind.Space, " "));
                }
                else if (c == '"')
                {
                    var end = sql.IndexOf('"', i + 1);
                    if (end < 0) throw NotSupported(sql);
                    tokens.Add(new(SqlTokenKind.QuotedIdentifier, sql.Substring(i + 1, end - i - 1)));
                    i = end + 1;
                }
                else if (c == '\'')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (true)
                    {
                        if (sql.Length <= i) throw NotSupported(sql);
                        if (sql[i] == '\'')
                        {
                            if (i + 1 < sql.Length && sql[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                            i++;
                            break;
                        }
                        sb.Append(sql[i++]);
                    }
                    tokens.Add(new(SqlTokenKind.StringLiteral, sb.ToString()));
                }
                else if (c == '@' && i + 1 < sql.Length && (char.IsAsciiLetter(sql[i + 1]) || sql[i + 1] == '_'))
                {
                    var start = i++;
                    while (i < sql.Length && (char.IsAsciiLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                    tokens.Add(new(SqlTokenKind.Parameter, sql[start..i]));
                }
                else if (char.IsAsciiDigit(c))
                {
                    var start = i;
                    while (i < sql.Length && (char.IsAsciiDigit(sql[i]) || sql[i] == '.')) i++;
                    tokens.Add(new(SqlTokenKind.Number, sql[start..i]));
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '$')) i++;
                    tokens.Add(new(SqlTokenKind.Word, sql[start..i]));
                }
                else
                {
                    var two = i + 1 < sql.Length ? sql.Substring(i, 2) : string.Empty;
                    if (two is "<=" or ">=" or "<>" or "!=" or "||" or "::" or "->")
                    {
                        tokens.Add(new(SqlTokenKind.Symbol, two));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new(SqlTokenKind.Symbol, c.ToString()));
                        i++;
                    }
                }
            }
            return tokens;
        }

        static List<SqlToken> WithoutSpaces(IEnumerable<SqlToken> tokens) => tokens.Where(e => e.Kind != SqlTokenKind.Space).ToList();

        internal static PleasanterException NotSupported(string sql)
            => new("This operation is not supported for a Pleasanter data source: " + Shorten(sql));

        internal static PleasanterException NotSupported(string what, string sql)
            => new($"{what} is not supported for a Pleasanter data source: " + Shorten(sql));

        static string Shorten(string sql)
        {
            var text = string.Join(" ", sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= 200 ? text : text[..200] + "...";
        }

        // ---------------------------------------------------------------- 読み出し

        /// <summary>from の表と結合。exists のサブクエリでは Parent に外側の select がある。</summary>
        internal class Scope
        {
            public Scope? Parent;
            public string Table = string.Empty;
            public string MainAlias = string.Empty;
            public PleasanterTableDefinition Definition = new();
            public Dictionary<string, PleasanterTableDefinition> Aliases = new(StringComparer.Ordinal);
            public List<JoinPlan> Joins = new();

            public JoinPlan Join(string alias) => Joins.First(e => e.Alias == alias);
        }

        /// <summary>集計の SQL か (select の項目に関数・case がある / group by / from の後ろがサブクエリ)。一覧の select count(*) は含まない。</summary>
        internal static bool IsAggregate(string sql)
        {
            var tokens = WithoutSpaces(Tokenize(sql));
            if (tokens.Count == 0 || !tokens[0].IsWord("select")) return false;
            var from = FindTopLevel(tokens, 0, t => t.IsWord("from"));
            if (from < 0) return false;
            if (from + 1 < tokens.Count && tokens[from + 1].IsSymbol("(")) return true;
            if (FindTopLevel(tokens, 0, t => t.IsWord("group")) >= 0) return true;
            //行を選ぶ定義 (WithinGroupsOf) の付いた集計の行数 (select count(*) from ... inner join (集計) 別名 on ...)
            var inner = FindTopLevel(tokens, from, t => t.IsWord("inner"));
            if (0 <= inner && inner + 2 < tokens.Count && tokens[inner + 1].IsWord("join") && tokens[inner + 2].IsSymbol("(")) return true;
            var items = tokens.GetRange(1, from - 1);
            var isPlainCount = items.Count == 4 && items[0].IsWord("count") && items[1].IsSymbol("(") && items[2].IsSymbol("*") && items[3].IsSymbol(")");
            return !isPlainCount && items.Any(e => e.IsSymbol("(") || e.IsWord("case"));
        }

        //括弧の外の最初の位置 (無ければ -1)
        static int FindTopLevel(List<SqlToken> tokens, int start, Func<SqlToken, bool> predicate)
        {
            var depth = 0;
            for (var i = start; i < tokens.Count; i++)
            {
                if (tokens[i].IsSymbol("(")) depth++;
                else if (tokens[i].IsSymbol(")")) depth--;
                else if (depth == 0 && predicate(tokens[i])) return i;
            }
            return -1;
        }

        /// <summary>
        /// 本体の select を読み出しの計画にする。受け付けるのは:
        /// select "t"."c" as "t_c",... / select count(*) / select * の from "t" [left join "u" as "u_1" on ...] [where ...] [order by ...] [limit N offset M]。
        /// 条件には exists (一覧をまたぐ条件) と結合先の列の比較を含められる。並びには結合先の列を含められる (メモリで並べる)。
        /// </summary>
        internal static async Task<ReadPlan> TranslateSelectAsync(string sql, IReadOnlyDictionary<string, object?> args, Func<string, Task<PleasanterTableDefinition>> getDefinition)
        {
            var tokens = WithoutSpaces(Tokenize(sql));
            if (tokens.Count == 0 || !tokens[0].IsWord("select")) throw NotSupported(sql);
            if (tokens.Any(e => e.IsSymbol(";"))) throw NotSupported(sql);
            if (IsAggregate(sql)) throw NotSupported("This form of aggregation", sql);

            var (limit, offset) = ReadLimit(tokens, sql);

            //句の位置 (括弧の外だけ見る。exists のサブクエリは括弧の中)
            int from = -1, where = -1, orderBy = -1;
            var depth = 0;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].IsSymbol("(")) depth++;
                else if (tokens[i].IsSymbol(")")) depth--;
                if (depth != 0) continue;
                if (tokens[i].IsWord("from") && from < 0) from = i;
                else if (tokens[i].IsWord("where") && where < 0) where = i;
                else if (tokens[i].IsWord("order") && i + 1 < tokens.Count && tokens[i + 1].IsWord("by") && orderBy < 0) orderBy = i;
                else if (tokens[i].IsWord("group") || tokens[i].IsWord("having") || tokens[i].IsWord("union")) throw NotSupported(sql);
            }
            if (from < 0 || (0 <= where && where < from) || (0 <= orderBy && (orderBy < from || orderBy < where))) throw NotSupported(sql);
            var fromEnd = 0 <= where ? where : 0 <= orderBy ? orderBy : tokens.Count;
            var whereEnd = 0 <= orderBy ? orderBy : tokens.Count;

            //from と結合
            var scope = await ParseFromAsync(tokens.GetRange(from + 1, fromEnd - from - 1), null, getDefinition, sql);
            var mainAlias = scope.MainAlias;
            var definition = scope.Definition;

            //select の項目
            var selectItems = SplitByComma(tokens.GetRange(1, from - 1), sql);
            var isCount = selectItems.Count == 1 && selectItems[0].Count == 4 && selectItems[0][0].IsWord("count") && selectItems[0][1].IsSymbol("(") &&
                          selectItems[0][2].IsSymbol("*") && selectItems[0][3].IsSymbol(")");
            var isSelectAll = selectItems.Count == 1 && selectItems[0].Count == 1 && selectItems[0][0].IsSymbol("*");
            if ((isCount || isSelectAll) && limit != null) throw NotSupported(sql);
            if (isSelectAll && scope.Joins.Count != 0) throw NotSupported(sql);

            var read = new ReadPlan
            {
                Table = scope.Table,
                MainAlias = mainAlias,
                Definition = definition,
                IsCount = isCount,
                IsSelectAll = isSelectAll,
                Limit = limit,
                Offset = offset,
            };
            read.Joins.AddRange(scope.Joins);
            if (isSelectAll)
            {
                foreach (var column in definition.Columns) read.Columns.Add(new ResultColumn { Key = column.Name, Alias = mainAlias, Column = column.Name });
            }
            else if (!isCount)
            {
                foreach (var item in selectItems)
                {
                    var q = 0;
                    var column = ReadColumn(item, ref q, scope, sql);
                    var key = column.Column;
                    if (q + 2 == item.Count && item[q].IsWord("as") && item[q + 1].IsIdentifier) key = item[q + 1].Text;
                    else if (q != item.Count) throw NotSupported(sql);
                    read.Columns.Add(new ResultColumn { Key = key, Alias = column.Alias, Column = column.Column });
                }
            }

            //条件
            if (0 <= where)
            {
                var whereTokens = tokens.GetRange(where + 1, whereEnd - where - 1);
                var node = Fold(await new FilterParser(whereTokens, scope, args, sql, getDefinition).ParseAsync());
                if (node is ConstNode constant)
                {
                    if (!constant.Value) read.IsEmpty = true;
                }
                else
                {
                    read.Where = node;
                }
            }

            //並び
            if (0 <= orderBy)
            {
                var orderTokens = tokens.GetRange(orderBy + 2, tokens.Count - orderBy - 2);
                foreach (var item in SplitByComma(orderTokens, sql))
                {
                    var q = 0;
                    var column = ReadColumn(item, ref q, scope, sql);
                    RejectComments(column.Column);
                    var descending = false;
                    if (q < item.Count && (item[q].IsWord("desc") || item[q].IsWord("asc")))
                    {
                        descending = item[q].IsWord("desc");
                        q++;
                    }
                    if (q != item.Count) throw NotSupported(sql);
                    if (column.Alias != mainAlias && isCount) throw NotSupported(sql);
                    read.Order.Add(new OrderItem(column.Alias, column.Column, descending));
                }
            }
            return read;
        }

        //末尾の limit N offset M (無ければ null, 0)。読んだ分を tokens から外す
        static (int? Limit, int Offset) ReadLimit(List<SqlToken> tokens, string sql)
        {
            if (tokens.Count < 4) return (null, 0);
            var t = tokens.GetRange(tokens.Count - 4, 4);
            if (!(t[0].IsWord("limit") && t[1].Kind == SqlTokenKind.Number && t[2].IsWord("offset") && t[3].Kind == SqlTokenKind.Number)) return (null, 0);
            if (!int.TryParse(t[1].Text, NumberStyles.None, CultureInfo.InvariantCulture, out var l) ||
                !int.TryParse(t[3].Text, NumberStyles.None, CultureInfo.InvariantCulture, out var o)) throw NotSupported(sql);
            tokens.RemoveRange(tokens.Count - 4, 4);
            return (l, o);
        }

        //from "t" [as "a"] [left join "u" as "u_1" on "a"."x"="u_1"."y" ...]
        static async Task<Scope> ParseFromAsync(List<SqlToken> fromTokens, Scope? parent, Func<string, Task<PleasanterTableDefinition>> getDefinition, string sql)
        {
            var scope = new Scope { Parent = parent };
            var p = 0;
            var table = ReadTableName(fromTokens, ref p, sql);
            var mainAlias = table;
            if (p < fromTokens.Count && fromTokens[p].IsWord("as")) p++;
            if (p < fromTokens.Count && fromTokens[p].IsIdentifier && !fromTokens[p].IsWord("left")) mainAlias = fromTokens[p++].Text;
            scope.Table = table;
            scope.MainAlias = mainAlias;
            scope.Definition = await getDefinition(table);
            scope.Aliases[mainAlias] = scope.Definition;

            while (p < fromTokens.Count)
            {
                if (!(fromTokens[p].IsWord("left") && p + 1 < fromTokens.Count && fromTokens[p + 1].IsWord("join"))) throw NotSupported(sql);
                p += 2;
                var joinTable = ReadTableName(fromTokens, ref p, sql);
                if (p < fromTokens.Count && fromTokens[p].IsWord("as")) p++;
                if (fromTokens.Count <= p || !fromTokens[p].IsIdentifier || fromTokens[p].IsWord("on")) throw NotSupported(sql);
                var joinAlias = fromTokens[p++].Text;
                if (scope.Aliases.ContainsKey(joinAlias)) throw NotSupported(sql);
                if (fromTokens.Count <= p || !fromTokens[p++].IsWord("on")) throw NotSupported(sql);
                var left = ReadJoinOperand(fromTokens, ref p, sql);
                if (fromTokens.Count <= p || !fromTokens[p++].IsSymbol("=")) throw NotSupported(sql);
                var right = ReadJoinOperand(fromTokens, ref p, sql);
                //片方が新しい別名 (結合先)、もう片方が先に出てきた別名 (結合元)
                var (source, destination) = left.Alias == joinAlias ? (right, left) : (left, right);
                if (destination.Alias != joinAlias || !scope.Aliases.ContainsKey(source.Alias)) throw NotSupported(sql);
                var joinDefinition = await getDefinition(joinTable);
                CheckColumn(scope.Aliases[source.Alias], source.Column);
                CheckColumn(joinDefinition, destination.Column);
                scope.Aliases[joinAlias] = joinDefinition;
                scope.Joins.Add(new JoinPlan
                {
                    Table = joinTable,
                    Alias = joinAlias,
                    SourceAlias = source.Alias,
                    SourceColumn = source.Column,
                    DestinationColumn = destination.Column,
                    Definition = joinDefinition,
                });
            }
            return scope;
        }

        static string ReadTableName(List<SqlToken> tokens, ref int p, string sql)
        {
            if (tokens.Count <= p || !tokens[p].IsIdentifier) throw NotSupported(sql);
            var name = tokens[p++].Text;
            //スキーマつき ("s"."t") は扱わない (表名は Sites の別名で、. を含まない)
            if (p < tokens.Count && tokens[p].IsSymbol(".")) throw NotSupported(sql);
            return name;
        }

        //on の片側: "別名"."列" か CAST("別名"."列" AS VARCHAR(255)) (本体が数値と文字の結合に付ける)
        static (string Alias, string Column) ReadJoinOperand(List<SqlToken> tokens, ref int p, string sql)
        {
            if (p < tokens.Count && tokens[p].IsWord("cast"))
            {
                p++;
                if (tokens.Count <= p || !tokens[p++].IsSymbol("(")) throw NotSupported(sql);
                var inner = ReadQualified(tokens, ref p, sql);
                if (tokens.Count <= p + 1 || !tokens[p].IsWord("as") || !tokens[p + 1].IsWord("varchar")) throw NotSupported(sql);
                p += 2;
                if (p < tokens.Count && tokens[p].IsSymbol("("))
                {
                    if (tokens.Count <= p + 2 || tokens[p + 1].Kind != SqlTokenKind.Number || !tokens[p + 2].IsSymbol(")")) throw NotSupported(sql);
                    p += 3;
                }
                if (tokens.Count <= p || !tokens[p++].IsSymbol(")")) throw NotSupported(sql);
                return inner;
            }
            return ReadQualified(tokens, ref p, sql);
        }

        static (string Alias, string Column) ReadQualified(List<SqlToken> tokens, ref int p, string sql)
        {
            if (tokens.Count <= p + 2 || !tokens[p].IsIdentifier || !tokens[p + 1].IsSymbol(".") || !tokens[p + 2].IsIdentifier) throw NotSupported(sql);
            var result = (tokens[p].Text, tokens[p + 2].Text);
            p += 3;
            return result;
        }

        /// <summary>列の参照: "別名"."列" か "列" (主表)。別名はこの select から外側へ探す (exists の中から外側の表を指す相関)。</summary>
        static (Scope Owner, string Alias, string Column) ReadColumnReference(List<SqlToken> tokens, ref int p, Scope scope, string sql)
        {
            if (tokens.Count <= p || !tokens[p].IsIdentifier || IsKeyword(tokens[p])) throw NotSupported(sql);
            string alias, column;
            Scope? owner = scope;
            if (p + 2 < tokens.Count && tokens[p + 1].IsSymbol("."))
            {
                if (!tokens[p + 2].IsIdentifier) throw NotSupported(sql);
                alias = tokens[p].Text;
                column = tokens[p + 2].Text;
                p += 3;
                while (owner != null && !owner.Aliases.ContainsKey(alias)) owner = owner.Parent;
                if (owner == null) throw NotSupported(sql);
            }
            else
            {
                alias = scope.MainAlias;
                column = tokens[p].Text;
                p++;
            }
            CheckColumn(owner.Aliases[alias], column);
            return (owner, alias, column);
        }

        //列の参照 (この select の表だけ)
        static (string Alias, string Column) ReadColumn(List<SqlToken> tokens, ref int p, Scope scope, string sql)
        {
            var (owner, alias, column) = ReadColumnReference(tokens, ref p, scope, sql);
            if (owner != scope) throw NotSupported(sql);
            return (alias, column);
        }

        //コメントと添付の列は読み書きだけ (条件・並び・集計には使えない。値が JSON)
        static void RejectComments(string column)
        {
            if (column == PleasanterTableDefinition.CommentsColumn)
                throw new PleasanterException("The Comments column of Pleasanter can only be read and written. It cannot be used in conditions, sorting or aggregation.");
            if (PleasanterTableDefinition.IsAttachmentsColumn(column))
                throw new PleasanterException($"The attachments column '{column}' of Pleasanter can only be read and written. It cannot be used in conditions, sorting or aggregation.");
        }

        static void CheckColumn(PleasanterTableDefinition definition, string column)
        {
            if (definition.FindColumn(column) == null)
                throw new PleasanterException($"Table '{definition.Name}' has no column '{column}' (it is not a standard column and does not appear in the editor, grid or column settings of the Pleasanter site).");
        }

        static bool IsKeyword(SqlToken token)
            => token.Kind == SqlTokenKind.Word &&
               (token.IsWord("select") || token.IsWord("from") || token.IsWord("where") || token.IsWord("and") || token.IsWord("or") || token.IsWord("not") ||
                token.IsWord("is") || token.IsWord("null") || token.IsWord("like") || token.IsWord("in") || token.IsWord("exists") || token.IsWord("as") ||
                token.IsWord("cast") || token.IsWord("count") || token.IsWord("case"));

        static List<List<SqlToken>> SplitByComma(List<SqlToken> tokens, string sql)
        {
            var items = new List<List<SqlToken>>();
            var item = new List<SqlToken>();
            var depth = 0;
            foreach (var token in tokens)
            {
                if (token.IsSymbol("(")) depth++;
                else if (token.IsSymbol(")")) depth--;
                if (depth == 0 && token.IsSymbol(","))
                {
                    if (item.Count == 0) throw NotSupported(sql);
                    items.Add(item);
                    item = new();
                    continue;
                }
                item.Add(token);
            }
            if (item.Count == 0) throw NotSupported(sql);
            items.Add(item);
            return items;
        }

        // ---------------------------------------------------------------- 条件

        /// <summary>
        /// 結合先の列の比較を、主表の条件 (真になる行・偽になる行) に持ち上げる。
        /// 結合ごとに「結合先の表を条件で読んで結合先の列の値を集め、結合元の列がそのどれかに一致」に置き換える (多段は主表までくり返す)。
        /// missing は結合先の行が無いとき (結合元が null・相手が無い) の比較の値: true (is null) / false (is not null) / null (unknown)。
        /// </summary>
        static FilterNode LiftJoined(Scope scope, string alias, FilterNode trueFilter, FilterNode falseFilter, bool? missing)
        {
            while (alias != scope.MainAlias)
            {
                var join = scope.Join(alias);
                var trueKeys = new InSetNode(join.SourceColumn, new KeySource { Table = join.Table, Definition = join.Definition, Filter = trueFilter, Column = join.DestinationColumn });
                var falseKeys = new InSetNode(join.SourceColumn, new KeySource { Table = join.Table, Definition = join.Definition, Filter = falseFilter, Column = join.DestinationColumn });
                //結合先の行が無い行は missing の値になる (not (一致) は結合元が空の行も真。InSetNode)
                trueFilter = missing == true ? new NotNode(falseKeys) : trueKeys;
                falseFilter = missing == false ? new NotNode(trueKeys) : falseKeys;
                alias = join.SourceAlias;
            }
            return new TriNode(trueFilter, falseFilter);
        }

        class FilterParser
        {
            readonly List<SqlToken> _tokens;
            readonly Scope _scope;
            readonly IReadOnlyDictionary<string, object?> _args;
            readonly string _sql;
            readonly Func<string, Task<PleasanterTableDefinition>> _getDefinition;
            int _p;

            public FilterParser(List<SqlToken> tokens, Scope scope, IReadOnlyDictionary<string, object?> args, string sql, Func<string, Task<PleasanterTableDefinition>> getDefinition)
            {
                _tokens = tokens;
                _scope = scope;
                _args = args;
                _sql = sql;
                _getDefinition = getDefinition;
            }

            public async Task<FilterNode> ParseAsync()
            {
                var node = await ParseOrAsync();
                if (_p != _tokens.Count) throw NotSupported(_sql);
                return node;
            }

            SqlToken? Peek(int offset = 0) => _p + offset < _tokens.Count ? _tokens[_p + offset] : null;

            SqlToken Next()
            {
                if (_tokens.Count <= _p) throw NotSupported(_sql);
                return _tokens[_p++];
            }

            async Task<FilterNode> ParseOrAsync()
            {
                var children = new List<FilterNode> { await ParseAndAsync() };
                while (Peek()?.IsWord("or") == true)
                {
                    _p++;
                    children.Add(await ParseAndAsync());
                }
                return children.Count == 1 ? children[0] : new OrNode(children);
            }

            async Task<FilterNode> ParseAndAsync()
            {
                var children = new List<FilterNode> { await ParseUnaryAsync() };
                while (Peek()?.IsWord("and") == true)
                {
                    _p++;
                    children.Add(await ParseUnaryAsync());
                }
                return children.Count == 1 ? children[0] : new AndNode(children);
            }

            async Task<FilterNode> ParseUnaryAsync()
            {
                if (Peek()?.IsWord("not") == true)
                {
                    _p++;
                    return new NotNode(await ParseUnaryAsync());
                }
                return await ParsePrimaryAsync();
            }

            async Task<FilterNode> ParsePrimaryAsync()
            {
                var token = Peek() ?? throw NotSupported(_sql);
                if (token.IsSymbol("("))
                {
                    _p++;
                    var inner = await ParseOrAsync();
                    if (!Next().IsSymbol(")")) throw NotSupported(_sql);
                    return inner;
                }
                //1=1 / 1=0 (本体が「絞らない」「何も返さない」に使う)
                if (token.Kind == SqlTokenKind.Number)
                {
                    var left = Next().Text;
                    if (!Next().IsSymbol("=")) throw NotSupported(_sql);
                    var right = Next();
                    if (right.Kind != SqlTokenKind.Number ||
                        !decimal.TryParse(left, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var l) ||
                        !decimal.TryParse(right.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var r)) throw NotSupported(_sql);
                    return new ConstNode(l == r);
                }
                //一覧をまたぐ条件 exists(select 1 from 子 ... where 子.列 = 親.列 and ...)
                if (token.IsWord("exists") && Peek(1)?.IsSymbol("(") == true) return await ParseExistsAsync();

                var (owner, alias, column) = ReadColumnReference(_tokens, ref _p, _scope, _sql);
                RejectComments(column);
                var op = Next();

                //列どうしの比較: 一覧をまたぐ条件の相関 (子の主表の列 = 外側の主表の列) だけ
                if (Peek()?.IsIdentifier == true && !IsKeyword(Peek()!) && op.Kind == SqlTokenKind.Symbol)
                {
                    var right = ReadColumnReference(_tokens, ref _p, _scope, _sql);
                    return Correlation((owner, alias, column), op, right);
                }
                if (owner != _scope) throw NotSupported("A condition that refers to the outer table other than the link of a list", _sql);

                var joined = alias != _scope.MainAlias;
                var notNull = new PredicateNode(column, PredicateOperator.IsNotNull, null);

                if (op.IsWord("is"))
                {
                    var not = false;
                    if (Peek()?.IsWord("not") == true) { _p++; not = true; }
                    if (!Next().IsWord("null")) throw NotSupported(_sql);
                    var isNull = new PredicateNode(column, PredicateOperator.IsNull, null);
                    if (!joined) return not ? notNull : isNull;
                    //結合先の行が無ければ結合先の列は null
                    return not ? LiftJoined(_scope, alias, notNull, isNull, false) : LiftJoined(_scope, alias, isNull, notNull, true);
                }
                if (op.IsWord("like"))
                {
                    var pattern = Value(Next());
                    if (!Next().IsWord("escape")) throw NotSupported(_sql);
                    var escape = Value(Next());
                    var term = new PredicateNode(column, PredicateOperator.Like, UnescapeLikePattern(pattern, escape));
                    return joined ? LiftJoined(_scope, alias, term, And(notNull, new NotNode(term)), null) : term;
                }
                if (op.IsWord("in") || (op.IsWord("not") && Peek()?.IsWord("in") == true))
                {
                    var isNot = op.IsWord("not");
                    if (isNot) _p++;
                    if (!Next().IsSymbol("(")) throw NotSupported(_sql);
                    var values = new List<FilterNode>();
                    while (true)
                    {
                        //null の要素との比較は SQL では unknown (In では落ち、Not In は全体が真にならない)
                        var value = Value(Next());
                        if (IsNull(value))
                        {
                            values.Add(new UnknownNode());
                        }
                        else
                        {
                            var term = new PredicateNode(column, PredicateOperator.Equal, value);
                            values.Add(joined ? LiftJoined(_scope, alias, term, And(notNull, new NotNode(term)), null) : term);
                        }
                        var separator = Next();
                        if (separator.IsSymbol(")")) break;
                        if (!separator.IsSymbol(",")) throw NotSupported(_sql);
                    }
                    FilterNode node = values.Count == 1 ? values[0] : new OrNode(values);
                    return isNot ? new NotNode(node) : node;
                }
                var comparison = ToOperator(op);
                if (comparison == null) throw NotSupported(_sql);
                //パラメータが null の比較 (= / <> も含む) は SQL では unknown (is null / is not null は上で別に扱う)
                var rightValue = Value(Next());
                if (IsNull(rightValue)) return new UnknownNode();
                var compare = new PredicateNode(column, comparison.Value, rightValue);
                if (!joined) return compare;
                return LiftJoined(_scope, alias, And(notNull, compare), And(notNull, new NotNode(compare)), null);
            }

            static FilterNode And(FilterNode a, FilterNode b) => new AndNode(new List<FilterNode> { a, b });

            static PredicateOperator? ToOperator(SqlToken op) => op.Kind == SqlTokenKind.Symbol ? op.Text switch
            {
                "=" => PredicateOperator.Equal,
                "<>" or "!=" => PredicateOperator.NotEqual,
                "<" => PredicateOperator.LessThan,
                "<=" => PredicateOperator.LessThanOrEqual,
                ">" => PredicateOperator.GreaterThan,
                ">=" => PredicateOperator.GreaterThanOrEqual,
                _ => null,
            } : null;

            //子の主表の列 = 外側 (1 つ外の select) の主表の列 だけを相関として受け付ける
            FilterNode Correlation((Scope Owner, string Alias, string Column) left, SqlToken op, (Scope Owner, string Alias, string Column) right)
            {
                if (left.Owner == right.Owner) throw NotSupported("Comparing two columns", _sql);
                var (inner, outer) = left.Owner == _scope ? (left, right) : (right, left);
                if (inner.Owner != _scope || outer.Owner != _scope.Parent || !op.IsSymbol("="))
                    throw NotSupported("A condition across a list other than 'child column = parent column'", _sql);
                if (inner.Alias != _scope.MainAlias || outer.Alias != _scope.Parent!.MainAlias)
                    throw NotSupported("A condition across a list that is linked through a joined table", _sql);
                return new CorrelationNode(inner.Column, outer.Column);
            }

            //exists(select 1 from 子 [left join ...] where (子.列 = 親.列) and (...)) → 子の表を条件で読んで子の列の値を集め、親の列がそのどれかに一致
            async Task<FilterNode> ParseExistsAsync()
            {
                _p++;
                if (!Next().IsSymbol("(")) throw NotSupported(_sql);
                var start = _p;
                var depth = 1;
                while (true)
                {
                    var t = Next();
                    if (t.IsSymbol("(")) depth++;
                    else if (t.IsSymbol(")") && --depth == 0) break;
                }
                var inner = _tokens.GetRange(start, _p - 1 - start);
                if (inner.Count < 4 || !inner[0].IsWord("select") || inner[1].Kind != SqlTokenKind.Number || inner[1].Text != "1" || !inner[2].IsWord("from"))
                    throw NotSupported("This form of a condition across a list (exists)", _sql);
                var where = FindTopLevel(inner, 3, e => e.IsWord("where"));
                if (where < 0) throw NotSupported("A condition across a list without a link to the parent", _sql);
                if (FindTopLevel(inner, 3, e => e.IsWord("order") || e.IsWord("group") || e.IsWord("having") || e.IsWord("limit") || e.IsWord("union")) >= 0)
                    throw NotSupported("This form of a condition across a list (exists)", _sql);

                var child = await ParseFromAsync(inner.GetRange(3, where - 3), _scope, _getDefinition, _sql);
                var tree = await new FilterParser(inner.GetRange(where + 1, inner.Count - where - 1), child, _args, _sql, _getDefinition).ParseAsync();
                CorrelationNode? correlation = null;
                tree = ExtractCorrelation(tree, ref correlation);
                if (correlation == null) throw NotSupported("A condition across a list without a link to the parent", _sql);

                var keys = new InSetNode(correlation.OuterColumn, new KeySource { Table = child.Table, Definition = child.Definition, Filter = tree, Column = correlation.InnerColumn });
                //exists は真か偽 (unknown にならない)。親の列が null なら偽 (not exists は真。InSetNode の not)
                return new TriNode(keys, new NotNode(keys));
            }

            //相関は and でつながる位置に 1 つだけ (or / not の中は拒否)。取り除いて「真」にする
            FilterNode ExtractCorrelation(FilterNode node, ref CorrelationNode? found)
            {
                switch (node)
                {
                    case CorrelationNode correlation:
                        if (found != null) throw NotSupported("A condition across a list with more than one link column", _sql);
                        found = correlation;
                        return new ConstNode(true);
                    case AndNode and:
                        {
                            var children = new List<FilterNode>();
                            foreach (var child in and.Children) children.Add(ExtractCorrelation(child, ref found));
                            return new AndNode(children);
                        }
                    default:
                        if (HasCorrelation(node)) throw NotSupported("A condition across a list whose link is inside or / not", _sql);
                        return node;
                }
            }

            static bool HasCorrelation(FilterNode node) => node switch
            {
                CorrelationNode => true,
                NotNode not => HasCorrelation(not.Child),
                AndNode and => and.Children.Any(HasCorrelation),
                OrNode or => or.Children.Any(HasCorrelation),
                _ => false,
            };

            static bool IsNull(object? value) => value == null || value is DBNull;

            object? Value(SqlToken token)
            {
                if (token.Kind != SqlTokenKind.Parameter) throw NotSupported(_sql);
                if (!_args.TryGetValue(token.Text, out var value)) throw new PleasanterException($"The value of parameter '{token.Text}' is missing.");
                return value;
            }

            //本体の部分一致のパターンは必ず %文字% で、文字の中の % _ とエスケープ文字はエスケープ文字が前に付いている
            string UnescapeLikePattern(object? pattern, object? escape)
            {
                var text = pattern as string;
                var escapeText = Convert.ToString(escape, CultureInfo.InvariantCulture) ?? string.Empty;
                if (text == null || text.Length < 2 || text[0] != '%' || text[^1] != '%' || escapeText.Length != 1)
                    throw NotSupported("A like pattern other than %text%", _sql);
                var escapeChar = escapeText[0];
                var sb = new StringBuilder();
                var body = text[1..^1];
                for (var i = 0; i < body.Length; i++)
                {
                    var c = body[i];
                    if (c == escapeChar)
                    {
                        if (body.Length <= i + 1) throw NotSupported("A like pattern other than %text%", _sql);
                        sb.Append(body[++i]);
                        continue;
                    }
                    if (c == '%' || c == '_') throw NotSupported("A like pattern other than %text%", _sql);
                    sb.Append(c);
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 定数を畳む (and / or / not の中の 1=1 / 1=0 を消す)。positive = 式が真になる条件、false = 式が偽になる条件。
        /// unknown (値が null の比較) はどちらにもならない (SQL の 3 値論理: not (x &lt; null) も 0 行)。
        /// 真の条件と偽の条件を別に持つ比較 (TriNode) は向きに合う方を使う。それらを含まない部分は not をそのまま残す。
        /// </summary>
        internal static FilterNode Fold(FilterNode node, bool positive = true)
        {
            if (!positive && !HasUnknown(node))
            {
                var folded = Fold(node, true);
                return folded is ConstNode c ? new ConstNode(!c.Value) : new NotNode(folded);
            }
            switch (node)
            {
                case UnknownNode:
                    return new ConstNode(false);
                case TriNode tri:
                    return Fold(positive ? tri.True : tri.False, true);
                case NotNode not:
                    return Fold(not.Child, !positive);
                case AndNode and:
                    //偽になる条件は「どれかが偽」
                    return positive ? FoldAnd(and.Children.Select(e => Fold(e, true))) : FoldOr(and.Children.Select(e => Fold(e, false)));
                case OrNode or:
                    return positive ? FoldOr(or.Children.Select(e => Fold(e, true))) : FoldAnd(or.Children.Select(e => Fold(e, false)));
                default:
                    return node;
            }
        }

        static bool HasUnknown(FilterNode node) => node switch
        {
            UnknownNode => true,
            TriNode => true,
            NotNode not => HasUnknown(not.Child),
            AndNode and => and.Children.Any(HasUnknown),
            OrNode or => or.Children.Any(HasUnknown),
            _ => false,
        };

        /// <summary>別の表を読んで値を集める条件を含むか。</summary>
        internal static bool HasInSet(FilterNode node) => node switch
        {
            InSetNode => true,
            TriNode tri => HasInSet(tri.True) || HasInSet(tri.False),
            NotNode not => HasInSet(not.Child),
            AndNode and => and.Children.Any(HasInSet),
            OrNode or => or.Children.Any(HasInSet),
            _ => false,
        };

        static FilterNode FoldAnd(IEnumerable<FilterNode> folded)
        {
            var children = new List<FilterNode>();
            foreach (var child in folded)
            {
                if (child is ConstNode c)
                {
                    if (!c.Value) return new ConstNode(false);
                    continue;
                }
                children.Add(child);
            }
            return children.Count == 0 ? new ConstNode(true) : children.Count == 1 ? children[0] : new AndNode(children);
        }

        static FilterNode FoldOr(IEnumerable<FilterNode> folded)
        {
            var children = new List<FilterNode>();
            foreach (var child in folded)
            {
                if (child is ConstNode c)
                {
                    if (c.Value) return new ConstNode(true);
                    continue;
                }
                children.Add(child);
            }
            return children.Count == 0 ? new ConstNode(false) : children.Count == 1 ? children[0] : new OrNode(children);
        }

        // ---------------------------------------------------------------- 集計

        /// <summary>
        /// 本体の集計の SQL (AggregateSqlCreator) を集計の計画にする。受け付けるのは:
        /// select 軸 as "g0", case when 軸 is null then 1 else 0 end as "z0", [case 軸 when '..' then 0 ... else n end as "r0",] [min(結合先の列) as "d0",]
        /// count(*) / count(distinct 列) / sum / avg / min / max(列) as "m0", ..., count(*) as "n" from ... [where ...] [group by ...] [having ...] [order by 別名 ...] [limit N offset M]
        /// と、グループの数を数える select count(*) from (select 1 as "x" from ... group by ... having ...) x。
        /// 行を選ぶ定義 (WithinGroupsOf) は、結合の最後の inner join (同じ表の集計) 別名 on (列 = 別名."gN" or (列 is null and 別名."gN" is null)) and ... [on 1=1]。
        /// その集計の行数を数える select count(*) from ... inner join (...) ... where ... も受け付ける (別名なしの count(*) 1 つ)。
        /// 軸は列か日付の丸め (<see cref="ReadGroupKey"/>)。
        /// </summary>
        internal static async Task<AggregatePlan> TranslateAggregateAsync(string sql, IReadOnlyDictionary<string, object?> args, Func<string, Task<PleasanterTableDefinition>> getDefinition)
        {
            var tokens = WithoutSpaces(Tokenize(sql));
            if (tokens.Count == 0 || !tokens[0].IsWord("select")) throw NotSupported(sql);
            if (tokens.Any(e => e.IsSymbol(";"))) throw NotSupported(sql);
            return await TranslateAggregateCoreAsync(tokens, sql, args, getDefinition, true);
        }

        static async Task<AggregatePlan> TranslateAggregateCoreAsync(List<SqlToken> tokens, string sql, IReadOnlyDictionary<string, object?> args,
            Func<string, Task<PleasanterTableDefinition>> getDefinition, bool allowWithin)
        {
            if (tokens.Count == 0 || !tokens[0].IsWord("select")) throw NotSupported(sql);

            //グループの数: select count(*) from ( 集計 ) 別名
            var isGroupCount = false;
            if (tokens.Count > 8 && tokens[1].IsWord("count") && tokens[2].IsSymbol("(") && tokens[3].IsSymbol("*") && tokens[4].IsSymbol(")") &&
                tokens[5].IsWord("from") && tokens[6].IsSymbol("("))
            {
                var close = FindClosingParenthesis(tokens, 6);
                if (close < 0 || close + 2 != tokens.Count || !tokens[close + 1].IsIdentifier) throw NotSupported(sql);
                tokens = tokens.GetRange(7, close - 7);
                isGroupCount = true;
                if (tokens.Count == 0 || !tokens[0].IsWord("select")) throw NotSupported(sql);
            }

            var (limit, offset) = ReadLimit(tokens, sql);
            int from = -1, where = -1, groupBy = -1, having = -1, orderBy = -1, inner = -1;
            var depth = 0;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].IsSymbol("(")) depth++;
                else if (tokens[i].IsSymbol(")")) depth--;
                if (depth != 0) continue;
                if (tokens[i].IsWord("from") && from < 0) from = i;
                else if (tokens[i].IsWord("where") && where < 0) where = i;
                else if (tokens[i].IsWord("group") && i + 1 < tokens.Count && tokens[i + 1].IsWord("by") && groupBy < 0) groupBy = i;
                else if (tokens[i].IsWord("having") && having < 0) having = i;
                else if (tokens[i].IsWord("order") && i + 1 < tokens.Count && tokens[i + 1].IsWord("by") && orderBy < 0) orderBy = i;
                else if (tokens[i].IsWord("inner"))
                {
                    if (!allowWithin || 0 <= inner) throw NotSupported("Nested aggregation within the groups of another aggregation", sql);
                    inner = i;
                }
                else if (tokens[i].IsWord("union")) throw NotSupported(sql);
            }
            var clauses = new[] { from, where, groupBy, having, orderBy };
            if (from < 0) throw NotSupported(sql);
            for (var i = 1; i < clauses.Length; i++)
            {
                if (clauses[i] < 0) continue;
                if (clauses.Take(i).Any(e => e > clauses[i])) throw NotSupported(sql);
            }
            int End(int clause) => clauses.Where(e => e > clause).DefaultIfEmpty(tokens.Count).Min();
            //行を選ぶ定義は from の句の最後 (本体は結合の後ろに置く)
            var fromEnd = End(from);
            if (0 <= inner && !(from < inner && inner < fromEnd)) throw NotSupported(sql);

            var scope = await ParseFromAsync(tokens.GetRange(from + 1, (0 <= inner ? inner : fromEnd) - from - 1), null, getDefinition, sql);
            var plan = new AggregatePlan
            {
                Table = scope.Table,
                MainAlias = scope.MainAlias,
                Definition = scope.Definition,
                Limit = limit,
                Offset = offset,
                IsGroupCount = isGroupCount,
            };
            plan.Joins.AddRange(scope.Joins);
            if (0 <= inner) plan.Within = await ParseWithinAsync(tokens.GetRange(inner, fromEnd - inner), scope, sql, args, getDefinition);

            //select の項目 (別名つき)。行を選ぶ定義の付いた集計の行数は別名なしの count(*) 1 つ
            var selectItems = SplitByComma(tokens.GetRange(1, from - 1), sql);
            if (plan.Within != null && groupBy < 0 && !isGroupCount && selectItems.Count == 1 && selectItems[0].Count == 4 && selectItems[0][0].IsWord("count") &&
                selectItems[0][1].IsSymbol("(") && selectItems[0][2].IsSymbol("*") && selectItems[0][3].IsSymbol(")"))
            {
                selectItems[0] = selectItems[0].Concat(new[] { new SqlToken(SqlTokenKind.Word, "as"), new SqlToken(SqlTokenKind.QuotedIdentifier, "count") }).ToList();
            }
            foreach (var item in selectItems)
            {
                if (item.Count < 3 || !item[^2].IsWord("as") || !item[^1].IsIdentifier) throw NotSupported(sql);
                var expression = ParseAggregateExpression(item.GetRange(0, item.Count - 2), scope, sql);
                if (expression is AggregateConstant && !isGroupCount) throw NotSupported(sql);
                plan.Items.Add(new AggregateItem(item[^1].Text, expression));
                if (expression is AggregateColumn column) plan.Groups.Add(column);
                if (expression is AggregateFunctionCall call && !plan.Measures.Contains(call)) plan.Measures.Add(call);
            }

            //group by (select の軸と、その空値の印・候補の順だけ)
            if (0 <= groupBy)
            {
                var groupTokens = tokens.GetRange(groupBy + 2, End(groupBy) - groupBy - 2);
                var grouped = new HashSet<AggregateColumn>();
                var groupItems = SplitByComma(groupTokens, sql).Select(e => ParseAggregateExpression(e, scope, sql)).ToList();
                //グループの数を数える SQL は select に軸が無い (group by の列が軸)
                if (isGroupCount)
                {
                    foreach (var column in groupItems.OfType<AggregateColumn>()) if (!plan.Groups.Contains(column)) plan.Groups.Add(column);
                }
                foreach (var expression in groupItems)
                {
                    var column = expression switch
                    {
                        AggregateColumn c => c,
                        AggregateNullFlag f => f.Column,
                        AggregateRank r => r.Column,
                        _ => throw NotSupported(sql),
                    };
                    if (!plan.Groups.Contains(column)) throw NotSupported(sql);
                    if (expression is AggregateColumn) grouped.Add(column);
                }
                if (!plan.Groups.All(grouped.Contains)) throw NotSupported(sql);
            }
            else if (plan.Groups.Count != 0 || plan.Items.Any(e => e.Expression is AggregateNullFlag or AggregateRank))
            {
                throw NotSupported(sql);
            }

            //軸に出ない列 (空値の印・候補の順) は軸の列だけ
            foreach (var item in plan.Items)
            {
                var column = item.Expression switch { AggregateNullFlag f => f.Column, AggregateRank r => r.Column, _ => null };
                if (column != null && !plan.Groups.Contains(column)) throw NotSupported(sql);
            }

            //having 集計値 演算子 数値 [and ...]
            if (0 <= having)
            {
                var havingTokens = tokens.GetRange(having + 1, End(having) - having - 1);
                foreach (var part in SplitTopLevel(havingTokens, e => e.IsWord("and"), sql))
                {
                    var op = FindTopLevel(part, 0, e => e.Kind == SqlTokenKind.Symbol && e.Text is "=" or "<>" or "!=" or "<" or "<=" or ">" or ">=");
                    if (op <= 0) throw NotSupported(sql);
                    if (ParseAggregateExpression(part.GetRange(0, op), scope, sql) is not AggregateFunctionCall measure) throw NotSupported(sql);
                    var valueTokens = part.GetRange(op + 1, part.Count - op - 1);
                    var negative = valueTokens.Count == 2 && valueTokens[0].IsSymbol("-");
                    if (negative) valueTokens.RemoveAt(0);
                    if (valueTokens.Count != 1 || valueTokens[0].Kind != SqlTokenKind.Number ||
                        !decimal.TryParse(valueTokens[0].Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) throw NotSupported(sql);
                    if (!plan.Measures.Contains(measure)) plan.Measures.Add(measure);
                    plan.Having.Add(new HavingCondition(measure, part[op].Text == "!=" ? "<>" : part[op].Text, negative ? -value : value));
                }
            }

            //order by 別名 [asc|desc]
            if (0 <= orderBy)
            {
                foreach (var item in SplitByComma(tokens.GetRange(orderBy + 2, End(orderBy) - orderBy - 2), sql))
                {
                    if (!item[0].IsIdentifier || !plan.Items.Any(e => e.Alias == item[0].Text)) throw NotSupported(sql);
                    var descending = false;
                    if (item.Count == 2 && (item[1].IsWord("desc") || item[1].IsWord("asc"))) descending = item[1].IsWord("desc");
                    else if (item.Count != 1) throw NotSupported(sql);
                    plan.OrderBy.Add((item[0].Text, descending));
                }
            }

            //条件
            if (0 <= where)
            {
                var node = Fold(await new FilterParser(tokens.GetRange(where + 1, End(where) - where - 1), scope, args, sql, getDefinition).ParseAsync());
                if (node is ConstNode constant)
                {
                    if (!constant.Value) plan.IsEmpty = true;
                }
                else
                {
                    plan.Where = node;
                }
            }
            return plan;
        }

        //inner join ( 集計 ) 別名 on (列 = 別名."gN" or (列 is null and 別名."gN" is null)) and ... / on 1=1
        static async Task<WithinPlan> ParseWithinAsync(List<SqlToken> tokens, Scope scope, string sql, IReadOnlyDictionary<string, object?> args,
            Func<string, Task<PleasanterTableDefinition>> getDefinition)
        {
            if (tokens.Count < 7 || !tokens[0].IsWord("inner") || !tokens[1].IsWord("join") || !tokens[2].IsSymbol("(")) throw NotSupported(sql);
            var close = FindClosingParenthesis(tokens, 2);
            if (close < 0 || tokens.Count < close + 4 || !tokens[close + 1].IsIdentifier || !tokens[close + 2].IsWord("on")) throw NotSupported(sql);
            var alias = tokens[close + 1].Text;
            var plan = await TranslateAggregateCoreAsync(tokens.GetRange(3, close - 3), sql, args, getDefinition, false);
            if (plan.Table != scope.Table || plan.IsGroupCount) throw NotSupported("Aggregation within the groups of another table", sql);
            var within = new WithinPlan { Plan = plan };

            var conditions = tokens.GetRange(close + 3, tokens.Count - close - 3);
            if (conditions.Count == 3 && conditions[0].Kind == SqlTokenKind.Number && conditions[0].Text == "1" && conditions[1].IsSymbol("=") &&
                conditions[2].Kind == SqlTokenKind.Number && conditions[2].Text == "1")
                return within;
            foreach (var part in SplitTopLevel(conditions, e => e.IsWord("and"), sql))
            {
                if (!part[0].IsSymbol("(") || FindClosingParenthesis(part, 0) != part.Count - 1) throw NotSupported(sql);
                var t = part.GetRange(1, part.Count - 2);
                var p = 0;
                void Expect(Func<SqlToken, bool> predicate)
                {
                    if (t.Count <= p || !predicate(t[p++])) throw NotSupported(sql);
                }
                string Inner()
                {
                    Expect(e => e.IsIdentifier && e.Text == alias);
                    Expect(e => e.IsSymbol("."));
                    if (t.Count <= p || !t[p].IsIdentifier) throw NotSupported(sql);
                    return t[p++].Text;
                }
                var outer = ReadGroupKey(t, ref p, scope, sql);
                Expect(e => e.IsSymbol("="));
                var innerAlias = Inner();
                Expect(e => e.IsWord("or"));
                Expect(e => e.IsSymbol("("));
                if (ReadGroupKey(t, ref p, scope, sql) != outer) throw NotSupported(sql);
                Expect(e => e.IsWord("is"));
                Expect(e => e.IsWord("null"));
                Expect(e => e.IsWord("and"));
                if (Inner() != innerAlias) throw NotSupported(sql);
                Expect(e => e.IsWord("is"));
                Expect(e => e.IsWord("null"));
                Expect(e => e.IsSymbol(")"));
                if (p != t.Count) throw NotSupported(sql);
                //選ぶ集計の軸の列だけと突き合わせる
                if (!plan.Items.Any(e => e.Alias == innerAlias && e.Expression is AggregateColumn)) throw NotSupported(sql);
                within.On.Add((outer, innerAlias));
            }
            return within;
        }

        static int FindClosingParenthesis(List<SqlToken> tokens, int open)
        {
            var depth = 0;
            for (var i = open; i < tokens.Count; i++)
            {
                if (tokens[i].IsSymbol("(")) depth++;
                else if (tokens[i].IsSymbol(")") && --depth == 0) return i;
            }
            return -1;
        }

        static List<List<SqlToken>> SplitTopLevel(List<SqlToken> tokens, Func<SqlToken, bool> separator, string sql)
        {
            var parts = new List<List<SqlToken>>();
            var current = new List<SqlToken>();
            var depth = 0;
            foreach (var token in tokens)
            {
                if (token.IsSymbol("(")) depth++;
                else if (token.IsSymbol(")")) depth--;
                if (depth == 0 && separator(token))
                {
                    if (current.Count == 0) throw NotSupported(sql);
                    parts.Add(current);
                    current = new();
                    continue;
                }
                current.Add(token);
            }
            if (current.Count == 0) throw NotSupported(sql);
            parts.Add(current);
            return parts;
        }

        /// <summary>
        /// 軸の式: 列か、本体の日付の軸 (AggregateSqlCreator の PostgreSQL の丸め) の式。受け付ける形は
        /// date_trunc('hour' | 'day' | 'week' | 'month', C) と (date_trunc('quarter' | 'year', S)[ + interval 'F months'])。
        /// C は 列 か (列 + interval 'N minutes')、S は C か (C - interval 'F months')。
        /// </summary>
        static AggregateColumn ReadGroupKey(List<SqlToken> t, ref int p, Scope scope, string sql)
        {
            bool At(int i, Func<SqlToken, bool> predicate) => i < t.Count && predicate(t[i]);
            var wrapped = At(p, e => e.IsSymbol("(")) && At(p + 1, e => e.IsWord("date_trunc"));
            if (!wrapped && !At(p, e => e.IsWord("date_trunc")))
            {
                var (alias, column) = ReadColumn(t, ref p, scope, sql);
                RejectComments(column);
                return new AggregateColumn(alias, column);
            }
            var q = p;
            void Expect(Func<SqlToken, bool> predicate)
            {
                if (!At(q, predicate)) throw NotSupported(sql);
                q++;
            }
            //interval 'N 単位' の N
            int Interval(string unit)
            {
                Expect(e => e.IsWord("interval"));
                if (!At(q, e => e.Kind == SqlTokenKind.StringLiteral)) throw NotSupported(sql);
                var parts = t[q++].Text.Split(' ');
                if (parts.Length != 2 || parts[1] != unit || !int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) throw NotSupported(sql);
                return n;
            }

            if (wrapped) q++;
            Expect(e => e.IsWord("date_trunc"));
            Expect(e => e.IsSymbol("("));
            if (!At(q, e => e.Kind == SqlTokenKind.StringLiteral)) throw NotSupported(sql);
            var unit = t[q++].Text.ToLowerInvariant();
            Expect(e => e.IsSymbol(","));

            //S = C か (C - interval 'F months')、C = 列 か (列 + interval 'N minutes')
            var shift = 0;
            var fiscal = 0;
            var opens = 0;
            while (At(q, e => e.IsSymbol("("))) { q++; opens++; }
            if (opens > 2) throw NotSupported(sql);
            var (columnAlias, columnName) = ReadColumn(t, ref q, scope, sql);
            for (var i = 0; i < opens; i++)
            {
                if (At(q, e => e.IsSymbol("+")) && i == 0)
                {
                    q++;
                    shift = Interval("minutes");
                }
                else if (At(q, e => e.IsSymbol("-")))
                {
                    q++;
                    fiscal = Interval("months");
                    if (i != opens - 1) throw NotSupported(sql);
                }
                else throw NotSupported(sql);
                Expect(e => e.IsSymbol(")"));
            }
            Expect(e => e.IsSymbol(")"));

            if (unit is "hour" or "day" or "week" or "month")
            {
                if (wrapped || fiscal != 0) throw NotSupported(sql);
            }
            else if (unit is "quarter" or "year")
            {
                if (!wrapped) throw NotSupported(sql);
                var back = 0;
                if (At(q, e => e.IsSymbol("+")))
                {
                    q++;
                    back = Interval("months");
                }
                if (back != fiscal || fiscal is < 0 or > 11) throw NotSupported(sql);
                Expect(e => e.IsSymbol(")"));
            }
            else throw NotSupported(sql);
            p = q;
            return new AggregateColumn(columnAlias, columnName, new DateBucketSpec(unit, shift, fiscal));
        }

        static AggregateExpression ParseAggregateExpression(List<SqlToken> tokens, Scope scope, string sql)
        {
            if (tokens.Count == 1 && tokens[0].Kind == SqlTokenKind.Number && tokens[0].Text == "1") return new AggregateConstant();

            var p = 0;
            //軸 (日付の丸めを含む)
            AggregateColumn Column() => ReadGroupKey(tokens, ref p, scope, sql);
            //集計する列 (丸めない)
            AggregateColumn Measured()
            {
                var (alias, column) = ReadColumn(tokens, ref p, scope, sql);
                RejectComments(column);
                return new AggregateColumn(alias, column);
            }
            void Expect(Func<SqlToken, bool> predicate)
            {
                if (tokens.Count <= p || !predicate(tokens[p++])) throw NotSupported(sql);
            }
            AggregateExpression Done(AggregateExpression expression)
            {
                if (p != tokens.Count) throw NotSupported(sql);
                return expression;
            }

            if (tokens.Count > 0 && tokens[0].IsWord("case"))
            {
                p = 1;
                if (tokens.Count > p && tokens[p].IsWord("when"))
                {
                    //case when 列 is null then 1 else 0 end
                    p++;
                    var column = Column();
                    Expect(t => t.IsWord("is"));
                    Expect(t => t.IsWord("null"));
                    Expect(t => t.IsWord("then"));
                    Expect(t => t.Kind == SqlTokenKind.Number && t.Text == "1");
                    Expect(t => t.IsWord("else"));
                    Expect(t => t.Kind == SqlTokenKind.Number && t.Text == "0");
                    Expect(t => t.IsWord("end"));
                    return Done(new AggregateNullFlag(column));
                }
                {
                    //case 列 when 'a' then 0 ... else n end
                    var column = Column();
                    var codes = new List<(string, int)>();
                    while (p < tokens.Count && tokens[p].IsWord("when"))
                    {
                        p++;
                        if (tokens.Count <= p || tokens[p].Kind != SqlTokenKind.StringLiteral) throw NotSupported(sql);
                        var code = tokens[p++].Text;
                        Expect(t => t.IsWord("then"));
                        codes.Add((code, ReadInt()));
                    }
                    Expect(t => t.IsWord("else"));
                    var otherwise = ReadInt();
                    Expect(t => t.IsWord("end"));
                    if (codes.Count == 0) throw NotSupported(sql);
                    return Done(new AggregateRank(column, codes, otherwise));
                }
            }
            if (tokens.Count >= 4 && tokens[0].Kind == SqlTokenKind.Word && tokens[1].IsSymbol("(") && !tokens[0].IsWord("date_trunc"))
            {
                var function = tokens[0].Text.ToLowerInvariant();
                p = 2;
                AggregateFunctionCall call;
                if (function == "count" && tokens[2].IsSymbol("*"))
                {
                    p = 3;
                    call = new AggregateFunctionCall("count", null);
                }
                else if (function == "count" && tokens[2].IsWord("distinct"))
                {
                    p = 3;
                    call = new AggregateFunctionCall("countdistinct", Measured());
                }
                else if (function is "sum" or "avg" or "min" or "max")
                {
                    call = new AggregateFunctionCall(function, Measured());
                }
                else
                {
                    throw NotSupported(sql);
                }
                Expect(t => t.IsSymbol(")"));
                return Done(call);
            }
            return Done(Column());

            int ReadInt()
            {
                if (tokens.Count <= p || tokens[p].Kind != SqlTokenKind.Number || !int.TryParse(tokens[p].Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    throw NotSupported(sql);
                p++;
                return n;
            }
        }

        // ---------------------------------------------------------------- 書き込み

        internal static bool IsUpdate(string sql) => WithoutSpaces(Tokenize(sql)).FirstOrDefault()?.IsWord("update") == true;
        internal static bool IsDelete(string sql) => WithoutSpaces(Tokenize(sql)).FirstOrDefault()?.IsWord("delete") == true;
        internal static bool IsInsert(string sql) => WithoutSpaces(Tokenize(sql)).FirstOrDefault()?.IsWord("insert") == true;

        /// <summary>
        /// 本体の insert を読む: insert into "t"("a",...) values(@p0,...) returning "k" / insert into "t" default values returning "k" /
        /// 一括 insert into "t" ("a",...) values (@p0,...),(@p1,...)
        /// </summary>
        internal static InsertPlan ParseInsert(string sql)
        {
            var tokens = WithoutSpaces(Tokenize(sql));
            var i = 0;
            Expect(tokens, ref i, t => t.IsWord("insert"), sql);
            Expect(tokens, ref i, t => t.IsWord("into"), sql);
            var insert = new InsertPlan { Table = ReadTableName(tokens, ref i, sql) };
            if (i < tokens.Count && tokens[i].IsWord("default"))
            {
                i++;
                Expect(tokens, ref i, t => t.IsWord("values"), sql);
                insert.Rows.Add(new());
            }
            else
            {
                Expect(tokens, ref i, t => t.IsSymbol("("), sql);
                while (true)
                {
                    var column = Next(tokens, ref i, sql);
                    if (!column.IsIdentifier) throw NotSupported(sql);
                    insert.Columns.Add(column.Text);
                    var separator = Next(tokens, ref i, sql);
                    if (separator.IsSymbol(")")) break;
                    if (!separator.IsSymbol(",")) throw NotSupported(sql);
                }
                Expect(tokens, ref i, t => t.IsWord("values"), sql);
                while (true)
                {
                    Expect(tokens, ref i, t => t.IsSymbol("("), sql);
                    var row = new List<string>();
                    while (true)
                    {
                        var parameter = Next(tokens, ref i, sql);
                        if (parameter.Kind != SqlTokenKind.Parameter) throw NotSupported(sql);
                        row.Add(parameter.Text);
                        var separator = Next(tokens, ref i, sql);
                        if (separator.IsSymbol(")")) break;
                        if (!separator.IsSymbol(",")) throw NotSupported(sql);
                    }
                    if (row.Count != insert.Columns.Count) throw NotSupported(sql);
                    insert.Rows.Add(row);
                    if (i < tokens.Count && tokens[i].IsSymbol(","))
                    {
                        i++;
                        continue;
                    }
                    break;
                }
            }
            if (i < tokens.Count && tokens[i].IsWord("returning"))
            {
                i++;
                var column = Next(tokens, ref i, sql);
                if (!column.IsIdentifier) throw NotSupported(sql);
                if (i != tokens.Count) throw NotSupported(sql);
                insert.ReturningColumn = column.Text;
            }
            if (i != tokens.Count) throw NotSupported(sql);
            return insert;
        }

        /// <summary>本体の update (update "t" set "c"=@p0,... where "k"=@pid0 [and ...]) を読む。</summary>
        internal static WritePlan ParseUpdate(string sql)
        {
            var tokens = WithoutSpaces(Tokenize(sql));
            var i = 0;
            Expect(tokens, ref i, t => t.IsWord("update"), sql);
            var update = new WritePlan { Table = ReadTableName(tokens, ref i, sql) };
            Expect(tokens, ref i, t => t.IsWord("set"), sql);
            while (true)
            {
                var column = Next(tokens, ref i, sql);
                if (!column.IsIdentifier) throw NotSupported(sql);
                Expect(tokens, ref i, t => t.IsSymbol("="), sql);
                var parameter = Next(tokens, ref i, sql);
                if (parameter.Kind != SqlTokenKind.Parameter) throw NotSupported(sql);
                update.Values.Add((column.Text, parameter.Text));
                var separator = Next(tokens, ref i, sql);
                if (separator.IsWord("where")) break;
                if (!separator.IsSymbol(",")) throw NotSupported(sql);
            }
            ReadWhere(update, tokens.GetRange(i, tokens.Count - i), sql);
            return update;
        }

        /// <summary>本体の delete (delete from "t" where "k"=@pid0 [and ...]) を読む。</summary>
        internal static WritePlan ParseDelete(string sql)
        {
            var tokens = WithoutSpaces(Tokenize(sql));
            var i = 0;
            Expect(tokens, ref i, t => t.IsWord("delete"), sql);
            Expect(tokens, ref i, t => t.IsWord("from"), sql);
            var delete = new WritePlan { Table = ReadTableName(tokens, ref i, sql) };
            Expect(tokens, ref i, t => t.IsWord("where"), sql);
            ReadWhere(delete, tokens.GetRange(i, tokens.Count - i), sql);
            return delete;
        }

        //先頭はキーの一致 ("k"=@pid0)。続きは and でつながる条件 (形は確かめの読み出しが見る)
        static void ReadWhere(WritePlan write, List<SqlToken> where, string sql)
        {
            if (where.Any(e => e.IsSymbol(";"))) throw NotSupported(sql);
            if (where.Count < 3 || !where[0].IsIdentifier || !where[1].IsSymbol("=") || where[2].Kind != SqlTokenKind.Parameter) throw NotSupported(sql);
            if (3 < where.Count && !where[3].IsWord("and")) throw NotSupported(sql);
            write.KeyColumn = where[0].Text;
            write.KeyParameter = where[2].Text;
            write.WhereSql = string.Join(" ", where.Select(e => e.Kind switch
            {
                SqlTokenKind.QuotedIdentifier => "\"" + e.Text + "\"",
                SqlTokenKind.StringLiteral => "'" + e.Text.Replace("'", "''") + "'",
                _ => e.Text,
            }));
        }

        static SqlToken Next(List<SqlToken> tokens, ref int i, string sql)
        {
            if (tokens.Count <= i) throw NotSupported(sql);
            return tokens[i++];
        }

        static void Expect(List<SqlToken> tokens, ref int i, Func<SqlToken, bool> predicate, string sql)
        {
            if (!predicate(Next(tokens, ref i, sql))) throw NotSupported(sql);
        }
    }
}
