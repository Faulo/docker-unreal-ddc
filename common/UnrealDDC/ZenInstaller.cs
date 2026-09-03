using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnrealDDC;

sealed record ZenInstallation(string Directory, string Server, string Client);

sealed record ZenInstallationMarker(
    string Release,
    string Platform,
    string Asset,
    string ArchiveSha256,
    string ServerSha256,
    string ClientSha256
);

sealed class ZenInstaller(
    string installRoot,
    EZenPlatform platform,
    ZenRelease release,
    IZenAssetDownloader downloader
) {
    const string MARKER_NAME = ".docker-unreal-ddc.json";

    readonly ZenReleaseAsset _asset = release.AssetFor(platform);
    readonly string _platformName = platform switch {
        EZenPlatform.Linux => "linux",
        EZenPlatform.Windows => "windows",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
    };

    string InstallationDirectory => Path.Combine(installRoot, release.Tag, _platformName);

    public async Task<ZenInstallation> PrepareAsync(GitHubCredentials? credentials, CancellationToken cancellationToken = default) {
        var existing = ValidateInstallation();
        if (existing is not null) {
            return existing;
        }

        using var installationLock = InstallationLock.Acquire(Path.Combine(installRoot, ".docker-unreal-ddc.lock"));
        existing = ValidateInstallation();
        if (existing is not null) {
            return existing;
        }
        if (credentials is null) {
            throw new InvalidOperationException(
                $"Zen {release.Version} is not installed; supply {EnvironmentVariableNames.UNREAL_CREDENTIALS_USR} and {EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW} for the first start"
            );
        }

        Directory.CreateDirectory(installRoot);
        string staging = Path.Combine(installRoot, $".staging-{release.Tag}-{_platformName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try {
            string archivePath = Path.Combine(staging, _asset.Name);
            Console.Out.WriteLine($"docker-unreal-ddc: downloading Epic Zen {release.Version} for {_platformName}");
            await downloader.DownloadAsync(_asset, credentials, archivePath, cancellationToken);
            string archiveHash = HashFile(archivePath);
            if (!string.Equals(archiveHash, _asset.ArchiveSha256, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException($"Zen archive checksum mismatch: expected {_asset.ArchiveSha256}, got {archiveHash}");
            }

            string serverPath = Path.Combine(staging, _asset.ServerFile);
            string clientPath = Path.Combine(staging, _asset.ClientFile);
            await ExtractAsync(archivePath, _asset.ServerFile, serverPath, cancellationToken);
            await ExtractAsync(archivePath, _asset.ClientFile, clientPath, cancellationToken);
            File.Delete(archivePath);
            MakeExecutable(serverPath);
            MakeExecutable(clientPath);

            var marker = new ZenInstallationMarker(
                release.Version,
                _platformName,
                _asset.Name,
                archiveHash,
                HashFile(serverPath),
                HashFile(clientPath)
            );
            await File.WriteAllTextAsync(
                Path.Combine(staging, MARKER_NAME),
                JsonSerializer.Serialize(marker),
                cancellationToken
            );

            string destination = InstallationDirectory;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination)) {
                Directory.Delete(destination, true);
            }
            Directory.Move(staging, destination);
            Console.Out.WriteLine($"docker-unreal-ddc: installed Epic Zen {release.Version} for {_platformName}");
        } catch {
            if (Directory.Exists(staging)) {
                Directory.Delete(staging, true);
            }
            throw;
        }

        return ValidateInstallation()
               ?? throw new InvalidDataException("The published Zen installation failed validation");
    }

    ZenInstallation? ValidateInstallation() {
        string directory = InstallationDirectory;
        string markerPath = Path.Combine(directory, MARKER_NAME);
        string serverPath = Path.Combine(directory, _asset.ServerFile);
        string clientPath = Path.Combine(directory, _asset.ClientFile);
        if (!File.Exists(markerPath) || !File.Exists(serverPath) || !File.Exists(clientPath)) {
            return null;
        }

        try {
            var marker = JsonSerializer.Deserialize<ZenInstallationMarker>(File.ReadAllText(markerPath));
            if (marker is null
                || !string.Equals(marker.Release, release.Version, StringComparison.Ordinal)
                || !string.Equals(marker.Platform, _platformName, StringComparison.Ordinal)
                || !string.Equals(marker.Asset, _asset.Name, StringComparison.Ordinal)
                || !string.Equals(marker.ArchiveSha256, _asset.ArchiveSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(marker.ServerSha256, HashFile(serverPath), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(marker.ClientSha256, HashFile(clientPath), StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
        } catch (IOException) {
            return null;
        } catch (JsonException) {
            return null;
        }

        return new ZenInstallation(directory, serverPath, clientPath);
    }

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
