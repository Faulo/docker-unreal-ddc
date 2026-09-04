using System;
using System.Globalization;
using System.Linq;

namespace UnrealDDC;

sealed record ZenVersionRange(Version minimum, Version exclusiveMaximum, Version? exactVersion, string displayName) {
    public static ZenVersionRange Parse(string? value) {
        string expression = string.IsNullOrWhiteSpace(value) ? "5" : value.Trim();
        if (expression.StartsWith("v", StringComparison.OrdinalIgnoreCase)) {
            expression = expression[1..];
        }

        bool wildcard = expression.EndsWith(".*", StringComparison.Ordinal);
        if (wildcard) {
            expression = expression[..^2];
        }

        string[] parts = expression.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3 || (wildcard && parts.Length == 3)) {
            throw InvalidVersion(value);
        }

        int[] numbers = [
            .. parts.Select(part => ParsePart(part, value)),
            .. Enumerable.Repeat(0, 3 - parts.Length)
        ];

        var minimum = new Version(numbers[0], numbers[1], numbers[2]);
        if (parts.Length == 3) {
            return new ZenVersionRange(minimum, minimum, minimum, minimum.ToString());
        }

        var maximum = parts.Length switch {
            1 => new Version(checked(numbers[0] + 1), 0, 0),
            2 => new Version(numbers[0], checked(numbers[1] + 1), 0),
            _ => throw InvalidVersion(value)
        };
        return new ZenVersionRange(minimum, maximum, null, $"{expression}.*");
    }

    public bool Contains(Version version) => exactVersion is not null
        ? version == exactVersion
        : version >= minimum && version < exclusiveMaximum;

    static InvalidOperationException InvalidVersion(string? value) => new(
        $"{EnvironmentVariableNames.ZEN_VERSION} must be a major, major.minor, or complete semantic version, got '{value}'"
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
