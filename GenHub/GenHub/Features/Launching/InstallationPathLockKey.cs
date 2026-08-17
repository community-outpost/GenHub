using System;
using System.IO;

namespace GenHub.Features.Launching;

/// <summary>
/// Creates stable lock keys for installation paths, including filesystem aliases.
/// </summary>
internal static class InstallationPathLockKey
{
    /// <summary>
    /// Gets the platform-appropriate lock-key comparer.
    /// </summary>
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Creates a lock key with existing symbolic-link and junction components resolved.
    /// </summary>
    /// <param name="installationPath">The installation directory path.</param>
    /// <returns>The canonical installation lock key.</returns>
    public static string Create(string installationPath)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationPath));
        return Path.TrimEndingDirectorySeparator(ResolvePathComponents(fullPath));
    }

    private static string ResolvePathComponents(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return fullPath;
        }

        var currentPath = root;
        var relativePath = fullPath[root.Length..];
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        foreach (var segment in relativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!Directory.Exists(currentPath))
            {
                continue;
            }

            FileSystemInfo? resolvedTarget = null;
            try
            {
                resolvedTarget = Directory.ResolveLinkTarget(currentPath, returnFinalTarget: true);
            }
            catch (UnauthorizedAccessException)
            {
                // Directory permissions prevent inspecting reparse point target
            }
            catch (IOException)
            {
                // Path or I/O error inspecting reparse point target
            }

            if (resolvedTarget is not null)
            {
                currentPath = ResolvePathComponents(Path.GetFullPath(resolvedTarget.FullName));
            }
        }

        return currentPath;
    }
}
