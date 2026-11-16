//ログイン中のユーザーの表示名を出す (ユーザーモジュール AppUser の 表示名)
void DetailLayoutDesign_OnAfterInitialization()
{
    CurrentUserName.Text = "ログイン中: " + CurrentUser.表示名.Value;
}
