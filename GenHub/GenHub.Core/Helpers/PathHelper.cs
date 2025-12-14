using System.IO;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper methods for path manipulation operations.
/// </summary>
public static class PathHelper
{
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
