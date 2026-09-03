using System;
using NUnit.Framework;

namespace UnrealDDC.Tests;

public sealed class ZenReleaseTests {
    [TestCase(0, "zenserver", "zen")]
    [TestCase(1, "zenserver.exe", "zen.exe")]
    public void SelectsNativeServerAndClient(int platformValue, string server, string client) {
        var platform = (EZenPlatform)platformValue;
        var linux = new ZenReleaseAsset(1, "linux.zip", "hash", "zenserver", "zen");
        var windows = new ZenReleaseAsset(2, "windows.zip", "hash", "zenserver.exe", "zen.exe");
        var asset = new ZenRelease("v5.8.20", new Version(5, 8, 20), linux, windows).AssetFor(platform);
        Assert.Multiple(() => {
            Assert.That(asset.serverFile, Is.EqualTo(server));
            Assert.That(asset.clientFile, Is.EqualTo(client));
        });
    }

    [TestCase("5", "5.0.0", true)]
    [TestCase("5", "5.99.1", true)]
    [TestCase("5", "6.0.0", false)]
    [TestCase("5.8", "5.9.0", true)]
    [TestCase("^5.8.20", "5.8.19", false)]
    [TestCase("^5.8.20", "5.10.0", true)]
    [TestCase("=5.7.4", "5.7.4", true)]
    [TestCase("=5.7.4", "5.7.5", false)]
    public void ParsesImplicitAndExplicitVersionRanges(string expression, string candidate, bool expected) {
        Assert.That(ZenVersionRange.Parse(expression).Contains(Version.Parse(candidate)), Is.EqualTo(expected));
    }
}
