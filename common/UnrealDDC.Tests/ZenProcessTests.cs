using System;
using System.Diagnostics;
using NUnit.Framework;

namespace UnrealDDC.Tests;

[NonParallelizable]
public sealed class ZenProcessTests {
    string? _originalUsername;
    string? _originalToken;

    [SetUp]
    public void SetCredentials() {
        _originalUsername = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        _originalToken = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, "user");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, "secret-token");
    }

    [TearDown]
    public void RestoreCredentials() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, _originalUsername);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, _originalToken);
    }

    [Test]
    public void PreservesArgumentBoundariesAndRemovesInstallerCredentials() {
        var start = ZenProcess.CreateStartInfo("zenserver", ["--dedicated", "--data-dir=value with spaces"], "/working directory");
        Assert.Multiple(() => {
            Assert.That(start.FileName, Is.EqualTo("zenserver"));
            Assert.That(start.WorkingDirectory, Is.EqualTo("/working directory"));
            Assert.That(start.ArgumentList, Is.EqualTo(new[] { "--dedicated", "--data-dir=value with spaces" }));
            Assert.That(start.UseShellExecute, Is.False);
            Assert.That(start.Environment.ContainsKey(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR), Is.False);
            Assert.That(start.Environment.ContainsKey(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW), Is.False);
        });
    }
}
