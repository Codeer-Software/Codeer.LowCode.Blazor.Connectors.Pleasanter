using Codeer.LowCode.Blazor.Connectors.Pleasanter.Designer;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.SystemSettings;

namespace LowCodeApp.Designer
{
    public class DbAccessorFactory : IDbAccessorFactory
    {
        //プリザンターのデータソースはコネクターが、それ以外は DbAccessor が受け持つ
        public IDbAccessor Create(DataSource[] dataSources)
            => PleasanterDesignerInitializer.CreateDbAccessor(new DbAccessor(dataSources), dataSources);
    }
}
