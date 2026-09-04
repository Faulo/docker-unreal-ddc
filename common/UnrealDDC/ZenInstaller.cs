using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnrealDDC;

sealed record ZenInstallation(string directory, string server, string client, Version version);

sealed record ZenInstallationMarker(
    string release,
    string platform,
    string asset,
    string archiveSha256,
    string serverSha256,
    string clientSha256
);

sealed record ZenActiveInstallation(string release, string platform, string serverFile, string clientFile, string version);

sealed class ZenInstaller(
    string installRoot,
    EZenPlatform platform,
    ZenRelease release,
    IZenAssetDownloader downloader
) {
    const string MARKER_NAME = ".docker-unreal-ddc.json";

    readonly ZenReleaseAsset asset = release.AssetFor(platform);
    readonly string platformName = platform switch {
        EZenPlatform.LINUX => "linux",
        EZenPlatform.WINDOWS => "windows",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
    };

    string installationDirectory => Path.Combine(installRoot, release.tag, platformName);

    public async Task<ZenInstallation> PrepareAsync(GitHubCredentials credentials, CancellationToken cancellationToken = default) {
        var existing = ValidateInstallation();
        if (existing is not null) {
            await ActivateAsync(existing, cancellationToken);
            return existing;
        }

        using var installationLock = InstallationLock.Acquire(Path.Combine(installRoot, ".docker-unreal-ddc.lock"), cancellationToken);
        existing = ValidateInstallation();
        if (existing is not null) {
            await ActivateAsync(existing, cancellationToken);
            return existing;
        }

        Directory.CreateDirectory(installRoot);
        string staging = Path.Combine(installRoot, $".staging-{release.tag}-{platformName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try {
            string archivePath = Path.Combine(staging, asset.name);
            await Console.Out.WriteLineAsync($"docker-unreal-ddc: downloading Epic Zen {release.version} for {platformName}");
            await downloader.DownloadAsync(asset, credentials, archivePath, cancellationToken);
            string archiveHash = HashFile(archivePath);
            if (!string.Equals(archiveHash, asset.archiveSha256, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException($"Zen archive checksum mismatch: expected {asset.archiveSha256}, got {archiveHash}");
            }

            string serverPath = Path.Combine(staging, asset.serverFile);
            string clientPath = Path.Combine(staging, asset.clientFile);
            await ExtractAsync(archivePath, asset.serverFile, serverPath, cancellationToken);
            await ExtractAsync(archivePath, asset.clientFile, clientPath, cancellationToken);
            File.Delete(archivePath);
            MakeExecutable(serverPath);
            MakeExecutable(clientPath);

            var marker = new ZenInstallationMarker(
                release.version.ToString(),
                platformName,
                asset.name,
                archiveHash,
                HashFile(serverPath),
                HashFile(clientPath)
            );
            await File.WriteAllTextAsync(
                Path.Combine(staging, MARKER_NAME),
                JsonSerializer.Serialize(marker),
                cancellationToken
            );

            string destination = installationDirectory;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination)) {
                Directory.Delete(destination, true);
            }
            Directory.Move(staging, destination);
            await Console.Out.WriteLineAsync($"docker-unreal-ddc: installed Epic Zen {release.version} for {platformName}");
        } catch (Exception exception) {
            try {
                if (Directory.Exists(staging)) {
                    Directory.Delete(staging, true);
                }
            } catch (Exception cleanupException) {
                throw new AggregateException("Zen installation and staging cleanup both failed", exception, cleanupException);
            }
            throw;
        }

        var installation = ValidateInstallation()
                           ?? throw new InvalidDataException("The published Zen installation failed validation");
        await ActivateAsync(installation, cancellationToken);
        return installation;
    }

    ZenInstallation? ValidateInstallation() {
        string directory = installationDirectory;
        string markerPath = Path.Combine(directory, MARKER_NAME);
        string serverPath = Path.Combine(directory, asset.serverFile);
        string clientPath = Path.Combine(directory, asset.clientFile);
        if (!File.Exists(markerPath) || !File.Exists(serverPath) || !File.Exists(clientPath)) {
            return null;
        }

        try {
            var marker = JsonSerializer.Deserialize<ZenInstallationMarker>(File.ReadAllText(markerPath));
            if (marker is null
                || !string.Equals(marker.release, release.version.ToString(), StringComparison.Ordinal)
                || !string.Equals(marker.platform, platformName, StringComparison.Ordinal)
                || !string.Equals(marker.asset, asset.name, StringComparison.Ordinal)
                || !string.Equals(marker.archiveSha256, asset.archiveSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(marker.serverSha256, HashFile(serverPath), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(marker.clientSha256, HashFile(clientPath), StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
        } catch (IOException) {
            return null;
        } catch (JsonException) {
            return null;
        }

        return new ZenInstallation(directory, serverPath, clientPath, release.version);
    }

    async Task ActivateAsync(ZenInstallation installation, CancellationToken cancellationToken) {
        Directory.CreateDirectory(installRoot);
        string activePath = ActivePath(installRoot, platform);
        string temporaryPath = activePath + $".{Guid.NewGuid():N}.tmp";
        var active = new ZenActiveInstallation(release.tag, platformName, asset.serverFile, asset.clientFile, installation.version.ToString());
        try {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(active), cancellationToken);
            File.Move(temporaryPath, activePath, true);
        } finally {
            File.Delete(temporaryPath);
        }
    }

    public static ZenInstallation ReadActive(string installRoot, EZenPlatform platform) {
        string platformName = PlatformName(platform);
        string path = ActivePath(installRoot, platform);
        var active = JsonSerializer.Deserialize<ZenActiveInstallation>(File.ReadAllText(path))
                     ?? throw new InvalidDataException("The active Zen installation marker is empty");
        if (!string.Equals(active.platform, platformName, StringComparison.Ordinal)
            || Path.GetFileName(active.release) != active.release
            || Path.GetFileName(active.serverFile) != active.serverFile
            || Path.GetFileName(active.clientFile) != active.clientFile
            || !Version.TryParse(active.version, out var version)) {
            throw new InvalidDataException("The active Zen installation marker is invalid");
        }
        string directory = Path.Combine(installRoot, active.release, platformName);
        string server = Path.Combine(directory, active.serverFile);
        string client = Path.Combine(directory, active.clientFile);
        if (!File.Exists(server) || !File.Exists(client)) {
            throw new InvalidDataException("The active Zen installation is incomplete");
        }
        return new ZenInstallation(directory, server, client, version);
    }

    public static ZenInstallation ReadVerifiedActive(string installRoot, EZenPlatform platform) {
        try {
            var installation = ReadActive(installRoot, platform);
            var marker = JsonSerializer.Deserialize<ZenInstallationMarker>(File.ReadAllText(Path.Combine(installation.directory, MARKER_NAME)))
                         ?? throw new InvalidDataException("The active Zen installation validation marker is empty");
            if (!string.Equals(marker.release, installation.version.ToString(), StringComparison.Ordinal)
                || !string.Equals(marker.platform, PlatformName(platform), StringComparison.Ordinal)
                || !string.Equals(marker.serverSha256, HashFile(installation.server), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(marker.clientSha256, HashFile(installation.client), StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException("The active Zen installation failed validation");
            }
            return installation;
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) {
            throw new InvalidDataException("The active Zen installation could not be validated", exception);
        }
    }

    static string ActivePath(string installRoot, EZenPlatform platform) => Path.Combine(
        installRoot,
        platform switch {
            EZenPlatform.LINUX => ".docker-unreal-ddc-active-linux.json",
            EZenPlatform.WINDOWS => ".docker-unreal-ddc-active-windows.json",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
        }
    );

    static string PlatformName(EZenPlatform platform) => platform switch {
        EZenPlatform.LINUX => "linux",
        EZenPlatform.WINDOWS => "windows",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
    };

    static async Task ExtractAsync(string archivePath, string entryName, string destination, CancellationToken cancellationToken) {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(entryName);
        if (entry is null || string.IsNullOrEmpty(entry.Name)) {
            throw new InvalidDataException($"Zen archive does not contain required file '{entryName}'");
        }

        await using var source = entry.Open();
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(target, cancellationToken);
    }

    static string HashFile(string path) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static void MakeExecutable(string path) {
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
            );
        }
    }
}
