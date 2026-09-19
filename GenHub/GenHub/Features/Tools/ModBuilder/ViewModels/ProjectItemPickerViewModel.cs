using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Features.Tools.ModBuilder.Models;
using GenHub.Features.Tools.ModBuilder.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// Mode for adding directory selections.
/// </summary>
public enum DirectoryGlobOption
{
    /// <summary>
    /// Recursive match for all files: **/*.*
    /// </summary>
    AllFiles,

    /// <summary>
    /// Recursive match for INI files: **/*.ini
    /// </summary>
    IniFiles,

    /// <summary>
    /// Recursive match for Textures: **/*.tga
    /// </summary>
    Textures,

    /// <summary>
    /// Recursive match for Window UI: **/*.wnd
    /// </summary>
    WindowUI,

    /// <summary>
    /// Recursive match for CSF Strings: **/*.csf
    /// </summary>
    StringTable,

    /// <summary>
    /// Recursive match for Audio files: **/*.wav
    /// </summary>
    Audio,
}

/// <summary>
/// ViewModel for picking project directories or files to add to a bundle item.
/// </summary>
public partial class ProjectItemPickerViewModel : ObservableObject
{
    private readonly string _projectDir;
    private readonly List<string> _initialPatterns = [];
    private readonly List<string> _preservedPatterns = [];
    private readonly HashSet<string> _initialMatchedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _initialCheckedDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _initialDirLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProjectFileSnapshot? _providedSnapshot;
    private ProjectFileSnapshot? _snapshot;
    private FileTreeNode? _pendingRoot;
    private bool _isPropagatingSelection;

    /// <summary>
    /// Gets the root nodes of the project file tree.
    /// </summary>
    public ObservableCollection<FileTreeNode> Nodes { get; } = [];

    /// <summary>
    /// Gets or sets the search filter text.
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Gets or sets the selected directory glob option.
    /// </summary>
    [ObservableProperty]
    private DirectoryGlobOption _globOption = DirectoryGlobOption.AllFiles;

    /// <summary>
    /// Gets or sets the selected count summary text.
    /// </summary>
    [ObservableProperty]
    private string _selectionSummary = "No items selected";

    /// <summary>
    /// Gets or sets a value indicating whether any items are selected.
    /// </summary>
    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>
    /// Gets or sets a value indicating whether the file tree is still loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectItemPickerViewModel"/> class.
    /// The constructor is intentionally lightweight; call <see cref="InitializeAsync"/>
    /// to crawl the project and build the tree on a background thread.
    /// </summary>
    /// <param name="projectDir">The root directory of the project.</param>
    /// <param name="existingPatterns">Optional initial patterns to pre-select.</param>
    /// <param name="snapshot">Optional existing file snapshot to avoid crawling disk again.</param>
    public ProjectItemPickerViewModel(
        string projectDir,
        IEnumerable<string>? existingPatterns = null,
        ProjectFileSnapshot? snapshot = null)
    {
        _projectDir = projectDir;
        _providedSnapshot = snapshot;
        if (existingPatterns != null)
        {
            _initialPatterns.AddRange(NormalizeInitialPatterns(existingPatterns, projectDir));
        }
    }

    /// <summary>
    /// Builds the file tree and pre-selects initial patterns on a background thread,
    /// then publishes the result to the UI thread.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSnapshot();
                CollectInitialMatches();
                cancellationToken.ThrowIfCancellationRequested();
                BuildTreeCore();
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await RunOnUIThreadAsync(PublishTree).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUIThreadAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    private void EnsureSnapshot()
    {
        if (!Directory.Exists(_projectDir))
        {
            return;
        }

        _snapshot = _providedSnapshot is { } provided && provided.IsSameRoot(_projectDir)
            ? provided
            : ProjectFileSnapshot.Create(_projectDir);
    }

    private void PublishTree()
    {
        Nodes.Clear();
        if (_pendingRoot != null)
        {
            Nodes.Add(_pendingRoot);
            _pendingRoot = null;
        }

        UpdateSelectionSummary();
    }

    private static async Task RunOnUIThreadAsync(Action action)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    private static List<string> NormalizeInitialPatterns(IEnumerable<string> existingPatterns, string projectDir)
    {
        var normalized = new List<string>();
        foreach (var raw in existingPatterns.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var trimmed = raw.Trim().Replace('\\', '/');
            if (TryRelativizePattern(trimmed, projectDir, out var relative))
            {
                normalized.Add(relative);
                continue;
            }

            normalized.Add(trimmed.TrimStart('/'));
        }

        return normalized;
    }

    private static bool TryRelativizePattern(string trimmed, string projectDir, out string relative)
    {
        relative = string.Empty;
        if (!Path.IsPathRooted(trimmed) && (trimmed.Length <= 2 || trimmed[1] != ':'))
        {
            return false;
        }

        try
        {
            var candidate = Path.GetRelativePath(projectDir, trimmed).Replace('\\', '/');
            if (candidate.StartsWith("..", StringComparison.Ordinal))
            {
                return false;
            }

            relative = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void CollectInitialMatches()
    {
        BuildInitialDirLookup();
        if (_snapshot == null || _initialPatterns.Count == 0)
        {
            return;
        }

        var rootRel = GetTreeRootRelativePath();
        foreach (var matched in _snapshot.MatchFiles(_initialPatterns).Where(matched => IsTreeVisible(matched, rootRel)))
        {
            _initialMatchedFiles.Add(matched);
        }

        // Patterns matching only outside the visible tree cannot be edited here; carry them through saves.
        // Single-pattern matches stay cheap: literal patterns resolve through hash lookups.
        foreach (var pattern in _initialPatterns)
        {
            var matches = _snapshot.MatchFiles([pattern]);
            if (!matches.Any(m => IsTreeVisible(m, rootRel)))
            {
                _preservedPatterns.Add(pattern);
            }
        }
    }

    private void BuildInitialDirLookup()
    {
        _initialDirLookup.Clear();
        foreach (var pattern in _initialPatterns)
        {
            var normPat = NormalizeRelativePath(pattern);
            _initialDirLookup.Add(normPat);
            _initialDirLookup.Add(StripEditedPrefix(normPat));

            var dirClean = normPat.Replace("/**/*.*", string.Empty)
                                  .Replace("/**", string.Empty)
                                  .Replace("/*.*", string.Empty);
            if (dirClean.EndsWith("/*", StringComparison.Ordinal))
            {
                dirClean = dirClean[..^2];
            }

            _initialDirLookup.Add(dirClean);
            _initialDirLookup.Add(StripEditedPrefix(dirClean));
        }
    }

    private string GetTreeRootRelativePath()
    {
        var gameFilesDir = Path.Combine(_projectDir, ModBuilderConstants.GameFilesEditedDir);
        return Directory.Exists(gameFilesDir) ? ModBuilderConstants.GameFilesEditedDir : string.Empty;
    }

    private static bool IsUnderTreeRoot(string relativePath, string rootRel) =>
        rootRel.Length == 0 ||
        relativePath.Equals(rootRel, StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(rootRel + "/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTreeVisible(string relativePath, string rootRel) =>
        IsUnderTreeRoot(relativePath, rootRel) &&
        !relativePath.Split('/').Any(ModBuilderConstants.IsIgnoredProjectFile);

    private void BuildTreeCore()
    {
        if (string.IsNullOrWhiteSpace(_projectDir) || !Directory.Exists(_projectDir))
        {
            return;
        }

        var gameFilesDir = Path.Combine(_projectDir, ModBuilderConstants.GameFilesEditedDir);
        var rootDir = Directory.Exists(gameFilesDir) ? gameFilesDir : _projectDir;

        var rootNode = CreateDirectoryNode(rootDir, _projectDir);
        rootNode.IsExpanded = true;
        ExpandTreeNodes(rootNode);
        _pendingRoot = rootNode;

        foreach (var dirNode in GetSelectedNodes([rootNode]).Where(n => n.IsDirectory))
        {
            _initialCheckedDirs.Add(NormalizeRelativePath(dirNode.RelativePath));
        }
    }

    private FileTreeNode CreateDirectoryNode(string dirPath, string baseProjectDir)
    {
        var relPath = Path.GetRelativePath(baseProjectDir, dirPath).Replace('\\', '/');
        var dirInfo = new DirectoryInfo(dirPath);

        var node = new FileTreeNode
        {
            Name = dirInfo.Name,
            FullPath = dirPath,
            RelativePath = relPath,
            IsDirectory = true,
            IsExpanded = false,
            IsSelected = IsNodeInitiallySelected(relPath, true),
        };

        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileTreeNode.IsSelected))
            {
                OnNodeSelectedChanged(node);
            }
        };

        try
        {
            var subDirs = dirInfo.GetDirectories()
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(subDir => subDir.FullName)
                .Where(fullPath => !ModBuilderConstants.IsIgnoredProjectFile(fullPath));

            foreach (var subDirPath in subDirs)
            {
                var subNode = CreateDirectoryNode(subDirPath, baseProjectDir);
                subNode.Parent = node;
                node.Children.Add(subNode);
            }

            foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (ModBuilderConstants.IsIgnoredProjectFile(file.FullName))
                {
                    continue;
                }

                var fileRelPath = Path.GetRelativePath(baseProjectDir, file.FullName).Replace('\\', '/');
                var fileNode = new FileTreeNode
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    RelativePath = fileRelPath,
                    IsDirectory = false,
                    Size = file.Length,
                    Extension = file.Extension,
                    Parent = node,
                    IsSelected = IsNodeInitiallySelected(fileRelPath, false),
                };

                fileNode.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FileTreeNode.IsSelected))
                    {
                        OnNodeSelectedChanged(fileNode);
                    }
                };

                node.Children.Add(fileNode);
            }
        }
        catch
        {
            // Ignore access errors on individual directories
        }

        return node;
    }

    private void OnNodeSelectedChanged(FileTreeNode node)
    {
        if (_isPropagatingSelection)
        {
            return;
        }

        try
        {
            _isPropagatingSelection = true;
            if (node.IsDirectory)
            {
                CascadeSelectionDown(node, node.IsSelected);
            }

            // Upward propagation
            if (!node.IsSelected)
            {
                var parent = node.Parent;
                while (parent != null)
                {
                    parent.IsSelected = false;
                    parent = parent.Parent;
                }
            }
            else
            {
                var parent = node.Parent;
                while (parent != null && parent.Children.All(c => c.IsSelected))
                {
                    parent.IsSelected = true;
                    parent = parent.Parent;
                }
            }

            UpdateSelectionSummary();
        }
        finally
        {
            _isPropagatingSelection = false;
        }
    }

    private static void CascadeSelectionDown(FileTreeNode parent, bool isSelected)
    {
        foreach (var child in parent.Children)
        {
            child.IsSelected = isSelected;
            if (child.IsDirectory)
            {
                CascadeSelectionDown(child, isSelected);
            }
        }
    }

    /// <summary>
    /// Updates the selection summary count.
    /// </summary>
    public void UpdateSelectionSummary()
    {
        var selectedNodes = GetSelectedNodes(Nodes).ToList();
        var dirCount = selectedNodes.Count(n => n.IsDirectory);
        var fileCount = selectedNodes.Count(n => !n.IsDirectory);

        HasSelection = selectedNodes.Count > 0;
        if (!HasSelection)
        {
            SelectionSummary = "No items selected";
            return;
        }

        var parts = new List<string>();
        if (dirCount > 0)
        {
            parts.Add($"{dirCount} {(dirCount == 1 ? "directory" : "directories")}");
        }

        if (fileCount > 0)
        {
            parts.Add($"{fileCount} {(fileCount == 1 ? "file" : "files")}");
        }

        SelectionSummary = $"Selected: {string.Join(", ", parts)}";
    }

    private bool IsNodeInitiallySelected(string relativePath, bool isDirectory)
    {
        if (_initialPatterns.Count == 0)
        {
            return false;
        }

        var normRel = NormalizeRelativePath(relativePath);
        if (!isDirectory)
        {
            return _initialMatchedFiles.Contains(normRel);
        }

        return _initialDirLookup.Contains(normRel) ||
            _initialDirLookup.Contains(StripEditedPrefix(normRel));
    }

    private static string NormalizeRelativePath(string path) =>
        path.Trim('/').Replace('\\', '/');

    private static bool ExpandTreeNodes(FileTreeNode node)
    {
        var hasSelectedDescendant = false;
        foreach (var child in node.Children)
        {
            var childHasSelected = ExpandTreeNodes(child);
            if (child.IsSelected || childHasSelected)
            {
                hasSelectedDescendant = true;
            }
        }

        if (hasSelectedDescendant || node.IsSelected)
        {
            node.IsExpanded = true;
        }

        return hasSelectedDescendant || node.IsSelected;
    }

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var term = value.Trim();
        foreach (var root in Nodes)
        {
            HighlightMatching(root, term);
        }
    }

    private static string StripEditedPrefix(string normalizedPath) =>
        normalizedPath.StartsWith(ModBuilderConstants.GameFilesEditedPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalizedPath[ModBuilderConstants.GameFilesEditedPrefix.Length..]
            : normalizedPath;

    private static bool HighlightMatching(FileTreeNode node, string term)
    {
        var matches = node.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
        var childMatches = false;
        foreach (var child in node.Children.Where(child => HighlightMatching(child, term)))
        {
            childMatches = true;
        }

        if (matches || childMatches)
        {
            node.IsExpanded = true;
            return true;
        }

        return false;
    }

    private static IEnumerable<FileTreeNode> GetSelectedNodes(IEnumerable<FileTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsSelected)
            {
                yield return node;
            }

            foreach (var child in GetSelectedNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// Generates glob pattern strings and file paths based on current selection.
    /// An unchanged selection returns the original patterns verbatim so no-op saves
    /// never rewrite the configuration.
    /// </summary>
    /// <returns>A list of relative source paths or globs.</returns>
    public List<string> GetGeneratedPatterns()
    {
        var selectedNodes = GetSelectedNodes(Nodes).ToList();
        if (GlobOption == DirectoryGlobOption.AllFiles && IsUnchangedSelection(selectedNodes))
        {
            return new List<string>(_initialPatterns);
        }

        var patterns = new List<string>();
        var emittedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var globCoveredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var checkedFileNodes = selectedNodes.Where(n => !n.IsDirectory).ToList();
        var checkedFiles = new HashSet<string>(
            checkedFileNodes.Select(n => NormalizeRelativePath(n.RelativePath)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var dirNode in selectedNodes.Where(n => n.IsDirectory))
        {
            var covered = EmitDirectorySelection(dirNode, checkedFiles, patterns, emittedPaths);
            globCoveredFiles.UnionWith(covered);
        }

        foreach (var fileNode in checkedFileNodes)
        {
            var rel = NormalizeRelativePath(fileNode.RelativePath);
            if (!globCoveredFiles.Contains(rel) && emittedPaths.Add(rel))
            {
                patterns.Add(rel);
            }
        }

        foreach (var preserved in _preservedPatterns.Where(p => !patterns.Contains(p, StringComparer.OrdinalIgnoreCase)))
        {
            patterns.Add(preserved);
        }

        return patterns;
    }

    private bool IsUnchangedSelection(List<FileTreeNode> selectedNodes)
    {
        var checkedFiles = selectedNodes
            .Where(n => !n.IsDirectory)
            .Select(n => NormalizeRelativePath(n.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checkedDirs = selectedNodes
            .Where(n => n.IsDirectory)
            .Select(n => NormalizeRelativePath(n.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return checkedFiles.SetEquals(_initialMatchedFiles) && checkedDirs.SetEquals(_initialCheckedDirs);
    }

    private IReadOnlyCollection<string> EmitDirectorySelection(
        FileTreeNode dirNode,
        HashSet<string> checkedFiles,
        List<string> patterns,
        HashSet<string> emittedPaths)
    {
        var rel = NormalizeRelativePath(dirNode.RelativePath);
        var glob = BuildDirectoryGlob(rel, GlobOption);
        HashSet<string> matchedByGlob = _snapshot?.MatchFiles([glob]) ?? [];

        // Emit the directory glob when every file it covers is checked, or when it is
        // a pure directory selection. Otherwise explode to the checked files it covers
        // so unchecking individual files is honored on save.
        var matchedChecked = matchedByGlob
            .Where(checkedFiles.Contains)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matchedChecked.Count == matchedByGlob.Count || matchedChecked.Count == 0)
        {
            if (!patterns.Contains(glob, StringComparer.OrdinalIgnoreCase))
            {
                patterns.Add(glob);
            }

            return matchedByGlob;
        }

        foreach (var matched in matchedChecked.Where(emittedPaths.Add))
        {
            patterns.Add(matched);
        }

        return Array.Empty<string>();
    }

    private static string BuildDirectoryGlob(string relativeDir, DirectoryGlobOption globOption) => globOption switch
    {
        DirectoryGlobOption.IniFiles => $"{relativeDir}/**/*.ini",
        DirectoryGlobOption.Textures => $"{relativeDir}/**/*.tga",
        DirectoryGlobOption.WindowUI => $"{relativeDir}/**/*.wnd",
        DirectoryGlobOption.StringTable => $"{relativeDir}/**/*.csf",
        DirectoryGlobOption.Audio => $"{relativeDir}/**/*.wav",
        _ => $"{relativeDir}/**/*.*",
    };
}
