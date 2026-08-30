using System;
using System.IO;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Helpers;

/// <summary>
/// Policy and validation helper for ensuring file system paths remain safely contained
/// within a target root directory, preventing directory traversal and zip slip attacks across OS platforms.
/// </summary>
public static class ContentPathPolicy
{
    /// <summary>
    /// Resolves a candidate relative path within a designated root directory, ensuring that the
    /// resolved canonical path is strictly contained within that root directory.
    /// </summary>
    /// <param name="rootDirectory">The trusted root directory.</param>
    /// <param name="relativePath">The relative path to validate and resolve.</param>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing the normalized absolute destination path if safe,
    /// or a failure result if the path escapes the root directory or contains illegal rooted/traversal components.
    /// </returns>
    public static OperationResult<string> ResolveContainedFile(string? rootDirectory, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return OperationResult<string>.CreateFailure("Root directory cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return OperationResult<string>.CreateFailure("Relative path cannot be null or empty.");
        }

        if (Path.IsPathRooted(relativePath) ||
            (relativePath.Length >= 2 && relativePath[1] == ':' && char.IsLetter(relativePath[0])) ||
            relativePath.StartsWith("\\\\", StringComparison.Ordinal) ||
            relativePath.StartsWith("//", StringComparison.Ordinal))
        {
            return OperationResult<string>.CreateFailure($"Relative path cannot be rooted or absolute: {relativePath}");
        }

        // Normalize directory separators
        var normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                             .Replace('\\', Path.DirectorySeparatorChar)
                                             .TrimStart(Path.DirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(normalizedRelative))
        {
            return OperationResult<string>.CreateFailure("Normalized relative path cannot be empty.");
        }

        var normalizedRoot = rootDirectory.Replace('\\', Path.DirectorySeparatorChar)
                                          .Replace('/', Path.DirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(normalizedRoot);
        var fullCandidate = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelative));

        if (!IsContainedInternal(fullRoot, fullCandidate))
        {
            return OperationResult<string>.CreateFailure(
                $"Path '{relativePath}' escapes target root directory '{rootDirectory}'.");
        }

        return OperationResult<string>.CreateSuccess(fullCandidate);
    }

    /// <summary>
    /// Validates whether a candidate path is strictly contained within a designated root directory.
    /// </summary>
    /// <param name="rootDirectory">The root directory.</param>
    /// <param name="candidatePath">The candidate path to check.</param>
    /// <returns><see langword="true"/> if the candidate path is contained within the root; otherwise <see langword="false"/>.</returns>
    public static bool IsContained(string? rootDirectory, string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        try
        {
            var normalizedRoot = rootDirectory.Replace('\\', Path.DirectorySeparatorChar)
                                              .Replace('/', Path.DirectorySeparatorChar);
            var normalizedCandidate = candidatePath.Replace('\\', Path.DirectorySeparatorChar)
                                                   .Replace('/', Path.DirectorySeparatorChar);

            var fullRoot = Path.GetFullPath(normalizedRoot);
            var fullCandidate = Path.GetFullPath(normalizedCandidate);

            return IsContainedInternal(fullRoot, fullCandidate);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsContainedInternal(string fullRoot, string fullCandidate)
    {
        var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!fullCandidate.StartsWith(rootPrefix, PathHelper.PathComparison) &&
            !fullCandidate.Equals(fullRoot, PathHelper.PathComparison))
        {
            return false;
        }

        var realRoot = ResolveRealPath(fullRoot);
        var realCandidate = ResolveRealPath(fullCandidate);

        var realRootPrefix = realRoot.EndsWith(Path.DirectorySeparatorChar)
            ? realRoot
            : realRoot + Path.DirectorySeparatorChar;

        return realCandidate.StartsWith(realRootPrefix, PathHelper.PathComparison) ||
               realCandidate.Equals(realRoot, PathHelper.PathComparison);
    }

    private static string ResolveRealPath(string path)
    {
        try
        {
            var current = path;
            while (!string.IsNullOrEmpty(current))
            {
                if (TryResolveLink(current, path, out var resolvedPath))
                {
                    return resolvedPath;
                }

                if (File.Exists(current))
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
            }
        }
        catch
        {
            // Fallback to path if resolution fails
        }

        return path;
    }

    private static bool TryResolveLink(string current, string originalPath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        var targetFullName = TryGetLinkTargetFullName(current);
        if (string.IsNullOrEmpty(targetFullName))
        {
            return false;
        }

        var relativeSuffix = Path.GetRelativePath(current, originalPath);
        resolvedPath = relativeSuffix == "."
            ? targetFullName
            : Path.GetFullPath(Path.Combine(targetFullName, relativeSuffix));
        return true;
    }

    private static string? TryGetLinkTargetFullName(string path)
    {
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            return fileInfo.LinkTarget != null
                ? fileInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                : null;
        }

        if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            return dirInfo.LinkTarget != null
                ? dirInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                : null;
        }

        return null;
    }
}
