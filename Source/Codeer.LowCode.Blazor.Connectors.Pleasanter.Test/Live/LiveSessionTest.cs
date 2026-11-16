using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Live
{
    /// <summary>実物のプリザンターでのブラウザのセッションの確認 (ログインしたセッションでの成功は自動のテストでは確かめない)。</summary>
    public class LiveSessionTest
    {
        [Test]
        public async Task ログインしていないCookieは認証済みにならない()
        {
            var connection = new PleasanterConnection(LiveFixture.CreateSettings());
            var result = await connection.VerifySessionAsync(new[] { new KeyValuePair<string, string>(".AspNetCore.Cookies", "not-a-session") });
            Assert.That(result.Status, Is.Not.EqualTo(PleasanterSessionStatus.Authenticated), result.Message);
            Assert.That(result.Identity, Is.Null);
            TestContext.Progress.WriteLine("VerifySession without a session: " + result.Status + " / " + result.Message);
        }
    }
}
