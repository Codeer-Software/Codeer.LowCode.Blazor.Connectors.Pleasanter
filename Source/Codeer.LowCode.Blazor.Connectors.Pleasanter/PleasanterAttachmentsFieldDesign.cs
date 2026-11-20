using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter
{
    /// <summary>
    /// プリザンターの添付の列 (AttachmentsA など) のファイルの一覧・ダウンロード・追加・削除。
    /// DbColumn はプリザンターのデータソースのモジュールの添付の列。中身はホストのダウンロードの口 (<see cref="DownloadUrl"/>) から読む。
    /// </summary>
    [Designer(DisplayName = "プリザンターの添付")]
    [ToolboxIcon(PackIconMaterialKind = "Paperclip")]
    public class PleasanterAttachmentsFieldDesign() : ValueFieldDesignBase(typeof(PleasanterAttachmentsFieldDesign).FullName!), IConfirmCopyFieldDesign
    {
        /// <summary>レコードのコピーでは写さない (ファイルは元のレコードのもので、新しいレコードには無い)。</summary>
        bool IConfirmCopyFieldDesign.NeedCopy => false;

        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定 (追加は末尾・欠番は再利用しない)。</summary>
        const int CodeMaxFileSizeMustBePositive = 1;

        /// <summary>
        /// ダウンロードの URL (アプリのルートからの相対)。クエリ module・id・field・guid を付けて GET する。
        /// ホストはこのパスに、.Server の PleasanterDbAccessor.GetAttachmentAsync を呼ぶ口を置く。
        /// </summary>
        public const string DownloadUrl = "api/pleasanter/attachments/download";

        [Designer(Index = 0, CandidateType = CandidateType.DbColumn, DisplayName = "$DbColumn"), DbColumn(nameof(PleasanterAttachmentsFieldData.Value))]
        public string DbColumn { get; set; } = string.Empty;

        /// <summary>1 ファイルの大きさの上限 (MB)。超えたファイルは足せない (入力エラー)。</summary>
        [Designer(Index = 1, DisplayName = "1 ファイルの上限 (MB)")]
        public int MaxFileSizeMB { get; set; } = 10;

        public override string GetWebComponentTypeFullName() => typeof(PleasanterAttachmentsFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new PleasanterAttachmentsField(this);

        public override FieldDataBase? CreateData() => new PleasanterAttachmentsFieldData();

        internal long MaxFileSizeBytes => (long)MaxFileSizeMB * 1024 * 1024;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumn), DbColumn).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged,
                context.GetScriptMethodAttribute(GetType(), nameof(OnDataChanged))).AddTo(result);
            if (MaxFileSizeMB <= 0)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(PleasanterAttachmentsFieldDesign), CodeMaxFileSizeMustBePositive),
                    Location = new() { Module = context.OwnerModule, Field = Name, Member = nameof(MaxFileSizeMB) },
                    Message = "MaxFileSizeMB must be 1 or more.",
                });
            }
            return result;
        }
    }
}
