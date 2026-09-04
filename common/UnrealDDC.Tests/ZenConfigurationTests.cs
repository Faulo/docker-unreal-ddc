using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace UnrealDDC.Tests;

[NonParallelizable]
public sealed class ZenConfigurationTests {
    static readonly string[] variables = [
        EnvironmentVariableNames.ZEN_PORT,
        EnvironmentVariableNames.ZEN_DATA_DIR,
        EnvironmentVariableNames.ZEN_GC_DISKSIZE_SOFTLIMIT,
        EnvironmentVariableNames.ZEN_GC_LOW_DISKSPACE_THRESHOLD,
        EnvironmentVariableNames.ZEN_GC_CACHE_DURATION
    ];
    readonly Dictionary<string, string?> originalValues = new();

    [SetUp]
    public void SaveEnvironment() {
        foreach (string variable in variables) {
            originalValues[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [TearDown]
    public void RestoreEnvironment() {
        foreach ((string variable, string? value) in originalValues) {
            Environment.SetEnvironmentVariable(variable, value);
        }
        originalValues.Clear();
    }

    [TestCase("100GB", 100_000_000_000)]
    [TestCase("1000MB", 1_000_000_000)]
    [TestCase("1GiB", 1_073_741_824)]
    public void ParsesHumanReadableByteSizes(string value, long expected) {
        Assert.That(ZenConfiguration.ParseSize(value), Is.EqualTo(expected));
    }

    [TestCase("10D", 864_000)]
    [TestCase("1Y60S", 31_536_060)]
    [TestCase("PT1H30M", 5_400)]
    public void ParsesCompactDurations(string value, long expected) {
        Assert.That(ZenConfiguration.ParseDuration(value), Is.EqualTo(expected));
    }

    [Test]
    public void AddsNormalizedEnvironmentOptionsAndAdditionalArguments() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.ZEN_GC_DISKSIZE_SOFTLIMIT, "100GB");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.ZEN_GC_LOW_DISKSPACE_THRESHOLD, "1000MB");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.ZEN_GC_CACHE_DURATION, "1Y60S");

        var configuration = ZenConfiguration.FromEnvironment(Path.GetTempPath(), EZenPlatform.LINUX, ["--extra"]);

        Assert.That(configuration.arguments, Does.Contain("--gc-disksize-softlimit=100000000000"));
        Assert.That(configuration.arguments, Does.Contain("--gc-low-diskspace-threshold=1000000000"));
        Assert.That(configuration.arguments, Does.Contain("--gc-cache-duration-seconds=31536060"));
        Assert.That(configuration.arguments[^1], Is.EqualTo("--extra"));
    }
}
