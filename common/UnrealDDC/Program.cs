using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace UnrealDDC;

static class Program {
    static async Task<int> Main(string[] arguments) {
        if (arguments.Length == 1 && string.Equals(arguments[0], "--launcher-version", StringComparison.OrdinalIgnoreCase)) {
            Console.Out.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
            return 0;
        }

        try {
            var platform = ZenRelease.CurrentPlatform();
            string root = ResolveRoot(platform);
            var credentials = GitHubCredentials.FromEnvironment();
            using var client = new HttpClient {
                Timeout = TimeSpan.FromMinutes(15)
            };
            var installer = new ZenInstaller(
                Path.Combine(root, "install"),
                platform,
                ZenRelease.Pinned,
                new GitHubAssetDownloader(client)
            );
            var installation = await installer.PrepareAsync(credentials);
            Console.Out.WriteLine($"docker-unreal-ddc: starting Epic Zen {ZenRelease.Pinned.Version}");
            return await ZenProcess.RunAsync(installation, arguments);
        } catch (Exception exception) {
            Console.Error.WriteLine("docker-unreal-ddc: " + exception.Message);
            return 1;
        }
    }

    static string ResolveRoot(EZenPlatform platform) {
        string? configured = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_DDC_ROOT);
        string root = string.IsNullOrWhiteSpace(configured)
            ? platform switch {
                EZenPlatform.Linux => "/unreal-ddc",
                EZenPlatform.Windows => @"C:\unreal-ddc",
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
            }
            : configured.Trim();
        if (!Path.IsPathFullyQualified(root)) {
            throw new InvalidOperationException($"{EnvironmentVariableNames.UNREAL_DDC_ROOT} must be an absolute path");
        }
        return Path.GetFullPath(root);
    }
}
