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
public partial class FileManagerViewModel : ObservableObject
{
    private readonly IGameInstallationService _gameInstallationService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<FileManagerViewModel> _logger;
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
                        _logger.LogError(ex, "Failed to reload files on installation change");
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
    private FileTreeNode? _selectedGameFile;

    /// <summary>
    /// Gets or sets the selected project file node.
    /// </summary>
    [ObservableProperty]
    private FileTreeNode? _selectedProjectFile;

    /// <summary>
    /// Gets or sets a value indicating whether files are being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

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

    public FileManagerViewModel(
        IGameInstallationService gameInstallationService,
        INotificationService notificationService,
        ILogger<FileManagerViewModel> logger)
    {
        _gameInstallationService = gameInstallationService;
        _notificationService = notificationService;
        _logger = logger;
    }

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
            StatusMessage = "Initializing file manager...";

            _projectPath = projectPath;

            // Load all available installations
            var installationsResult = await _gameInstallationService.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
            if (installationsResult.Success && installationsResult.Data?.Count > 0)
            {
                void PopulateInstallations()
                {
                    AvailableInstallations.Clear();
                    foreach (var installation in installationsResult.Data)
                    {
                        // Add Generals option if available
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

                        // Add Zero Hour option if available
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

                    // Select first installation by default
                    if (AvailableInstallations.Count > 0)
                    {
                        SelectedInstallation = AvailableInstallations[0];
                        _gameInstallationPath = SelectedInstallation.Path;
                    }
                }

                PopulateInstallations();
                await LoadGameFilesAsync(cancellationToken).ConfigureAwait(false);
            }

            await LoadProjectFilesAsync(cancellationToken).ConfigureAwait(false);

            StatusMessage = $"Loaded {TotalFiles} project files";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize file manager");
            StatusMessage = "Failed to load files";
        }
        finally
        {
            IsLoading = false;
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
            _logger.LogWarning(ex, "Failed to build file tree for {Path}", path);
        }

        return nodes;
    }

    /// <summary>
    /// Calculates file statuses by comparing with game installation.
    /// </summary>
    private async Task CalculateFileStatusesAsync(List<FileTreeNode> nodes, CancellationToken cancellationToken)
    {
        foreach (var node in nodes)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (node.IsDirectory)
            {
                await CalculateFileStatusesAsync(node.Children.ToList(), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                node.Status = await DetermineFileStatusAsync(node, cancellationToken).ConfigureAwait(false);
            }
        }
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

            // If sizes match, check hash for accuracy
            var projectHash = await ComputeFileHashAsync(node.FullPath, cancellationToken).ConfigureAwait(false);
            var gameHash = await ComputeFileHashAsync(gameFilePath, cancellationToken).ConfigureAwait(false);

            return projectHash == gameHash ? FileStatus.Unchanged : FileStatus.Modified;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compare file {Path}", node.FullPath);
            return FileStatus.Unknown;
        }
    }

    /// <summary>
    /// Computes MD5 hash of a file.
    /// </summary>
    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await MD5.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Adds selected files from game installation to project.
    /// </summary>
    [RelayCommand]
    private async Task AddFilesToProjectAsync()
    {
        if (SelectedGameFile == null || string.IsNullOrEmpty(_projectPath))
            return;

        try
        {
            IsLoading = true;
            StatusMessage = "Adding files to project...";

            var filesToAdd = SelectedGameFile.IsDirectory
                ? GetAllFiles([SelectedGameFile]).ToList()
                : [SelectedGameFile];

            var gameFilesEditedPath = Path.Combine(_projectPath, "GameFilesEdited");
            var copiedCount = await Task.Run(() =>
            {
                var count = 0;
                foreach (var file in filesToAdd)
                {
                    var destPath = Path.Combine(gameFilesEditedPath, file.RelativePath);
                    var destDir = Path.GetDirectoryName(destPath);

                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    if (!File.Exists(destPath))
                    {
                        File.Copy(file.FullPath, destPath, overwrite: false);
                        count++;
                    }
                }

                return count;
            }).ConfigureAwait(false);

            await LoadProjectFilesAsync(default).ConfigureAwait(false);

            _notificationService.ShowSuccess("Files Added", $"Added {copiedCount} file(s) to project");
            StatusMessage = $"Added {copiedCount} file(s)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add files to project");
            _notificationService.ShowError("Add Files Failed", "Failed to add files to project");
            StatusMessage = "Failed to add files";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Removes selected files from project.
    /// </summary>
    [RelayCommand]
    private async Task RemoveFilesFromProjectAsync()
    {
        if (SelectedProjectFile == null)
            return;

        try
        {
            IsLoading = true;
            StatusMessage = "Removing files from project...";

            var filesToRemove = SelectedProjectFile.IsDirectory
                ? GetAllFiles([SelectedProjectFile]).ToList()
                : [SelectedProjectFile];

            await Task.Run(() =>
            {
                foreach (var file in filesToRemove)
                {
                    if (File.Exists(file.FullPath))
                        File.Delete(file.FullPath);
                }

                // Remove empty directories
                if (SelectedProjectFile.IsDirectory && Directory.Exists(SelectedProjectFile.FullPath))
                {
                    try
                    {
                        Directory.Delete(SelectedProjectFile.FullPath, recursive: true);
                    }
                    catch
                    {
                        // Directory might not be empty
                    }
                }
            }).ConfigureAwait(false);

            await LoadProjectFilesAsync(default).ConfigureAwait(false);

            _notificationService.ShowSuccess("Files Removed", $"Removed {filesToRemove.Count} file(s) from project");
            StatusMessage = $"Removed {filesToRemove.Count} file(s)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove files from project");
            _notificationService.ShowError("Remove Files Failed", "Failed to remove files from project");
            StatusMessage = "Failed to remove files";
        }
        finally
        {
            IsLoading = false;
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
