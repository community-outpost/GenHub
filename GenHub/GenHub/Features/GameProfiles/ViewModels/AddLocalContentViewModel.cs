using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// View model for the "Add Local Content" dialog.
/// </summary>
/// <param name="localContentService">Service for handling local content operations.</param>
/// <param name="contentStorageService">Service for content storage operations.</param>
/// <param name="genLauncherNormalizationService">Service for GenLauncher file normalization.</param>
/// <param name="dialogService">Service for showing dialogs.</param>
/// <param name="logger">Logger instance.</param>
public partial class AddLocalContentViewModel(
    ILocalContentService localContentService,
    IContentStorageService? contentStorageService,
    IGenLauncherNormalizationService? genLauncherNormalizationService,
    IDialogService? dialogService,
    ILogger<AddLocalContentViewModel>? logger = null) : ObservableObject
{
    /// <summary>
    /// Gets the list of available game types.
    /// </summary>
    public static GameType[] AvailableGameTypes { get; } =
    [
        GameType.Generals,
        GameType.ZeroHour,
    ];

    /// <summary>
    /// Gets the list of allowed content types for the dialog.
    /// </summary>
    public static ContentType[] AllowedContentTypes { get; } =
    [
        ContentType.Mod,
        ContentType.GameClient,
        ContentType.Executable,
        ContentType.ModdingTool,
        ContentType.Patch,
        ContentType.Addon,
        ContentType.Map,
        ContentType.MapPack,
        ContentType.Mission,
    ];

    private static FileTreeItem? FindFirstExecutable(IEnumerable<FileTreeItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsExecutable)
            {
                return item;
            }

            var childExe = FindFirstExecutable(item.Children);
            if (childExe != null)
            {
                return childExe;
            }
        }

        return null;
    }

    private static int CountExecutables(IEnumerable<FileTreeItem> items)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item.IsExecutable) count++;
            count += CountExecutables(item.Children);
        }

        return count;
    }

    private readonly string _stagingPath = Path.Combine(Path.GetTempPath(), "GenHub_Staging_" + Guid.NewGuid());

    private string? _originalManifestId;

    /// <summary>
    /// Gets a value indicating whether we are editing existing content.
    /// </summary>
    public bool IsEditing => _originalManifestId != null;

    /// <summary>
    /// Gets the title for the dialog.
    /// </summary>
    public string DialogTitle => IsEditing ? "Edit Local Content" : "Add Local Content";

    /// <summary>
    /// Gets the text to display on the action button.
    /// </summary>
    public string ActionButtonText => IsEditing ? "Save Changes" : "Add to Library";

    private CancellationTokenSource? _cts;

    /// <summary>
    /// Gets or sets the name of the content.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private string _contentName = string.Empty;

    /// <summary>
    /// Gets or sets the source path of the content.
    /// </summary>
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the source is a zip archive.
    /// </summary>
    [ObservableProperty]
    private bool _isSourceZip;

    /// <summary>
    /// Gets or sets the selected content type.
    /// </summary>
    [ObservableProperty]
    private ContentType _selectedContentType = ContentType.Mod; // Default to Mod as requested

    /// <summary>
    /// Gets or sets the selected game type.
    /// </summary>
    [ObservableProperty]
    private GameType _selectedGameType = GameType.ZeroHour;

    /// <summary>
    /// Gets the file structure tree for preview.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FileTreeItem> _fileTree = [];

    /// <summary>
    /// Gets or sets a value indicating whether the view model is busy.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingOverlay))]
    private bool _isBusy;

    /// <summary>
    /// Gets a value indicating whether the loading overlay should be visible.
    /// Virtual to allow demos to suppress it.
    /// </summary>
    public virtual bool ShowLoadingOverlay => IsBusy;

    /// <summary>
    /// Gets or sets the status message for the user.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether content can be added.
    /// </summary>
    [ObservableProperty]
    private bool _canAdd;

    /// <summary>
    /// Gets or sets a value indicating whether the view model is in demo mode.
    /// </summary>
    [ObservableProperty]
    private bool _isDemoMode;

    /// <summary>
    /// Gets or sets the selected executable item (for Executable/ModdingTool content type).
    /// </summary>
    [ObservableProperty]
    private FileTreeItem? _selectedExecutableItem;

    /// <summary>
    /// Gets or sets the number of executables found in the staging area.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExecutableSelection))]
    private int _executableCount;

    /// <summary>
    /// Gets a value indicating whether the executable selection should be shown.
    /// </summary>
    public bool ShowExecutableSelection => (SelectedContentType == ContentType.ModdingTool || SelectedContentType == ContentType.Executable) && ExecutableCount > 1;

    /// <summary>
    /// Gets the text to display in the preview area when no content is loaded.
    /// </summary>
    public string PreviewIdleText => SelectedContentType switch
    {
        ContentType.Mod => "Import mod content (e.g. .big, .zip)",
        ContentType.GameClient => "Import GameClient",
        ContentType.Executable => "Import executable",
        ContentType.ModdingTool => "Import tool executable",
        ContentType.Patch => "Import patch",
        ContentType.Addon => "Import addon content",
        ContentType.Map => "Import map files",
        ContentType.MapPack => "Import map pack files",
        ContentType.Mission => "Import mission content",
        _ => "Drag and drop content to begin",
    };

    /// <summary>
    /// Event triggered when the window should be closed.
    /// </summary>
    public event EventHandler<bool>? RequestClose;

    /// <summary>
    /// Event triggered when content has been successfully added.
    /// </summary>
    public event EventHandler? ContentAdded;

    /// <summary>
    /// Gets the created content item after successful import.
    /// </summary>
    public ContentDisplayItem? CreatedContentItem { get; private set; }

    /// <summary>
    /// Gets or sets the action to browse for a folder.
    /// </summary>
    public Func<Task<string?>>? BrowseFolderAction { get; set; }

    /// <summary>
    /// Gets or sets the action to browse for files.
    /// </summary>
    public Func<Task<IReadOnlyList<string>?>>? BrowseFileAction { get; set; }

    /// <summary>
    /// Loads existing content for editing.
    /// </summary>
    /// <param name="item">The item to load.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task LoadFromManifestAsync(ContentDisplayItem item)
    {
        if (contentStorageService == null)
        {
            StatusMessage = "Storage service unavailable.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Loading existing content...";

            _originalManifestId = item.ManifestId.Value;
            ContentName = item.DisplayName ?? string.Empty;
            SelectedContentType = item.ContentType;
            SelectedGameType = item.GameType;
            SourcePath = item.SourcePath ?? string.Empty;

            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(DialogTitle));
            OnPropertyChanged(nameof(ActionButtonText));

            // Prepare staging directory
            if (Directory.Exists(_stagingPath))
            {
                Directory.Delete(_stagingPath, true);
            }

            Directory.CreateDirectory(_stagingPath);

            // Retrieve content from CAS to staging
            var result = await contentStorageService.RetrieveContentAsync(
                Core.Models.Manifest.ManifestId.Create(_originalManifestId),
                _stagingPath);

            if (result.Success)
            {
                StatusMessage = "Success!";
                await RefreshStagingTreeAsync();
            }
            else
            {
                StatusMessage = $"Failed to load content: {result.FirstError}";
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error loading content for editing");
            StatusMessage = $"Error loading content: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Imports content from the specified path into the staging directory.
    /// </summary>
    /// <param name="path">The local path to the file or directory.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task ImportContentAsync(string path)
    {
        logger?.LogDebug("ImportContentAsync called with path: {Path}", path);

        if (string.IsNullOrWhiteSpace(path))
        {
            logger?.LogWarning("ImportContentAsync: Path is null or whitespace.");
            return;
        }

        // Only set SourcePath if not already set or empty (support multiple imports)
        if (string.IsNullOrEmpty(SourcePath))
        {
            SourcePath = path;
        }

        if (string.IsNullOrWhiteSpace(ContentName) && string.IsNullOrEmpty(SourcePath))
        {
            // Use the folder name or first file name as default content name if not set
            ContentName = Path.GetFileNameWithoutExtension(path);
        }
        else if (string.IsNullOrWhiteSpace(ContentName))
        {
            // If adding more files, don't overwrite name unless empty
            ContentName = Path.GetFileNameWithoutExtension(path);
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Importing {Path.GetFileName(path)}...";
            logger?.LogInformation("Importing content from {Path} to staging {Staging}", path, _stagingPath);

            if (!Directory.Exists(_stagingPath))
            {
                Directory.CreateDirectory(_stagingPath);
            }

            if (File.Exists(path))
            {
                var extension = Path.GetExtension(path);
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Run(() => ZipFile.ExtractToDirectory(path, _stagingPath, true));
                }
                else
                {
                    var destFile = Path.Combine(_stagingPath, Path.GetFileName(path));
                    File.Copy(path, destFile, true);
                }
            }
            else if (Directory.Exists(path))
            {
                // Preserve directory structure by copying the folder itself into staging
                var dirInfo = new DirectoryInfo(path);
                var dirName = dirInfo.Name;

                // Ensure we don't try to copy to the staging root itself if Name is somehow empty
                if (string.IsNullOrWhiteSpace(dirName))
                {
                    dirName = "Imported_Folder";
                }

                var targetSubDir = Path.Combine(_stagingPath, dirName);
                logger?.LogDebug("ImportContentAsync: Preserving directory structure. Source: {Source}, Target: {Target}", path, targetSubDir);

                await Task.Run(() => CopyDirectory(dirInfo, new DirectoryInfo(targetSubDir)));
            }

            // Auto-organization: If we have .map files at the root level, move them into subdirectories
            CreateMapFoldersIfNeeded();

            // Detect and normalize GenLauncher files
            try
            {
                if (genLauncherNormalizationService != null && dialogService != null)
                {
                    var detectionResult = await genLauncherNormalizationService.DetectGenLauncherFilesAsync(_stagingPath);

                    if (detectionResult.HasGenLauncherFiles)
                    {
                        logger?.LogInformation("GenLauncher files detected: {Summary}", detectionResult.GetSummary());

                        var shouldNormalize = await dialogService.ShowConfirmationAsync(
                            "GenLauncher Files Detected",
                            $"This content contains GenLauncher-modified files:\n\n{detectionResult.GetSummary()}\n\nWould you like to normalize these files to standard format?\n\n" +
                            "This will:\n" +
                            "• Convert .gib files to .big\n" +
                            "• Remove .GLR, .GOF, .GLTC suffixes\n" +
                            "• Remove symbolic links",
                            "Normalize",
                            "Skip",
                            sessionKey: GenLauncherConstants.NormalizationDialogSessionKey);

                        if (shouldNormalize)
                        {
                            StatusMessage = "Normalizing GenLauncher files...";
                            logger?.LogInformation("User confirmed normalization");

                            var normalizationResult = await genLauncherNormalizationService.NormalizeFilesAsync(
                                _stagingPath,
                                CancellationToken.None);

                            if (normalizationResult.Success)
                            {
                                var result = normalizationResult.Data;
                                StatusMessage = $"Normalized {result.NormalizedCount} file(s). Import successful.";
                                logger?.LogInformation(
                                    "Normalization completed: {NormalizedCount} files, {SymlinksRemoved} symlinks removed",
                                    result.NormalizedCount,
                                    result.SymbolicLinksRemoved);

                                if (!result.IsFullySuccessful)
                                {
                                    logger?.LogWarning(
                                        "Some files failed to normalize: {FailedFiles}",
                                        string.Join(", ", result.FailedFiles));
                                }
                            }
                            else
                            {
                                StatusMessage = $"Normalization warning: {normalizationResult.FirstError}. Import will continue.";
                                logger?.LogWarning("Normalization failed: {Error}", normalizationResult.FirstError);
                            }
                        }
                        else
                        {
                            logger?.LogInformation("User skipped normalization");
                            StatusMessage = "Import successful (GenLauncher files not normalized).";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during GenLauncher detection/normalization");
                StatusMessage = "Import successful (normalization check failed).";
            }

            await RefreshStagingTreeAsync();

            // Only set generic message if normalization didn't set a specific one
            if (!StatusMessage.Contains("Normalized") && !StatusMessage.Contains("GenLauncher"))
            {
                StatusMessage = "Import successful.";
            }

            Validate();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import Error: {ex.Message}";
            logger?.LogError(ex, "Error importing content to staging");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static List<FileTreeItem> BuildDirectoryTree(DirectoryInfo dir)
    {
        var items = new List<FileTreeItem>();

        if (!dir.Exists) return items;

        foreach (var d in dir.GetDirectories().Take(20))
        {
            items.Add(new FileTreeItem
            {
                Name = d.Name,
                IsFile = false,
                FullPath = d.FullName,
                Children = new ObservableCollection<FileTreeItem>(BuildDirectoryTree(d)),
            });
        }

        foreach (var f in dir.GetFiles().Take(50))
        {
            items.Add(new FileTreeItem { Name = f.Name, IsFile = true, FullPath = f.FullName });
        }

        return items;
    }

    private static void CopyDirectory(DirectoryInfo source, DirectoryInfo target)
    {
        if (!target.Exists)
        {
            Directory.CreateDirectory(target.FullName);
        }

        foreach (var file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(target.FullName, file.Name), true);
        }

        foreach (var subDirectory in source.GetDirectories())
        {
            var nextTargetSubDir = target.CreateSubdirectory(subDirectory.Name);
            CopyDirectory(subDirectory, nextTargetSubDir);
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (BrowseFolderAction != null)
        {
            var path = await BrowseFolderAction();
            if (!string.IsNullOrEmpty(path))
            {
                await ImportContentAsync(path);
            }
        }
    }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        if (BrowseFileAction != null)
        {
            var paths = await BrowseFileAction();
            if (paths != null && paths.Count > 0)
            {
                foreach (var path in paths)
                {
                    await ImportContentAsync(path);
                }
            }
        }
    }

    [RelayCommand]
    private async Task DeleteItemAsync(FileTreeItem item)
    {
        if (item == null)
        {
            logger?.LogWarning("DeleteItemAsync: Item is null.");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Removing {item.Name}...";
            logger?.LogInformation("Deleting item from staging: {Name} ({Path})", item.Name, item.FullPath);

            if (item.IsFile && File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
            }
            else if (!item.IsFile && Directory.Exists(item.FullPath))
            {
                Directory.Delete(item.FullPath, true);
            }

            await RefreshStagingTreeAsync();
            StatusMessage = $"Removed {item.Name}.";
            logger?.LogInformation("Item successfully deleted: {Name}", item.Name);
            Validate();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Removal Error: {ex.Message}";
            logger?.LogError(ex, "Error deleting item from staging: {Path}", item.FullPath);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        CleanupStaging();
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand]
    private async Task AddContentAsync()
    {
        if (string.IsNullOrWhiteSpace(ContentName))
        {
            StatusMessage = "Please enter a name for the content.";
            return;
        }

        if (!Directory.Exists(_stagingPath) || !Directory.EnumerateFileSystemEntries(_stagingPath).Any())
        {
            StatusMessage = "No content to add. Please import files or folders.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Processing content...";

            var targetGame = SelectedGameType;

            var progress = new Progress<Core.Models.Content.ContentStorageProgress>(p =>
            {
                if (p.TotalCount > 0)
                {
                    StatusMessage = $"{(IsEditing ? "Updating" : "Importing")}: {p.Percentage:0}% ({p.ProcessedCount}/{p.TotalCount} files)";
                }
            });

            _cts = new CancellationTokenSource();

            // Preserve SourcePath metadata if available
            // Note: We no longer write to "source.path" file to avoid polluting the content.
            // Instead we pass the SourcePath directly to the service.
            GenHub.Core.Models.Results.OperationResult<GenHub.Core.Models.Manifest.ContentManifest> result;

            if (IsEditing && _originalManifestId != null)
            {
                 result = await localContentService.UpdateLocalContentManifestAsync(
                    _originalManifestId,
                    ContentName,
                    _stagingPath,
                    SelectedContentType,
                    targetGame,
                    SourcePath,
                    progress,
                    _cts.Token);
            }
            else
            {
                result = await localContentService.CreateLocalContentManifestAsync(
                    _stagingPath,
                    ContentName,
                    SelectedContentType,
                    targetGame,
                    SourcePath,
                    progress,
                    _cts.Token);
            }

            if (result.Success)
            {
                var manifest = result.Data;
                CreatedContentItem = new ContentDisplayItem
                {
                    Id = manifest.Id.Value,
                    ManifestId = Core.Models.Manifest.ManifestId.Create(manifest.Id),
                    DisplayName = manifest.Name ?? ContentName,
                    ContentType = manifest.ContentType,
                    GameType = manifest.TargetGame,
                    InstallationType = GameInstallationType.Unknown,
                    Publisher = manifest.Publisher?.Name ?? "GenHub (Local)",
                    Version = manifest.Version ?? string.Empty,
                    SourcePath = SourcePath,
                    SourceId = SourcePath, // Preserve legacy field for compatibility
                    IsEnabled = false,
                    IsEditable = true,
                };

                // CleanupStaging(); // Moved to finally block
                ContentAdded?.Invoke(this, EventArgs.Empty);
                RequestClose?.Invoke(this, true);
            }
            else
            {
                StatusMessage = $"Error: {result.FirstError}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled";
            logger?.LogInformation("Content creation/update cancelled by user");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            logger?.LogError(ex, "Error adding local content");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            CleanupStaging(); // Ensure cleanup happens on success, failure, or cancellation
            IsBusy = false;
        }
    }

    private void CleanupStaging()
    {
        try
        {
            if (Directory.Exists(_stagingPath))
            {
                Directory.Delete(_stagingPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private void CreateMapFoldersIfNeeded()
    {
        try
        {
            if (!Directory.Exists(_stagingPath)) return;

            // Search recursively for ANY .map files
            var mapFiles = Directory.GetFiles(_stagingPath, "*.map", SearchOption.AllDirectories);
            foreach (var mapPath in mapFiles)
            {
                var fileNameCheck = Path.GetFileName(mapPath); // e.g. "MyMap.map"
                var mapName = Path.GetFileNameWithoutExtension(mapPath); // e.g. "MyMap"
                var parentDir = Path.GetDirectoryName(mapPath); // e.g. ".../Staging/Maps"
                if (parentDir == null) continue;
                var parentDirName = new DirectoryInfo(parentDir).Name; // e.g. "Maps"

                // If the map is NOT in a folder with its own name (case-insensitive check)
                if (!string.Equals(parentDirName, mapName, StringComparison.OrdinalIgnoreCase))
                {
                    // Create a new correct directory: ".../Staging/Maps/MyMap"
                    // We keep it in the same parent location to preserve "Maps/" structure if it exists,
                    // but we ensure the immediate parent is the map name.
                    var newMapDir = Path.Combine(parentDir, mapName);

                    if (!Directory.Exists(newMapDir))
                    {
                        Directory.CreateDirectory(newMapDir);
                        logger?.LogInformation("Auto-nesting map file: {Map} -> {Dir}", fileNameCheck, newMapDir);
                    }

                    var destPath = Path.Combine(newMapDir, fileNameCheck);

                    // Safety check if we are somehow moving it to itself (shouldn't happen due to parent check)
                    if (string.Equals(mapPath, destPath, StringComparison.OrdinalIgnoreCase)) continue;

                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Move(mapPath, destPath);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to auto-organize map files");
        }
    }

    private async Task RefreshStagingTreeAsync()
    {
        bool wasBusy = IsBusy;
        try
        {
            if (!wasBusy) IsBusy = true;

            FileTree.Clear();
            SelectedExecutableItem = null; // Clear previous selection on refresh
            if (Directory.Exists(_stagingPath))
            {
                var dirInfo = new DirectoryInfo(_stagingPath);
                var items = await Task.Run(() => BuildDirectoryTree(dirInfo));
                foreach (var item in items)
                {
                    FileTree.Add(item);
                }
            }

            ExecutableCount = CountExecutables(FileTree);

            // Auto-select first executable if content type requires it
            if (SelectedContentType == ContentType.ModdingTool || SelectedContentType == ContentType.Executable)
            {
                AutoSelectFirstExecutable();
            }

            Validate();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error refreshing staging tree");
        }
        finally
        {
            if (!wasBusy) IsBusy = false;
        }
    }

    private void Validate()
    {
        var hasName = !string.IsNullOrWhiteSpace(ContentName);
        var hasFiles = FileTree.Any();
        var stagingExists = Directory.Exists(_stagingPath);
        var stagingHasEntries = stagingExists && Directory.EnumerateFileSystemEntries(_stagingPath).Any();

        // For ModdingTool (Tool) and Executable, we also need an executable selected
        var requiresExecutable = SelectedContentType == ContentType.ModdingTool || SelectedContentType == ContentType.Executable;
        var hasExecutableIfNeeded = !requiresExecutable || SelectedExecutableItem != null;

        CanAdd = hasName && (hasFiles || stagingHasEntries) && hasExecutableIfNeeded;

        logger?.LogDebug(
            "Validate: CanAdd={CanAdd} (HasName={HasName}, HasFiles={HasFiles}, StagingExists={StagingExists}, StagingHasEntries={StagingHasEntries}, HasExecutableIfNeeded={HasExecutableIfNeeded})", CanAdd, hasName, hasFiles, stagingExists, stagingHasEntries, hasExecutableIfNeeded);

        if (!CanAdd)
        {
            if (!hasName) logger?.LogDebug("Validate failed: ContentName is empty.");
            if (!hasFiles && !stagingHasEntries) logger?.LogDebug("Validate failed: No files in tree or staging directory.");
            if (!hasExecutableIfNeeded) logger?.LogDebug("Validate failed: Executable content type requires an executable to be selected.");
        }
    }

    partial void OnContentNameChanged(string value) => Validate();

    partial void OnFileTreeChanged(ObservableCollection<FileTreeItem> value) => Validate();

    partial void OnSelectedContentTypeChanged(ContentType value)
    {
        OnPropertyChanged(nameof(ShowExecutableSelection));
        OnPropertyChanged(nameof(PreviewIdleText));

        // Auto-select first executable if switching to ModdingTool or Executable
        if ((value == ContentType.ModdingTool || value == ContentType.Executable) && SelectedExecutableItem == null)
        {
            AutoSelectFirstExecutable();
        }

        Validate();
    }

    partial void OnSelectedExecutableItemChanged(FileTreeItem? oldValue, FileTreeItem? newValue)
    {
        // Clear old selection
        if (oldValue != null)
        {
            oldValue.IsSelectedExecutable = false;
        }

        // Set new selection
        if (newValue != null)
        {
            newValue.IsSelectedExecutable = true;
        }

        Validate();
    }

    [RelayCommand]
    private void SelectExecutable(FileTreeItem item)
    {
        if (item?.IsExecutable == true)
        {
            SelectedExecutableItem = item;
            logger?.LogInformation("Selected executable: {Name}", item.Name);
        }
    }

    private void AutoSelectFirstExecutable()
    {
        var firstExe = FindFirstExecutable(FileTree);
        if (firstExe != null)
        {
            SelectedExecutableItem = firstExe;
            logger?.LogInformation("Auto-selected first executable: {Name}", firstExe.Name);
        }
    }
}