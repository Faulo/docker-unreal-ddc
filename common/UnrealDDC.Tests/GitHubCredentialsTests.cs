using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace UnrealDDC.Tests;

[NonParallelizable]
public sealed class GitHubCredentialsTests {
    static readonly string[] variables = [
        EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_USR,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW,
        EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE
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

    [Test]
    public void AllowsMissingCredentialsForCachedRestart() {
        Assert.That(GitHubCredentials.TryFromEnvironment(), Is.Null);
    }

    [Test]
    public void RequiresCredentialsForUpdateCheck() {
        Assert.That(GitHubCredentials.FromEnvironment, Throws.InvalidOperationException.With.Message.Contains(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT));
    }

    [Test]
    public void AcceptsDirectPatWithoutUsername() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT, "  direct-token  ");
        Assert.That(GitHubCredentials.FromEnvironment().token, Is.EqualTo("direct-token"));
    }

    [Test]
    public void AcceptsFileBackedPatWithoutUsername() {
        using var directory = new TemporaryDirectory();
        Environment.SetEnvironmentVariable(
            EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE,
            WriteFile(directory.path, "pat", " file-token\r\n")
        );
        Assert.That(GitHubCredentials.FromEnvironment().token, Is.EqualTo("file-token"));
    }

    [Test]
    public void RejectsBothPatForms() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT, "direct-token");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE, "missing-file");
        Assert.That(GitHubCredentials.TryFromEnvironment, Throws.InvalidOperationException.With.Message.Contains(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE));
    }

    [TestCase(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT)]
    [TestCase(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE)]
    public void RejectsEmptyPat(string variable) {
        Environment.SetEnvironmentVariable(variable, " ");
        Assert.That(GitHubCredentials.TryFromEnvironment, Throws.InvalidOperationException.With.Message.Contains(variable));
    }

    [Test]
    public void RejectsUnreadablePatFileWithoutExposingItsPath() {
        using var directory = new TemporaryDirectory();
        string missingPath = Path.Combine(directory.path, "missing-pat");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE, missingPath);
        var exception = Assert.Throws<InvalidOperationException>(() => GitHubCredentials.TryFromEnvironment());
        Assert.Multiple(() => {
            Assert.That(exception!.Message, Does.Contain(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE));
            Assert.That(exception.Message, Does.Not.Contain(missingPath));
        });
    }

    [Test]
    public void RejectsEmptyPatFile() {
        using var directory = new TemporaryDirectory();
        Environment.SetEnvironmentVariable(
            EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE,
            WriteFile(directory.path, "pat", " \r\n")
        );
        Assert.That(GitHubCredentials.TryFromEnvironment, Throws.InvalidOperationException.With.Message.Contains("must not be empty"));
    }

    [TestCase(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR)]
    [TestCase(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE)]
    [TestCase(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW)]
    [TestCase(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE)]
    public void RejectsMixingPatAndLegacyVariablesBeforeReadingFiles(string legacyVariable) {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT, "pat-token");
        Environment.SetEnvironmentVariable(legacyVariable, "legacy-value");
        Assert.That(GitHubCredentials.TryFromEnvironment, Throws.InvalidOperationException.With.Message.Contains(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT));
    }

    [Test]
    public void RejectsFileBackedPatMixedWithLegacyBeforeReadingEitherFile() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE, "missing-pat-file");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE, "missing-legacy-file");
        Assert.That(GitHubCredentials.TryFromEnvironment, Throws.InvalidOperationException.With.Message.Contains(EnvironmentVariableNames.UNREAL_CREDENTIALS_PAT_FILE));
    }

    [TestCase("user", null)]
    [TestCase(null, "token")]
    public void RejectsIncompleteCredentialPair(string? username, string? token) {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, username);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, token);
        Assert.That(GitHubCredentials.TryFromEnvironment, Throws.InvalidOperationException);
    }

    [Test]
    public void TrimsDirectCredentialValues() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, "  user  ");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, "  token  ");
        var credentials = GitHubCredentials.FromEnvironment();
        Assert.Multiple(() => {
            Assert.That(credentials.token, Is.EqualTo("token"));
        });
    }

    [Test]
    public void WarnsAboutLegacyVariablesWithoutPrintingCredentialValues() {
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, "secret-user");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, "secret-token");
        using var warning = new StringWriter();
        var originalError = Console.Error;
        try {
            Console.SetError(warning);
            Assert.That(GitHubCredentials.TryFromEnvironment()!.token, Is.EqualTo("secret-token"));
        } finally {
            Console.SetError(originalError);
        }
        Assert.Multiple(() => {
            Assert.That(warning.ToString(), Does.Contain(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR));
            Assert.That(warning.ToString(), Does.Contain(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW));
            Assert.That(warning.ToString(), Does.Contain("deprecated"));
            Assert.That(warning.ToString(), Does.Not.Contain("secret-user"));
            Assert.That(warning.ToString(), Does.Not.Contain("secret-token"));
        });
    }

    [Test]
    public void ReadsCredentialsFromFilesAndTrimsSecretNewlines() {
        using var directory = new TemporaryDirectory();
        string usernameFile = WriteFile(directory.path, "username", " file-user\n");
        string tokenFile = WriteFile(directory.path, "token", " file-token\r\n");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE, usernameFile);
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE, tokenFile);

        var credentials = GitHubCredentials.FromEnvironment();

        Assert.Multiple(() => {
            Assert.That(credentials.token, Is.EqualTo("file-token"));
        });
    }

    [Test]
    public void AllowsMixedDirectAndFileCredentials() {
        using var directory = new TemporaryDirectory();
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, "direct-user");
        Environment.SetEnvironmentVariable(
            EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE,
            WriteFile(directory.path, "token", "file-token")
        );

        var credentials = GitHubCredentials.FromEnvironment();

        Assert.Multiple(() => {
            Assert.That(credentials.token, Is.EqualTo("file-token"));
        });
    }

    [Test]
    public void AllowsFileUsernameWithDirectToken() {
        using var directory = new TemporaryDirectory();
        Environment.SetEnvironmentVariable(
            EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE,
            WriteFile(directory.path, "username", "file-user")
        );
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, "direct-token");

        var credentials = GitHubCredentials.FromEnvironment();

        Assert.Multiple(() => {
            Assert.That(credentials.token, Is.EqualTo("direct-token"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RejectsDirectAndFileFormsTogether(bool username) {
        using var directory = new TemporaryDirectory();
        string directVariable = username
            ? EnvironmentVariableNames.UNREAL_CREDENTIALS_USR
            : EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW;
        string fileVariable = username
            ? EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE
            : EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE;
        Environment.SetEnvironmentVariable(directVariable, "direct-secret");
        Environment.SetEnvironmentVariable(fileVariable, WriteFile(directory.path, "secret", "file-secret"));

        Assert.That(GitHubCredentials.TryFromEnvironment, Throws.InvalidOperationException.With.Message.Contains(fileVariable));
    }

    [Test]
    public void RejectsMissingCredentialFileWithoutExposingItsPath() {
        using var directory = new TemporaryDirectory();
        string missingPath = Path.Combine(directory.path, "missing-token");
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE, missingPath);

        var exception = Assert.Throws<InvalidOperationException>(() => GitHubCredentials.TryFromEnvironment());

        Assert.Multiple(() => {
            Assert.That(exception!.Message, Does.Contain(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE));
            Assert.That(exception.Message, Does.Not.Contain(missingPath));
        });
    }

    [Test]
    public void RejectsUnreadableCredentialFileWithoutExposingItsPath() {
        using var directory = new TemporaryDirectory();
        Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE, directory.path);

        var exception = Assert.Throws<InvalidOperationException>(() => GitHubCredentials.TryFromEnvironment());

        Assert.Multiple(() => {
            Assert.That(exception!.Message, Does.Contain(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE));
            Assert.That(exception.Message, Does.Not.Contain(directory.path));
        });
    }

    [Test]
    public void RejectsEmptyCredentialFile() {
        using var directory = new TemporaryDirectory();
        Environment.SetEnvironmentVariable(
            EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE,
            WriteFile(directory.path, "token", " \r\n")
        );

        Assert.That(
            GitHubCredentials.TryFromEnvironment,
            Throws.InvalidOperationException.With.Message.Contains("must not be empty")
        );
    }

    static string WriteFile(string directory, string name, string content) {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
