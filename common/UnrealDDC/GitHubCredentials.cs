using System;

namespace UnrealDDC;

sealed record GitHubCredentials(string Username, string Token) {
    public static GitHubCredentials? FromEnvironment() {
        string? username = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        string? token = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
        bool hasUsername = !string.IsNullOrWhiteSpace(username);
        bool hasToken = !string.IsNullOrWhiteSpace(token);
        if (!hasUsername && !hasToken) {
            return null;
        }
        if (!hasUsername || !hasToken) {
            throw new InvalidOperationException($"{EnvironmentVariableNames.UNREAL_CREDENTIALS_USR} and {EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW} must be supplied together");
        }

        return new GitHubCredentials(username!.Trim(), token!.Trim());
    }
}
