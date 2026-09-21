using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Messages;
using GenHub.Core.Models.GameInstallations;
using GenHub.Features.Tools.ModBuilder.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
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
    IWndDocumentService wndDocumentService,
    ILocalizationService localizationService,
    ILogger<FileManagerViewModel> logger) : ObservableObject, IDisposable
{
    private enum WndFileOperation
    {
        Validate,
        Format,
    }

    private readonly ConcurrentDictionary<string, (long Length, DateTime LastWriteTimeUtc, string Hash)> _fileHashCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private CancellationTokenSource? _reloadCts;
    private string? _projectPath;
    private string? _gameInstallationPath;
    private string? _gameFilesEditedDir;

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
                _reloadCts?.Cancel();
                _reloadCts?.Dispose();
                var cts = new CancellationTokenSource();
                _reloadCts = cts;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _loadLock.WaitAsync(cts.Token).ConfigureAwait(false);
                        try
                        {
                            await LoadGameFilesAsync(cts.Token).ConfigureAwait(false);
                            await LoadProjectFilesAsync(cts.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            try
                            {
                                _loadLock.Release();
                            }
                            catch (ObjectDisposedException)
                            {
                                // Disposed concurrently
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected on rapid selection change
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to reload files on installation change");
                    }
                }, cts.Token);
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
    /// <param name="gameFilesEditedDir">The optional configured game files edited directory name or path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync(
        string projectPath,
        string? gameFilesEditedDir = null,
        CancellationToken cancellationToken = default)
    {
        var lockAcquired = false;
        try
        {
            await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockAcquired = true;

            IsLoading = true;
            IsIndeterminateProgress = true;
            StatusMessage = "Initializing file manager...";

            _projectPath = projectPath;
            _gameFilesEditedDir = gameFilesEditedDir;

            // Load all available installations
            var installationsResult = await gameInstallationService.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
            if (installationsResult.Success && installationsResult.Data?.Count > 0)
            {
                await PopulateInstallationOptionsAsync(installationsResult.Data).ConfigureAwait(false);
                await LoadGameFilesAsync(cancellationToken).ConfigureAwait(false);
            }

            await LoadProjectFilesAsync(cancellationToken).ConfigureAwait(false);

            StatusMessage = $"Loaded {TotalFiles} project files";
            var loadedTitle = localizationService?.GetString("Tools.ModBuilder.Notification.FilesLoaded.Title") ?? "Files Loaded";
            var loadedMsg = localizationService?.GetString("Tools.ModBuilder.Notification.FilesLoaded.Message", TotalFiles) ?? $"Loaded {TotalFiles} project files";
            notificationService.ShowSuccess(loadedTitle, loadedMsg);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogDebug(ex, "Initialization of file manager was canceled");
            StatusMessage = "File loading canceled";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize file manager");
            StatusMessage = "Failed to load files";
        }
        finally
        {
            IsLoading = false;
            if (lockAcquired)
            {
                _loadLock.Release();
            }
        }
    }

    private async Task PopulateInstallationOptionsAsync(IReadOnlyList<GameInstallation> installations)
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
            await Dispatcher.UIThread.InvokeAsync(Apply);
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
                IconPath = UriConstants.GeneralsIconUri,
                InstallationType = installation.InstallationType.ToString()
            });
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            AvailableInstallations.Add(new GameInstallationOption
            {
                DisplayName = $"Zero Hour ({installation.InstallationType})",
                Path = installation.ZeroHourPath,
                IconPath = UriConstants.ZeroHourIconUri,
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
    /// Gets the absolute path to the game files edited directory for the current project.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when project path is not set and the directory name is relative.</exception>
    private string GetGameFilesEditedPath()
    {
        var dirName = !string.IsNullOrWhiteSpace(_gameFilesEditedDir)
            ? _gameFilesEditedDir
            : ModBuilderConstants.GameFilesEditedDir;

        if (Path.IsPathRooted(dirName))
        {
            return dirName;
        }

        if (string.IsNullOrEmpty(_projectPath))
        {
            throw new InvalidOperationException("Project path must be set before resolving relative GameFilesEdited directory.");
        }

        return Path.Combine(_projectPath, dirName);
    }

    /// <summary>
    /// Loads project files into the tree.
    /// </summary>
    private async Task LoadProjectFilesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_projectPath))
            return;

        var gameFilesEditedPath = GetGameFilesEditedPath();
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
            var projectHash = await GetOrComputeFileHashAsync(node.FullPath, projectInfo, cancellationToken).ConfigureAwait(false);
            var gameHash = await GetOrComputeFileHashAsync(gameFilePath, gameInfo, cancellationToken).ConfigureAwait(false);

            return string.Equals(projectHash, gameHash, StringComparison.OrdinalIgnoreCase)
                ? FileStatus.Unchanged
                : FileStatus.Modified;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to compare file {Path}", node.FullPath);
            return FileStatus.Unknown;
        }
    }

    private async Task<string> GetOrComputeFileHashAsync(string filePath, FileInfo fileInfo, CancellationToken cancellationToken)
    {
        if (_fileHashCache.TryGetValue(filePath, out var cached) &&
            cached.Length == fileInfo.Length &&
            cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
        {
            return cached.Hash;
        }

        var hash = await ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false);
        _fileHashCache[filePath] = (fileInfo.Length, fileInfo.LastWriteTimeUtc, hash);
        return hash;
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

    private static List<FileTreeNode> CollectFilesToAdd(IReadOnlyList<FileTreeNode> targetNodes)
    {
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

        return filesToAdd.Values.ToList();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates observable properties via Dispatcher")]
    private int CopyFilesToProject(IReadOnlyList<FileTreeNode> fileList, string gameFilesEditedPath, CancellationToken cancellationToken)
    {
        var count = 0;
        var total = fileList.Count;
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = fileList[i];
            var destPath = Path.Combine(gameFilesEditedPath, file.RelativePath);
            var destDir = Path.GetDirectoryName(destPath);

            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            if (File.Exists(destPath))
            {
                var backupPath = $"{destPath}{ModBuilderConstants.BackupFileExtension}";
                File.Copy(destPath, backupPath, overwrite: true);
                logger.LogInformation("Existing file backed up to {BackupPath} before copy", backupPath);
            }

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
    }

    /// <summary>
    /// Adds selected files from game installation to project.
    /// </summary>
    [RelayCommand]
    private async Task AddFilesToProjectAsync(CancellationToken cancellationToken = default)
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

            var fileList = CollectFilesToAdd(targetNodes);
            var gameFilesEditedPath = GetGameFilesEditedPath();

            var copiedCount = await Task.Run(
                () => CopyFilesToProject(fileList, gameFilesEditedPath, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            await LoadProjectFilesAsync(cancellationToken).ConfigureAwait(false);

            var addedTitle = localizationService?.GetString("Tools.ModBuilder.Notification.FilesAdded.Title") ?? "Files Added";
            var addedMsg = localizationService?.GetString("Tools.ModBuilder.Notification.FilesAdded.Message", copiedCount) ?? $"Added {copiedCount} file(s) to project";
            notificationService.ShowSuccess(addedTitle, addedMsg);
            StatusMessage = $"Added {copiedCount} file(s)";
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Add files to project was cancelled");
            StatusMessage = "Add files cancelled";
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
    private async Task RemoveFilesFromProjectAsync(CancellationToken cancellationToken = default)
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

            await Task.Run(() => DeleteProjectFiles(fileList, directoriesToRemove, cancellationToken), cancellationToken).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() => StatusMessage = $"Removed {fileList.Count} file(s) from project");
            await LoadProjectFilesAsync(cancellationToken).ConfigureAwait(false);

            var removedTitle = localizationService?.GetString("Tools.ModBuilder.Notification.FilesRemoved.Title") ?? "Files Removed";
            var removedMsg = localizationService?.GetString("Tools.ModBuilder.Notification.FilesRemoved.Message", fileList.Count) ?? $"Removed {fileList.Count} file(s) from project";
            notificationService.ShowSuccess(removedTitle, removedMsg);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Remove files from project was cancelled");
            StatusMessage = "Remove files cancelled";
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
    private void DeleteProjectFiles(IReadOnlyList<FileTreeNode> fileList, IEnumerable<string> directoriesToRemove, CancellationToken cancellationToken = default)
    {
        var total = fileList.Count;
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Ignore non-empty directory errors
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore non-empty directory errors
            }
        }
    }

    /// <summary>
    /// Validates selected window definition (.wnd) project files.
    /// </summary>
    [RelayCommand]
    private async Task ValidateWndFilesAsync(CancellationToken cancellationToken = default)
    {
        await RunWndFileOperationAsync(WndFileOperation.Validate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Formats selected window definition (.wnd) project files in canonical form.
    /// </summary>
    [RelayCommand]
    private async Task FormatWndFilesAsync(CancellationToken cancellationToken = default)
    {
        await RunWndFileOperationAsync(WndFileOperation.Format, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunWndFileOperationAsync(WndFileOperation operation, CancellationToken cancellationToken)
    {
        var keys = operation switch
        {
            WndFileOperation.Validate => (
                Progress: "Tools.ModBuilder.Wnd.ProgressValidating",
                SuccessTitle: "Tools.ModBuilder.Wnd.ValidateSuccessTitle",
                SuccessMessage: "Tools.ModBuilder.Wnd.ValidateSuccessMessage",
                IssuesTitle: "Tools.ModBuilder.Wnd.ValidateIssuesTitle",
                IssuesMessage: "Tools.ModBuilder.Wnd.ValidateIssuesMessage",
                RefreshAfter: false),
            WndFileOperation.Format => (
                Progress: "Tools.ModBuilder.Wnd.ProgressFormatting",
                SuccessTitle: "Tools.ModBuilder.Wnd.FormatSuccessTitle",
                SuccessMessage: "Tools.ModBuilder.Wnd.FormatSuccessMessage",
                IssuesTitle: "Tools.ModBuilder.Wnd.FormatIssuesTitle",
                IssuesMessage: "Tools.ModBuilder.Wnd.FormatIssuesMessage",
                RefreshAfter: true),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        if (IsLoading)
        {
            return;
        }

        var wndFiles = CollectSelectedWndFiles();
        if (wndFiles.Count == 0)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.ModBuilder.Wnd.NoSelectionTitle"),
                localizationService.GetString("Tools.ModBuilder.Wnd.NoSelectionMessage"),
                NotificationDurations.Short);
            return;
        }

        try
        {
            IsLoading = true;
            IsIndeterminateProgress = false;
            var succeededCount = 0;
            string? firstProblem = null;
            var total = wndFiles.Count;
            for (var i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = wndFiles[i];
                var outcome = operation == WndFileOperation.Validate
                    ? await ValidateSingleWndFileAsync(file, cancellationToken).ConfigureAwait(false)
                    : await FormatSingleWndFileAsync(file, cancellationToken).ConfigureAwait(false);
                if (outcome.Succeeded)
                {
                    succeededCount++;
                }
                else
                {
                    firstProblem ??= outcome.Problem ?? file;
                }

                var current = i + 1;
                var percent = (current / (double)total) * 100.0;
                ReportWndProgress(percent, keys.Progress, current, total, file);
            }

            if (keys.RefreshAfter)
            {
                await LoadProjectFilesAsync(cancellationToken).ConfigureAwait(false);
            }

            void SetFinalStatus(string msg)
            {
                if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
                {
                    StatusMessage = msg;
                }
                else
                {
                    Dispatcher.UIThread.Post(() => StatusMessage = msg);
                }
            }

            if (succeededCount == total)
            {
                var message = localizationService.GetString(keys.SuccessMessage, succeededCount, total);
                notificationService.ShowSuccess(
                    localizationService.GetString(keys.SuccessTitle),
                    message,
                    NotificationDurations.Medium);
                SetFinalStatus(message);
            }
            else
            {
                var message = localizationService.GetString(keys.IssuesMessage, succeededCount, total, firstProblem ?? string.Empty);
                notificationService.ShowWarning(
                    localizationService.GetString(keys.IssuesTitle),
                    message,
                    NotificationDurations.Long);
                SetFinalStatus(message);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "WND file operation was cancelled");
            if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
            {
                StatusMessage = localizationService.GetString("Tools.ModBuilder.Wnd.OperationCancelled");
            }
            else
            {
                Dispatcher.UIThread.Post(() => StatusMessage = localizationService.GetString("Tools.ModBuilder.Wnd.OperationCancelled"));
            }
        }
        finally
        {
            IsLoading = false;
            IsIndeterminateProgress = true;
        }
    }

    private void ReportWndProgress(double percent, string progressKey, int current, int total, string file)
    {
        void Apply()
        {
            ProgressPercentage = percent;
            StatusMessage = localizationService.GetString(progressKey, current, total, Path.GetFileName(file));
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

    private async Task<(bool Succeeded, string? Problem)> ValidateSingleWndFileAsync(string file, CancellationToken cancellationToken)
    {
        var result = await wndDocumentService.ValidateFileAsync(file, cancellationToken).ConfigureAwait(false);
        if (result.IsValid)
        {
            return (true, null);
        }

        var problem = result.Issues.Count > 0 ? result.Issues[0].Message : result.FirstError;
        return (false, problem);
    }

    private async Task<(bool Succeeded, string? Problem)> FormatSingleWndFileAsync(string file, CancellationToken cancellationToken)
    {
        var result = await wndDocumentService.FormatFileAsync(file, cancellationToken).ConfigureAwait(false);
        return (result.Success, result.FirstError);
    }

    /// <summary>
    /// Opens the first selected window definition (.wnd) file in the WND editor tool.
    /// </summary>
    [RelayCommand]
    private void EditWndFile()
    {
        var wndFiles = CollectSelectedWndFiles();
        if (wndFiles.Count == 0)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.ModBuilder.Wnd.NoSelectionTitle"),
                localizationService.GetString("Tools.ModBuilder.Wnd.NoSelectionMessage"),
                NotificationDurations.Short);
            return;
        }

        WeakReferenceMessenger.Default.Send(new OpenFileInToolMessage(ToolConstants.WndEditor.Id, wndFiles[0]));
    }

    private List<string> CollectSelectedWndFiles()
    {
        var selected = GetSelectedProjectFiles();
        var wndFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in selected)
        {
            if (node.IsDirectory)
            {
                foreach (var file in GetAllFiles([node]))
                {
                    AddIfWndFile(wndFiles, file.FullPath);
                }
            }
            else
            {
                AddIfWndFile(wndFiles, node.FullPath);
            }
        }

        return wndFiles.Values.ToList();
    }

    private static void AddIfWndFile(Dictionary<string, string> wndFiles, string fullPath)
    {
        if (string.Equals(Path.GetExtension(fullPath), ModBuilderConstants.FileExtensions.Wnd, StringComparison.OrdinalIgnoreCase))
        {
            wndFiles[fullPath] = fullPath;
        }
    }

    /// <summary>
    /// Event raised when the user requests importing .BIG files into the project.
    /// Handled by the parent ModBuilderViewModel which has access to the window dialog service.
    /// </summary>
    public event Func<Task>? ImportBigFilesRequested;

    /// <summary>
    /// Requests importing one or more .BIG archives into the project's GameFilesEdited folder.
    /// </summary>
    [RelayCommand]
    private async Task ImportBigFilesAsync()
    {
        if (ImportBigFilesRequested != null)
        {
            await ImportBigFilesRequested.Invoke().ConfigureAwait(false);
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
            if (_reloadCts != null)
            {
                await _reloadCts.CancelAsync().ConfigureAwait(false);
                _reloadCts.Dispose();
                _reloadCts = null;
            }

            var cts = new CancellationTokenSource();
            _reloadCts = cts;
            try
            {
                await InitializeAsync(_projectPath, _gameFilesEditedDir, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Refresh cancelled
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    /// <param name="disposing">Whether to dispose managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reloadCts?.Cancel();
            _reloadCts?.Dispose();
        }
    }
}
