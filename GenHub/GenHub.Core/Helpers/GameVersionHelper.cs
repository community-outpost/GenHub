using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper class for version string operations.
/// </summary>
public static partial class GameVersionHelper
{
    /// <summary>
    /// Determines whether a detected client version carries no usable version information.
    /// The set is deliberately broad: the manifest id generator rejects any version that is not
    /// numeric, so a value that slips through here throws when an id is minted from it.
    /// </summary>
    /// <param name="version">The detected version string.</param>
    /// <returns><c>true</c> when the version is absent or a placeholder; otherwise <c>false</c>.</returns>
    public static bool IsUnknownVersion([NotNullWhen(false)] string? version)
    {
        return string.IsNullOrWhiteSpace(version)
            || version.Equals(GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase)
            || version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase)
            || version.Equals(GameClientConstants.AutoUpdatedVersion, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the manifest version a game type falls back to when no version could be detected.
    /// Installation manifests are pooled under this version, so every site that mints an
    /// installation manifest id must agree on it or the id resolves to no manifest.
    /// </summary>
    /// <param name="gameType">The game type. Only Generals and Zero Hour are supported.</param>
    /// <returns>The default manifest version for the game type.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a game type with no known default.</exception>
    public static string GetDefaultManifestVersion(GameType gameType)
    {
        return gameType switch
        {
            GameType.ZeroHour => ManifestConstants.ZeroHourManifestVersion,
            GameType.Generals => ManifestConstants.GeneralsManifestVersion,
            _ => throw new ArgumentOutOfRangeException(
                nameof(gameType),
                gameType,
                "No default manifest version exists for this game type."),
        };
    }

    /// <summary>
    /// Resolves the version to use when minting a game installation manifest id, applying the
    /// game-type default when the client reports no usable version.
    /// </summary>
    /// <param name="detectedVersion">The version reported by the detected client.</param>
    /// <param name="gameType">The game type. Types other than Generals and Zero Hour have no
    /// default and resolve to an empty version rather than throwing.</param>
    /// <returns>
    /// The detected version when usable, the game-type default when it is not, or an empty string
    /// when the game type has no default.
    /// </returns>
    public static string ResolveInstallationVersion(string? detectedVersion, GameType gameType)
    {
        if (!IsUnknownVersion(detectedVersion))
        {
            return detectedVersion!;
        }

        // A game type with no default has no id that would honestly describe it. Returning empty
        // rather than the detected value matters: this branch only runs when the version is already
        // unknown, and the sentinels are non-numeric, so passing one through throws in the id
        // generator. Empty normalizes to 0, which is what null and whitespace already did.
        return gameType is GameType.Generals or GameType.ZeroHour
            ? GetDefaultManifestVersion(gameType)
            : string.Empty;
    }

    /// <summary>
    /// Extracts a numeric version from a version string like "2025-11-07" or "weekly-2025-11-21".
    /// Extracts all digits and returns them as an integer (e.g., "2025-11-07" -> 20251107).
    /// </summary>
    /// <param name="version">The version string to parse.</param>
    /// <returns>The numeric version as an integer, or 0 if parsing fails.</returns>
    public static int ExtractVersionFromVersionString(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return 0;
        }

        // Try extracting an 8-digit date pattern first (e.g., "2025-11-07", "weekly-2025-11-21", "1.20260116")
        var dateMatch = EightDigitDateRegex().Match(version);
        if (dateMatch.Success && int.TryParse($"{dateMatch.Groups[1].Value}{dateMatch.Groups[2].Value}{dateMatch.Groups[3].Value}", NumberStyles.Integer, CultureInfo.InvariantCulture, out var dateVal))
        {
            return dateVal;
        }

        // Extract all digits from the version string
        var digits = NonDigitRegex().Replace(version, string.Empty);
        if (string.IsNullOrEmpty(digits))
        {
            return 0;
        }

        digits = digits.TrimStart('0');
        if (digits.Length == 0)
        {
            return 0;
        }

        if (digits.Length > 10)
        {
            // int.MaxValue is 10 digits; truncate to 10 digits for legacy manifest ID compatibility
            digits = digits[..10];
        }

        if (long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longResult))
        {
            if (longResult > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)longResult;
        }

        return 0;
    }

    /// <summary>
    /// Checks if a version string is a "default" version that shouldn't be displayed.
    /// Matches "0", "0.0", "0.00", "0.0.0", "1", "1.0", "1.00", "1.0.0", etc.
    /// </summary>
    /// <param name="version">The version string to check.</param>
    /// <returns>True if it is a default version, false otherwise.</returns>
    public static bool IsDefaultVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return true;
        }

        var normalized = version.Trim().ToLowerInvariant();

        // Remove 'v' prefix if present
        if (normalized.StartsWith("v"))
        {
            normalized = normalized.Substring(1);
        }

        // Common default versions
        string[] defaultVersions = { "0", "0.0", "0.00", "0.000", "0.0.0", "0.0.0.0", "1", "1.0", "1.00", "1.000", "1.0.0", "1.0.0.0" };

        return defaultVersions.Contains(normalized);
    }

    /// <summary>
    /// Formats a version string for display in UI badges, or returns null if it shouldn't be displayed.
    /// Suppresses empty, whitespace, default (e.g. 0, 1.0, 1.00), and unknown sentinel versions.
    /// Prefixes with 'v' if the version starts with a digit.
    /// </summary>
    /// <param name="version">The raw version string.</param>
    /// <returns>The formatted version string, or null.</returns>
    public static string? FormatDisplayVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) ||
            IsDefaultVersion(version) ||
            IsUnknownVersion(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 1 && char.IsDigit(trimmed[1]))
        {
            return trimmed;
        }

        if (char.IsDigit(trimmed[0]))
        {
            return $"v{trimmed}";
        }

        return trimmed;
    }

    /// <summary>
    /// Converts a version string to a normalized integer format.
    /// Examples: "1.04" -> 104, "1.08" -> 108, "20251226" -> 20251226.
    /// Used primarily for manifest ID components where a simple integer is needed.
    /// </summary>
    /// <param name="version">The version string to normalize.</param>
    /// <returns>A normalized integer representation of the version.</returns>
    public static int NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return 0;
        }

        // Handle semantic versions like 1.04
        if (version.Contains('.'))
        {
            var parts = version.Split('.');
            if (parts.Length >= 1 && int.TryParse(parts[0], out int major))
            {
                int minor = 0;
                if (parts.Length >= 2)
                {
                    _ = int.TryParse(parts[1], out minor);
                }

                return (major * 100) + minor;
            }
        }

        // Try to parse as direct integer
        if (int.TryParse(version, out int parsed))
        {
            return parsed;
        }

        // Fallback to extraction for composite strings
        return ExtractVersionFromVersionString(version);
    }

    /// <summary>
    /// Builds the numeric version component of a Generals Online manifest ID.
    /// Converts "101525_QFE2" to 1015252, and "082826" to 828260.
    /// </summary>
    /// <remarks>
    /// This value identifies a release inside an existing manifest ID; it is not a sort key.
    /// MMddyy is month-major and drops leading zeros, so it does not order across months or
    /// years — use <see cref="Interfaces.Providers.IContentVersionComparer"/> for that. The
    /// encoding is frozen because changing it would invalidate the IDs of installed content.
    /// </remarks>
    /// <param name="version">The version string to convert.</param>
    /// <returns>The manifest ID component, or 0 if parsing fails.</returns>
    public static int GetGeneralsOnlineManifestIdComponent(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return 0;
        }

        // Preserve the exact legacy behavior used to generate installed manifest IDs.
        // Extended versions previously fell through to digit extraction, so this encoder
        // intentionally accepts the original two-segment format or single-segment day releases.
        var parts = version.Split(
            '_',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 1 && parts.Length != 2)
        {
            return ExtractVersionFromVersionString(version);
        }

        var datePart = parts[0];
        var qfe = 0;
        if (parts.Length == 2)
        {
            var qfePart = parts[1];
            var hasQfePrefix = qfePart.StartsWith("QFE", StringComparison.OrdinalIgnoreCase);
            var qfeDigits = hasQfePrefix ? qfePart[3..] : string.Empty;

            if (qfeDigits.Length == 0
                || !qfeDigits.All(character => character is >= '0' and <= '9')
                || !int.TryParse(qfeDigits, NumberStyles.None, CultureInfo.InvariantCulture, out qfe))
            {
                return ExtractVersionFromVersionString(version);
            }
        }

        if (datePart.Length != 6
            || !datePart.All(character => character is >= '0' and <= '9')
            || !DateOnly.TryParseExact(datePart, "MMddyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return ExtractVersionFromVersionString(version);
        }

        try
        {
            var month = date.Month;
            var day = date.Day;
            var twoDigitYear = date.Year % 100;
            var mmddyy = (month * 10000) + (day * 100) + twoDigitYear;
            return checked((mmddyy * 10) + qfe);
        }
        catch (OverflowException)
        {
            return ExtractVersionFromVersionString(version);
        }
    }

    /// <summary>
    /// Parses a version string to a weighted integer for comparative semantic versioning.
    /// Handles versions like "1.04", "1.08", "2.0.0" etc.
    /// </summary>
    /// <param name="version">The version string to parse.</param>
    /// <returns>A weighted integer for comparison.</returns>
    public static int ParseVersionToInt(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return 0;
        }

        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var result = 0;
        var multiplier = 10000;

        foreach (var part in parts)
        {
            if (int.TryParse(part, out var value))
            {
                result += value * multiplier;
                multiplier /= 100;

                if (multiplier < 1)
                {
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Strips a leading 'v' or 'V' character from a version or tag string if present.
    /// </summary>
    /// <param name="tag">The version or tag string.</param>
    /// <returns>The string without the leading version prefix.</returns>
    public static string StripVersionPrefix(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var trimmed = tag.Trim();
        if ((trimmed.StartsWith('v') || trimmed.StartsWith('V')) &&
            trimmed.Length > 1 &&
            char.IsDigit(trimmed[1]))
        {
            return trimmed[1..];
        }

        return trimmed;
    }

    /// <summary>
    /// Formats a numeric manifest version segment into a standard version string.
    /// Handles Generals Online (D6 formatting), date-based versions (preserving YYYYMMDD),
    /// and standard major/minor numeric versions (dividing by 100 when &gt;= 100).
    /// </summary>
    /// <param name="versionNumber">The parsed integer version number.</param>
    /// <param name="publisherType">The publisher type identifier.</param>
    /// <param name="includePrefix">Whether to include the 'v' prefix for semantic versions.</param>
    /// <returns>The formatted version string, or an empty string if versionNumber &lt;= 0.</returns>
    public static string FormatNumericManifestVersion(int versionNumber, string? publisherType, bool includePrefix = false)
    {
        if (versionNumber <= 0)
        {
            return string.Empty;
        }

        if (string.Equals(publisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
        {
            return versionNumber.ToString("D6");
        }

        if (versionNumber >= ManifestConstants.DateBasedVersionThreshold)
        {
            return versionNumber.ToString();
        }

        var prefix = includePrefix ? "v" : string.Empty;
        return versionNumber >= ManifestConstants.NumericVersionDivisorThreshold
            ? $"{prefix}{versionNumber / ManifestConstants.NumericVersionDivisorThreshold}.{versionNumber % ManifestConstants.NumericVersionDivisorThreshold:D2}"
            : $"{prefix}{versionNumber}";
    }

    /// <summary>
    /// Attempts to parse a version string into a strict numeric version number.
    /// Handles integer versions (e.g. "000104" -&gt; 104, "104" -&gt; 104, "20260821" -&gt; 20260821)
    /// and two-part semantic versions (e.g. "1.04" -&gt; 104, "v1.04" -&gt; 104).
    /// Does not match versions containing alphanumeric suffixes or non-numeric characters (e.g. "1.04b").
    /// </summary>
    /// <param name="version">The version string to parse.</param>
    /// <param name="numericVersion">When this method returns, contains the parsed numeric version if successful, or 0 if parsing failed.</param>
    /// <returns>True if the version string is strictly numeric and greater than 0; otherwise, false.</returns>
    public static bool TryParseStrictNumericVersion(string? version, out int numericVersion)
    {
        numericVersion = 0;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var trimmed = version.Trim().TrimStart('v', 'V');
        if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var direct) && direct > 0)
        {
            numericVersion = direct;
            return true;
        }

        if (trimmed.Contains('.'))
        {
            var parts = trimmed.Split('.');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) &&
                parts[1].Length <= 2 &&
                major >= 0 &&
                minor >= 0)
            {
                var computed = (major * ManifestConstants.NumericVersionDivisorThreshold) + minor;
                if (computed > 0)
                {
                    numericVersion = computed;
                    return true;
                }
            }
        }

        return false;
    }

    [GeneratedRegex(@"\b(\d{4})[-_.]?(\d{2})[-_.]?(\d{2})\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EightDigitDateRegex();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitRegex();
}
