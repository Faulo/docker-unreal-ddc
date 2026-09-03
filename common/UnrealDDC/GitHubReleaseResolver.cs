using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UnrealDDC;

sealed partial class GitHubReleaseResolver(HttpClient client) {
    const int PAGE_SIZE = 100;

    public async Task<ZenRelease> ResolveAsync(
        EZenPlatform platform,
        ZenVersionRange range,
        GitHubCredentials credentials,
        CancellationToken cancellationToken = default
    ) {
        var releases = new List<ZenRelease>();
        for (int page = 1; ; page++) {
            using var request = CreateRequest(credentials, page);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                throw CreateRequestException(response.StatusCode, "release lookup");
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            var entries = document.RootElement;
            releases.AddRange(entries.EnumerateArray().Select(ParseRelease).OfType<ZenRelease>());
            if (entries.GetArrayLength() < PAGE_SIZE) {
                break;
            }
        }

        return releases
                   .Where(release => range.Contains(release.version))
                   .Where(release => HasAssetFor(release, platform))
                   .OrderByDescending(release => release.version)
                   .FirstOrDefault()
               ?? throw new InvalidOperationException($"No stable Epic Zen release matching {range.displayName} is available for {PlatformName(platform)}");
    }

    static HttpRequestMessage CreateRequest(GitHubCredentials credentials, int page) {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/EpicGames/zen/releases?per_page={PAGE_SIZE}&page={page}"
        );
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.token);
        request.Headers.UserAgent.ParseAdd("docker-unreal-ddc/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    static ZenRelease? ParseRelease(JsonElement entry) {
        if (entry.GetProperty("draft").GetBoolean() || entry.GetProperty("prerelease").GetBoolean()) {
            return null;
        }

        string? tag = entry.GetProperty("tag_name").GetString();
        var match = VersionPattern().Match(tag ?? string.Empty);
        if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var version)) {
            return null;
        }

        ZenReleaseAsset? linux = null;
        ZenReleaseAsset? windows = null;
        foreach (var asset in entry.GetProperty("assets").EnumerateArray()) {
            string? name = asset.GetProperty("name").GetString();
            switch (name) {
                case "zenserver-linux.zip":
                    linux = ParseAsset(asset, "zenserver", "zen");
                    break;
                case "zenserver-win64.zip":
                    windows = ParseAsset(asset, "zenserver.exe", "zen.exe");
                    break;
            }
        }

        return new ZenRelease(
            tag!,
            version,
            linux ?? MissingAsset("zenserver-linux.zip"),
            windows ?? MissingAsset("zenserver-win64.zip")
        );
    }

    static ZenReleaseAsset ParseAsset(JsonElement asset, string serverFile, string clientFile) {
        string name = asset.GetProperty("name").GetString()!;
        string? digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71) {
            return MissingAsset(name);
        }
        string sha256 = digest[7..];
        try {
            Convert.FromHexString(sha256);
        } catch (FormatException) {
            return MissingAsset(name);
        }
        return new ZenReleaseAsset(asset.GetProperty("id").GetInt64(), name, sha256.ToLowerInvariant(), serverFile, clientFile);
    }

    static ZenReleaseAsset MissingAsset(string name) => new(0, name, string.Empty, string.Empty, string.Empty);

    static bool HasAssetFor(ZenRelease release, EZenPlatform platform) => release.AssetFor(platform).id > 0;

    static string PlatformName(EZenPlatform platform) => platform switch {
        EZenPlatform.LINUX => "linux",
        EZenPlatform.WINDOWS => "windows",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
    };

    static HttpRequestException CreateRequestException(HttpStatusCode statusCode, string operation) {
        string detail = statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            ? "; verify that the supplied GitHub account can access EpicGames/zen"
            : string.Empty;
        return new HttpRequestException($"Epic Zen {operation} failed with HTTP {(int)statusCode}{detail}", null, statusCode);
    }

    [GeneratedRegex(@"^v(?<version>[0-9]+\.[0-9]+\.[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
