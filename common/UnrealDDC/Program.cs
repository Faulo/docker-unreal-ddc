using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace UnrealDDC;

static class Program {
    static async Task<int> Main(string[] arguments) {
        if (arguments.Length == 1 && string.Equals(arguments[0], "--launcher-version", StringComparison.OrdinalIgnoreCase)) {
            await Console.Out.WriteLineAsync(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
            return 0;
        }

        try {
            var platform = ZenRelease.CurrentPlatform();
            string root = ResolveRoot(platform);
            string installRoot = Path.Combine(root, "install");
            if (arguments.Length == 1 && string.Equals(arguments[0], "--health", StringComparison.OrdinalIgnoreCase)) {
                var activeInstallation = ZenInstaller.ReadActive(installRoot, platform);
                int healthPort = ZenConfiguration.FromEnvironment(root, platform, []).port;
                return await ZenProcess.RunHealthAsync(activeInstallation, healthPort);
            }

            var range = ZenVersionRange.Parse(Environment.GetEnvironmentVariable(EnvironmentVariableNames.ZEN_VERSION));
            var credentials = GitHubCredentials.TryFromEnvironment();
            ZenInstallation installation;
            if (credentials is null) {
                installation = ZenInstaller.ReadVerifiedActive(installRoot, platform);
                if (!range.Contains(installation.version)) {
                    throw new InvalidOperationException(
                        $"The cached Epic Zen {installation.version} does not match {EnvironmentVariableNames.ZEN_VERSION}={range.displayName}; credentials are required to select another release"
                    );
                }
                await Console.Out.WriteLineAsync(
                    $"docker-unreal-ddc: credentials not configured; starting verified cached Epic Zen {installation.version} without an update check"
                );
            } else {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(15);
                await Console.Out.WriteLineAsync($"docker-unreal-ddc: checking Epic Zen releases matching {range.displayName}");
                var release = await new GitHubReleaseResolver(client).ResolveAsync(platform, range, credentials);
                var installer = new ZenInstaller(
                    installRoot,
                    platform,
                    release,
                    new GitHubAssetDownloader(client)
                );
                installation = await installer.PrepareAsync(credentials);
            }
            var configuration = ZenConfiguration.FromEnvironment(root, platform, arguments);
            await Console.Out.WriteLineAsync($"docker-unreal-ddc: starting Epic Zen {installation.version}");
            return await ZenProcess.RunAsync(installation, configuration.arguments, configuration.port);
        } catch (Exception exception) {
            await Console.Error.WriteLineAsync("docker-unreal-ddc: " + exception.Message);
            return 1;
        }
    }

    static string ResolveRoot(EZenPlatform platform) {
        string? configured = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_DDC_ROOT);
        string root = string.IsNullOrWhiteSpace(configured)
            ? platform switch {
                EZenPlatform.LINUX => "/unreal-ddc",
                EZenPlatform.WINDOWS => @"C:\unreal-ddc",
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
            }
            : configured.Trim();
        return Path.IsPathFullyQualified(root)
            ? Path.GetFullPath(root)
            : throw new InvalidOperationException($"{EnvironmentVariableNames.UNREAL_DDC_ROOT} must be an absolute path");
    }
}
