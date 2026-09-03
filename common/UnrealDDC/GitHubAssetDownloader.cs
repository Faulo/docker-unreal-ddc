using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace UnrealDDC;

interface IZenAssetDownloader {
    Task DownloadAsync(ZenReleaseAsset asset, GitHubCredentials credentials, string destination, CancellationToken cancellationToken);
}

sealed class GitHubAssetDownloader : IZenAssetDownloader {
    const int MAX_ATTEMPTS = 4;

    readonly HttpClient _client;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public GitHubAssetDownloader(HttpClient client)
        : this(client, static (delay, cancellationToken) => Task.Delay(delay, cancellationToken)) { }

    internal GitHubAssetDownloader(HttpClient client, Func<TimeSpan, CancellationToken, Task> delay) {
        _client = client;
        _delay = delay;
    }

    public async Task DownloadAsync(ZenReleaseAsset asset, GitHubCredentials credentials, string destination, CancellationToken cancellationToken) {
        for (int attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
            try {
                await DownloadOnceAsync(asset, credentials, destination, cancellationToken);
                return;
            } catch (Exception exception) when (attempt < MAX_ATTEMPTS && IsTransient(exception, cancellationToken)) {
                File.Delete(destination);
                var delay = TimeSpan.FromSeconds(1 << (attempt - 1));
                Console.Error.WriteLine(
                    $"docker-unreal-ddc: Zen download attempt {attempt}/{MAX_ATTEMPTS} failed ({exception.Message}); retrying in {delay.TotalSeconds:0} second(s)"
                );
                await _delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException("Zen download retry loop ended unexpectedly");
    }

    async Task DownloadOnceAsync(ZenReleaseAsset asset, GitHubCredentials credentials, string destination, CancellationToken cancellationToken) {
        var uri = new Uri($"https://api.github.com/repos/EpicGames/zen/releases/assets/{asset.Id}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Token);
        request.Headers.UserAgent.ParseAdd("docker-unreal-ddc/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            string detail = response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.NotFound
                ? "; verify that the supplied GitHub account can access EpicGames/zen"
                : string.Empty;
            throw new HttpRequestException($"Epic Zen asset download failed with HTTP {(int)response.StatusCode}{detail}", null, response.StatusCode);
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(target, cancellationToken);
    }

    static bool IsTransient(Exception exception, CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return false;
        }
        if (exception is TaskCanceledException) {
            return true;
        }
        if (exception is IOException) {
            return true;
        }
        if (exception is not HttpRequestException httpException) {
            return false;
        }

        int? status = (int?)httpException.StatusCode;
        return status is null
               || status == 408
               || status == 429
               || status >= 500;
    }
}
