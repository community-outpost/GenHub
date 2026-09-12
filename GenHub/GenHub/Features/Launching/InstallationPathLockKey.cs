using System;
using System.IO;
using Microsoft.Extensions.Logging;

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
    /// <param name="logger">Optional logger for recording resolution failures.</param>
    /// <returns>The canonical installation lock key.</returns>
    public static string Create(string installationPath, ILogger? logger = null)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationPath));
        return Path.TrimEndingDirectorySeparator(ResolvePathComponents(fullPath, logger));
    }

    private static string ResolvePathComponents(string fullPath, ILogger? logger)
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
            catch (UnauthorizedAccessException ex)
            {
                logger?.LogWarning(ex, "[InstallationPathLockKey] Access denied resolving link target for path segment: {Path}", currentPath);
            }
            catch (IOException ex)
            {
                logger?.LogWarning(ex, "[InstallationPathLockKey] I/O error resolving link target for path segment: {Path}", currentPath);
            }

            if (resolvedTarget is not null)
            {
                currentPath = ResolvePathComponents(Path.GetFullPath(resolvedTarget.FullName), logger);
            }
        }

        return currentPath;
    }
}
