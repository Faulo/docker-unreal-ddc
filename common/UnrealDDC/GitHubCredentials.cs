using System;
using System.IO;

namespace UnrealDDC;

sealed record GitHubCredentials(string username, string token) {
    public static GitHubCredentials FromEnvironment() => TryFromEnvironment()
        ?? throw new InvalidOperationException(
            $"{EnvironmentVariableNames.UNREAL_CREDENTIALS_USR} or {EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE}, and "
            + $"{EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW} or {EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE}, are required to check for Zen updates"
        );

    public static GitHubCredentials? TryFromEnvironment() {
        string? username = Resolve(
            EnvironmentVariableNames.UNREAL_CREDENTIALS_USR,
            EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE
        );
        string? token = Resolve(
            EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW,
            EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE
        );
        if (username is null && token is null) {
            return null;
        }
        if (username is null || token is null) {
            throw new InvalidOperationException(
                $"{EnvironmentVariableNames.UNREAL_CREDENTIALS_USR} or {EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE}, and "
                + $"{EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW} or {EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE}, must be supplied together"
            );
        }

        return new GitHubCredentials(username, token);
    }

    static string? Resolve(string directName, string fileName) {
        string? directValue = Environment.GetEnvironmentVariable(directName);
        string? configuredFile = Environment.GetEnvironmentVariable(fileName);
        if (directValue is not null && configuredFile is not null) {
            throw new InvalidOperationException($"{directName} and {fileName} cannot both be set");
        }
        if (directValue is not null) {
            return RequireValue(directValue, directName);
        }
        if (configuredFile is null) {
            return null;
        }
        if (string.IsNullOrWhiteSpace(configuredFile)) {
            throw new InvalidOperationException($"{fileName} must name a readable, non-empty file");
        }

        string value;
        try {
            value = File.ReadAllText(configuredFile);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            throw new InvalidOperationException($"The credential file configured by {fileName} could not be read", exception);
        }

        return RequireValue(value, fileName);
    }

    static string RequireValue(string value, string sourceName) => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"The credential value supplied by {sourceName} is empty")
        : value.Trim();
}
