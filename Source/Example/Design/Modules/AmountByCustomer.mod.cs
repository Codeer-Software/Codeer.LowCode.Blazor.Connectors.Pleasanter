//案件の金額を 取引先 × 区分 でまとめる。2 つ目の軸 (区分) を列にする (Show の第 2 引数 = 行にする軸の数)
void DetailLayoutDesign_OnAfterInitialization()
{
    var agg = new ModuleAggregator<Project>();
    agg.GroupBy(e => e.Customer);
    agg.GroupBy(e => e.Category);
    var amount = agg.Sum(e => e.Amount);
    agg.OrderByMeasureDescending(amount);
    Amounts.Show(agg, 1);

    //受注日の月 × 区分。日付の軸は月の初日にまとまる (受注日の無い案件は空の 1 行)
    var monthly = new ModuleAggregator<Project>();
    monthly.GroupByMonth(e => e.OrderDate);
    monthly.GroupBy(e => e.Category);
    monthly.Sum(e => e.Amount);
    monthly.OrderByGroup(0);
    MonthlyAmounts.Show(monthly, 1);
}
