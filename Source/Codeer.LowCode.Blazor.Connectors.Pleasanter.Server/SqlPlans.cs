namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    enum SqlTokenKind
    {
        Space,
        Word,
        QuotedIdentifier,
        StringLiteral,
        Parameter,
        Number,
        Symbol,
    }

    record SqlToken(SqlTokenKind Kind, string Text)
    {
        public bool IsWord(string word) => Kind == SqlTokenKind.Word && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);
        public bool IsSymbol(string symbol) => Kind == SqlTokenKind.Symbol && Text == symbol;
        /// <summary>識別子 (引用符つき・なし)。</summary>
        public bool IsIdentifier => Kind == SqlTokenKind.QuotedIdentifier || Kind == SqlTokenKind.Word;
    }

    /// <summary>読み出しの結果の 1 列。</summary>
    class ResultColumn
    {
        /// <summary>本体に返す行のキー (本体が書いた別名のまま)。</summary>
        public string Key { get; init; } = string.Empty;
        /// <summary>表の別名 (主表か結合)。</summary>
        public string Alias { get; init; } = string.Empty;
        public string Column { get; init; } = string.Empty;
    }

    /// <summary>結合 1 つ (left join "表" as "別名" on "元"."列"="別名"."列")。プリザンターでは結合せず、主表を読んだあと値を集めて読み、メモリで突き合わせる。</summary>
    class JoinPlan
    {
        public string Table { get; init; } = string.Empty;
        public string Alias { get; init; } = string.Empty;
        public string SourceAlias { get; init; } = string.Empty;
        public string SourceColumn { get; init; } = string.Empty;
        public string DestinationColumn { get; init; } = string.Empty;
        public PleasanterTableDefinition Definition { get; init; } = new();
    }

    /// <summary>並びの 1 項目。</summary>
    record OrderItem(string Alias, string Column, bool Descending);

    /// <summary>読み出しの計画。</summary>
    class ReadPlan
    {
        public string Table { get; init; } = string.Empty;
        public string MainAlias { get; init; } = string.Empty;
        public PleasanterTableDefinition Definition { get; init; } = new();
        /// <summary>select count(*)。</summary>
        public bool IsCount { get; init; }
        /// <summary>select * (行のキーは列名)。</summary>
        public bool IsSelectAll { get; init; }
        public List<ResultColumn> Columns { get; } = new();
        public List<JoinPlan> Joins { get; } = new();
        /// <summary>条件 (畳んだ構文木)。条件なしは null。結合先の列・一覧をまたぐ条件は、別の表を読んで集めた値との一致 (<see cref="InSetNode"/>) になっている。</summary>
        public FilterNode? Where { get; set; }
        /// <summary>条件が必ず偽 (1=0)。要求を送らずに 0 行を返す。</summary>
        public bool IsEmpty { get; set; }
        /// <summary>並び (結合先の列を含む)。</summary>
        public List<OrderItem> Order { get; } = new();
        /// <summary>読む行数 (本体がページングを付けたとき)。null なら全部。</summary>
        public int? Limit { get; set; }
        public int Offset { get; set; }
    }

    // ---------------------------------------------------------------- 条件の構文木

    /// <summary>条件の構文木。</summary>
    abstract record FilterNode;
    record ConstNode(bool Value) : FilterNode;
    record AndNode(List<FilterNode> Children) : FilterNode;
    record OrNode(List<FilterNode> Children) : FilterNode;
    record NotNode(FilterNode Child) : FilterNode;

    enum PredicateOperator
    {
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        /// <summary>部分一致 (本体のパターン %文字%)。値はエスケープを外した文字。</summary>
        Like,
        IsNull,
        IsNotNull,
    }

    /// <summary>主表 (この条件で絞る表) の列の比較 1 つ。値は本体が渡したパラメータの値 (null でない。値が null の比較は <see cref="UnknownNode"/>)。</summary>
    record PredicateNode(string Column, PredicateOperator Operator, object? Value) : FilterNode;

    /// <summary>SQL で unknown になる比較 (値が null の比較)。真にも偽にもならない (not を付けても真にならない)。</summary>
    record UnknownNode : FilterNode;
    /// <summary>
    /// 真になる行の条件と偽になる行の条件を別に持つ比較 (結合先の列の比較・一覧をまたぐ条件)。どちらにも入らない行は SQL の unknown。
    /// not の中では False を使う (SQL の 3 値の論理と同じ結果にする)。
    /// </summary>
    record TriNode(FilterNode True, FilterNode False) : FilterNode;
    /// <summary>
    /// 列の値が、別の表を読んで集める値 (<see cref="KeySource"/>) のどれかに一致する。真か偽 (unknown にならない): 列が null の行は偽、
    /// その not は列が null の行も真 (結合先・子の行が無い行を SQL の意味で拾うのに使う)。
    /// </summary>
    record InSetNode(string Column, KeySource Source) : FilterNode;
    /// <summary>列の値が Values のどれかに一致する (<see cref="InSetNode"/> の値を集めたあと。値は列の種類の .NET の値)。列が null の行は偽、not は列が null の行も真。</summary>
    sealed record ValuesNode(string Column, List<object> Values) : FilterNode
    {
        readonly HashSet<string> _keys = Values.Select(PleasanterValues.KeyText).ToHashSet(StringComparer.Ordinal);

        /// <summary>行の値が一致するか (値の文字 = <see cref="PleasanterValues.KeyText"/> で比べる)。</summary>
        public bool Contains(object? value) => value != null && _keys.Contains(PleasanterValues.KeyText(value));
    }
    /// <summary>一覧をまたぐ条件の相関 (子の列 = 親の列)。exists の中でだけ現れ、読み替えで取り除く。</summary>
    record CorrelationNode(string InnerColumn, string OuterColumn) : FilterNode;

    /// <summary>別の表を条件で読んで、1 つの列の値を集める (結合先の列の条件・一覧をまたぐ条件)。</summary>
    sealed class KeySource
    {
        public string Table { get; init; } = string.Empty;
        public PleasanterTableDefinition Definition { get; init; } = new();
        /// <summary>その表の条件 (畳む前)。</summary>
        public FilterNode Filter { get; init; } = new ConstNode(true);
        /// <summary>集める列。</summary>
        public string Column { get; init; } = string.Empty;
    }

    // ---------------------------------------------------------------- 集計

    /// <summary>集計の SQL の式。</summary>
    abstract record AggregateExpression;
    /// <summary>列 ("別名"."列")。</summary>
    record AggregateColumn(string Alias, string Column) : AggregateExpression;
    /// <summary>case when 列 is null then 1 else 0 end (空値を最後に並べる印)。</summary>
    record AggregateNullFlag(AggregateColumn Column) : AggregateExpression;
    /// <summary>case 列 when 'a' then 0 ... else n end (候補の順)。</summary>
    record AggregateRank(AggregateColumn Column, List<(string Code, int Rank)> Codes, int Else) : AggregateExpression
    {
        public virtual bool Equals(AggregateRank? other) => other != null && Column == other.Column && Else == other.Else && Codes.SequenceEqual(other.Codes);
        public override int GetHashCode() => HashCode.Combine(Column, Else, Codes.Count);
    }
    /// <summary>集計関数。Function は count (Column が null なら count(*)) / countdistinct / sum / avg / min / max。</summary>
    record AggregateFunctionCall(string Function, AggregateColumn? Column) : AggregateExpression;
    /// <summary>定数 1 (グループの数を数える SQL の中の select 1)。</summary>
    record AggregateConstant : AggregateExpression;

    /// <summary>集計の select の 1 項目。</summary>
    record AggregateItem(string Alias, AggregateExpression Expression);

    /// <summary>集計後の絞り込み 1 つ (集計値 演算子 数値)。</summary>
    record HavingCondition(AggregateFunctionCall Measure, string Operator, decimal Value);

    /// <summary>集計の計画 (本体の AggregateSqlCreator が組む SQL)。プリザンターの API に集計は無いので、行を読んでメモリで集計する。</summary>
    class AggregatePlan
    {
        public string Table { get; init; } = string.Empty;
        public string MainAlias { get; init; } = string.Empty;
        public PleasanterTableDefinition Definition { get; init; } = new();
        public List<JoinPlan> Joins { get; } = new();
        /// <summary>条件 (畳んだもの)。条件なしは null。</summary>
        public FilterNode? Where { get; set; }
        /// <summary>条件が必ず偽 (1=0)。要求を送らない。</summary>
        public bool IsEmpty { get; set; }
        public List<AggregateItem> Items { get; } = new();
        /// <summary>軸の列 (select の列の項目)。group by が無ければ空。</summary>
        public List<AggregateColumn> Groups { get; } = new();
        /// <summary>集計関数 (select と having に出るもの。重ならない)。</summary>
        public List<AggregateFunctionCall> Measures { get; } = new();
        public List<HavingCondition> Having { get; } = new();
        /// <summary>並び (select の別名)。</summary>
        public List<(string Alias, bool Descending)> OrderBy { get; } = new();
        public int? Limit { get; set; }
        public int Offset { get; set; }
        /// <summary>グループの数を数える SQL (select count(*) from (select 1 ... group by ...) x)。</summary>
        public bool IsGroupCount { get; init; }
        /// <summary>行を選ぶ定義 (WithinGroupsOf。inner join (集計) 別名 on ...)。無ければ null。</summary>
        public WithinPlan? Within { get; set; }
    }

    /// <summary>
    /// 行を選ぶ定義 (本体の WithinGroupsOf): 別の集計 (同じ表) の結果の軸の値の組と一致する行だけを集計する。
    /// On は (この集計の列, 選ぶ集計の結果の別名) の組。空なら選ぶ集計が 1 行でも返せば全部の行 (on 1=1)。空の値は空の値と一致する。
    /// </summary>
    class WithinPlan
    {
        public AggregatePlan Plan { get; init; } = new();
        public List<(AggregateColumn Outer, string InnerAlias)> On { get; } = new();
    }

    class InsertPlan
    {
        public string Table { get; init; } = string.Empty;
        public List<string> Columns { get; } = new();
        /// <summary>行ごとのパラメータ名 (Columns と同じ並び)。一括の追加は複数行。</summary>
        public List<List<string>> Rows { get; } = new();
        /// <summary>returning の列 (無ければ空)。</summary>
        public string ReturningColumn { get; set; } = string.Empty;
    }

    /// <summary>update / delete の対象。</summary>
    class WritePlan
    {
        public string Table { get; init; } = string.Empty;
        /// <summary>update の set (列, パラメータ)。delete は空。</summary>
        public List<(string Column, string Parameter)> Values { get; } = new();
        public string KeyColumn { get; set; } = string.Empty;
        public string KeyParameter { get; set; } = string.Empty;
        /// <summary>where より後ろの SQL (本体が書いたまま)。対象の行の確かめに使う。</summary>
        public string WhereSql { get; set; } = string.Empty;
    }
}
