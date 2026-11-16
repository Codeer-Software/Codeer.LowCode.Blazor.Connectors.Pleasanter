//案件の金額を 取引先 × 区分 でまとめる。2 つ目の軸 (区分) を列にする (Show の第 2 引数 = 行にする軸の数)
void DetailLayoutDesign_OnAfterInitialization()
{
    var agg = new ModuleAggregator<Project>();
    agg.GroupBy(e => e.Customer);
    agg.GroupBy(e => e.Category);
    var amount = agg.Sum(e => e.Amount);
    agg.OrderByMeasureDescending(amount);
    Amounts.Show(agg, 1);
}
