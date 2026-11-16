using System.Net;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>プリザンターとの連携の失敗の種類。</summary>
    public enum PleasanterErrorKind
    {
        /// <summary>設定の誤り・このコネクターが扱えない操作。</summary>
        NotSupported,
        /// <summary>認証に失敗した (API キーが違う・無効。401)。</summary>
        Authentication,
        /// <summary>権限が無い (403)。プリザンターは無いレコードの更新・削除にも 403 を返す。</summary>
        Forbidden,
        /// <summary>対象が無い (404)。</summary>
        NotFound,
        /// <summary>要求の誤り・プリザンターの検証のエラー (400 など)。</summary>
        InvalidRequest,
        /// <summary>プリザンター側・通信の障害 (429・5xx・つながらない)。</summary>
        ServiceUnavailable,
        /// <summary>書き込みを送ったあと結果を受け取れなかった。プリザンター側で処理されたかどうかは分からない。</summary>
        WriteResultUnknown,
        /// <summary>上のどれにも当たらない応答。</summary>
        Unknown,
    }

    /// <summary>プリザンターとの連携の失敗。メッセージに API キーは入らない。</summary>
    public class PleasanterException : Exception
    {
        public PleasanterErrorKind Kind { get; }

        /// <summary>HTTP ステータス (応答を受け取っていれば)。</summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>応答の JSON の StatusCode (読めたとき)。</summary>
        public int? ApiStatusCode { get; }

        public PleasanterException(string message, PleasanterErrorKind kind = PleasanterErrorKind.NotSupported, Exception? innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
        }

        public PleasanterException(string message, PleasanterErrorKind kind, HttpStatusCode? statusCode, int? apiStatusCode, Exception? innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
            StatusCode = statusCode;
            ApiStatusCode = apiStatusCode;
        }
    }
}
