using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper methods for path manipulation operations.
/// </summary>
public static class PathHelper
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

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
    /// Determines whether two paths point at the same physical filesystem location,
    /// following symbolic links and junctions before comparing.
    /// </summary>
    /// <param name="first">The first path.</param>
    /// <param name="second">The second path.</param>
    /// <returns><see langword="true"/> when both paths resolve to the same physical location.</returns>
    public static bool AreSamePhysicalPath(string first, string second)
    {
        try
        {
            return AreSamePath(FollowLinks(first), FollowLinks(second));
        }
        catch (ArgumentException)
        {
            return AreSamePath(first, second);
        }
        catch (NotSupportedException)
        {
            return AreSamePath(first, second);
        }
    }

    /// <summary>
    /// Validates whether a path is a non-UNC, locally-rooted directory path, sanitizing surrounding quotes and whitespace.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <param name="sanitizedPath">The sanitized path if valid; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the path is a valid non-UNC local path; otherwise, <see langword="false"/>.</returns>
    public static bool TrySanitizeLocalPath(string? path, [NotNullWhen(true)] out string? sanitizedPath)
    {
        sanitizedPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim().Trim('"');
        if (!Path.IsPathRooted(trimmed) ||
            trimmed.StartsWith(@"\\", StringComparison.Ordinal) ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
            (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsUnc))
        {
            return false;
        }

        sanitizedPath = trimmed;
        return true;
    }

    /// <summary>
    /// Determines whether two filesystem paths reside on the same drive volume.
    /// </summary>
    /// <param name="first">The first path.</param>
    /// <param name="second">The second path.</param>
    /// <returns><see langword="true"/> when both paths share the same volume root; otherwise, <see langword="false"/>.</returns>
    public static bool AreSameVolume(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            var firstRoot = Path.GetPathRoot(Path.GetFullPath(first));
            var secondRoot = Path.GetPathRoot(Path.GetFullPath(second));

            return !string.IsNullOrEmpty(firstRoot) &&
                   !string.IsNullOrEmpty(secondRoot) &&
                   string.Equals(firstRoot, secondRoot, PathComparison);
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Enumerates directory paths starting from <paramref name="startPath"/> and walking up
    /// through its parent directories, stopping at the volume root. All returned paths reside
    /// on the same filesystem volume as <paramref name="startPath"/>.
    /// </summary>
    /// <param name="startPath">The initial path to begin traversal from.</param>
    /// <returns>An enumeration of ancestor directory paths on the same volume.</returns>
    public static IEnumerable<string> EnumerateSameVolumeAncestors(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath) || !TryGetFullPathAndVolumeRoot(startPath, out var fullPath, out var volumeRoot))
        {
            yield break;
        }

        var current = Path.TrimEndingDirectorySeparator(fullPath);
        if (string.IsNullOrEmpty(current))
        {
            current = fullPath;
        }

        var canonicalVolumeRoot = Path.TrimEndingDirectorySeparator(volumeRoot);

        while (!string.IsNullOrEmpty(current))
        {
            yield return current;

            if (AreSamePath(current, volumeRoot) || (!string.IsNullOrEmpty(canonicalVolumeRoot) && AreSamePath(current, canonicalVolumeRoot)))
            {
                break;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || AreSamePath(current, parent))
            {
                break;
            }

            var parentVolume = Path.GetPathRoot(parent);
            if (string.IsNullOrEmpty(parentVolume) || !string.Equals(parentVolume, volumeRoot, PathComparison))
            {
                break;
            }

            current = parent;
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
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (ArgumentException)
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
    /// Sanitizes a file name by removing invalid filesystem characters, trimming trailing dots and whitespace, and prefixing Windows reserved device names.
    /// </summary>
    /// <param name="fileName">The file name to sanitize.</param>
    /// <returns>The sanitized file name.</returns>
    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return string.Empty;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(fileName.Where(c => !invalidChars.Contains(c))).Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(sanitized))
        {
            return string.Empty;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(sanitized);
        if (ReservedDeviceNames.Contains(nameWithoutExtension))
        {
            sanitized = $"_{sanitized}";
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a file name by replacing invalid characters with underscores.
    /// </summary>
    /// <param name="fileName">The file name to sanitize.</param>
    /// <param name="replaceSpaces">When true, spaces are also replaced (desktop-entry rule).</param>
    /// <returns>A sanitized file name.</returns>
    public static string SanitizeFileName(string fileName, bool replaceSpaces)
    {
        var sanitized = new System.Text.StringBuilder(fileName);
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            sanitized.Replace(invalidCharacter, '_');
        }

        if (replaceSpaces)
        {
            sanitized.Replace(' ', '_');
        }

        return sanitized.ToString().Trim();
    }

    /// <summary>
    /// Opens the native file explorer and selects the specified file or folder, or ignores if not supported.
    /// </summary>
    /// <param name="filePath">The absolute path to the file to reveal.</param>
    public static void RevealInExplorer(string filePath)
    {
        try
        {
            var startInfo = CreateRevealStartInfo(filePath);
            if (startInfo != null)
            {
                Process.Start(startInfo);
            }
        }
        catch (Win32Exception)
        {
            /* Ignore explorer errors */
        }
        catch (IOException)
        {
            /* Ignore explorer errors */
        }
        catch (UnauthorizedAccessException)
        {
            /* Ignore explorer errors */
        }
        catch (InvalidOperationException)
        {
            /* Ignore explorer errors */
        }
    }

    [SuppressMessage("Security", "S4036:Make sure the executable exists, and provide an absolute path or configure PATH securely", Justification = "Resolves standard desktop launch utilities (open, xdg-open) from PATH across heterogeneous Unix distributions.")]
    private static ProcessStartInfo? CreateRevealStartInfo(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            var info = new ProcessStartInfo
            {
                FileName = PlatformConstants.WindowsExplorerPath,
                Arguments = string.Format(PlatformConstants.WindowsExplorerSelectArgument, filePath),
                UseShellExecute = false,
            };
            return info;
        }

        if (OperatingSystem.IsMacOS())
        {
            var info = new ProcessStartInfo
            {
                FileName = PlatformConstants.MacOSOpenExecutable,
                UseShellExecute = false,
            };
            info.ArgumentList.Add("-R");
            info.ArgumentList.Add(filePath);
            return info;
        }

        if (OperatingSystem.IsLinux())
        {
            string? targetDir;
            if (File.Exists(filePath))
            {
                targetDir = Path.GetDirectoryName(filePath);
            }
            else if (Directory.Exists(filePath))
            {
                targetDir = filePath;
            }
            else
            {
                targetDir = null;
            }

            if (string.IsNullOrEmpty(targetDir))
            {
                return null;
            }

            var info = new ProcessStartInfo
            {
                FileName = PlatformConstants.LinuxXdgOpenExecutable,
                UseShellExecute = false,
            };
            info.ArgumentList.Add(targetDir);
            return info;
        }

        return null;
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
    }

    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// Resolves all symbolic links and intermediate link segments in <paramref name="path"/>,
    /// returning the fully canonicalized absolute path, or <c>null</c> if resolution fails or a loop is detected.
    /// </summary>
    /// <param name="path">The path to canonicalize.</param>
    /// <returns>The canonicalized path, or <c>null</c> if resolution fails or a loop is detected.</returns>
    public static string? CanonicalizePath(string path)
    {
        string current;
        try
        {
            current = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        // ResolveLinkTarget leaves intermediate directory links unresolved, so walk
        // each segment and splice link targets until no links remain (bounded: chains
        // longer than this are treated as cycles).
        var seen = new HashSet<string>(PathComparer);
        for (var guard = 0; guard < 40; guard++)
        {
            if (!seen.Add(current))
            {
                return null;
            }

            var resolved = ResolveFirstLinkSegment(current, out var failed);
            if (failed)
            {
                return null;
            }

            if (resolved is null)
            {
                return current;
            }

            current = resolved;
        }

        return null;
    }

    /// <summary>
    /// Checks whether <paramref name="path"/> is within or equal to <paramref name="root"/> after
    /// canonicalizing all intermediate symbolic link segments.
    /// </summary>
    /// <param name="root">The root directory path.</param>
    /// <param name="path">The candidate path.</param>
    /// <returns><c>true</c> if candidate path is under root; otherwise <c>false</c>.</returns>
    public static bool IsUnderRoot(string root, string path)
    {
        try
        {
            var rootFull = Path.GetFullPath(root);
            var pathFull = Path.GetFullPath(path);

            var rootCanonical = CanonicalizePath(rootFull) ?? rootFull;
            var pathCanonical = CanonicalizePath(pathFull);

            if (pathCanonical is null)
            {
                return false;
            }

            return pathCanonical.Equals(rootCanonical, PathComparison)
                || pathCanonical.StartsWith(rootCanonical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? ResolveFirstLinkSegment(string fullPath, out bool failed)
    {
        failed = false;
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            failed = true;
            return null;
        }

        var relative = Path.GetRelativePath(root, fullPath);

        // Defensive check: Path.GetFullPath collapses .. segments, but guard against any drive/prefix edge cases.
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            failed = true;
            return null;
        }

        var segments = relative.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries);
        var prefix = root;
        for (var i = 0; i < segments.Length; i++)
        {
            prefix = Path.Combine(prefix, segments[i]);
            string? target;
            try
            {
                FileSystemInfo info = new FileInfo(prefix);
                target = info.LinkTarget ?? new DirectoryInfo(prefix).LinkTarget;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failed = true;
                return null;
            }

            if (string.IsNullOrEmpty(target))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(prefix) ?? root;
            try
            {
                var resolvedPrefix = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(parent, target));
                if (i + 1 >= segments.Length)
                {
                    return resolvedPrefix;
                }

                return Path.Combine([resolvedPrefix, .. segments[(i + 1)..]]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failed = true;
                return null;
            }
        }

        return null;
    }

    private static bool TryGetFullPathAndVolumeRoot(
        string path,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(true)] out string? volumeRoot)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            volumeRoot = Path.GetPathRoot(fullPath);
            return !string.IsNullOrEmpty(volumeRoot);
        }
        catch (IOException)
        {
            fullPath = null;
            volumeRoot = null;
            return false;
        }
        catch (ArgumentException)
        {
            fullPath = null;
            volumeRoot = null;
            return false;
        }
        catch (NotSupportedException)
        {
            fullPath = null;
            volumeRoot = null;
            return false;
        }
        catch (SecurityException)
        {
            fullPath = null;
            volumeRoot = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            fullPath = null;
            volumeRoot = null;
            return false;
        }
    }
}
