namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>ブラウザのプリザンターのセッションの確認の結果の種類。</summary>
    public enum PleasanterSessionStatus
    {
        /// <summary>プリザンターで認証済み。<see cref="PleasanterSessionResult.Identity"/> に本人が入る。</summary>
        Authenticated,
        /// <summary>プリザンターで認証されていない (転送する Cookie が無い・期限切れ・ログアウト済み・本人が解決できない)。</summary>
        Unauthenticated,
        /// <summary>確かめられなかった (プリザンターにつながらない・応答が想定の形でない)。認証済みとして扱ってはいけない。</summary>
        Failed,
    }

    /// <summary>プリザンターが解決した本人 (ブラウザからの申告ではない)。</summary>
    public sealed class PleasanterIdentity
    {
        public int TenantId { get; init; }
        public int UserId { get; init; }
        public string LoginId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    /// <summary><see cref="PleasanterConnection.VerifySessionAsync"/> の結果。Cookie の値・API キーは入らない。</summary>
    public sealed class PleasanterSessionResult
    {
        PleasanterSessionResult(PleasanterSessionStatus status, PleasanterIdentity? identity, string message)
        {
            Status = status;
            Identity = identity;
            Message = message;
        }

        public PleasanterSessionStatus Status { get; }

        public bool IsAuthenticated => Status == PleasanterSessionStatus.Authenticated;

        /// <summary>本人 (認証済みのときだけ)。</summary>
        public PleasanterIdentity? Identity { get; }

        /// <summary>認証済みでない理由 (ログ用。利用者に見せる文言ではない)。認証済みなら空。</summary>
        public string Message { get; }

        internal static PleasanterSessionResult Authenticated(PleasanterIdentity identity) => new(PleasanterSessionStatus.Authenticated, identity, string.Empty);
        internal static PleasanterSessionResult Unauthenticated(string message) => new(PleasanterSessionStatus.Unauthenticated, null, message);
        internal static PleasanterSessionResult Failed(string message) => new(PleasanterSessionStatus.Failed, null, message);
    }
}
