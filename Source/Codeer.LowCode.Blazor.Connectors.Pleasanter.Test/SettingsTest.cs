using Codeer.LowCode.Blazor.Connectors.Pleasanter.Server;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Test
{
    /// <summary>接続文字列の読み方と設定の検査。</summary>
    public class SettingsTest
    {
        static DataSource Source(string connectionString) => new() { Name = "PL", ConnectionString = connectionString };

        [Test]
        public void 接続文字列を読む()
        {
            var settings = PleasanterConnectionSettings.FromDataSource(Source(
                "Provider=Pleasanter;Url=https://example.com/pleasanter;ApiKey=k;Sites=Projects:1200, Customers:1300;MaxRows=50;TimeoutSeconds=7;EmptyDate=1900-01-01"))!;

            Assert.That(settings.Name, Is.EqualTo("PL"));
            Assert.That(settings.Url, Is.EqualTo("https://example.com/pleasanter/"));
            Assert.That(settings.ApiKey, Is.EqualTo("k"));
            Assert.That(settings.Sites, Is.EqualTo(new Dictionary<string, long> { ["Projects"] = 1200, ["Customers"] = 1300 }));
            Assert.That((settings.MaxRows, settings.TimeoutSeconds), Is.EqualTo((50, 7)));
            Assert.That(settings.EmptyDate, Is.EqualTo(new DateTime(1900, 1, 1)));
        }

        [Test]
        public void 既定値()
        {
            var settings = PleasanterConnectionSettings.FromDataSource(Source("Provider=pleasanter;Url=https://example.com/;ApiKey=k;Sites=A:1"))!;
            Assert.That((settings.MaxRows, settings.TimeoutSeconds), Is.EqualTo((10000, 100)));
            Assert.That(settings.EmptyDate, Is.EqualTo(new DateTime(1899, 12, 30)));
        }

        [TestCase("Server=x;Database=y")]
        [TestCase("Provider=FileMaker;Url=https://example.com/")]
        [TestCase("not a connection string ===")]
        public void プリザンター用でなければnull(string connectionString)
            => Assert.That(PleasanterConnectionSettings.FromDataSource(Source(connectionString)), Is.Null);

        [TestCase("Sites=A")]
        [TestCase("Sites=A:0")]
        [TestCase("Sites=A:-1")]
        [TestCase("Sites=A-B:1")]
        [TestCase("Sites=A:1,A:2")]
        [TestCase("Sites=A:1,B:1")]
        [TestCase("Sites=A:1;MaxRows=0")]
        [TestCase("Sites=A:1;TimeoutSeconds=x")]
        [TestCase("Sites=A:1;EmptyDate=1900/01/01")]
        public void 書き方の誤りは黙って既定値にしない(string part)
            => Assert.Throws<PleasanterException>(() => PleasanterConnectionSettings.FromDataSource(Source("Provider=Pleasanter;Url=https://example.com/;ApiKey=k;" + part)));

        [TestCase("http://example.com/")]
        [TestCase("ftp://example.com/")]
        [TestCase("https://example.com/?a=1")]
        [TestCase("https://user@example.com/")]
        [TestCase("")]
        public void URLはhttpsだけ(string url)
        {
            var settings = new PleasanterConnectionSettings { Name = "PL", Url = url, ApiKey = "k" };
            Assert.Throws<PleasanterException>(() => new PleasanterConnection(settings));
        }

        [TestCase("http://localhost:59803/")]
        [TestCase("http://127.0.0.1/pleasanter/")]
        [TestCase("https://example.com/pleasanter/")]
        public void このマシン自身ならhttpを許す(string url)
            => Assert.DoesNotThrow(() => new PleasanterConnection(new PleasanterConnectionSettings { Name = "PL", Url = url, ApiKey = "k" }));

        [Test]
        public void APIキーは必須()
            => Assert.Throws<PleasanterException>(() => new PleasanterConnection(new PleasanterConnectionSettings { Name = "PL", Url = "https://example.com/" }));

        [Test]
        public void 設定の誤りのメッセージにAPIキーを出さない()
        {
            var e = Assert.Throws<PleasanterException>(() => PleasanterConnectionSettings.FromDataSource(Source("Provider=Pleasanter;Url=https://example.com/;ApiKey=secret-key-123;Sites=A:x")))!;
            Assert.That(e.Message, Does.Not.Contain("secret-key-123"));
        }

        [Test]
        public void FromDataSourcesはプリザンター用だけを同じ設定なら同じ接続で返す()
        {
            var sources = new[]
            {
                Source("Provider=Pleasanter;Url=https://example.com/;ApiKey=k;Sites=A:1"),
                new DataSource { Name = "Main", ConnectionString = "Server=x;Database=y" },
            };
            var first = PleasanterConnection.FromDataSources(sources);
            var second = PleasanterConnection.FromDataSources(sources);
            Assert.That(first, Has.Count.EqualTo(1));
            Assert.That(second.Single(), Is.SameAs(first.Single()));

            var otherKey = PleasanterConnection.FromDataSources(new[] { Source("Provider=Pleasanter;Url=https://example.com/;ApiKey=k2;Sites=A:1") });
            Assert.That(otherKey.Single(), Is.Not.SameAs(first.Single()));
        }

        [Test]
        public void 既定のCookieの名前()
            => Assert.That(PleasanterConnection.DefaultSessionCookiePrefixes, Is.EqualTo(new[] { ".AspNetCore.Cookies", "Pleasanter_SessionGuid", ".AspNetCore.Session" }));
    }
}
