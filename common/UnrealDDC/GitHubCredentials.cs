using System;
using System.IO;
using System.Security;

namespace UnrealDDC;

sealed record GitHubCredentials(string username, string token) {
    public static GitHubCredentials FromEnvironment() => TryFromEnvironment()
        ?? throw new InvalidOperationException(
            $"{EnvironmentVariableNames.UNREAL_CREDENTIALS_USR} and {EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW} are required to check for Zen updates"
        );

    public static GitHubCredentials? TryFromEnvironment() {
        string? username = ResolveValue(
            EnvironmentVariableNames.UNREAL_CREDENTIALS_USR,
            EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE
        );
        string? token = ResolveValue(
            EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW,
            EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE
        );
        if (username is null && token is null) {
            return null;
        }
        if (username is null || token is null) {
            throw new InvalidOperationException(
                $"{EnvironmentVariableNames.UNREAL_CREDENTIALS_USR} and {EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW} must be supplied together, directly or through their _FILE variants"
            );
        }

        return new GitHubCredentials(username, token);
    }

    static string? ResolveValue(string directVariable, string fileVariable) {
        string? directValue = Environment.GetEnvironmentVariable(directVariable);
        string? filePath = Environment.GetEnvironmentVariable(fileVariable);
        if (directValue is not null && filePath is not null) {
            throw new InvalidOperationException($"Set either {directVariable} or {fileVariable}, not both");
        }
        if (directValue is not null) {
            return RequireValue(directVariable, directValue);
        }
        if (filePath is null) {
            return null;
        }
        if (string.IsNullOrWhiteSpace(filePath)) {
            throw new InvalidOperationException($"{fileVariable} must contain a file path");
        }

        string value;
        try {
            value = File.ReadAllText(filePath.Trim());
        } catch (Exception exception) when (exception is ArgumentException
                                            or IOException
                                            or NotSupportedException
                                            or SecurityException
                                            or UnauthorizedAccessException) {
            throw new InvalidOperationException($"The credential file specified by {fileVariable} could not be read", exception);
        }
        return RequireValue(fileVariable, value);
    }

    static string RequireValue(string variable, string value) => !string.IsNullOrWhiteSpace(value)
        ? value.Trim()
        : throw new InvalidOperationException($"The credential value supplied through {variable} must not be empty");
}
