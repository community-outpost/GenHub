using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Editors;

/// <summary>
/// Reusable file explorer used by the document editors to browse project folders.
/// Hosts configure file patterns and a folder picker, then open <see cref="FileActivated"/> files.
/// </summary>
public sealed partial class FileExplorerViewModel : ObservableObject
{
    private static readonly EnumerationOptions SafeDirectoryEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.ReparsePoint | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    private readonly ILogger _logger;
    private Func<CancellationToken, Task<string?>>? _browseFolderAsync;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryName))]
    [NotifyPropertyChangedFor(nameof(HasDirectory))]
    private string? _directory;

    [ObservableProperty]
    private string? _currentPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileExplorerViewModel"/> class.
    /// </summary>
    /// <param name="logger">The optional logger for enumeration warnings.</param>
    public FileExplorerViewModel(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets the root explorer nodes.
    /// </summary>
    public ObservableCollection<EditorFileTreeNodeViewModel> Nodes { get; } = [];

    /// <summary>
    /// Gets or sets the file search patterns listed in the explorer. Set before <see cref="Directory"/>.
    /// </summary>
    public IReadOnlyList<string> FilePatterns { get; set; } = ["*"];

    /// <summary>
    /// Gets or sets a value indicating whether file extensions are shown. Set before <see cref="Directory"/>.
    /// </summary>
    public bool ShowFileExtensions { get; set; } = true;

    /// <summary>
    /// Gets or sets the directory names skipped while building the tree. Set before <see cref="Directory"/>.
    /// </summary>
    public IReadOnlyList<string> ExcludedDirectoryNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the node factory used when building the tree. Set before <see cref="Directory"/>.
    /// </summary>
    public Func<string, string, bool, bool, EditorFileTreeNodeViewModel?, EditorFileTreeNodeViewModel> NodeFactory { get; set; } =
        (name, fullPath, isDirectory, isCurrent, parent) => new EditorFileTreeNodeViewModel(name, fullPath, isDirectory, isCurrent, parent);

    /// <summary>
    /// Gets or sets the folder picker invoked by the browse command.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? BrowseFolderAsync
    {
        get => _browseFolderAsync;
        set
        {
            if (SetProperty(ref _browseFolderAsync, value))
            {
                OnPropertyChanged(nameof(CanBrowse));
                BrowseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether browsing for a folder is available.
    /// </summary>
    public bool CanBrowse => _browseFolderAsync is not null;

    /// <summary>
    /// Gets or sets the callback invoked after browsing sets a new directory,
    /// letting hosts open a default file or switch tabs.
    /// </summary>
    public Func<string, CancellationToken, Task>? DirectoryAdoptedAsync { get; set; }

    /// <summary>
    /// Gets the directory name for display in the explorer.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated Directory instance state and is bound from XAML.")]
    public string? DirectoryName
    {
        get
        {
            if (string.IsNullOrEmpty(Directory))
            {
                return null;
            }

            var trimmed = Directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileName = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(fileName) ? Directory : fileName;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a directory is listed.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated Directory instance state and is bound from XAML.")]
    public bool HasDirectory => !string.IsNullOrEmpty(Directory);

    /// <summary>
    /// Raised when the user activates a file node.
    /// </summary>
    public event EventHandler<EditorFileTreeNodeViewModel>? FileActivated;

    /// <summary>
    /// Finds the first file path in the tree, depth first.
    /// </summary>
    /// <returns>The first file path, or null when the tree has no files.</returns>
    public string? FindFirstFile() => FindFirstFilePath(Nodes);

    /// <summary>
    /// Rebuilds the tree from the current directory.
    /// </summary>
    public void Refresh()
    {
        Nodes.Clear();
        if (string.IsNullOrEmpty(Directory) || !System.IO.Directory.Exists(Directory))
        {
            return;
        }

        try
        {
            var rootNode = BuildDirectoryNode(new DirectoryInfo(Directory), CurrentPath, null);
            if (rootNode is not null)
            {
                Nodes.Add(rootNode);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nodes is still empty here: nothing is added until BuildDirectoryNode returns.
            _logger.LogWarning(ex, "Failed to list files in {Directory}", Directory);
        }
    }

    private static string? FindFirstFilePath(IEnumerable<EditorFileTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile)
            {
                return node.FullPath;
            }

            var childFile = FindFirstFilePath(node.Children);
            if (childFile is not null)
            {
                return childFile;
            }
        }

        return null;
    }

    private static void SetNodesExpanded(IEnumerable<EditorFileTreeNodeViewModel> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            if (node.IsDirectory)
            {
                node.IsExpanded = expanded;
                SetNodesExpanded(node.Children, expanded);
            }
        }
    }

    private static void UpdateNodesCurrentState(IEnumerable<EditorFileTreeNodeViewModel> nodes, string? currentPath)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile)
            {
                node.IsCurrent = string.Equals(node.FullPath, currentPath, StringComparison.OrdinalIgnoreCase);
            }

            UpdateNodesCurrentState(node.Children, currentPath);
        }
    }

    partial void OnDirectoryChanged(string? value) => Refresh();

    partial void OnCurrentPathChanged(string? value) => UpdateNodesCurrentState(Nodes, value);

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseAsync(CancellationToken cancellationToken)
    {
        if (_browseFolderAsync is null)
        {
            return;
        }

        var folder = await _browseFolderAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory = folder;
            if (DirectoryAdoptedAsync is not null)
            {
                await DirectoryAdoptedAsync(folder, cancellationToken).ConfigureAwait(true);
            }
        }
    }

    [RelayCommand]
    private void RefreshTree() => Refresh();

    [RelayCommand]
    private void ExpandAll() => SetNodesExpanded(Nodes, true);

    [RelayCommand]
    private void CollapseAll() => SetNodesExpanded(Nodes, false);

    [RelayCommand]
    private void ToggleDirectory(EditorFileTreeNodeViewModel? node)
    {
        if (node is not null && node.IsDirectory)
        {
            node.IsExpanded = !node.IsExpanded;
        }
    }

    [RelayCommand]
    private void OpenFile(EditorFileTreeNodeViewModel? node)
    {
        if (node is null || node.IsDirectory)
        {
            return;
        }

        FileActivated?.Invoke(this, node);
    }

    private EditorFileTreeNodeViewModel? BuildDirectoryNode(DirectoryInfo directoryInfo, string? currentPath, EditorFileTreeNodeViewModel? parent, int depth = 0)
    {
        if (depth > EditorConstants.FileExplorerMaxDepth || IsExcludedDirectory(directoryInfo, parent))
        {
            return null;
        }

        var node = NodeFactory(directoryInfo.Name, directoryInfo.FullName, true, false, parent);
        try
        {
            var subDirectories = directoryInfo
                .EnumerateDirectories("*", SafeDirectoryEnumerationOptions)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var subDirectory in subDirectories)
            {
                var childNode = BuildDirectoryNode(subDirectory, currentPath, node, depth + 1);
                if (childNode is not null && childNode.Children.Count > 0)
                {
                    node.AddChild(childNode);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Access denied enumerating subdirectories in {Path}", directoryInfo.FullName);
        }

        AddFileNodes(node, directoryInfo, currentPath);
        return node.Children.Count == 0 ? null : node;
    }

    private bool IsExcludedDirectory(DirectoryInfo directoryInfo, EditorFileTreeNodeViewModel? parent)
    {
        if (parent is null)
        {
            return false;
        }

        if (directoryInfo.Name.StartsWith('.'))
        {
            return true;
        }

        return ExcludedDirectoryNames.Contains(directoryInfo.Name, StringComparer.OrdinalIgnoreCase);
    }

    private void AddFileNodes(EditorFileTreeNodeViewModel node, DirectoryInfo directoryInfo, string? currentPath)
    {
        try
        {
            var files = FilePatterns
                .SelectMany(pattern => directoryInfo.EnumerateFiles(pattern))
                .DistinctBy(file => file.FullName, StringComparer.Ordinal)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var isCurrent = string.Equals(file.FullName, currentPath, StringComparison.OrdinalIgnoreCase);
                var displayName = ShowFileExtensions ? file.Name : Path.GetFileNameWithoutExtension(file.Name);
                node.AddChild(NodeFactory(displayName, file.FullName, false, isCurrent, node));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Access denied enumerating files in {Path}", directoryInfo.FullName);
        }
    }
}
