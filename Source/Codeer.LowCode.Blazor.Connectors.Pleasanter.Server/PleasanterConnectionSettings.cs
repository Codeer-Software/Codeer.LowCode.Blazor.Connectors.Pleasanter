using Codeer.LowCode.Blazor.SystemSettings;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>
    /// プリザンターへの接続 1 つ分の設定。データソース (DataSources) の ConnectionString に
    /// "Provider=Pleasanter;Url=https://example.com/pleasanter/;ApiKey=...;Sites=Projects:1200,Customers:1300" の形で書く。
    /// </summary>
    public class PleasanterConnectionSettings
    {
        /// <summary>ConnectionString の Provider の値。これがあるデータソースをプリザンターとして扱う。</summary>
        public const string ProviderName = "Pleasanter";

        /// <summary>空の日付の既定 (プリザンターが空の日付として返す 1899-12-30)。</summary>
        public static readonly DateTime DefaultEmptyDate = new(1899, 12, 30);

        static readonly Regex AliasPattern = new("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);

        /// <summary>データソース名 (モジュールの DataSourceName)。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>プリザンターのアプリのルートの URL (仮想ディレクトリまで。例: https://example.com/pleasanter/)。</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>連携用の利用者の API キー (接続文字列ごと秘密として扱う)。</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>表名 (モジュールの DbTable に書く別名) → サイト ID。ここに無いサイトは触らない。</summary>
        public Dictionary<string, long> Sites { get; set; } = new(StringComparer.Ordinal);

        /// <summary>ページングなしの読み出し・メモリで評価する読み出しで受け取る行の上限。超えたらエラーにする (黙って切り捨てない)。</summary>
        public int MaxRows { get; set; } = 10000;

        /// <summary>1 回の要求のタイムアウト (秒)。</summary>
        public int TimeoutSeconds { get; set; } = 100;

        /// <summary>プリザンターの「空の日付」の値 (API が空の日付として返す値。General.json の MinTime ではない)。読むときはこの値を null に、空にするときはこの値を書く。</summary>
        public DateTime EmptyDate { get; set; } = DefaultEmptyDate;

        /// <summary>
        /// データソースがプリザンター用 (ConnectionString が Provider=Pleasanter) なら設定を読む。違えば null。
        /// Sites・MaxRows・TimeoutSeconds・EmptyDate の書き方が誤っていれば PleasanterException (設定の誤りを黙って既定値にしない)。
        /// </summary>
        public static PleasanterConnectionSettings? FromDataSource(DataSource dataSource)
        {
            DbConnectionStringBuilder builder;
            try
            {
                builder = new DbConnectionStringBuilder { ConnectionString = dataSource.ConnectionString };
            }
            catch (ArgumentException)
            {
                return null;
            }
            if (!builder.TryGetValue("Provider", out var provider) ||
                !string.Equals(provider?.ToString(), ProviderName, StringComparison.OrdinalIgnoreCase)) return null;

            string Get(string key) => builder.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
            int GetInt(string key, int defaultValue)
            {
                var text = Get(key);
                if (string.IsNullOrWhiteSpace(text)) return defaultValue;
                if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && 0 < v) return v;
                throw new PleasanterException($"Pleasanter data source '{dataSource.Name}': {key} must be a positive integer.");
            }

            var defaults = new PleasanterConnectionSettings();
            var emptyDateText = Get(nameof(EmptyDate));
            var emptyDate = defaults.EmptyDate;
            if (!string.IsNullOrWhiteSpace(emptyDateText) &&
                !DateTime.TryParseExact(emptyDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out emptyDate))
                throw new PleasanterException($"Pleasanter data source '{dataSource.Name}': {nameof(EmptyDate)} must be a date (yyyy-MM-dd).");

            return new PleasanterConnectionSettings
            {
                Name = dataSource.Name,
                Url = NormalizeUrl(Get(nameof(Url))),
                ApiKey = Get(nameof(ApiKey)),
                Sites = ParseSites(dataSource.Name, Get(nameof(Sites))),
                MaxRows = GetInt(nameof(MaxRows), defaults.MaxRows),
                TimeoutSeconds = GetInt(nameof(TimeoutSeconds), defaults.TimeoutSeconds),
                EmptyDate = emptyDate,
            };
        }

        static string NormalizeUrl(string url)
        {
            url = url.Trim();
            return url.Length == 0 || url.EndsWith('/') ? url : url + "/";
        }

        /// <summary>"表名:サイトID,表名:サイトID" を読む。別名は英数字と _、ID は正の整数。重複はエラー。</summary>
        internal static Dictionary<string, long> ParseSites(string dataSourceName, string text)
        {
            var sites = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pair = part.Split(':');
                if (pair.Length != 2) throw InvalidSites(dataSourceName, part);
                var alias = pair[0].Trim();
                if (!AliasPattern.IsMatch(alias)) throw InvalidSites(dataSourceName, part);
                if (!long.TryParse(pair[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var siteId) || siteId <= 0) throw InvalidSites(dataSourceName, part);
                if (sites.ContainsKey(alias))
                    throw new PleasanterException($"Pleasanter data source '{dataSourceName}': the table name '{alias}' appears more than once in Sites.");
                if (sites.ContainsValue(siteId))
                    throw new PleasanterException($"Pleasanter data source '{dataSourceName}': the site id {siteId.ToString(CultureInfo.InvariantCulture)} appears more than once in Sites.");
                sites[alias] = siteId;
            }
            return sites;
        }

        static PleasanterException InvalidSites(string dataSourceName, string part)
            => new($"Pleasanter data source '{dataSourceName}': '{part}' in Sites is invalid. Write 'TableName:SiteId' (letters, digits and _ for the name, a positive integer for the id) separated by commas.");

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Url)) throw new PleasanterException($"Pleasanter data source '{Name}': {nameof(Url)} is required.");
            if (string.IsNullOrWhiteSpace(ApiKey)) throw new PleasanterException($"Pleasanter data source '{Name}': {nameof(ApiKey)} is required.");
            //https だけ。このマシン自身 (localhost / 127.0.0.1) に限り http を許す (API キーを平文で流さない)
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
                !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && (uri.Host == "localhost" || uri.Host == "127.0.0.1"))))
                throw new PleasanterException($"Pleasanter data source '{Name}': {nameof(Url)} must be an https URL.");
            if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new PleasanterException($"Pleasanter data source '{Name}': {nameof(Url)} must not have a query, a fragment or a user name.");
            foreach (var (alias, siteId) in Sites)
            {
                if (!AliasPattern.IsMatch(alias) || siteId <= 0) throw InvalidSites(Name, $"{alias}:{siteId.ToString(CultureInfo.InvariantCulture)}");
            }
            if (MaxRows <= 0 || TimeoutSeconds <= 0) throw new PleasanterException($"Pleasanter data source '{Name}': {nameof(MaxRows)} and {nameof(TimeoutSeconds)} must be positive.");
        }

        /// <summary>サイト ID の表名 (Sites にある別名)。無ければ null。</summary>
        internal string? FindTableName(long siteId) => Sites.FirstOrDefault(e => e.Value == siteId).Key;

        //同じ接続先・同じ API キー・同じ表なら同じ接続 (定義のキャッシュ) を使う。API キーは変えたら別の接続になるように含める
        internal string GetIdentityKey()
            => string.Join("\n", Name, Url, ApiKey, string.Join(",", Sites.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + ":" + e.Value.ToString(CultureInfo.InvariantCulture))),
                MaxRows, TimeoutSeconds, EmptyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
