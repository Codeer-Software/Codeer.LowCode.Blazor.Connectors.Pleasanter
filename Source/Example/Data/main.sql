-- データソース Main (SQLite) の表: ログインするユーザーと一時ファイル。プリザンターのデータはここに置かない
-- 初期ユーザー: admin / admin
-- プリザンターの SSO で入るユーザーは、email の列 (外部ログイン名) にプリザンターのログイン ID を入れる
CREATE TABLE app_users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_name TEXT NOT NULL UNIQUE,
    name TEXT,
    email TEXT,
    hash TEXT NOT NULL,
    salt TEXT NOT NULL,
    session_stamp TEXT
);

CREATE TABLE temporary_files (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    guid TEXT NOT NULL,
    created_date_time TEXT NULL
);

INSERT INTO app_users (id, user_name, name, email, hash, salt) VALUES
(1, 'admin', '管理者', NULL, 'pUNVPHEWYngAim7aUFjf6kWtIROxrpzJPprcipyPujw=', 'CPYn8K+ecURogSHP5KTZ04lEbiAjZoq9wvwk5aIAQQg=');
