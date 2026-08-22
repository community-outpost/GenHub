using System;
using System.ComponentModel;
using System.Diagnostics;
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
        catch (IOException)
        {
            return string.Equals(first, second, PathComparison);
        }
        catch (UnauthorizedAccessException)
        {
            return string.Equals(first, second, PathComparison);
        }
        catch (SecurityException)
        {
            return string.Equals(first, second, PathComparison);
        }
        catch (NotSupportedException)
        {
            return string.Equals(first, second, PathComparison);
        }
        catch (ArgumentException)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
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

    /// <summary>
    /// Generates a unique destination path in the directory, appending (1), (2), etc. if the file exists.
    /// </summary>
    /// <param name="destinationPath">The candidate destination path.</param>
    /// <returns>A unique non-colliding file path.</returns>
    public static string GetUniqueNumberedPath(string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var dir = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var nameOnly = Path.GetFileNameWithoutExtension(destinationPath);
        var ext = Path.GetExtension(destinationPath);
        int count = 1;
        var current = destinationPath;
        while (File.Exists(current))
        {
            current = Path.Combine(dir, $"{nameOnly} ({count}){ext}");
            count++;
        }

        return current;
    }

    /// <summary>
    /// Opens the Windows File Explorer and selects the specified file, or ignores if not supported.
    /// </summary>
    /// <param name="filePath">The absolute path to the file to reveal.</param>
    public static void RevealInExplorer(string filePath)
    {
        try
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var explorerPath = string.IsNullOrEmpty(windowsDir) ? "explorer.exe" : Path.Combine(windowsDir, "explorer.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = explorerPath,
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = false,
            };
            Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            /* Ignore explorer errors */
        }
    }

    private static bool IsContained(string normalizedRoot, string normalizedTarget)
    {
        var relative = Path.GetRelativePath(normalizedRoot, normalizedTarget);

        return !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static string FollowLinks(string fullPath, int maxDepth = 32)
    {
        if (maxDepth <= 0)
        {
            return fullPath;
        }

        try
        {
            var normalized = Path.GetFullPath(fullPath);
            var root = Path.GetPathRoot(normalized);
            if (string.IsNullOrEmpty(root))
            {
                return normalized;
            }

            var relativeFromRoot = Path.GetRelativePath(root, normalized);
            if (relativeFromRoot == "." || relativeFromRoot.Length == 0)
            {
                return root;
            }

            var segments = relativeFromRoot.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            var current = root;
            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);

                if (Directory.Exists(current) || File.Exists(current))
                {
                    FileSystemInfo info = Directory.Exists(current)
                        ? new DirectoryInfo(current)
                        : new FileInfo(current);

                    var target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target != null)
                    {
                        current = FollowLinks(target.FullName, maxDepth - 1);
                    }
                }
            }

            return Path.GetFullPath(current);
        }
        catch (IOException)
        {
            return fullPath;
        }
        catch (UnauthorizedAccessException)
        {
            return fullPath;
        }
        catch (SecurityException)
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
