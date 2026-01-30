using GenHub.Core.Constants;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides version comparison utilities for different version formats used by publishers.
/// </summary>
public static class VersionComparer
{
    /// <summary>
    /// Compares two version strings based on the publisher type.
    /// </summary>
    /// <param name="version1">The first version to compare.</param>
    /// <param name="version2">The second version to compare.</param>
    /// <param name="publisherType">The publisher type to determine version format.</param>
    /// <returns>
    /// Less than zero if version1 is less than version2.
    /// Zero if version1 equals version2.
    /// Greater than zero if version1 is greater than version2.
    /// </returns>
    public static int CompareVersions(string? version1, string? version2, string? publisherType)
    {
        // Handle null/empty cases
        if (string.IsNullOrWhiteSpace(version1) && string.IsNullOrWhiteSpace(version2))
            return 0;
        if (string.IsNullOrWhiteSpace(version1))
            return -1;
        if (string.IsNullOrWhiteSpace(version2))
            return 1;

        // Determine comparison strategy based on publisher type
        if (string.Equals(publisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            // Community Outpost uses date-based versions (YYYY-MM-DD)
            return CompareDateVersions(version1, version2);
        }
        else if (string.Equals(publisherType, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            // TheSuperHackers uses numeric versions (e.g., "20251226")
            return CompareNumericVersions(version1, version2);
        }
        else if (string.Equals(publisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
        {
            // GeneralsOnline uses numeric versions
            return CompareNumericVersions(version1, version2);
        }

        // Default: try numeric comparison, fall back to string comparison
        var numericResult = TryCompareNumericVersions(version1, version2);
        if (numericResult.HasValue)
            return numericResult.Value;

        // Fall back to Natural Sort comparison (alphanumeric chunks)
        return NaturalCompare(version1, version2);
    }

    /// <summary>
    /// Compares two date-based version strings in YYYY-MM-DD format.
    /// </summary>
    /// <param name="date1">The first date version.</param>
    /// <param name="date2">The second date version.</param>
    /// <returns>Comparison result.</returns>
    private static int CompareDateVersions(string date1, string date2)
    {
        // Try to parse as dates
        if (TryParseDateVersion(date1, out var parsedDate1) && TryParseDateVersion(date2, out var parsedDate2))
        {
            return parsedDate1.CompareTo(parsedDate2);
        }

        // If parsing fails, try to extract numeric representation (YYYYMMDD)
        var numeric1 = ExtractNumericFromDate(date1);
        var numeric2 = ExtractNumericFromDate(date2);

        if (numeric1.HasValue && numeric2.HasValue)
        {
            return numeric1.Value.CompareTo(numeric2.Value);
        }

        // Fall back to string comparison
        return string.Compare(date1, date2, StringComparison.Ordinal);
    }

    /// <summary>
    /// Compares two numeric version strings.
    /// </summary>
    /// <param name="ver1">The first version.</param>
    /// <param name="ver2">The second version.</param>
    /// <returns>Comparison result.</returns>
    private static int CompareNumericVersions(string ver1, string ver2)
    {
        // Try to parse as long integers for direct comparison
        if (long.TryParse(ver1, out var num1) && long.TryParse(ver2, out var num2))
        {
            return num1.CompareTo(num2);
        }

        // Try to extract digits and compare
        var digits1 = ExtractDigits(ver1);
        var digits2 = ExtractDigits(ver2);

        if (long.TryParse(digits1, out num1) && long.TryParse(digits2, out num2))
        {
            return num1.CompareTo(num2);
        }

        // Fall back to string comparison
        return string.Compare(ver1, ver2, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tries to compare two versions as numeric values.
    /// </summary>
    /// <returns>Comparison result if successful, null otherwise.</returns>
    private static int? TryCompareNumericVersions(string ver1, string ver2)
    {
        if (long.TryParse(ver1, out var num1) && long.TryParse(ver2, out var num2))
        {
            return num1.CompareTo(num2);
        }

        return null;
    }

    /// <summary>
    /// Tries to parse a date version string in YYYY-MM-DD format.
    /// </summary>
    private static bool TryParseDateVersion(string dateStr, out DateTime result)
    {
        // Try exact format YYYY-MM-DD first with InvariantCulture
        if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out result))
        {
            return true;
        }

        // Fallback to standard date parsing with InvariantCulture
        if (DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Extracts numeric representation from a date string (YYYYMMDD).
    /// </summary>
    private static long? ExtractNumericFromDate(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        // Remove common date separators
        var digits = dateStr.Replace("-", string.Empty)
                           .Replace("/", string.Empty)
                           .Replace(".", string.Empty);

        if (long.TryParse(digits, out var result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Extracts all digits from a version string.
    /// </summary>
    private static string ExtractDigits(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        var result = new System.Text.StringBuilder();
        foreach (var c in version)
        {
            if (char.IsDigit(c))
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Compares two strings using natural sort order (numeric chunks compared numerically).
    /// </summary>
    private static int NaturalCompare(string? s1, string? s2)
    {
        if (s1 == s2) return 0;
        if (s1 == null) return -1;
        if (s2 == null) return 1;

        int i1 = 0, i2 = 0;
        while (i1 < s1.Length && i2 < s2.Length)
        {
            // If both are digits, compare numerically
            if (char.IsDigit(s1[i1]) && char.IsDigit(s2[i2]))
            {
                // Extract numbers
                string n1 = ExtractNumber(s1, ref i1);
                string n2 = ExtractNumber(s2, ref i2);

                int result;
                if (long.TryParse(n1, out long v1) && long.TryParse(n2, out long v2))
                {
                    result = v1.CompareTo(v2);
                }
                else
                {
                     // Fallback to string comparison if too large for long
                     // Use length first for numeric semantic
                     result = n1.Length.CompareTo(n2.Length);
                     if (result == 0) result = string.Compare(n1, n2, StringComparison.Ordinal);
                }

                if (result != 0) return result;
            }
            else
            {
                // Compare chars
                int result = s1[i1].CompareTo(s2[i2]);
                if (result != 0) return result;
                i1++;
                i2++;
            }
        }

        return s1.Length.CompareTo(s2.Length);
    }

    private static string ExtractNumber(string s, ref int index)
    {
        int start = index;
        while (index < s.Length && char.IsDigit(s[index]))
        {
            index++;
        }

        return s[start..index];
    }
}
