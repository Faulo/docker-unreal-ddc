using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace UnrealDDC.Tests;

[NonParallelizable]
public sealed class GitHubCredentialsTests {
    static readonly string[] credentialVariables = [
        EnvironmentVariableNames.UNREAL_CREDENTIALS_USR,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE
    ];
    readonly Dictionary<string, string?> originalValues = new();
    TemporaryDirectory directory = null!;

    [SetUp]
    public void SaveEnvironment() {
        directory = new TemporaryDirectory();
        foreach (string variable in credentialVariables) {
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
        directory.Dispose();
    }

    [Test]
    public void ReturnsNullWhenCredentialsAreNotConfigured() {
        Assert.That(GitHubCredentials.TryFromEnvironment(), Is.Null);
    }

    [Test]
    public void RequiresCredentialsForInitialInstallation() {
        Assert.That(GitHubCredentials.FromEnvironment, Throws.InvalidOperationException);
    }

    [TestCase("UNREAL_CREDENTIALS_USR", "user")]
    [TestCase("UNREAL_CREDENTIALS_PSW_FILE", "token-file")]
    public void RejectsIncompleteCredentialPair(string variable, string value) {
        Environment.SetEnvironmentVariable(variable, value);
        Assert.That(GitHubCredentials.TryFromEnvironment, Throws.InvalidOperationException);
    }

    [Test]
    public void ReadsAndTrimsDirectValues() {
        SetDirectCredentials("  user  ", "  token  ");

        var credentials = GitHubCredentials.FromEnvironment();

        Assert.Multiple(() => {
            Assert.That(credentials.username, Is.EqualTo("user"));
            Assert.That(credentials.token, Is.EqualTo("token"));
        });
    }

    [Test]
    public void ReadsAndTrimsFileBackedValues() {
        SetFileCredentials("  user\r\n", "  token\n");

        var credentials = GitHubCredentials.FromEnvironment();

        Assert.Multiple(() => {
            Assert.That(credentials.username, Is.EqualTo("user"));
            Assert.That(credentials.token, Is.EqualTo("token"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AcceptsMixedDirectAndFileBackedValues(bool usernameIsDirect) {
        string file = WriteFile("credential", usernameIsDirect ? "token" : "user");
        Environment.SetEnvironmentVariable(
            usernameIsDirect ? EnvironmentVariableNames.UNREAL_CREDENTIALS_USR : EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE,
            usernameIsDirect ? "user" : file
        );
        Environment.SetEnvironmentVariable(
            usernameIsDirect ? EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE : EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW,
            usernameIsDirect ? file : "token"
        );

        Assert.That(GitHubCredentials.FromEnvironment(), Is.EqualTo(new GitHubCredentials("user", "token")));
    }

    [TestCase("UNREAL_CREDENTIALS_USR", "UNREAL_CREDENTIALS_USR_FILE")]
    [TestCase("UNREAL_CREDENTIALS_PSW", "UNREAL_CREDENTIALS_PSW_FILE")]
    public void RejectsDirectAndFileConflict(string directVariable, string fileVariable) {
        SetDirectCredentials("user", "token");
        Environment.SetEnvironmentVariable(fileVariable, WriteFile("conflict", "different"));

        var exception = Assert.Throws<InvalidOperationException>(() => GitHubCredentials.FromEnvironment());

        Assert.That(exception!.Message, Does.Contain(directVariable).And.Contain(fileVariable));
    }

    [Test]
    public void ReportsMissingCredentialFileWithoutExposingOtherCredential() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE, Path.Combine(directory.path, "missing"));
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, "secret-token");

        var exception = Assert.Throws<InvalidOperationException>(() => GitHubCredentials.FromEnvironment());

        Assert.Multiple(() => {
            Assert.That(exception!.Message, Does.Contain(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE));
            Assert.That(exception.Message, Does.Not.Contain("secret-token"));
        });
    }

    [Test]
    public void ReportsUnreadableCredentialFileWithoutExposingOtherCredential() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE, directory.path);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, "secret-token");

        var exception = Assert.Throws<InvalidOperationException>(() => GitHubCredentials.FromEnvironment());

        Assert.Multiple(() => {
            Assert.That(exception!.Message, Does.Contain(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE));
            Assert.That(exception.Message, Does.Not.Contain("secret-token"));
        });
    }

    [Test]
    public void RejectsEmptyCredentialFile() {
        SetFileCredentials("user", " \r\n");

        var exception = Assert.Throws<InvalidOperationException>(() => GitHubCredentials.FromEnvironment());

        Assert.That(exception!.Message, Does.Contain(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE).And.Contain("empty"));
    }

    void SetDirectCredentials(string username, string token) {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, username);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, token);
    }

    void SetFileCredentials(string username, string token) {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE, WriteFile("username", username));
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE, WriteFile("token", token));
    }

    string WriteFile(string name, string contents) {
        string path = Path.Combine(directory.path, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
