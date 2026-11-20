using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Utils;
using System.Reflection;
using Module = Codeer.LowCode.Blazor.OperatingModel.Module;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness
{
    /// <summary>
    /// 画面のフィールドを本物の Module に載せて確かめるための最小のハーネス (本体の公開の口だけで作る)。
    /// Module は ModuleCreationService.CreateModuleAsync で作り、保存は IModuleDataService の偽物が受ける。
    /// </summary>
    sealed class FieldHarness : IAppInfoService, IModuleDataService
    {
        readonly DesignData _designData;
        readonly ScriptRuntimeTypeManager _scriptRuntimeTypeManager = new();

        public Services Services { get; }

        /// <summary>保存で送られたデータ (送った順)。</summary>
        public List<List<ModuleSubmitData>> Submitted { get; } = new();

        /// <summary>保存の要求を受けてから応答を返すまでの間に呼ぶ (送信の間の画面の操作のため)。</summary>
        public Func<Task>? DuringSubmit { get; set; }

        public FieldHarness(DesignData designData)
        {
            _designData = designData;
            Services = new Services(new NullServiceProvider(), this, this, NullProxy<INavigationService>.Create(), NullProxy<IUIService>.Create(), NullProxy<ILogger>.Create());
        }

        public async Task<Module> CreateModuleAsync(ModuleData data, ModuleLayoutType layoutType = ModuleLayoutType.Detail)
            => await ModuleCreationService.CreateModuleAsync(Services, data, layoutType);

        /// <summary>
        /// 自動保存 (AutoSubmitField) で保存し、終わるまで待つ。読み直さない軽い保存 = 本体は送った値でフィールドの AcceptChanges を呼ぶ。
        /// </summary>
        public static async Task AutoSubmitAsync(AutoSubmitField autoSubmit)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            //状態の変化: 送信中 → 結果 (成功・失敗・変更なし)。2 回目で保存が終わっている
            autoSubmit.SetStateChangedReceiver(() =>
            {
                if (Interlocked.Increment(ref count) == 2) done.TrySetResult();
            });
            autoSubmit.ScheduleSubmit();
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        //IAppInfoService
        public string CurrentUserId => string.Empty;
        public ModuleData? CurrentUserData => null;
        public ScriptRuntimeTypeManager GetScriptRuntimeTypeManager() => _scriptRuntimeTypeManager;
        public Task<MemoryStream?> GetResourceAsync(string resourcePath) => Task.FromResult<MemoryStream?>(null);
        public DesignData GetDesignData() => _designData;

        //IModuleDataService
        public async Task<List<ModuleSubmitResult>?> SubmitAsync(List<ModuleSubmitData> data)
        {
            //送られた値はこの後の画面の操作で変わらないよう、ここで複製しておく
            Submitted.Add(data.Select(e => JsonConverterEx.DeserializeObject<ModuleSubmitData>(JsonConverterEx.SerializeObject(e))!).ToList());
            if (DuringSubmit != null) await DuringSubmit();
            return data.Select(e => new ModuleSubmitResult { DestinationId = (e.Update.FirstOrDefault()?.Fields.GetValueOrDefault("Id") as IdFieldData)?.Value ?? string.Empty }).ToList();
        }

        public Task<List<Paging<ModuleData>>> GetListAsync(List<GetListRequest> requests)
            => Task.FromResult(requests.Select(_ => new Paging<ModuleData>()).ToList());
        public Task<List<AggregateResult>> AggregateAsync(List<AggregateCondition> conditions)
            => Task.FromResult(conditions.Select(_ => new AggregateResult()).ToList());
        public Task<Codeer.LowCode.Blazor.DataIO.FileInfo?> UploadFile(string moduleName, string fieldName, string fileName, StreamContent content)
            => throw new NotSupportedException();
        public Task<MemoryStream?> DownloadFile(string moduleName, string fieldName, string id)
            => throw new NotSupportedException();
        public Task<MemoryStream?> GetListFileAsync(SearchCondition condition)
            => throw new NotSupportedException();
        public Task<List<ModuleSubmitResult>?> SubmitByFileAsync(string moduleName, StreamContent content)
            => throw new NotSupportedException();

        sealed class NullServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }
    }

    /// <summary>テストが使わないサービス: 何もしない (Task は完了済み・値は既定値)。</summary>
    public class NullProxy<T> : DispatchProxy where T : class
    {
        public static T Create() => DispatchProxy.Create<T, NullProxy<T>>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var type = targetMethod?.ReturnType;
            if (type == null || type == typeof(void)) return null;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var valueType = type.GetGenericArguments()[0];
                var value = valueType == typeof(string) ? string.Empty : valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(valueType).Invoke(null, [value]);
            }
            if (type == typeof(string)) return string.Empty;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
