using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.Connectors.Pleasanter.Test.Harness;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>送信の作法 (本文・成功の判定・エラーの分類・やり直し・書き込みの結果不明) を偽物のプリザンターで確かめる。</summary>
    public class ConnectionTest
    {
        FakeHarness _h = null!;

        [SetUp]
        public void SetUp() => _h = new FakeHarness();

        string ReadUrl => FakePleasanter.Url + "api/items/11/get";
        string WriteUrl => FakePleasanter.Url + "api/items/11/update";

        async Task ReadAsync() => (await _h.Connection.SendAsync(ReadUrl, new JsonObject(), true)).Dispose();
        async Task WriteAsync() => (await _h.Connection.SendAsync(WriteUrl, new JsonObject(), false)).Dispose();

        static HttpResponseMessage Text(HttpStatusCode code, string text, string mediaType = "text/html")
            => new(code) { Content = new StringContent(text, Encoding.UTF8, mediaType) };

        [Test]
        public async Task 本文にApiVersionとApiKeyを入れてPOSTする()
        {
            await ReadAsync();
            var request = _h.Fake.Requests.Single();
            Assert.That(request.Json["ApiVersion"]!.GetValue<decimal>(), Is.EqualTo(1.1m));
            Assert.That(request.Json["ApiKey"]!.GetValue<string>(), Is.EqualTo(FakePleasanter.ApiKey));
            Assert.That(request.Header("Content-Type"), Does.StartWith("application/json"));
            Assert.That(request.Url, Is.EqualTo(ReadUrl));
        }

        [TestCase(HttpStatusCode.Unauthorized, PleasanterErrorKind.Authentication)]
        [TestCase(HttpStatusCode.Forbidden, PleasanterErrorKind.Forbidden)]
        [TestCase(HttpStatusCode.NotFound, PleasanterErrorKind.NotFound)]
        [TestCase(HttpStatusCode.BadRequest, PleasanterErrorKind.InvalidRequest)]
        [TestCase(HttpStatusCode.TooManyRequests, PleasanterErrorKind.ServiceUnavailable)]
        [TestCase(HttpStatusCode.InternalServerError, PleasanterErrorKind.ServiceUnavailable)]
        public void HTTPの状態でエラーを分類する(HttpStatusCode code, PleasanterErrorKind kind)
        {
            _h.Fake.Override(_ => true, _ => FakePleasanter.Error(code, "message from pleasanter"));
            var e = Assert.ThrowsAsync<PleasanterException>(ReadAsync)!;
            Assert.That(e.Kind, Is.EqualTo(kind));
            Assert.That(e.StatusCode, Is.EqualTo(code));
            Assert.That(e.Message, Does.Contain("message from pleasanter"));
        }

        [Test]
        public void HTTPが200でもJSONのStatusCodeが200でなければその値で分類する()
        {
            _h.Fake.Override(_ => true, _ => FakePleasanter.Json(HttpStatusCode.OK, "{\"Id\":0,\"StatusCode\":403,\"Message\":\"no\"}"));
            var e = Assert.ThrowsAsync<PleasanterException>(ReadAsync)!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.Forbidden));
            Assert.That(e.ApiStatusCode, Is.EqualTo(403));
        }

        [Test]
        public void 読み出しでJSONでない成功の応答はエラー()
        {
            _h.Fake.Override(_ => true, _ => Text(HttpStatusCode.OK, "<html>login</html>"));
            var e = Assert.ThrowsAsync<PleasanterException>(ReadAsync)!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.Unknown));
        }

        [Test]
        public void リダイレクトには従わない()
        {
            _h.Fake.Override(_ => true, _ =>
            {
                var response = Text(HttpStatusCode.Found, string.Empty);
                response.Headers.Location = new Uri("https://other.example.com/login");
                return response;
            });
            var e = Assert.ThrowsAsync<PleasanterException>(ReadAsync)!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.InvalidRequest));
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(HttpStatusCode.BadGateway)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.GatewayTimeout)]
        public async Task 読み出しは一時的なエラーを3回までやり直す(HttpStatusCode code)
        {
            _h.Fake.Override(_ => true, _ => FakePleasanter.Error(code, "busy"), 3);
            await ReadAsync();
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(4));
            Assert.That(_h.Delays, Has.Count.EqualTo(3));
            Assert.That(_h.Delays[0], Is.LessThan(_h.Delays[2]));
        }

        [Test]
        public void 読み出しのやり直しは4回で諦める()
        {
            _h.Fake.Override(_ => true, _ => FakePleasanter.Error(HttpStatusCode.ServiceUnavailable, "busy"), 10);
            var e = Assert.ThrowsAsync<PleasanterException>(ReadAsync)!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.ServiceUnavailable));
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(4));
        }

        [Test]
        public async Task 読み出しは通信の失敗もやり直す()
        {
            _h.Fake.Throw(_ => true, () => new HttpRequestException("connection refused by pleasanter.example.com"), 2);
            await ReadAsync();
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(3));
        }

        [Test]
        public void 通信の失敗のメッセージにホスト名を出さない()
        {
            _h.Fake.Throw(_ => true, () => new HttpRequestException("connection refused by pleasanter.example.com"), 10);
            var e = Assert.ThrowsAsync<PleasanterException>(ReadAsync)!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.ServiceUnavailable));
            Assert.That(e.Message, Does.Not.Contain("example.com"));
        }

        [Test]
        public void 回数の上限429はやり直さない()
        {
            _h.Fake.Override(_ => true, _ => FakePleasanter.Error(HttpStatusCode.TooManyRequests, "limit"), 10);
            Assert.ThrowsAsync<PleasanterException>(ReadAsync);
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void 書き込みはやり直さず通信の失敗は結果不明()
        {
            _h.Fake.Throw(_ => true, () => new TaskCanceledException("timeout"), 10);
            var e = Assert.ThrowsAsync<PleasanterException>(WriteAsync)!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.WriteResultUnknown));
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void 書き込みのリダイレクトは結果不明()
        {
            _h.Fake.Override(_ => true, _ =>
            {
                var response = Text(HttpStatusCode.Found, string.Empty);
                response.Headers.Location = new Uri("/errors", UriKind.Relative);
                return response;
            }, 10);
            var e = Assert.ThrowsAsync<PleasanterException>(WriteAsync)!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.WriteResultUnknown));
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.GatewayTimeout)]
        public void 書き込みの5xxは結果不明(HttpStatusCode code)
        {
            _h.Fake.Override(_ => true, _ => FakePleasanter.Error(code, "error"), 10);
            var e = Assert.ThrowsAsync<PleasanterException>(WriteAsync)!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.WriteResultUnknown));
            Assert.That(_h.Fake.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void 書き込みでJSONでない成功の応答は結果不明()
        {
            _h.Fake.Override(_ => true, _ => Text(HttpStatusCode.OK, "<html></html>"));
            Assert.That(Assert.ThrowsAsync<PleasanterException>(WriteAsync)!.Kind, Is.EqualTo(PleasanterErrorKind.WriteResultUnknown));
        }

        [TestCase(HttpStatusCode.BadRequest, PleasanterErrorKind.InvalidRequest)]
        [TestCase(HttpStatusCode.Forbidden, PleasanterErrorKind.Forbidden)]
        [TestCase(HttpStatusCode.TooManyRequests, PleasanterErrorKind.ServiceUnavailable)]
        public void 書き込みの4xxは処理されなかったエラー(HttpStatusCode code, PleasanterErrorKind kind)
        {
            _h.Fake.Override(_ => true, _ => FakePleasanter.Error(code, "rejected"));
            Assert.That(Assert.ThrowsAsync<PleasanterException>(WriteAsync)!.Kind, Is.EqualTo(kind));
        }

        [Test]
        public void 書き込みの4xxはJSONでなくても処理されなかったエラー()
        {
            _h.Fake.Override(_ => true, _ => Text(HttpStatusCode.BadRequest, "Bad Request"));
            Assert.That(Assert.ThrowsAsync<PleasanterException>(WriteAsync)!.Kind, Is.EqualTo(PleasanterErrorKind.InvalidRequest));
        }

        [Test]
        public void 作成でIdが返らなければ結果不明()
        {
            _h.Fake.Override(e => e.Action == "create", _ => FakePleasanter.Json(HttpStatusCode.OK, "{\"StatusCode\":200,\"Message\":\"ok\"}"));
            var definition = new PleasanterTableDefinition { Name = "Customers", SiteId = 11 };
            var e = Assert.ThrowsAsync<PleasanterException>(() => _h.Connection.CreateAsync(definition, new JsonObject()))!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.WriteResultUnknown));
        }

        [Test]
        public void エラーのメッセージにAPIキーを出さない()
        {
            _h.Fake.Override(_ => true, e => FakePleasanter.Error(HttpStatusCode.BadRequest, "bad"));
            var e = Assert.ThrowsAsync<PleasanterException>(ReadAsync)!;
            Assert.That(e.ToString(), Does.Not.Contain(FakePleasanter.ApiKey));
        }

        [Test]
        public void 表の定義が読めないときの分類()
        {
            _h.Fake.Override(e => e.Action == "getsite", _ => FakePleasanter.Error(HttpStatusCode.Unauthorized, "Unauthorized"));
            var e = Assert.ThrowsAsync<PleasanterException>(() => _h.Connection.GetTableDefinitionAsync("Customers"))!;
            Assert.That(e.Kind, Is.EqualTo(PleasanterErrorKind.Authentication));
        }

        [Test]
        public async Task 表の定義は1回だけ読む()
        {
            await _h.Connection.GetTableDefinitionAsync("Customers");
            await _h.Connection.GetTableDefinitionAsync("Customers");
            Assert.That(_h.Fake.Requests.Count(e => e.Action == "getsite"), Is.EqualTo(1));
        }

        [Test]
        public void Sitesに無い表はエラー()
            => Assert.That(Assert.ThrowsAsync<PleasanterException>(() => _h.Connection.GetTableDefinitionAsync("Nothing"))!.Kind, Is.EqualTo(PleasanterErrorKind.NotFound));
    }
}
