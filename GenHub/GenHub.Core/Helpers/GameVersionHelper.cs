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

        // Extract all digits from the version string
        var digits = NonDigitRegex().Replace(version, string.Empty);

        // Take first 8 digits (YYYYMMDD format) to avoid overflow
        if (digits.Length > 8)
        {
            digits = digits[..8];
        }

        return int.TryParse(digits, out var result) ? result : 0;
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
    /// Parses a version string (MMDDYY_QFE#) used by Generals Online.
    /// </summary>
    /// <param name="version">The version string to parse.</param>
    /// <returns>A tuple containing the extracted date and QFE number, or null if parsing fails.</returns>
    public static (DateTime Date, int Qfe)? ParseGeneralsOnlineVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        try
        {
            // Format: MMDDYY_QFE# or DDMMYY_QFE# (General Online CDN uses MMDDYY)
            var parts = version.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return null;
            }

            var datePart = parts[0];
            var qfePart = parts[1].Replace("QFE", string.Empty, StringComparison.OrdinalIgnoreCase);

            if (datePart.Length != 6 || !int.TryParse(qfePart, out var qfe))
            {
                return null;
            }

            var month = int.Parse(datePart[0..2]);
            var day = int.Parse(datePart[2..4]);
            var year = 2000 + int.Parse(datePart[4..6]);

            return (new DateTime(year, month, day), qfe);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets a sortable integer version for Generals Online versions.
    /// Converts "101525_QFE2" to 1015252.
    /// </summary>
    /// <param name="version">The version string to convert.</param>
    /// <returns>A sortable integer, or 0 if parsing fails.</returns>
    public static int GetGeneralsOnlineSortableVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return 0;
        }

        var parsed = ParseGeneralsOnlineVersion(version);
        if (parsed != null)
        {
            var dateValue = int.Parse(parsed.Value.Date.ToString("MMddyy"));
            return (dateValue * 10) + parsed.Value.Qfe;
        }

        // Fallback: extract all digits
        var digitsOnly = string.Concat(version.Where(char.IsDigit));
        return int.TryParse(digitsOnly, out var result) ? result : 0;
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
