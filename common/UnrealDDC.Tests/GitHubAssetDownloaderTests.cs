using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnrealDDC.Tests;

public sealed class GitHubAssetDownloaderTests {
    [Test]
    public async Task AuthenticatesApiAssetRequestWithoutPuttingTokenInUri() {
        using var directory = new TemporaryDirectory();
        var handler = new CaptureHandler(HttpStatusCode.OK, [1, 2, 3]);
        using var client = new HttpClient(handler);
        var downloader = new GitHubAssetDownloader(client);
        var asset = new ZenReleaseAsset(42, "zen.zip", "unused", "zenserver", "zen");

        string destination = Path.Combine(directory.path, "zen.zip");
        await downloader.DownloadAsync(asset, new GitHubCredentials("user", "secret-token"), destination, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(handler.uri, Is.EqualTo(new Uri("https://api.github.com/repos/EpicGames/zen/releases/assets/42")));
            Assert.That(handler.authorizationScheme, Is.EqualTo("Bearer"));
            Assert.That(handler.authorizationParameter, Is.EqualTo("secret-token"));
            Assert.That(handler.accept, Does.Contain("application/octet-stream"));
            Assert.That(handler.uri!.AbsoluteUri, Does.Not.Contain("secret-token"));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void ExplainsEntitlementFailure() {
        using var directory = new TemporaryDirectory();
        var handler = new CaptureHandler(HttpStatusCode.NotFound, []);
        using var client = new HttpClient(handler);
        var downloader = new GitHubAssetDownloader(client);
        var asset = new ZenReleaseAsset(42, "zen.zip", "unused", "zenserver", "zen");

        var exception = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await downloader.DownloadAsync(asset, new GitHubCredentials("user", "token"), Path.Combine(directory.path, "zen.zip"), CancellationToken.None));
        Assert.Multiple(() => {
            Assert.That(exception!.Message, Does.Contain("EpicGames/zen"));
            Assert.That(handler.count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RetriesTransientTransportFailure() {
        using var directory = new TemporaryDirectory();
        var handler = new TransientFailureHandler();
        using var client = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var downloader = new GitHubAssetDownloader(client, (delay, _) => {
            delays.Add(delay);
            return Task.CompletedTask;
        });
        var asset = new ZenReleaseAsset(42, "zen.zip", "unused", "zenserver", "zen");

        string destination = Path.Combine(directory.path, "zen.zip");
        await downloader.DownloadAsync(asset, new GitHubCredentials("user", "token"), destination, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(handler.count, Is.EqualTo(2));
            Assert.That(delays, Is.EqualTo([TimeSpan.FromSeconds(1)]));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 4, 5, 6 }));
        });
    }

    sealed class CaptureHandler(HttpStatusCode statusCode, byte[] content) : HttpMessageHandler {
        public int count { get; private set; }
        public Uri? uri { get; private set; }
        public string? authorizationScheme { get; private set; }
        public string? authorizationParameter { get; private set; }
        public string accept { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            count++;
            uri = request.RequestUri;
            authorizationScheme = request.Headers.Authorization?.Scheme;
            authorizationParameter = request.Headers.Authorization?.Parameter;
            accept = request.Headers.Accept.ToString();
            return Task.FromResult(new HttpResponseMessage(statusCode) {
                Content = new ByteArrayContent(content)
            });
        }
    }

    sealed class TransientFailureHandler : HttpMessageHandler {
        public int count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            count++;
            if (count == 1) {
                throw new HttpRequestException("The SSL connection could not be established");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent([4, 5, 6])
            });
        }
    }
}
