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

    /// <summary>
    /// Determines whether a candidate path resolves to a location inside a base directory.
    /// Both paths are fully normalized first, so <c>..</c> segments, redundant separators and
    /// rooted candidates cannot escape the base directory. Because normalization is textual and a
    /// symbolic link or junction redirects a path that reads as contained, both sides are also
    /// compared after their links are followed; a path that cannot be resolved — because it does
    /// not exist yet, or the filesystem refuses the query — is compared as written.
    /// </summary>
    /// <param name="baseDirectory">The directory that must contain the candidate path.</param>
    /// <param name="candidatePath">The path to test for containment.</param>
    /// <returns><see langword="true"/> when the candidate resolves inside the base directory; otherwise, <see langword="false"/>.</returns>
    public static bool IsPathWithinDirectory(string baseDirectory, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        try
        {
            var normalizedRoot = Path.GetFullPath(baseDirectory);
            var normalizedTarget = Path.GetFullPath(candidatePath);

            return IsContained(normalizedRoot, normalizedTarget) &&
                   IsContained(FollowLinks(normalizedRoot), FollowLinks(normalizedTarget));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Normalizes a relative path by standardizing directory separators and removing leading separators.
    /// </summary>
    /// <param name="relativePath">The relative path to normalize.</param>
    /// <returns>The normalized relative path.</returns>
    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        return relativePath
            .Replace('\\', '/')
            .TrimStart('/')
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsContained(string normalizedRoot, string normalizedTarget)
    {
        var relative = Path.GetRelativePath(normalizedRoot, normalizedTarget);

        return !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static string FollowLinks(string fullPath)
    {
        try
        {
            var existing = fullPath;
            var remainder = string.Empty;

            while (!Directory.Exists(existing) && !File.Exists(existing))
            {
                var parent = Path.GetDirectoryName(existing);
                if (string.IsNullOrEmpty(parent))
                {
                    return fullPath;
                }

                remainder = Path.Combine(Path.GetFileName(existing), remainder);
                existing = parent;
            }

            FileSystemInfo info = Directory.Exists(existing)
                ? new DirectoryInfo(existing)
                : new FileInfo(existing);
            var resolved = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? existing;

            return remainder.Length == 0 ? resolved : Path.GetFullPath(Path.Combine(resolved, remainder));
        }
        catch (IOException)
        {
            return fullPath;
        }
        catch (UnauthorizedAccessException)
        {
            return fullPath;
        }
        catch (NotSupportedException)
        {
            return fullPath;
        }
        catch (ArgumentException)
        {
            return fullPath;
        }
    }
}
