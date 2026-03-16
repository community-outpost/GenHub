using System.Collections.Generic;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Standard string-to-int comparer for version comparisons.
/// </summary>
public class IntegerVersionComparer : IVersionComparer, IComparer<string>
{
    /// <inheritdoc />
    public int Compare(string version1, string version2)
    {
        if (int.TryParse(version1, out var v1) && int.TryParse(version2, out var v2))
        {
            return v1.CompareTo(v2);
        }

        return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool CanParse(string version)
    {
        return int.TryParse(version, out _);
    }

    /// <inheritdoc />
    int IComparer<string>.Compare(string? x, string? y)
    {
        if (x == null && y == null)
        {
            return 0;
        }

        if (x == null)
        {
            return -1;
        }

        if (y == null)
        {
            return 1;
        }

        return Compare(x, y);
    }
}
