namespace LowCodeApp.Server.Sso
{
    /// <summary>
    /// プリザンターのログインを使うシングルサインオン (appsettings の PleasanterSso)。秘密の値は持たない。
    /// ブラウザが持つプリザンターの認証 Cookie を、サーバーからプリザンターの拡張 SQL (ClbWhoAmI) に転送して本人を確かめ、CLB の認証 Cookie を発行する。
    /// </summary>
    public class PleasanterSsoSettings
    {
        /// <summary>使うか。false ならテンプレートどおりの ID / パスワードのログイン画面。</summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 本人を問い合わせるプリザンターのデータソースの名前 (DataSources の Name)。問い合わせ先はその接続文字列の Url。
        /// 空なら最初のプリザンターのデータソース (プリザンターのデータソースが 1 つなら空でよい)。
        /// </summary>
        public string DataSourceName { get; set; } = string.Empty;

        /// <summary>ブラウザで開くプリザンターのログイン画面の URL (例: https://pleasanter.example.com/users/login)。ログイン待ち画面 (sso-login.html) が開く。</summary>
        public string LoginUrl { get; set; } = string.Empty;

        /// <summary>ブラウザで開くプリザンターのログアウトの URL (例: https://pleasanter.example.com/users/logout)。空ならログイン待ち画面に「プリザンターからもログアウト」を出さない。</summary>
        public string LogoutUrl { get; set; } = string.Empty;

        /// <summary>
        /// プリザンターで本人が確かめられたのにユーザーモジュールに行が無いとき、プリザンターのログイン ID をログイン名にして行を作るか。
        /// false なら事前に登録したユーザーだけが入れる (ユーザーモジュールの外部ログイン名の列にプリザンターのログイン ID を入れておく)。
        /// </summary>
        public bool AutoProvisionUser { get; set; }

        /// <summary>CLB のセッションをプリザンターに確かめ直す間隔 (秒)。過ぎたら次の API 呼び出しでプリザンターに問い合わせ、ログアウト・失効を反映する。</summary>
        public int RevalidateSeconds { get; set; } = 60;

        /// <summary>プリザンターへの問い合わせのタイムアウト (秒)。</summary>
        public int TimeoutSeconds { get; set; } = 5;

        public bool IsEnabled => Enabled;
    }
}
