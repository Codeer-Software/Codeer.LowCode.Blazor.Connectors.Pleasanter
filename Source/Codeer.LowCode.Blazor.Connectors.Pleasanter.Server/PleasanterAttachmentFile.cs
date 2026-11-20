namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>プリザンターの添付 1 つ (<see cref="PleasanterDbAccessor.GetAttachmentAsync"/> の結果)。</summary>
    public sealed class PleasanterAttachmentFile
    {
        internal PleasanterAttachmentFile(string fileName, byte[] content)
        {
            FileName = fileName;
            Content = content;
        }

        /// <summary>ファイル名 (レコードの添付の一覧の名前)。応答のヘッダに入れるときはホストが安全な形にする。</summary>
        public string FileName { get; }

        /// <summary>中身。</summary>
        public byte[] Content { get; }
    }
}
