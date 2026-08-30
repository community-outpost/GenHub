using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.GameInstallations;
using GenHub.Features.Tools.ModBuilder.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for the file manager panel in ModBuilder.
/// </summary>
public partial class FileManagerViewModel(
    IGameInstallationService gameInstallationService,
    INotificationService notificationService,
    ILogger<FileManagerViewModel> logger) : ObservableObject
{
    private string? _projectPath;
    private string? _gameInstallationPath;

    /// <summary>
    /// Gets the collection of available game installations.
    /// </summary>
    public ObservableCollection<GameInstallationOption> AvailableInstallations { get; } = [];

    /// <summary>
    /// Gets or sets the selected game installation option.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedInstallationPath))]
    private GameInstallationOption? _selectedInstallation;

    /// <summary>
    /// Gets the path of the selected installation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Observable property dependent on SelectedInstallation")]
    public string? SelectedInstallationPath => SelectedInstallation?.Path;

    partial void OnSelectedInstallationChanged(GameInstallationOption? value)
    {
        if (value != null)
        {
            _gameInstallationPath = value.Path;
            if (!IsLoading)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LoadGameFilesAsync(default).ConfigureAwait(false);
                        await LoadProjectFilesAsync(default).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to reload files on installation change");
                    }
                });
            }
        }
    }

    /// <summary>
    /// Gets the collection of game installation file tree nodes.
    /// </summary>
    public ObservableCollection<FileTreeNode> GameFiles { get; } = [];

    /// <summary>
    /// Gets the collection of project file tree nodes.
    /// </summary>
    public ObservableCollection<FileTreeNode> ProjectFiles { get; } = [];

    /// <summary>
    /// Gets the collection of selected game file nodes.
    /// </summary>
    public ObservableCollection<FileTreeNode> SelectedGameFiles { get; } = [];

    /// <summary>
    /// Gets the collection of selected project file nodes.
    /// </summary>
    public ObservableCollection<FileTreeNode> SelectedProjectFiles { get; } = [];

    /// <summary>
    /// Gets or sets the search text for filtering files.
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Gets or sets the selected file type filter.
    /// </summary>
    [ObservableProperty]
    private string _selectedFileType = "All Files";

    /// <summary>
    /// Gets or sets the selected game file node.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGameFiles))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesToProjectCommand))]
    private FileTreeNode? _selectedGameFile;

    /// <summary>
    /// Gets or sets the selected project file node.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProjectFiles))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFilesFromProjectCommand))]
    private FileTreeNode? _selectedProjectFile;

    /// <summary>
    /// Gets a value indicating whether there are selected game files.
    /// </summary>
    public bool HasSelectedGameFiles => SelectedGameFiles.Count > 0 || SelectedGameFile != null;

    /// <summary>
    /// Gets a value indicating whether there are selected project files.
    /// </summary>
    public bool HasSelectedProjectFiles => SelectedProjectFiles.Count > 0 || SelectedProjectFile != null;

    /// <summary>
    /// Gets or sets a value indicating whether files are being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Gets or sets the progress percentage.
    /// </summary>
    [ObservableProperty]
    private double _progressPercentage;

    /// <summary>
    /// Gets or sets a value indicating whether progress is indeterminate.
    /// </summary>
    [ObservableProperty]
    private bool _isIndeterminateProgress = true;

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Ready";

    /// <summary>
    /// Gets or sets the total file count in project.
    /// </summary>
    [ObservableProperty]
    private int _totalFiles;

    /// <summary>
    /// Gets or sets the count of modified files.
    /// </summary>
    [ObservableProperty]
    private int _modifiedFiles;

    /// <summary>
    /// Gets or sets the count of new files.
    /// </summary>
    [ObservableProperty]
    private int _newFiles;

    /// <summary>
    /// Gets the available file type filters.
    /// </summary>
    public ObservableCollection<string> FileTypeFilters { get; } =
    [
        "All Files",
        "INI Files",
        "Image Files (TGA/DDS)",
        "3D Models (W3D)",
        "Scripts (LUA/PY)",
        "Audio Files",
        "Text Files"
    ];

    /// <summary>
    /// Initializes the file manager with project and game paths.
    /// </summary>
    /// <param name="projectPath">The root path of the project.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            IsIndeterminateProgress = true;
            StatusMessage = "Initializing file manager...";

            _projectPath = projectPath;

            // Load all available installations
            var installationsResult = await gameInstallationService.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
            if (installationsResult.Success && installationsResult.Data?.Count > 0)
            {
                PopulateInstallationOptions(installationsResult.Data);
                await LoadGameFilesAsync(cancellationToken).ConfigureAwait(false);
            }

            await LoadProjectFilesAsync(cancellationToken).ConfigureAwait(false);

            StatusMessage = $"Loaded {TotalFiles} project files";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize file manager");
            StatusMessage = "Failed to load files";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void PopulateInstallationOptions(IReadOnlyList<GameInstallation> installations)
    {
        void Apply()
        {
            AvailableInstallations.Clear();
            foreach (var installation in installations)
            {
                AddInstallationOption(installation);
            }

            if (AvailableInstallations.Count > 0 && SelectedInstallation == null)
            {
                SelectedInstallation = AvailableInstallations[0];
            }
        }

        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void AddInstallationOption(GameInstallation installation)
    {
        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
        {
            AvailableInstallations.Add(new GameInstallationOption
            {
                DisplayName = $"Generals ({installation.InstallationType})",
                Path = installation.GeneralsPath,
                IconPath = "avares://GenHub/Assets/Icons/generals-icon.png",
                InstallationType = installation.InstallationType.ToString()
            });
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            AvailableInstallations.Add(new GameInstallationOption
            {
                DisplayName = $"Zero Hour ({installation.InstallationType})",
                Path = installation.ZeroHourPath,
                IconPath = "avares://GenHub/Assets/Icons/zerohour-icon.png",
                InstallationType = installation.InstallationType.ToString()
            });
        }
    }

    /// <summary>
    /// Loads game installation files into the tree.
    /// </summary>
    private async Task LoadGameFilesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_gameInstallationPath) || !Directory.Exists(_gameInstallationPath))
            return;

        await Task.Run(() =>
        {
            var rootNodes = BuildFileTree(_gameInstallationPath, _gameInstallationPath);
            void Apply()
            {
                GameFiles.Clear();
                foreach (var node in rootNodes)
                    GameFiles.Add(node);
            }

            if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
            {
                Apply();
            }
            else
            {
                Dispatcher.UIThread.Post(Apply);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads project files into the tree.
    /// </summary>
    private async Task LoadProjectFilesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_projectPath))
            return;

        var gameFilesEditedPath = Path.Combine(_projectPath, "GameFilesEdited");
        if (!Directory.Exists(gameFilesEditedPath))
        {
            Directory.CreateDirectory(gameFilesEditedPath);
        }

        await Task.Run(async () =>
        {
            var rootNodes = BuildFileTree(gameFilesEditedPath, gameFilesEditedPath);

            // Calculate file statuses
            await CalculateFileStatusesAsync(rootNodes, cancellationToken).ConfigureAwait(false);

            void Apply()
            {
                ProjectFiles.Clear();
                foreach (var node in rootNodes)
                    ProjectFiles.Add(node);

                UpdateFileCounts();
            }

            if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
            {
                Apply();
            }
            else
            {
                Dispatcher.UIThread.Post(Apply);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a file tree from a directory path.
    /// </summary>
    private List<FileTreeNode> BuildFileTree(string path, string rootPath)
    {
        var nodes = new List<FileTreeNode>();

        if (!Directory.Exists(path))
            return nodes;

        try
        {
            // Add directories first
            foreach (var dir in Directory.GetDirectories(path))
            {
                var dirInfo = new DirectoryInfo(dir);
                if (ShouldIncludeDirectory(dirInfo.Name))
                {
                    var node = FileTreeNode.FromPath(dir, rootPath);
                    node.Children.Clear();
                    foreach (var child in BuildFileTree(dir, rootPath))
                        node.Children.Add(child);
                    nodes.Add(node);
                }
            }

            // Add files
            foreach (var file in Directory.GetFiles(path))
            {
                var fileInfo = new FileInfo(file);
                if (ShouldIncludeFile(fileInfo.Name))
                {
                    nodes.Add(FileTreeNode.FromPath(file, rootPath));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to build file tree for {Path}", path);
        }

        return nodes;
    }

    /// <summary>
    /// Calculates file statuses by comparing with game installation.
    /// </summary>
    private async Task CalculateFileStatusesAsync(List<FileTreeNode> rootNodes, CancellationToken cancellationToken)
    {
        var allProjectFileNodes = GetAllFiles(rootNodes).ToList();
        if (allProjectFileNodes.Count == 0 || string.IsNullOrEmpty(_gameInstallationPath))
        {
            return;
        }

        var total = allProjectFileNodes.Count;
        var processed = 0;

        await Parallel.ForEachAsync(
            allProjectFileNodes,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
                CancellationToken = cancellationToken
            },
            async (node, ct) =>
            {
                node.Status = await DetermineFileStatusAsync(node, ct).ConfigureAwait(false);
                var count = Interlocked.Increment(ref processed);
                if (count % 10 == 0 || count == total)
                {
                    var percent = (count / (double)total) * 100.0;
                    Dispatcher.UIThread.Post(() =>
                    {
                        ProgressPercentage = percent;
                        StatusMessage = $"Scanning project files ({count}/{total})...";
                    });
                }
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines the status of a file by comparing with game installation.
    /// </summary>
    private async Task<FileStatus> DetermineFileStatusAsync(FileTreeNode node, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_gameInstallationPath))
            return FileStatus.Unknown;

        var gameFilePath = Path.Combine(_gameInstallationPath, node.RelativePath);

        if (!File.Exists(gameFilePath))
            return FileStatus.New;

        try
        {
            // Fast size comparison first
            var projectInfo = new FileInfo(node.FullPath);
            var gameInfo = new FileInfo(gameFilePath);

            node.GameSizeBytes = gameInfo.Length;

            if (projectInfo.Length != gameInfo.Length)
                return FileStatus.Modified;

            // Fast timestamp check
            if (projectInfo.LastWriteTimeUtc == gameInfo.LastWriteTimeUtc)
                return FileStatus.Unchanged;

            // If sizes match but timestamps differ, check hash for accuracy
            var projectHash = await ComputeFileHashAsync(node.FullPath, cancellationToken).ConfigureAwait(false);
            var gameHash = await ComputeFileHashAsync(gameFilePath, cancellationToken).ConfigureAwait(false);

            return string.Equals(projectHash, gameHash, StringComparison.OrdinalIgnoreCase)
                ? FileStatus.Unchanged
                : FileStatus.Modified;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to compare file {Path}", node.FullPath);
            return FileStatus.Unknown;
        }
    }

    /// <summary>
    /// Computes hash of a file for comparison.
    /// </summary>
    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Updates file count statistics.
    /// </summary>
    private void UpdateFileCounts()
    {
        var allFiles = GetAllFiles(ProjectFiles).ToList();
        TotalFiles = allFiles.Count;
        ModifiedFiles = allFiles.Count(f => f.Status == FileStatus.Modified);
        NewFiles = allFiles.Count(f => f.Status == FileStatus.New);
    }

    /// <summary>
    /// Gets all files recursively from a collection of nodes.
    /// </summary>
    private static IEnumerable<FileTreeNode> GetAllFiles(IEnumerable<FileTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.IsDirectory)
                yield return node;

            foreach (var child in GetAllFiles(node.Children))
                yield return child;
        }
    }

    /// <summary>
    /// Determines if a directory should be included in the tree.
    /// </summary>
    private static bool ShouldIncludeDirectory(string name)
    {
        var excludedDirs = new[] { ".git", ".vs", "bin", "obj", "node_modules", "__pycache__" };
        return !excludedDirs.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines if a file should be included in the tree.
    /// </summary>
    private static bool ShouldIncludeFile(string name)
    {
        var excludedFiles = new[] { ".gitignore", ".gitattributes", "desktop.ini", "thumbs.db" };
        return !excludedFiles.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private List<FileTreeNode> GetSelectedGameFiles()
    {
        if (SelectedGameFiles.Count > 0)
        {
            return SelectedGameFiles.ToList();
        }

        if (SelectedGameFile != null)
        {
            return [SelectedGameFile];
        }

        return [];
    }

    private List<FileTreeNode> GetSelectedProjectFiles()
    {
        if (SelectedProjectFiles.Count > 0)
        {
            return SelectedProjectFiles.ToList();
        }

        if (SelectedProjectFile != null)
        {
            return [SelectedProjectFile];
        }

        return [];
    }

    /// <summary>
    /// Adds selected files from game installation to project.
    /// </summary>
    [RelayCommand]
    private async Task AddFilesToProjectAsync()
    {
        var targetNodes = GetSelectedGameFiles();

        if (targetNodes.Count == 0 || string.IsNullOrEmpty(_projectPath))
            return;

        try
        {
            IsLoading = true;
            IsIndeterminateProgress = false;
            ProgressPercentage = 0;
            StatusMessage = "Preparing files to add...";

            var filesToAdd = new Dictionary<string, FileTreeNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in targetNodes)
            {
                if (node.IsDirectory)
                {
                    foreach (var file in GetAllFiles([node]))
                    {
                        filesToAdd[file.FullPath] = file;
                    }
                }
                else
                {
                    filesToAdd[node.FullPath] = node;
                }
            }

            var fileList = filesToAdd.Values.ToList();
            var total = fileList.Count;
            var gameFilesEditedPath = Path.Combine(_projectPath, "GameFilesEdited");

            var copiedCount = await Task.Run(() =>
            {
                var count = 0;
                for (var i = 0; i < total; i++)
                {
                    var file = fileList[i];
                    var destPath = Path.Combine(gameFilesEditedPath, file.RelativePath);
                    var destDir = Path.GetDirectoryName(destPath);

                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(file.FullPath, destPath, overwrite: true);
                    count++;

                    var current = i + 1;
                    var percent = (current / (double)total) * 100.0;
                    Dispatcher.UIThread.Post(() =>
                    {
                        ProgressPercentage = percent;
                        StatusMessage = $"Adding ({current}/{total}): {file.Name}";
                    });
                }

                return count;
            }).ConfigureAwait(false);

            await LoadProjectFilesAsync(default).ConfigureAwait(false);

            notificationService.ShowSuccess("Files Added", $"Added {copiedCount} file(s) to project");
            StatusMessage = $"Added {copiedCount} file(s)";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add files to project");
            notificationService.ShowError("Add Files Failed", "Failed to add files to project");
            StatusMessage = "Failed to add files";
        }
        finally
        {
            IsLoading = false;
            IsIndeterminateProgress = true;
        }
    }

    /// <summary>
    /// Removes selected files from project.
    /// </summary>
    [RelayCommand]
    private async Task RemoveFilesFromProjectAsync()
    {
        var targetNodes = GetSelectedProjectFiles();

        if (targetNodes.Count == 0)
            return;

        try
        {
            IsLoading = true;
            IsIndeterminateProgress = false;
            ProgressPercentage = 0;
            StatusMessage = "Preparing files to remove...";

            var (filesToRemove, directoriesToRemove) = CollectItemsToRemove(targetNodes);
            var fileList = filesToRemove.Values.ToList();

            await Task.Run(() => DeleteProjectFiles(fileList, directoriesToRemove)).ConfigureAwait(false);

            await LoadProjectFilesAsync(default).ConfigureAwait(false);

            notificationService.ShowSuccess("Files Removed", $"Removed {fileList.Count} file(s) from project");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove files from project");
            notificationService.ShowError("Operation Failed", "Failed to remove some files from the project");
        }
        finally
        {
            IsLoading = false;
            IsIndeterminateProgress = true;
        }
    }

    private static (Dictionary<string, FileTreeNode> Files, List<string> Directories) CollectItemsToRemove(IReadOnlyList<FileTreeNode> targetNodes)
    {
        var filesToRemove = new Dictionary<string, FileTreeNode>(StringComparer.OrdinalIgnoreCase);
        var directoriesToRemove = new List<string>();

        foreach (var node in targetNodes)
        {
            if (node.IsDirectory)
            {
                directoriesToRemove.Add(node.FullPath);
                foreach (var file in GetAllFiles([node]))
                {
                    filesToRemove[file.FullPath] = file;
                }
            }
            else
            {
                filesToRemove[node.FullPath] = node;
            }
        }

        return (filesToRemove, directoriesToRemove);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates observable properties via Dispatcher")]
    private void DeleteProjectFiles(IReadOnlyList<FileTreeNode> fileList, IEnumerable<string> directoriesToRemove)
    {
        var total = fileList.Count;
        for (var i = 0; i < total; i++)
        {
            var file = fileList[i];
            if (File.Exists(file.FullPath))
            {
                File.Delete(file.FullPath);
            }

            var current = i + 1;
            var percent = (current / (double)total) * 100.0;
            Dispatcher.UIThread.Post(() =>
            {
                ProgressPercentage = percent;
                StatusMessage = $"Removing ({current}/{total}): {file.Name}";
            });
        }

        foreach (var dir in directoriesToRemove.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Ignore non-empty directory errors
            }
        }
    }

    /// <summary>
    /// Refreshes both game and project file trees.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(_projectPath))
        {
            await InitializeAsync(_projectPath).ConfigureAwait(false);
        }
    }
}

