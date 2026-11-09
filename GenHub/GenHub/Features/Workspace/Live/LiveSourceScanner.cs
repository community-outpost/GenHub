using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GenHub.Features.Workspace.Live;

/// <summary>
/// Enumerates live content folders without following reparse points and without
/// descending into known tool or VCS directories.
/// </summary>
internal static class LiveSourceScanner
{
    private static readonly HashSet<string> _ignoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".vscode", "bin", "obj", "node_modules", ".genhub",
    };

    private static readonly HashSet<string> _ignoredFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".log", ".pdb",
    };

    /// <summary>
    /// Enumerates the files under <paramref name="root"/> that belong in a workspace.
    /// </summary>
    /// <param name="root">The absolute live content root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The discovered files, excluding ignored entries and user-data targets.</returns>
    public static IReadOnlyList<LiveSourceFile> Enumerate(string root, CancellationToken cancellationToken)
    {
        var results = new List<LiveSourceFile>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            EnumerateDirectories(directory, pending);
            EnumerateFiles(directory, root, results, cancellationToken);
        }

        return results;
    }

    /// <summary>
    /// Maps a relative path to the same install target the manifest builder uses.
    /// Only <see cref="ContentInstallTarget.Workspace"/> entries are linked by live mode.
    /// </summary>
    /// <param name="relativePath">The path relative to the content root.</param>
    /// <returns>The resolved install target.</returns>
    public static ContentInstallTarget ResolveInstallTarget(string relativePath)
    {
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();

        if (extension == ".map" ||
            ((extension == ".tga" || extension == ".wak") &&
             relativePath.Contains("maps", StringComparison.OrdinalIgnoreCase)))
        {
            return ContentInstallTarget.UserMapsDirectory;
        }

        if (extension == ".rep")
        {
            return ContentInstallTarget.UserReplaysDirectory;
        }

        if (extension == ".bmp" &&
            relativePath.Contains("screenshots", StringComparison.OrdinalIgnoreCase))
        {
            return ContentInstallTarget.UserScreenshotsDirectory;
        }

        return ContentInstallTarget.Workspace;
    }

    private static void EnumerateDirectories(DirectoryInfo parent, Stack<DirectoryInfo> pending)
    {
        try
        {
            foreach (var child in parent.EnumerateDirectories())
            {
                if (_ignoredDirectoryNames.Contains(child.Name))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable directory is skipped; the rest of the tree still mounts.
        }
    }

    private static void EnumerateFiles(
        DirectoryInfo directory,
        string root,
        List<LiveSourceFile> results,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var file in directory.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_ignoredFileExtensions.Contains(file.Extension))
                {
                    continue;
                }

                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // Do not mount a source symlink; it can point outside the live root
                    // and would bypass the root containment check.
                    continue;
                }

                var relativePath = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
                if (ResolveInstallTarget(relativePath) != ContentInstallTarget.Workspace)
                {
                    continue;
                }

                results.Add(new LiveSourceFile(relativePath, file.FullName, file.Length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; a locked or unreadable directory is skipped.
        }
    }
}
