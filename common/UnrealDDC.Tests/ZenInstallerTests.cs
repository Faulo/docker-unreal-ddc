using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnrealDDC.Tests;

public sealed class ZenInstallerTests {
    [TestCase(0, "zenserver", "zen")]
    [TestCase(1, "zenserver.exe", "zen.exe")]
    public async Task DownloadsVerifiesActivatesAndReusesInstallation(int platformValue, string serverName, string clientName) {
        var platform = (EZenPlatform)platformValue;
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive((serverName, "server-v1"), (clientName, "client-v1"));
        var release = CreateRelease(platform, archive, serverName, clientName);
        var downloader = new MemoryDownloader(archive);
        var installer = new ZenInstaller(directory.path, platform, release, downloader);

        var first = await installer.PrepareAsync(new GitHubCredentials("user", "token"));
        var second = await installer.PrepareAsync(new GitHubCredentials("user", "token"));
        var active = ZenInstaller.ReadActive(directory.path, platform);

        Assert.Multiple(() => {
            Assert.That(downloader.count, Is.EqualTo(1));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(active, Is.EqualTo(second));
            Assert.That(File.ReadAllText(first.server), Is.EqualTo("server-v1"));
            Assert.That(File.ReadAllText(first.client), Is.EqualTo("client-v1"));
            Assert.That(File.Exists(Path.Combine(first.directory, ".docker-unreal-ddc.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(first.directory, "zen.zip")), Is.False);
        });
    }

    [Test]
    public void RejectsArchiveWithWrongChecksumWithoutPublishingIt() {
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive(("zenserver", "server"), ("zen", "client"));
        var asset = new ZenReleaseAsset(1, "zen.zip", new string('0', 64), "zenserver", "zen");
        var release = new ZenRelease("v-test", new Version(1, 0, 0), asset, asset);
        var installer = new ZenInstaller(directory.path, EZenPlatform.LINUX, release, new MemoryDownloader(archive));

        var exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await installer.PrepareAsync(new GitHubCredentials("user", "token")));

        Assert.Multiple(() => {
            Assert.That(exception!.Message, Does.Contain("checksum mismatch"));
            Assert.That(Directory.Exists(Path.Combine(directory.path, "v-test", "linux")), Is.False);
            Assert.That(Directory.GetDirectories(directory.path, ".staging-*"), Is.Empty);
        });
    }

    [Test]
    public async Task ReplacesInstallationWhoseBinaryWasModified() {
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive(("zenserver", "server"), ("zen", "client"));
        var release = CreateRelease(EZenPlatform.LINUX, archive, "zenserver", "zen");
        var downloader = new MemoryDownloader(archive);
        var installer = new ZenInstaller(directory.path, EZenPlatform.LINUX, release, downloader);
        var installation = await installer.PrepareAsync(new GitHubCredentials("user", "token"));
        await File.AppendAllTextAsync(installation.server, "tampered");

        var repaired = await installer.PrepareAsync(new GitHubCredentials("user", "token"));

        Assert.Multiple(() => {
            Assert.That(downloader.count, Is.EqualTo(2));
            Assert.That(File.ReadAllText(repaired.server), Is.EqualTo("server"));
        });
    }

    static ZenRelease CreateRelease(EZenPlatform platform, byte[] archive, string serverName, string clientName) {
        string digest = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var asset = new ZenReleaseAsset(1, "zen.zip", digest, serverName, clientName);
        var unused = new ZenReleaseAsset(2, "unused.zip", new string('0', 64), "unused-server", "unused-client");
        return platform == EZenPlatform.LINUX
            ? new ZenRelease("v-test", new Version(1, 0, 0), asset, unused)
            : new ZenRelease("v-test", new Version(1, 0, 0), unused, asset);
    }

    static byte[] CreateArchive(params (string Name, string Content)[] files) {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
            foreach ((string name, string content) in files) {
                var entry = archive.CreateEntry(name);
                using var target = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                target.Write(bytes);
            }
        }
        return stream.ToArray();
    }

    sealed class MemoryDownloader(byte[] content) : IZenAssetDownloader {
        public int count { get; private set; }

        public async Task DownloadAsync(ZenReleaseAsset asset, GitHubCredentials credentials, string destination, CancellationToken cancellationToken) {
            count++;
            await File.WriteAllBytesAsync(destination, content, cancellationToken);
        }
    }
}
