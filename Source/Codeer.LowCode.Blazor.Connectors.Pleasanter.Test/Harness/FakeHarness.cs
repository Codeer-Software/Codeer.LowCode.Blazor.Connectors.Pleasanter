using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness
{
    /// <summary>
    /// 偽物のプリザンター (<see cref="FakePleasanter"/>) + 本物のコネクター + 本体の ModuleDataIO。本体が組み立てる SQL がどんな View の要求になるかを確かめる。
    /// サイト ID: Customers = 11 / Orders = 12 / Tasks = 13 / Many = 14。
    /// </summary>
    public sealed class FakeHarness
    {
        public const long CustomersId = 11;
        public const long OrdersId = 12;
        public const long TasksId = 13;
        public const long ManyId = 14;

        public FakePleasanter Fake { get; } = new();
        public List<TimeSpan> Delays { get; } = new();
        public RecordingDbAccessor Inner { get; } = new();
        internal PleasanterConnection Connection { get; }
        public PleasanterDbAccessor Accessor { get; }
        public DesignData Design { get; }
        public ModuleDataIO IO { get; }

        /// <param name="editAccessor">コネクターの作り方を替えるとき (表の定義の出所を渡すなど)。</param>
        public FakeHarness(Action<PleasanterConnectionSettings>? edit = null, Action<DesignData>? editDesign = null,
            Func<RecordingDbAccessor, PleasanterConnection, PleasanterDbAccessor>? editAccessor = null)
        {
            Fake.Sites[CustomersId] = TestDesign.SiteData(CustomersId, "Results", TestDesign.CustomersSettings());
            Fake.Sites[OrdersId] = TestDesign.SiteData(OrdersId, "Results", TestDesign.OrdersSettings(CustomersId));
            Fake.Sites[TasksId] = TestDesign.SiteData(TasksId, "Issues", TestDesign.TasksSettings(CustomersId));
            Fake.Sites[ManyId] = TestDesign.SiteData(ManyId, "Results", TestDesign.ManySettings());
            Connection = CreateConnection(Fake, Delays, edit);
            Accessor = editAccessor?.Invoke(Inner, Connection) ?? new PleasanterDbAccessor(Inner, new[] { Connection });
            Design = TestDesign.CreateDesign(editDesign);
            IO = new ModuleDataIO(Design, new TestDesign.Auth(), Accessor, null!);
        }

        internal static PleasanterConnectionSettings CreateSettings(Action<PleasanterConnectionSettings>? edit = null)
        {
            var settings = new PleasanterConnectionSettings
            {
                Name = TestDesign.DataSourceName,
                Url = FakePleasanter.Url,
                ApiKey = FakePleasanter.ApiKey,
                Sites = new() { ["Customers"] = CustomersId, ["Orders"] = OrdersId, ["Tasks"] = TasksId, ["Many"] = ManyId },
            };
            edit?.Invoke(settings);
            return settings;
        }

        /// <summary>偽物への接続。やり直しは待たずに delays に記録する。</summary>
        internal static PleasanterConnection CreateConnection(FakePleasanter fake, List<TimeSpan> delays, Action<PleasanterConnectionSettings>? edit = null)
            => new(CreateSettings(edit), fake)
            {
                Delay = e =>
                {
                    delays.Add(e);
                    return Task.CompletedTask;
                },
            };
    }
}
