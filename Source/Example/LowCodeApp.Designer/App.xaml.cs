using Codeer.LowCode.Blazor.Connectors.Pleasanter.Designer;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.Extras.Designer;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace LowCodeApp.Designer;

public partial class App : DesignerApp
{
    protected override void OnStartup(StartupEventArgs e)
    {
        //DB への接続 (プリザンターのデータソースは DbAccessorFactory で包む)
        Services.AddSingleton<IDbAccessorFactory, DbAccessorFactory>();

        //Extras のフィールド (Example/Design のユーザーモジュールのログイン設定)
        ExtrasDesignerInitializer.Initialize(BlazorRuntime);

        //プリザンター: AI 用の仕様ドキュメントの登録と、メタ情報をファイルから読む設定。base.OnStartup より前に呼ぶ
        PleasanterDesignerInitializer.Initialize(DesignerEnvironment);

        base.OnStartup(e);

        //プリザンター: メニュー Tools > プリザンターのメタ情報を更新。base.OnStartup の後に呼ぶ
        PleasanterDesignerInitializer.Setup(DesignerEnvironment);
    }
}
