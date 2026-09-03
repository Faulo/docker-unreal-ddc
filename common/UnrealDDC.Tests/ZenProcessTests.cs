using System;
using NUnit.Framework;

namespace UnrealDDC.Tests;

[NonParallelizable]
public sealed class ZenProcessTests {
    string? originalUsername;
    string? originalToken;

    [SetUp]
    public void SetCredentials() {
        originalUsername = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        originalToken = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, "user");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, "secret-token");
    }

    [TearDown]
    public void RestoreCredentials() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, originalUsername);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, originalToken);
    }

    [Test]
    public void PreservesArgumentBoundariesAndRemovesInstallerCredentials() {
        var start = ZenProcess.CreateStartInfo("zenserver", ["--dedicated", "--data-dir=value with spaces"], "/working directory");
        Assert.Multiple(() => {
            Assert.That(start.FileName, Is.EqualTo("zenserver"));
            Assert.That(start.WorkingDirectory, Is.EqualTo("/working directory"));
            Assert.That(start.ArgumentList, Is.EqualTo(["--dedicated", "--data-dir=value with spaces"]));
            Assert.That(start.UseShellExecute, Is.False);
            Assert.That(start.Environment.ContainsKey(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR), Is.False);
            Assert.That(start.Environment.ContainsKey(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW), Is.False);
        });
    }

    [Test]
    public void StopsZenByConfiguredPortWithoutPassingInstallerCredentials() {
        var start = ZenProcess.CreateStopStartInfo("zen", "/working directory", 9123);
        Assert.Multiple(() => {
            Assert.That(start.FileName, Is.EqualTo("zen"));
            Assert.That(start.ArgumentList, Is.EqualTo(["--no-sentry", "down", "--port=9123", "--force"]));
            Assert.That(start.Environment.ContainsKey(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR), Is.False);
            Assert.That(start.Environment.ContainsKey(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW), Is.False);
        });
    }
}
