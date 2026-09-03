using System;
using NUnit.Framework;

namespace UnrealDDC.Tests;

[NonParallelizable]
public sealed class GitHubCredentialsTests {
    string? _originalUsername;
    string? _originalToken;

    [SetUp]
    public void SaveEnvironment() {
        _originalUsername = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        _originalToken = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
    }

    [TearDown]
    public void RestoreEnvironment() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, _originalUsername);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, _originalToken);
    }

    [Test]
    public void AllowsCredentialsToBeOmittedAfterInstallation() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, null);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, null);
        Assert.That(GitHubCredentials.FromEnvironment(), Is.Null);
    }

    [TestCase("user", null)]
    [TestCase(null, "token")]
    public void RejectsIncompleteCredentialPair(string? username, string? token) {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, username);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, token);
        Assert.That(GitHubCredentials.FromEnvironment, Throws.InvalidOperationException);
    }

    [Test]
    public void TrimsCredentialValues() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, "  user  ");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, "  token  ");
        var credentials = GitHubCredentials.FromEnvironment()!;
        Assert.Multiple(() => {
            Assert.That(credentials.Username, Is.EqualTo("user"));
            Assert.That(credentials.Token, Is.EqualTo("token"));
        });
    }
}
