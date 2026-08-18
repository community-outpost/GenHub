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
        var dateMatch = Regex.Match(version, @"\b(\d{4})[-_.]?(\d{2})[-_.]?(\d{2})\b", RegexOptions.None, TimeSpan.FromSeconds(1));
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
    /// Matches "0", "0.0", "0.0.0", "1.0", "1.0.0", etc.
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
        string[] defaultVersions = { "0", "0.0", "0.0.0", "0.0.0.0", "1.0", "1.0.0", "1.0.0.0", "1" };

        return defaultVersions.Contains(normalized);
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
    /// Converts "101525_QFE2" to 1015252.
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
        // intentionally accepts only the original two-segment format.
        var parts = version.Split(
            '_',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return ExtractVersionFromVersionString(version);
        }

        var datePart = parts[0];
        var qfePart = parts[1];
        var hasQfePrefix = qfePart.StartsWith("QFE", StringComparison.OrdinalIgnoreCase);
        var qfeDigits = hasQfePrefix ? qfePart[3..] : string.Empty;

        if (datePart.Length != 6
            || !datePart.All(character => character is >= '0' and <= '9')
            || qfeDigits.Length == 0
            || !qfeDigits.All(character => character is >= '0' and <= '9')
            || !int.TryParse(qfeDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var qfe)
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

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitRegex();
}
