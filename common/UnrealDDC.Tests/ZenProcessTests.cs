using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UnrealDDC.Tests;

[NonParallelizable]
public sealed class ZenProcessTests {
    static readonly string[] credentialVariables = [
        EnvironmentVariableNames.UNREAL_CREDENTIALS_USR,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE
    ];
    readonly Dictionary<string, string?> originalValues = new();

    [SetUp]
    public void SetCredentials() {
        foreach (string variable in credentialVariables) {
            originalValues[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, "secret");
        }
    }

    [TearDown]
    public void RestoreCredentials() {
        foreach ((string variable, string? value) in originalValues) {
            Environment.SetEnvironmentVariable(variable, value);
        }
        originalValues.Clear();
    }

    [Test]
    public void PreservesArgumentBoundariesAndRemovesInstallerCredentials() {
        var start = ZenProcess.CreateStartInfo("zenserver", ["--dedicated", "--data-dir=value with spaces"], "/working directory");
        Assert.Multiple(() => {
            Assert.That(start.FileName, Is.EqualTo("zenserver"));
            Assert.That(start.WorkingDirectory, Is.EqualTo("/working directory"));
            Assert.That(start.ArgumentList, Is.EqualTo(["--dedicated", "--data-dir=value with spaces"]));
            Assert.That(start.UseShellExecute, Is.False);
            foreach (string variable in credentialVariables) {
                Assert.That(start.Environment.ContainsKey(variable), Is.False, variable);
            }
        });
    }

    [Test]
    public void StopsZenByConfiguredPortWithoutPassingInstallerCredentials() {
        var start = ZenProcess.CreateStopStartInfo("zen", "/working directory", 9123);
        Assert.Multiple(() => {
            Assert.That(start.FileName, Is.EqualTo("zen"));
            Assert.That(start.ArgumentList, Is.EqualTo(["--no-sentry", "down", "--port=9123", "--force"]));
            foreach (string variable in credentialVariables) {
                Assert.That(start.Environment.ContainsKey(variable), Is.False, variable);
            }
        });
    }
}
