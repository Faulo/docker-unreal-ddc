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

        string destination = Path.Combine(directory.Path, "zen.zip");
        await downloader.DownloadAsync(asset, new GitHubCredentials("user", "secret-token"), destination, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(handler.Uri, Is.EqualTo(new Uri("https://api.github.com/repos/EpicGames/zen/releases/assets/42")));
            Assert.That(handler.AuthorizationScheme, Is.EqualTo("Bearer"));
            Assert.That(handler.AuthorizationParameter, Is.EqualTo("secret-token"));
            Assert.That(handler.Accept, Does.Contain("application/octet-stream"));
            Assert.That(handler.Uri!.AbsoluteUri, Does.Not.Contain("secret-token"));
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
            await downloader.DownloadAsync(asset, new GitHubCredentials("user", "token"), Path.Combine(directory.Path, "zen.zip"), CancellationToken.None));
        Assert.Multiple(() => {
            Assert.That(exception!.Message, Does.Contain("EpicGames/zen"));
            Assert.That(handler.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RetriesTransientTransportFailure() {
        using var directory = new TemporaryDirectory();
        var handler = new TransientFailureHandler();
        using var client = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var downloader = new GitHubAssetDownloader(client, (delay, cancellationToken) => {
            delays.Add(delay);
            return Task.CompletedTask;
        });
        var asset = new ZenReleaseAsset(42, "zen.zip", "unused", "zenserver", "zen");

        string destination = Path.Combine(directory.Path, "zen.zip");
        await downloader.DownloadAsync(asset, new GitHubCredentials("user", "token"), destination, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(handler.Count, Is.EqualTo(2));
            Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(1) }));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 4, 5, 6 }));
        });
    }

    sealed class CaptureHandler(HttpStatusCode statusCode, byte[] content) : HttpMessageHandler {
        public int Count { get; private set; }
        public Uri? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string Accept { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Count++;
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Accept = request.Headers.Accept.ToString();
            return Task.FromResult(new HttpResponseMessage(statusCode) {
                Content = new ByteArrayContent(content)
            });
        }
    }

    sealed class TransientFailureHandler : HttpMessageHandler {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Count++;
            if (Count == 1) {
                throw new HttpRequestException("The SSL connection could not be established");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent([4, 5, 6])
            });
        }
    }
}
