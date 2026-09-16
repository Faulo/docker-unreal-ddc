using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace UnrealDDC;

static class Program {
    static async Task<int> Main(string[] arguments) {
        try {
            var command = ParseCommand(arguments);
            var platform = ZenRelease.CurrentPlatform();
            string root = ResolveRoot(platform);
            string installRoot = Path.Combine(root, "install");
            return command switch {
                ELauncherCommand.SERVE => await ServeAsync(root, installRoot, platform),
                ELauncherCommand.HEALTH => await HealthAsync(root, installRoot, platform),
                ELauncherCommand.VERSION => await PrintVersionAsync(installRoot, platform),
                _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported launcher command")
            };
        } catch (Exception exception) {
            await Console.Error.WriteLineAsync("docker-unreal-ddc: " + exception.Message);
            return 1;
        }
    }

    internal static ELauncherCommand ParseCommand(string[] arguments) {
        if (arguments.Length != 1) {
            throw new InvalidOperationException("Usage: unreal-ddc <serve|health|version>");
        }
        return arguments[0] switch {
            "serve" => ELauncherCommand.SERVE,
            "health" => ELauncherCommand.HEALTH,
            "version" => ELauncherCommand.VERSION,
            _ => throw new InvalidOperationException($"Unknown command '{arguments[0]}'; expected serve, health, or version")
        };
    }

    static async Task<int> ServeAsync(string root, string installRoot, EZenPlatform platform) {
        var range = ZenVersionRange.Parse(Environment.GetEnvironmentVariable(EnvironmentVariableNames.ZEN_VERSION));
        var credentials = GitHubCredentials.TryFromEnvironment();
        ZenInstallation installation;
        if (credentials is null) {
            installation = ResolveCachedInstallation(installRoot, platform, range);
            await Console.Out.WriteLineAsync(
                $"docker-unreal-ddc: using cached Epic Zen {installation.version} without checking for updates because credentials were not supplied"
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
        var configuration = ZenConfiguration.FromEnvironment(root, platform);
        await Console.Out.WriteLineAsync($"docker-unreal-ddc: starting Epic Zen {installation.version}");
        return await ZenProcess.RunAsync(installation, configuration.arguments, configuration.port);
    }

    static async Task<int> HealthAsync(string root, string installRoot, EZenPlatform platform) {
        var activeInstallation = ZenInstaller.ReadActive(installRoot, platform);
        int healthPort = ZenConfiguration.FromEnvironment(root, platform).port;
        return await ZenProcess.RunHealthAsync(activeInstallation, healthPort);
    }

    static async Task<int> PrintVersionAsync(string installRoot, EZenPlatform platform) {
        string launcherVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        await Console.Out.WriteLineAsync($"Unreal DDC launcher: {launcherVersion}");
        var status = ZenInstaller.GetActiveStatus(installRoot, platform);
        string zen = status.state switch {
            EZenInstallationState.NOT_INSTALLED => "Zen: not installed",
            EZenInstallationState.VERIFIED => $"Zen: {status.version} (installed, verified)",
            EZenInstallationState.INVALID when status.version is not null => $"Zen: {status.version} (installed, invalid)",
            EZenInstallationState.INVALID => "Zen: unknown (installed, invalid)",
            _ => throw new ArgumentOutOfRangeException(nameof(status.state), status.state, "Unsupported installation state")
        };
        await Console.Out.WriteLineAsync(zen);
        return 0;
    }

    static ZenInstallation ResolveCachedInstallation(string installRoot, EZenPlatform platform, ZenVersionRange range) {
        ZenInstallation installation;
        try {
            installation = ZenInstaller.ReadVerifiedActive(installRoot, platform);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            throw new InvalidOperationException(
                "Zen update credentials are required because no verified cached installation is available",
                exception
            );
        }
        if (!range.Contains(installation.version)) {
            throw new InvalidOperationException(
                $"Zen update credentials are required because cached version {installation.version} does not match {range.displayName}"
            );
        }
        return installation;
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

enum ELauncherCommand {
    SERVE,
    HEALTH,
    VERSION
}
