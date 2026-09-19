using GenHub.Core.Constants;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// An immutable in-memory snapshot of a project's files for fast glob matching.
/// Enumerates the project directory once, then answers match queries from memory
/// instead of walking the disk on every bundle item or selection change.
/// Create one snapshot per editing session and share it across all match calls.
/// </summary>
public sealed class ProjectFileSnapshot
{
    private readonly InMemoryDirectory _root;
    private readonly HashSet<string> _allFiles;

    private ProjectFileSnapshot(string rootPath, IReadOnlyList<string> relativeFilePaths, InMemoryDirectory root)
    {
        RootPath = rootPath;
        RelativeFilePaths = relativeFilePaths;
        _root = root;
        _allFiles = new HashSet<string>(relativeFilePaths, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the absolute project root this snapshot was taken from.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Gets all file paths relative to <see cref="RootPath"/>, using forward slashes.
    /// </summary>
    public IReadOnlyList<string> RelativeFilePaths { get; }

    /// <summary>
    /// Creates a snapshot with a single recursive directory walk.
    /// Inaccessible directories are skipped; a missing root yields an empty snapshot.
    /// </summary>
    /// <param name="projectDir">The project root directory.</param>
    /// <returns>The file snapshot.</returns>
    public static ProjectFileSnapshot Create(string projectDir)
    {
        var rootPath = projectDir;
        try
        {
            rootPath = Path.GetFullPath(projectDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Fall back to projectDir when full path resolution fails.
        }

        var root = new InMemoryDirectory(GetDirectoryName(rootPath), rootPath, null);
        var relativePaths = new List<string>();
        if (Directory.Exists(projectDir))
        {
            CollectFiles(new DirectoryInfo(projectDir), rootPath, root, relativePaths);
        }

        return new ProjectFileSnapshot(rootPath, relativePaths, root);
    }

    /// <summary>
    /// Counts files matching any of the given glob patterns.
    /// </summary>
    /// <param name="patterns">Glob patterns or relative paths.</param>
    /// <returns>The number of matching files.</returns>
    public int CountMatches(IEnumerable<string> patterns)
    {
        return MatchFiles(patterns).Count;
    }

    /// <summary>
    /// Returns the relative paths of files matching any of the given glob patterns.
    /// </summary>
    /// <param name="patterns">Glob patterns or relative paths.</param>
    /// <returns>Matching relative paths with forward slashes (case-insensitive set).</returns>
    public HashSet<string> MatchFiles(IEnumerable<string> patterns)
    {
        var matcher = CreateMatcher(patterns);
        var result = matcher.Execute(_root);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in result.Files)
        {
            var normalized = NormalizeRelativePath(file.Path);
            if (_allFiles.Contains(normalized))
            {
                matched.Add(normalized);
            }
        }

        return matched;
    }

    /// <summary>
    /// Determines whether this snapshot was taken from the given project directory.
    /// </summary>
    /// <param name="projectDir">The project directory to compare.</param>
    /// <returns>True when both paths refer to the same directory.</returns>
    public bool IsSameRoot(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(projectDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static Matcher CreateMatcher(IEnumerable<string> patterns)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var rawPattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(rawPattern))
            {
                continue;
            }

            var pattern = rawPattern.TrimStart('/', '\\').Replace('\\', '/');
            matcher.AddInclude(pattern);

            // If pattern does not start with GameFilesEdited/, also match within GameFilesEdited
            if (!pattern.StartsWith(ModBuilderConstants.GameFilesEditedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                matcher.AddInclude($"{ModBuilderConstants.GameFilesEditedDir}/{pattern}");
            }
        }

        return matcher;
    }

    private static void CollectFiles(DirectoryInfo dir, string rootPath, InMemoryDirectory node, List<string> relativePaths)
    {
        FileInfo[] files;
        DirectoryInfo[] subDirs;
        try
        {
            files = dir.GetFiles();
            subDirs = dir.GetDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var rel = NormalizeRelativePath(Path.GetRelativePath(rootPath, file.FullName));
            relativePaths.Add(rel);
            node.AddFile(file.Name, file.FullName);
        }

        foreach (var subDir in subDirs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var child = node.AddDirectory(subDir.Name, subDir.FullName);
            CollectFiles(subDir, rootPath, child, relativePaths);
        }
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string GetDirectoryName(string path)
    {
        try
        {
            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return string.Empty;
        }
    }

    private sealed class InMemoryFile : FileInfoBase
    {
        public InMemoryFile(string name, string fullName, DirectoryInfoBase parent)
        {
            Name = name;
            FullName = fullName;
            ParentDirectory = parent;
        }

        public override string Name { get; }

        public override string FullName { get; }

        public override DirectoryInfoBase ParentDirectory { get; }
    }

    private sealed class InMemoryDirectory : DirectoryInfoBase
    {
        private readonly Dictionary<string, FileSystemInfoBase> _children = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryDirectory(string name, string fullName, DirectoryInfoBase? parent)
        {
            Name = name;
            FullName = fullName;
            ParentDirectory = parent;
        }

        public override string Name { get; }

        public override string FullName { get; }

        public override DirectoryInfoBase? ParentDirectory { get; }

        public void AddFile(string name, string fullName)
        {
            _children[name] = new InMemoryFile(name, fullName, this);
        }

        public InMemoryDirectory AddDirectory(string name, string fullName)
        {
            var child = new InMemoryDirectory(name, fullName, this);
            _children[name] = child;
            return child;
        }

        public override IEnumerable<FileSystemInfoBase> EnumerateFileSystemInfos() => _children.Values;

        public override DirectoryInfoBase? GetDirectory(string path)
        {
            var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            InMemoryDirectory current = this;
            for (var i = 0; i < segments.Length; i++)
            {
                if (!current._children.TryGetValue(segments[i], out var child) || child is not InMemoryDirectory dir)
                {
                    return null;
                }

                current = dir;
            }

            return current;
        }

        public override FileInfoBase? GetFile(string path)
        {
            var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            InMemoryDirectory current = this;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (!current._children.TryGetValue(segments[i], out var child) || child is not InMemoryDirectory dir)
                {
                    return null;
                }

                current = dir;
            }

            return current._children.TryGetValue(segments[^1], out var file) ? file as FileInfoBase : null;
        }
    }
}
