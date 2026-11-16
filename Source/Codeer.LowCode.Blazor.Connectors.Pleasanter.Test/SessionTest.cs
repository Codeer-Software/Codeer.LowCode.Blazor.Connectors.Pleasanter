using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using System.Net;
using System.Text;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>ブラウザのセッションの確認 (VerifySessionAsync) を偽物のプリザンターで確かめる。</summary>
    public class SessionTest
    {
        FakeHarness _h = null!;

        [SetUp]
        public void SetUp() => _h = new FakeHarness();

        static readonly KeyValuePair<string, string>[] SessionCookies =
        {
            new(".AspNetCore.Cookies", "auth-value"),
            new("Pleasanter_SessionGuid", "guid-value"),
            new("OtherApp", "other-value"),
        };

        const string WhoAmI = "{\"StatusCode\":200,\"Response\":{\"Data\":{\"Table\":[{\"TenantId\":1,\"UserId\":5,\"LoginId\":\"taro\",\"Name\":\"太郎\"}]}}}";

        void Respond(Func<HttpResponseMessage> response) => _h.Fake.Override(e => e.Path == "/api/extended/sql", _ => response());

        [Test]
        public async Task 拡張SQLが返した本人で認証済み()
        {
            Respond(() => FakePleasanter.Json(HttpStatusCode.OK, WhoAmI));

            var result = await _h.Connection.VerifySessionAsync(SessionCookies);

            Assert.That(result.Status, Is.EqualTo(PleasanterSessionStatus.Authenticated));
            Assert.That((result.Identity!.TenantId, result.Identity.UserId, result.Identity.LoginId, result.Identity.Name), Is.EqualTo((1, 5, "taro", "太郎")));
            var request = _h.Fake.Requests.Single();
            Assert.That(request.Url, Is.EqualTo(FakePleasanter.Url + "api/extended/sql"));
            Assert.That(request.Json["Name"]!.GetValue<string>(), Is.EqualTo(PleasanterConnection.DefaultWhoAmISqlName));
            Assert.That(request.Json.ContainsKey("ApiKey"), Is.False, "API キーは送らない");
            Assert.That(request.Header("Cookie"), Is.EqualTo(".AspNetCore.Cookies=auth-value; Pleasanter_SessionGuid=guid-value"));
        }

        [Test]
        public async Task 名前を指定した拡張SQLとCookie()
        {
            Respond(() => FakePleasanter.Json(HttpStatusCode.OK, WhoAmI));
            await _h.Connection.VerifySessionAsync(SessionCookies, new[] { "Other" }, "MyWhoAmI");
            var request = _h.Fake.Requests.Single();
            Assert.That(request.Json["Name"]!.GetValue<string>(), Is.EqualTo("MyWhoAmI"));
            Assert.That(request.Header("Cookie"), Is.EqualTo("OtherApp=other-value"));
        }

        [Test]
        public async Task 転送するCookieが無ければ問い合わせない()
        {
            var result = await _h.Connection.VerifySessionAsync(new[] { new KeyValuePair<string, string>("OtherApp", "x") });
            Assert.That(result.Status, Is.EqualTo(PleasanterSessionStatus.Unauthenticated));
            Assert.That(_h.Fake.Requests, Is.Empty);
        }

        [Test]
        public async Task ヘッダを壊すCookieは送らない()
        {
            Respond(() => FakePleasanter.Json(HttpStatusCode.OK, WhoAmI));
            await _h.Connection.VerifySessionAsync(new[]
            {
                new KeyValuePair<string, string>(".AspNetCore.Cookies", "a; Injected=1"),
                new KeyValuePair<string, string>(".AspNetCore.CookiesC1", "line\r\nX: y"),
                new KeyValuePair<string, string>(".AspNetCore.Session=", "x"),
                new KeyValuePair<string, string>(".AspNetCore.CookiesC2", "ok"),
            });
            Assert.That(_h.Fake.Requests.Single().Header("Cookie"), Is.EqualTo(".AspNetCore.CookiesC2=ok"));
        }

        static IEnumerable<TestCaseData> Responses()
        {
            yield return new TestCaseData(HttpStatusCode.Unauthorized, "{}", "application/json", PleasanterSessionStatus.Unauthenticated);
            yield return new TestCaseData(HttpStatusCode.OK, "{\"StatusCode\":401,\"Message\":\"Unauthorized\"}", "application/json", PleasanterSessionStatus.Unauthenticated);
            yield return new TestCaseData(HttpStatusCode.OK, "{\"StatusCode\":200,\"Response\":{\"Data\":{\"Table\":[{\"TenantId\":1,\"UserId\":0}]}}}", "application/json", PleasanterSessionStatus.Unauthenticated);
            yield return new TestCaseData(HttpStatusCode.Found, "", "text/html", PleasanterSessionStatus.Failed);
            yield return new TestCaseData(HttpStatusCode.OK, "<html>login</html>", "text/html", PleasanterSessionStatus.Failed);
            yield return new TestCaseData(HttpStatusCode.InternalServerError, "{}", "application/json", PleasanterSessionStatus.Failed);
            yield return new TestCaseData(HttpStatusCode.OK, "{\"StatusCode\":500,\"Message\":\"x\"}", "application/json", PleasanterSessionStatus.Failed);
            yield return new TestCaseData(HttpStatusCode.OK, "{\"Message\":\"x\"}", "application/json", PleasanterSessionStatus.Failed);
            yield return new TestCaseData(HttpStatusCode.OK, "{\"StatusCode\":200,\"Response\":{\"Data\":{\"Table\":[]}}}", "application/json", PleasanterSessionStatus.Failed);
            yield return new TestCaseData(HttpStatusCode.OK, "{\"StatusCode\":200,\"Response\":{\"Data\":{\"Table\":[{\"LoginId\":\"x\"}]}}}", "application/json", PleasanterSessionStatus.Failed);
            yield return new TestCaseData(HttpStatusCode.OK, "not json", "application/json", PleasanterSessionStatus.Failed);
        }

        [TestCaseSource(nameof(Responses))]
        public async Task 応答の判定(HttpStatusCode code, string body, string mediaType, PleasanterSessionStatus expected)
        {
            Respond(() => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
            var result = await _h.Connection.VerifySessionAsync(SessionCookies);
            Assert.That(result.Status, Is.EqualTo(expected), result.Message);
            Assert.That(result.Identity, Is.Null);
        }

        [Test]
        public async Task 通信の失敗は例外にせず失敗で返しホスト名を出さない()
        {
            _h.Fake.Throw(_ => true, () => new HttpRequestException("No such host: pleasanter.example.com"));
            var result = await _h.Connection.VerifySessionAsync(SessionCookies);
            Assert.That(result.Status, Is.EqualTo(PleasanterSessionStatus.Failed));
            Assert.That(result.Message, Does.Not.Contain("example.com"));
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(1), "やり直さない");
        }
    }
}
