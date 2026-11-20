using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter
{
    /// <summary>
    /// 添付のフィールドのデータ。Value はプリザンターの添付の列 1 つ分と同じ形の JSON の配列の文字。
    /// 読んだ値は [{"Guid","Name","Size"}...]。保存で送る値には、足すファイル {"Name","ContentType","Base64"} と消すファイル {"Guid","Deleted":true} が入る。
    /// </summary>
    public class PleasanterAttachmentsFieldData() : ValueFieldDataBase<string>(typeof(PleasanterAttachmentsFieldData).FullName!), ICloneable<PleasanterAttachmentsFieldData>
    {
        public PleasanterAttachmentsFieldData Clone() => (PleasanterAttachmentsFieldData)MemberwiseClone();
    }
}
