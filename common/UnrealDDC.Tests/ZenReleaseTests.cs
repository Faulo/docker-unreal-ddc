using NUnit.Framework;

namespace UnrealDDC.Tests;

public sealed class ZenReleaseTests {
    [Test]
    public void PinsCurrentStableReleaseAndEpicDigests() {
        Assert.Multiple(() => {
            Assert.That(ZenRelease.Pinned.Tag, Is.EqualTo("v5.8.20"));
            Assert.That(ZenRelease.Pinned.Version, Is.EqualTo("5.8.20"));
            Assert.That(ZenRelease.Pinned.Linux.Name, Is.EqualTo("zenserver-linux.zip"));
            Assert.That(ZenRelease.Pinned.Linux.ArchiveSha256, Is.EqualTo("fe3926e7c1cc27352a10ce3a0771b6d1334a77394f105dd5fb468a47513164cf"));
            Assert.That(ZenRelease.Pinned.Windows.Name, Is.EqualTo("zenserver-win64.zip"));
            Assert.That(ZenRelease.Pinned.Windows.ArchiveSha256, Is.EqualTo("1dc0c68e613162e6a2d29d96a0bae52121922a9eb804966ce7b98ba181de999b"));
        });
    }

    [TestCase(0, "zenserver", "zen")]
    [TestCase(1, "zenserver.exe", "zen.exe")]
    public void SelectsNativeServerAndClient(int platformValue, string server, string client) {
        var platform = (EZenPlatform)platformValue;
        var asset = ZenRelease.Pinned.AssetFor(platform);
        Assert.Multiple(() => {
            Assert.That(asset.ServerFile, Is.EqualTo(server));
            Assert.That(asset.ClientFile, Is.EqualTo(client));
        });
    }
}
