using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness
{
    /// <summary>
    /// テストのサイトの設定 (実物のサイトを作るときの SiteSettings と、偽物の getsite の応答に使う) とモジュールのデザイン。データソース名は "PL"。
    /// 表: Customers (記録テーブル) / Orders (記録テーブル・ClassA が Customers へのリンク) / Tasks (期限付きテーブル・ClassA が Customers へのリンク) / Many (記録テーブル・205 行)。
    /// </summary>
    public static class TestDesign
    {
        public const string DataSourceName = "PL";

        // ---------------------------------------------------------------- サイトの設定

        static JsonObject Column(string name, string label, Action<JsonObject>? edit = null)
        {
            var column = new JsonObject { ["ColumnName"] = name, ["LabelText"] = label };
            edit?.Invoke(column);
            return column;
        }

        static JsonArray Names(params string[] names) => new(names.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray());

        public static JsonObject CustomersSettings() => new()
        {
            ["Columns"] = new JsonArray(
                Column("ClassA", "コード"),
                Column("ClassB", "ランク", e => e["ChoicesText"] = "A,ゴールド\nB,シルバー\nC,ブロンズ"),
                Column("ClassC", "担当", e => e["ChoicesText"] = "[[Users]]"),
                Column("NumA", "与信", e => e["DecimalPlaces"] = 2),
                Column("NumB", "数量", e => e["Nullable"] = true),
                Column("DateA", "登録日", e => e["EditorFormat"] = "Ymd"),
                Column("DateB", "最終連絡", e => e["EditorFormat"] = "Ymdhms"),
                Column("CheckA", "有効"),
                Column("DescriptionA", "メモ")),
            ["EditorColumnHash"] = new JsonObject
            {
                ["General"] = Names("Title", "Body", "Status", "Manager", "Owner", "ClassA", "ClassB", "ClassC", "NumA", "NumB", "DateA", "DateB", "CheckA", "DescriptionA"),
            },
        };

        public static JsonObject OrdersSettings(long customersSiteId) => new()
        {
            ["Columns"] = new JsonArray(
                Column("ClassA", "顧客", e => e["ChoicesText"] = $"[[{customersSiteId}]]"),
                Column("ClassB", "区分"),
                Column("NumA", "金額", e => e["DecimalPlaces"] = 2),
                Column("DateA", "受注日", e => e["EditorFormat"] = "Ymd")),
            ["EditorColumnHash"] = new JsonObject { ["General"] = Names("Title", "ClassA", "ClassB", "NumA", "DateA") },
        };

        public static JsonObject TasksSettings(long customersSiteId) => new()
        {
            ["Columns"] = new JsonArray(
                Column("ClassA", "顧客", e => e["ChoicesText"] = $"[[{customersSiteId}]]"),
                Column("NumA", "見積")),
            ["EditorColumnHash"] = new JsonObject
            {
                ["General"] = Names("Title", "Body", "StartTime", "CompletionTime", "WorkValue", "ProgressRate", "RemainingWorkValue", "Status", "Manager", "Owner", "ClassA", "NumA"),
            },
        };

        public static JsonObject ManySettings() => new()
        {
            ["Columns"] = new JsonArray(Column("NumA", "番号")),
            ["EditorColumnHash"] = new JsonObject { ["General"] = Names("Title", "NumA") },
        };

        /// <summary>getsite の Response.Data の形 (偽物用)。</summary>
        public static JsonObject SiteData(long siteId, string referenceType, JsonObject settings)
        {
            settings["ReferenceType"] = referenceType;
            return new JsonObject { ["SiteId"] = siteId, ["ReferenceType"] = referenceType, ["Title"] = "test", ["SiteSettings"] = settings };
        }

        // ---------------------------------------------------------------- モジュール

        static ModuleDesign Module(string name, string table)
            => new() { Name = name, DataSourceName = DataSourceName, DbTable = table, CanCreate = true, CanUpdate = true, CanDelete = true };

        static LinkFieldDesign Link(string name, string column, string module, string display)
            => new() { Name = name, DbColumn = column, SearchCondition = new SearchCondition(module), ValueVariable = "Id.Value", DisplayTextVariable = display };

        public static DesignData CreateDesign(Action<DesignData>? edit = null)
        {
            var d = new DesignData();
            var modules = (IEditableModuleDesign)d.Modules;

            var customer = Module("Customer", "Customers");
            customer.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "ResultId" });
            customer.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Title" });
            customer.Fields.Add(new TextFieldDesign { Name = "Body", DbColumn = "Body" });
            customer.Fields.Add(new TextFieldDesign { Name = "Code", DbColumn = "ClassA" });
            customer.Fields.Add(new TextFieldDesign { Name = "Rank", DbColumn = "ClassB" });
            customer.Fields.Add(new TextFieldDesign { Name = "Person", DbColumn = "ClassC" });
            customer.Fields.Add(new NumberFieldDesign { Name = "Credit", DbColumn = "NumA" });
            customer.Fields.Add(new NumberFieldDesign { Name = "Quantity", DbColumn = "NumB" });
            customer.Fields.Add(new DateFieldDesign { Name = "Since", DbColumn = "DateA" });
            customer.Fields.Add(new DateTimeFieldDesign { Name = "LastContact", DbColumn = "DateB" });
            customer.Fields.Add(new BooleanFieldDesign { Name = "Active", DbColumn = "CheckA" });
            customer.Fields.Add(new TextFieldDesign { Name = "Memo", DbColumn = "DescriptionA" });
            customer.Fields.Add(new NumberFieldDesign { Name = "Status", DbColumn = "Status" });
            customer.Fields.Add(new NumberFieldDesign { Name = "Manager", DbColumn = "Manager" });
            //フィールド名を UpdatedAt / CreatedAt にすると本体のシステムフィールドになり、保存のたびに今の時刻が書かれる
            customer.Fields.Add(new DateTimeFieldDesign { Name = "Updated", DbColumn = "UpdatedTime" });
            customer.Fields.Add(new NumberFieldDesign { Name = "Ver", DbColumn = "Ver" });
            customer.Fields.Add(new ListFieldDesign
            {
                Name = "Orders",
                SearchCondition = new SearchCondition("Order")
                {
                    Condition = MultiMatchCondition.And(new FieldVariableMatchCondition { SearchTargetVariable = "Customer.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" }),
                },
            });
            modules.Add(customer);

            var locked = Module("CustomerLocked", "Customers");
            locked.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "ResultId" });
            locked.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Title" });
            locked.Fields.Add(new OptimisticLockingFieldDesign { Name = SystemFieldNames.OptimisticLocking, DbColumn = "UpdatedTime", IncrementVersion = false });
            modules.Add(locked);

            var order = Module("Order", "Orders");
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "ResultId" });
            order.Fields.Add(new TextFieldDesign { Name = "Note", DbColumn = "Title" });
            order.Fields.Add(Link("Customer", "ClassA", "Customer", "Name.Value"));
            order.Fields.Add(new TextFieldDesign { Name = "Kind", DbColumn = "ClassB" });
            order.Fields.Add(new NumberFieldDesign { Name = "Amount", DbColumn = "NumA" });
            order.Fields.Add(new DateFieldDesign { Name = "OrderDate", DbColumn = "DateA" });
            //リンク越しの項目 (結合先の列)
            order.Fields.Add(new TextFieldDesign { Name = "Customer.Name", DbColumn = "Title" });
            modules.Add(order);

            var task = Module("Task", "Tasks");
            task.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "IssueId" });
            task.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "Start", DbColumn = "StartTime" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "Due", DbColumn = "CompletionTime" });
            task.Fields.Add(new NumberFieldDesign { Name = "Work", DbColumn = "WorkValue" });
            task.Fields.Add(new NumberFieldDesign { Name = "Progress", DbColumn = "ProgressRate" });
            task.Fields.Add(new NumberFieldDesign { Name = "Remaining", DbColumn = "RemainingWorkValue" });
            task.Fields.Add(Link("Customer", "ClassA", "Customer", "Name.Value"));
            modules.Add(task);

            var many = Module("Many", "Many");
            many.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "ResultId" });
            many.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            many.Fields.Add(new NumberFieldDesign { Name = "No", DbColumn = "NumA" });
            modules.Add(many);

            edit?.Invoke(d);
            AdjustDesign(d);
            return d;
        }

        //実行時のデザインの読み込みと同じ正規化 (本体の internal なので、テストだけリフレクションで呼ぶ。FileMaker 版のハーネスと同じ)
        static void AdjustDesign(DesignData d)
        {
            var adjust = typeof(DesignData).Assembly.GetType("Codeer.LowCode.Blazor.DesignLogic.ModuleDesignConsistencyLogic")!
                .GetMethod("Adjust", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, new[] { typeof(DesignData) })!;
            adjust.Invoke(null, new object[] { d });
        }

        public sealed class Auth : IAuthenticationContext
        {
            public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");
        }
    }

    /// <summary>テストの共通の道具。</summary>
    public static class TestUtil
    {
        public static FieldValueMatchCondition Match(string variable, MatchComparison comparison, MultiTypeValue value)
            => new() { SearchTargetVariable = variable, Comparison = comparison, Value = value };

        public static FieldValueMatchCondition Value(string variable, MatchComparison comparison, object? value)
            => new() { SearchTargetVariable = variable, Comparison = comparison, Value = value == null ? new NullValue() : MultiTypeValue.Create(value) };

        public static MatchConditionBase Not(MatchConditionBase condition)
        {
            var not = MultiMatchCondition.And(condition);
            ((MultiMatchCondition)not).IsNot = true;
            return not;
        }

        public static string Id(ModuleData data) => ((IdFieldData)data.Fields["Id"]).Value!;

        public static T? Field<T>(ModuleData data, string name) where T : FieldDataBase
            => data.Fields.TryGetValue(name, out var value) ? (T)value : null;
    }
}
