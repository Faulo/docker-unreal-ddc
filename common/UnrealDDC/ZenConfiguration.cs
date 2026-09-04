using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace UnrealDDC;

sealed partial record ZenConfiguration(IReadOnlyList<string> arguments, int port) {

    public static ZenConfiguration FromEnvironment(
        string root,
        EZenPlatform platform,
        IEnumerable<string> additionalArguments
    ) {
        int port = ParsePort(Environment.GetEnvironmentVariable(EnvironmentVariableNames.ZEN_PORT));
        string dataDirectory = Environment.GetEnvironmentVariable(EnvironmentVariableNames.ZEN_DATA_DIR)?.Trim()
                               ?? Path.Combine(root, "data");
        if (!Path.IsPathFullyQualified(dataDirectory)) {
            throw new InvalidOperationException($"{EnvironmentVariableNames.ZEN_DATA_DIR} must be an absolute path");
        }

        var arguments = new List<string> {
            "--dedicated",
            $"--port={port.ToString(CultureInfo.InvariantCulture)}",
            $"--data-dir={Path.GetFullPath(dataDirectory)}",
            platform switch {
                EZenPlatform.LINUX => "--http=asio",
                EZenPlatform.WINDOWS => "--http=httpsys",
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
            },
            "--no-sentry",
            "--detach=false"
        };
        AddSizeArgument(arguments, EnvironmentVariableNames.ZEN_GC_DISKSIZE_SOFTLIMIT, "--gc-disksize-softlimit");
        AddSizeArgument(arguments, EnvironmentVariableNames.ZEN_GC_LOW_DISKSPACE_THRESHOLD, "--gc-low-diskspace-threshold");
        AddDurationArgument(arguments);
        arguments.AddRange(additionalArguments);
        return new ZenConfiguration(arguments, port);
    }

    public static long ParseSize(string value) {
        var match = SizePattern().Match(value.Trim());
        if (!match.Success
            || !decimal.TryParse(match.Groups["value"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount)) {
            throw new InvalidOperationException($"Invalid byte size '{value}'");
        }
        decimal multiplier = match.Groups["unit"].Value.ToUpperInvariant() switch {
            "" or "B" => 1m,
            "KB" => 1_000m,
            "MB" => 1_000_000m,
            "GB" => 1_000_000_000m,
            "TB" => 1_000_000_000_000m,
            "KIB" => 1_024m,
            "MIB" => 1_048_576m,
            "GIB" => 1_073_741_824m,
            "TIB" => 1_099_511_627_776m,
            _ => throw new InvalidOperationException($"Invalid byte-size unit in '{value}'")
        };
        decimal bytes = amount * multiplier;
        if (bytes < 0 || bytes > long.MaxValue || decimal.Truncate(bytes) != bytes) {
            throw new InvalidOperationException($"Byte size '{value}' does not resolve to a whole 64-bit byte count");
        }
        return decimal.ToInt64(bytes);
    }

    public static long ParseDuration(string value) {
        string expression = value.Trim().ToUpperInvariant();
        if (expression.StartsWith('P')) {
            expression = expression[1..];
        }
        expression = expression.Replace("T", string.Empty, StringComparison.Ordinal);
        var matches = DurationPartPattern().Matches(expression);
        if (matches.Count == 0 || string.Concat(matches).Length != expression.Length) {
            throw new InvalidOperationException($"Invalid duration '{value}'");
        }

        decimal seconds = 0;
        foreach (Match match in matches) {
            decimal amount = decimal.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            decimal multiplier = match.Groups["unit"].Value switch {
                "Y" => 365m * 24m * 60m * 60m,
                "W" => 7m * 24m * 60m * 60m,
                "D" => 24m * 60m * 60m,
                "H" => 60m * 60m,
                "M" => 60m,
                "S" => 1m,
                _ => throw new InvalidOperationException($"Invalid duration unit in '{value}'")
            };
            seconds += amount * multiplier;
        }
        if (seconds < 0 || seconds > long.MaxValue || decimal.Truncate(seconds) != seconds) {
            throw new InvalidOperationException($"Duration '{value}' does not resolve to a whole 64-bit second count");
        }
        return decimal.ToInt64(seconds);
    }

    static int ParsePort(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return 8558;
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535) {
            throw new InvalidOperationException($"{EnvironmentVariableNames.ZEN_PORT} must be an integer from 1 through 65535");
        }
        return port;
    }

    static void AddSizeArgument(List<string> arguments, string variable, string option) {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(value)) {
            arguments.Add($"{option}={ParseSize(value).ToString(CultureInfo.InvariantCulture)}");
        }
    }

    static void AddDurationArgument(List<string> arguments) {
        string? value = Environment.GetEnvironmentVariable(EnvironmentVariableNames.ZEN_GC_CACHE_DURATION);
        if (!string.IsNullOrWhiteSpace(value)) {
            arguments.Add($"--gc-cache-duration-seconds={ParseDuration(value).ToString(CultureInfo.InvariantCulture)}");
        }
    }

    [GeneratedRegex(
        @"^(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>B|KB|MB|GB|TB|KIB|MIB|GIB|TIB)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SizePattern();

    [GeneratedRegex(
        @"(?<value>[0-9]+(?:\.[0-9]+)?)(?<unit>Y|W|D|H|M|S)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex DurationPartPattern();
}
