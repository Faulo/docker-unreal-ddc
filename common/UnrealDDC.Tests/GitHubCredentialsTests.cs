using System;
using NUnit.Framework;

namespace UnrealDDC.Tests;

[NonParallelizable]
public sealed class GitHubCredentialsTests {
    string? originalUsername;
    string? originalToken;

    [SetUp]
    public void SaveEnvironment() {
        originalUsername = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        originalToken = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
    }

    [TearDown]
    public void RestoreEnvironment() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, originalUsername);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, originalToken);
    }

    [Test]
    public void RequiresCredentialsForUpdateCheck() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, null);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, null);
        Assert.That(GitHubCredentials.FromEnvironment, Throws.InvalidOperationException);
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
        var credentials = GitHubCredentials.FromEnvironment();
        Assert.Multiple(() => {
            Assert.That(credentials.username, Is.EqualTo("user"));
            Assert.That(credentials.token, Is.EqualTo("token"));
        });
    }
}
