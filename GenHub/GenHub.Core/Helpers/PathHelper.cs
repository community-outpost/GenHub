using System;
using System.IO;
using System.Security;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper methods for path manipulation operations.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Gets the string comparison to use when comparing filesystem paths. Windows paths
    /// are compared case-insensitively; other platforms use conservative case-sensitive semantics.
    /// </summary>
    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// Gets the string comparer to use when keying collections by filesystem path. Windows paths
    /// are compared case-insensitively; other platforms use conservative case-sensitive semantics.
    /// </summary>
    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    /// Determines whether two paths point at the same filesystem location, normalizing both and
    /// comparing them with the platform-appropriate case sensitivity.
    /// </summary>
    /// <param name="first">The first path.</param>
    /// <param name="second">The second path.</param>
    /// <returns><see langword="true"/> when both paths resolve to the same location.</returns>
    public static bool AreSamePath(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
                PathComparison);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
        {
            return string.Equals(first, second, PathComparison);
        }
    }

    /// <summary>
    /// Gets the parent directory of a path, with fallback to the path itself if at drive root.
    /// </summary>
    /// <param name="path">The path to get the parent directory from.</param>
    /// <returns>
    /// The parent directory path, or the original path if it's at the drive root.
    /// For example, "D:\" returns "D:\" while "D:\Games" returns "D:\".
    /// </returns>
    public static string GetSafeParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(parent) ? path : parent;
    }
}
