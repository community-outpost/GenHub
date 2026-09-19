using GenHub.Core.Constants;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Immutable snapshot of all project files and their directory structure.
/// Allows fast in-memory glob matching without repeated disk access.
/// </summary>
public sealed class ProjectFileSnapshot
{
    private readonly HashSet<string> _allFiles;
    private readonly InMemoryDirectory _root;

    private ProjectFileSnapshot(string rootPath, IEnumerable<string> relativePaths, InMemoryDirectory root)
    {
        RootPath = Path.GetFullPath(rootPath);
        _allFiles = new HashSet<string>(relativePaths, StringComparer.OrdinalIgnoreCase);
        _root = root;
    }

    /// <summary>
    /// Gets the root directory path of the project.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Gets all relative file paths in the snapshot (forward-slash normalized).
    /// </summary>
    public IReadOnlyCollection<string> AllFiles => _allFiles;

    /// <summary>
    /// Gets all relative file paths in the snapshot (forward-slash normalized).
    /// </summary>
    public IReadOnlyCollection<string> RelativeFilePaths => _allFiles;

    /// <summary>
    /// Gets the total number of files in the project.
    /// </summary>
    public int TotalFiles => _allFiles.Count;

    /// <summary>
    /// Creates a snapshot by crawling the project directory once.
    /// </summary>
    /// <param name="projectDir">The root project directory.</param>
    /// <returns>A new <see cref="ProjectFileSnapshot"/> instance.</returns>
    public static ProjectFileSnapshot Create(string projectDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        var rootPath = Path.GetFullPath(projectDir);
        var root = new InMemoryDirectory(Path.GetFileName(rootPath));
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

    private Matcher CreateMatcher(IEnumerable<string> patterns)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var rawPattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(rawPattern))
            {
                continue;
            }

            var pattern = rawPattern.Trim();
            if (Path.IsPathRooted(pattern) || (pattern.Length > 2 && pattern[1] == ':'))
            {
                try
                {
                    var rel = Path.GetRelativePath(RootPath, pattern).Replace('\\', '/');
                    if (!rel.StartsWith("..", StringComparison.Ordinal))
                    {
                        pattern = rel;
                    }
                }
                catch
                {
                    // Fall back to original pattern
                }
            }

            pattern = pattern.TrimStart('/', '\\').Replace('\\', '/');
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

        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
            relativePaths.Add(rel);
            node.AddFile(file.Name);
        }

        foreach (var subDir in subDirs)
        {
            var childNode = node.GetOrCreateDirectory(subDir.Name);
            CollectFiles(subDir, rootPath, childNode, relativePaths);
        }
    }

    private static string NormalizeRelativePath(string path) =>
        path.Trim('/').Replace('\\', '/');

    private sealed class InMemoryDirectory(string name, InMemoryDirectory? parent = null) : DirectoryInfoBase
    {
        private readonly Dictionary<string, InMemoryDirectory> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, InMemoryFile> _files = new(StringComparer.OrdinalIgnoreCase);

        public override string Name { get; } = name;

        public override string FullName => ParentDirectory == null ? Name : $"{ParentDirectory.FullName}/{Name}";

        public override DirectoryInfoBase? ParentDirectory { get; } = parent;

        public InMemoryDirectory GetOrCreateDirectory(string name)
        {
            if (!_directories.TryGetValue(name, out var dir))
            {
                dir = new InMemoryDirectory(name, this);
                _directories[name] = dir;
            }

            return dir;
        }

        public void AddFile(string name)
        {
            _files[name] = new InMemoryFile(name, this);
        }

        public override IEnumerable<FileSystemInfoBase> EnumerateFileSystemInfos()
        {
            foreach (var dir in _directories.Values)
            {
                yield return dir;
            }

            foreach (var file in _files.Values)
            {
                yield return file;
            }
        }

        public override DirectoryInfoBase? GetDirectory(string name) =>
            _directories.GetValueOrDefault(name);

        public override FileInfoBase? GetFile(string name) =>
            _files.GetValueOrDefault(name);
    }

    private sealed class InMemoryFile(string name, InMemoryDirectory parent) : FileInfoBase
    {
        public override string Name { get; } = name;

        public override string FullName => $"{ParentDirectory?.FullName}/{Name}";

        public override DirectoryInfoBase? ParentDirectory { get; } = parent;
    }
}
