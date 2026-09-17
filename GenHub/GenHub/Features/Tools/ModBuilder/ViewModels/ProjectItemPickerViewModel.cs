using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Features.Tools.ModBuilder.Models;

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
    /// Initializes a new instance of the <see cref="ProjectItemPickerViewModel"/> class.
    /// </summary>
    /// <param name="projectDir">The root directory of the project.</param>
    public ProjectItemPickerViewModel(string projectDir)
    {
        _projectDir = projectDir;
        BuildTree();
    }

    private void BuildTree()
    {
        Nodes.Clear();
        if (string.IsNullOrWhiteSpace(_projectDir) || !Directory.Exists(_projectDir))
        {
            return;
        }

        var gameFilesDir = Path.Combine(_projectDir, ModBuilderConstants.GameFilesEditedDir);
        var rootDir = Directory.Exists(gameFilesDir) ? gameFilesDir : _projectDir;

        var rootNode = CreateDirectoryNode(rootDir, _projectDir);
        rootNode.IsExpanded = true;
        Nodes.Add(rootNode);

        UpdateSelectionSummary();
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
            IsExpanded = true,
        };

        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileTreeNode.IsSelected))
            {
                UpdateSelectionSummary();
            }
        };

        try
        {
            foreach (var subDir in dirInfo.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (ModBuilderConstants.IsIgnoredProjectFile(subDir.FullName))
                {
                    continue;
                }

                var subNode = CreateDirectoryNode(subDir.FullName, baseProjectDir);
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
                };

                fileNode.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FileTreeNode.IsSelected))
                    {
                        UpdateSelectionSummary();
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
    /// </summary>
    /// <returns>A list of relative source paths or globs.</returns>
    public List<string> GetGeneratedPatterns()
    {
        var patterns = new List<string>();
        var selectedNodes = GetSelectedNodes(Nodes).ToList();

        // 1. Process directories
        foreach (var dirNode in selectedNodes.Where(n => n.IsDirectory))
        {
            var rel = dirNode.RelativePath.Trim('/');
            var pattern = GlobOption switch
            {
                DirectoryGlobOption.IniFiles => $"{rel}/**/*.ini",
                DirectoryGlobOption.Textures => $"{rel}/**/*.tga",
                DirectoryGlobOption.WindowUI => $"{rel}/**/*.wnd",
                DirectoryGlobOption.StringTable => $"{rel}/**/*.csf",
                DirectoryGlobOption.Audio => $"{rel}/**/*.wav",
                _ => $"{rel}/**/*.*",
            };

            if (!patterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
            {
                patterns.Add(pattern);
            }
        }

        // 2. Process standalone selected files (if their parent dir wasn't selected)
        var selectedDirPaths = selectedNodes
            .Where(n => n.IsDirectory)
            .Select(n => n.RelativePath.Trim('/'))
            .ToList();

        foreach (var fileNode in selectedNodes.Where(n => !n.IsDirectory))
        {
            var rel = fileNode.RelativePath.Trim('/');
            var coveredByDir = selectedDirPaths.Any(dp => rel.StartsWith($"{dp}/", StringComparison.OrdinalIgnoreCase));
            if (!coveredByDir && !patterns.Contains(rel, StringComparer.OrdinalIgnoreCase))
            {
                patterns.Add(rel);
            }
        }

        return patterns;
    }
}
