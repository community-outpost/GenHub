using System.Globalization;
using System.Text;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Services.Providers.VersionSchemes;

/// <summary>
/// Numeric and semantic versions such as "20251226", "weekly-2025-12-26", "v1.7.2".
/// Applied to any provider that declares no scheme of its own.
/// </summary>
public sealed class NumericVersionScheme : VersionSchemeBase
{
    /// <summary>
    /// Six-digit values are read as YYMMDD so they order against full YYYYMMDD dates.
    /// </summary>
    private const long ShortDateFloor = 100000;
    private const long ShortDateCeiling = 991231;
    private const long CenturyOffset = 20000000;

    private static readonly string[] KnownPrefixes = ["weekly-", "release-", "version-"];

    /// <inheritdoc/>
    public override string SchemeId => VersionSchemeConstants.Numeric;

    /// <inheritdoc/>
    public override bool TryParse(string? version, out ContentVersion result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var normalized = Normalize(version);

        if (long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            result = new ContentVersion(ExpandShortDate(whole));
            return true;
        }

        var segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        var components = new long[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!long.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out components[i]))
            {
                return false;
            }
        }

        result = new ContentVersion(components);
        return true;
    }

    /// <inheritdoc/>
    public override int Compare(string? version1, string? version2)
    {
        if (string.IsNullOrWhiteSpace(version1) || string.IsNullOrWhiteSpace(version2))
        {
            return base.Compare(version1, version2);
        }

        var normalized1 = Normalize(version1);
        var normalized2 = Normalize(version2);

        var isNumeric1 = long.TryParse(normalized1, out var numeric1);
        var isNumeric2 = long.TryParse(normalized2, out var numeric2);

        if (isNumeric1 && isNumeric2)
        {
            return ExpandShortDate(numeric1).CompareTo(ExpandShortDate(numeric2));
        }

        var hasDot1 = version1.Contains('.');
        var hasDot2 = version2.Contains('.');

        // A dotted version with a major of 1 or higher outranks a bare date stamp,
        // so "1.20260116" is newer than "20260116" rather than astronomically older.
        if (hasDot1 && isNumeric2 && OutranksDateStamp(version1, numeric2))
        {
            return 1;
        }

        if (isNumeric1 && hasDot2 && OutranksDateStamp(version2, numeric1))
        {
            return -1;
        }

        if (hasDot1 || hasDot2)
        {
            return CompareSegments(version1, version2);
        }

        var digits1 = ExtractDigits(version1);
        var digits2 = ExtractDigits(version2);

        // Only collapse to digits when nothing but digits was dropped; otherwise
        // "beta2" and "2" would compare equal.
        var isPureDigits1 = normalized1.All(char.IsDigit);
        var isPureDigits2 = normalized2.All(char.IsDigit);

        if (isPureDigits1 && isPureDigits2
            && long.TryParse(digits1, out var extracted1)
            && long.TryParse(digits2, out var extracted2))
        {
            return ExpandShortDate(extracted1).CompareTo(ExpandShortDate(extracted2));
        }

        return string.Compare(version1, version2, StringComparison.Ordinal);
    }

    private static bool OutranksDateStamp(string dottedVersion, long dateStamp)
    {
        if (dateStamp <= ShortDateFloor)
        {
            return false;
        }

        var major = dottedVersion.Split('.')[0].TrimStart('v', 'V');
        return int.TryParse(major, out var majorNumber) && majorNumber >= 1;
    }

    private static string Normalize(string version)
    {
        var normalized = version;

        foreach (var prefix in KnownPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        normalized = normalized.TrimStart('v', 'V');

        if (normalized.Length == 10 && normalized[4] == '-' && normalized[7] == '-')
        {
            normalized = normalized.Replace("-", string.Empty);
        }

        return normalized;
    }

    private static long ExpandShortDate(long version) =>
        version is >= ShortDateFloor and <= ShortDateCeiling ? CenturyOffset + version : version;

    private static int CompareSegments(string version1, string version2)
    {
        var segments1 = version1.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var segments2 = version2.Split('.', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < Math.Max(segments1.Length, segments2.Length); i++)
        {
            var raw1 = i < segments1.Length ? segments1[i] : "0";
            var raw2 = i < segments2.Length ? segments2[i] : "0";

            var trimmed1 = raw1.TrimStart('v', 'V');
            var trimmed2 = raw2.TrimStart('v', 'V');

            if (long.TryParse(trimmed1, NumberStyles.None, CultureInfo.InvariantCulture, out var number1)
                && long.TryParse(trimmed2, NumberStyles.None, CultureInfo.InvariantCulture, out var number2))
            {
                if (number1 != number2)
                {
                    return number1.CompareTo(number2);
                }

                continue;
            }

            var segmentCompare = string.Compare(raw1, raw2, StringComparison.OrdinalIgnoreCase);
            if (segmentCompare != 0)
            {
                return segmentCompare;
            }
        }

        return 0;
    }

    private static string ExtractDigits(string version)
    {
        var digits = new StringBuilder(version.Length);

        foreach (var character in version)
        {
            if (char.IsDigit(character))
            {
                digits.Append(character);
            }
        }

        return digits.ToString();
    }
}
