using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnrealDDC.Tests;

public sealed class GitHubReleaseResolverTests {
    [Test]
    public async Task SelectsNewestCompatibleStableReleaseWithVerifiedPlatformAsset() {
        string digest = new('a', 64);
        string json = $$"""
                        [
                          {
                            "tag_name": "v5.8.20",
                            "draft": false,
                            "prerelease": false,
                            "assets": [
                              { "id": 1, "name": "zenserver-linux.zip", "digest": "sha256:{{digest}}" }
                            ]
                          },
                          {
                            "tag_name": "v5.9.1",
                            "draft": false,
                            "prerelease": false,
                            "assets": [
                              { "id": 2, "name": "zenserver-linux.zip", "digest": "sha256:{{digest}}" }
                            ]
                          },
                          {
                            "tag_name": "v5.10.0-preview",
                            "draft": false,
                            "prerelease": true,
                            "assets": []
                          }
                        ]
                        """;
        var handler = new ReleaseHandler(json);
        using var client = new HttpClient(handler);

        var release = await new GitHubReleaseResolver(client).ResolveAsync(
            EZenPlatform.LINUX,
            ZenVersionRange.Parse("5"),
            new GitHubCredentials("user", "secret-token")
        );

        Assert.Multiple(() => {
            Assert.That(release.version, Is.EqualTo(new Version(5, 9, 1)));
            Assert.That(release.linux.id, Is.EqualTo(2));
            Assert.That(release.linux.archiveSha256, Is.EqualTo(digest));
            Assert.That(handler.authorization, Is.EqualTo("Bearer secret-token"));
            Assert.That(handler.uri, Is.EqualTo(new Uri("https://api.github.com/repos/EpicGames/zen/releases?per_page=100&page=1")));
        });
    }

    sealed class ReleaseHandler(string json) : HttpMessageHandler {
        public string? authorization { get; private set; }
        public Uri? uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            authorization = request.Headers.Authorization?.ToString();
            uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(json)
            });
        }
    }
}
