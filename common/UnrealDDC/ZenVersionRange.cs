using System;
using System.Globalization;
using System.Linq;

namespace UnrealDDC;

sealed record ZenVersionRange(Version minimum, Version exclusiveMaximum, Version? exactVersion, string displayName) {
    public static ZenVersionRange Parse(string? value) {
        string expression = string.IsNullOrWhiteSpace(value) ? "5" : value.Trim();
        bool exact = expression.StartsWith('=');
        if (exact || expression.StartsWith('^')) {
            expression = expression[1..].Trim();
        }
        if (expression.StartsWith("v", StringComparison.OrdinalIgnoreCase)) {
            expression = expression[1..];
        }

        string[] parts = expression.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3) {
            throw InvalidVersion(value);
        }

        int[] numbers = [
            .. parts.Select(part => ParsePart(part, value)),
            .. Enumerable.Repeat(0, 3 - parts.Length)
        ];

        var minimum = new Version(numbers[0], numbers[1], numbers[2]);
        if (exact) {
            return parts.Length == 3
                ? new ZenVersionRange(minimum, minimum, minimum, $"={minimum}")
                : throw new InvalidOperationException($"Exact {EnvironmentVariableNames.ZEN_VERSION} selectors require major, minor, and patch components");
        }

        var maximum = numbers[0] switch {
            > 0 => new Version(checked(numbers[0] + 1), 0, 0),
            _ when numbers[1] > 0 => new Version(0, checked(numbers[1] + 1), 0),
            _ => new Version(0, 0, checked(numbers[2] + 1))
        };
        return new ZenVersionRange(minimum, maximum, null, $"^{expression}");
    }

    public bool Contains(Version version) => exactVersion is not null
        ? version == exactVersion
        : version >= minimum && version < exclusiveMaximum;

    static InvalidOperationException InvalidVersion(string? value) => new(
        $"{EnvironmentVariableNames.ZEN_VERSION} must be a semantic version or caret range, got '{value}'"
    );

    static int ParsePart(string part, string? value) => int.TryParse(
        part,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out int number
    )
        ? number
        : throw InvalidVersion(value);
}
