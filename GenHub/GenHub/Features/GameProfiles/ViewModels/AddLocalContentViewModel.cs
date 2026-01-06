using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// View model for the "Add Local Content" dialog.
/// </summary>
/// <param name="localContentService">Service for handling local content operations.</param>
/// <param name="logger">Logger instance.</param>
public partial class AddLocalContentViewModel(
    ILocalContentService localContentService,
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
        ContentType.Addon,
        ContentType.Map,
        ContentType.MapPack,
        ContentType.Mission,
        ContentType.ModdingTool,
    ];

    private readonly string _stagingPath = Path.Combine(Path.GetTempPath(), "GenHub_Staging_" + Guid.NewGuid());

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
    private bool _isBusy;

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
    /// Gets or sets the action to browse for a file.
    /// </summary>
    public Func<Task<string?>>? BrowseFileAction { get; set; }

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

        SourcePath = path;

        if (string.IsNullOrWhiteSpace(ContentName))
        {
            ContentName = Path.GetFileNameWithoutExtension(path);
            logger?.LogDebug("ImportContentAsync: ContentName set to {Name}", ContentName);
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
            // This is required because the game expects maps to be in their own folders
            CreateMapFoldersIfNeeded();

            await RefreshStagingTreeAsync();
            StatusMessage = "Import successful.";
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
        Directory.CreateDirectory(target.FullName);

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
            var path = await BrowseFileAction();
            if (!string.IsNullOrEmpty(path))
            {
                await ImportContentAsync(path);
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
                    StatusMessage = $"Importing: {p.Percentage:0}% ({p.ProcessedCount}/{p.TotalCount} files)";
                }
            });

            var result = await localContentService.CreateLocalContentManifestAsync(
                _stagingPath,
                ContentName,
                SelectedContentType,
                targetGame,
                progress);

            if (result.Success)
            {
                var manifest = result.Data;
                CreatedContentItem = new ContentDisplayItem
                {
                    ManifestId = Core.Models.Manifest.ManifestId.Create(manifest.Id),
                    DisplayName = manifest.Name ?? ContentName,
                    ContentType = manifest.ContentType,
                    GameType = manifest.TargetGame,
                    InstallationType = GameInstallationType.Unknown,
                    Publisher = manifest.Publisher?.Name ?? "GenHub (Local)",
                    Version = manifest.Version ?? "1.0.0",
                    SourceId = SourcePath,
                    IsEnabled = false,
                };

                CleanupStaging();
                ContentAdded?.Invoke(this, EventArgs.Empty);
                RequestClose?.Invoke(this, true);
            }
            else
            {
                StatusMessage = $"Error: {result.FirstError}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            logger?.LogError(ex, "Error adding local content");
        }
        finally
        {
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
                var parentDirName = new DirectoryInfo(parentDir!).Name; // e.g. "Maps"

                // If the map is NOT in a folder with its own name (case-insensitive check)
                if (!string.Equals(parentDirName, mapName, StringComparison.OrdinalIgnoreCase))
                {
                    // Create a new correct directory: ".../Staging/Maps/MyMap"
                    // We keep it in the same parent location to preserve "Maps/" structure if it exists,
                    // but we ensure the immediate parent is the map name.
                    var newMapDir = Path.Combine(parentDir!, mapName);

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
            if (Directory.Exists(_stagingPath))
            {
                var dirInfo = new DirectoryInfo(_stagingPath);
                var items = await Task.Run(() => BuildDirectoryTree(dirInfo));
                foreach (var item in items)
                {
                    FileTree.Add(item);
                }
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

        CanAdd = hasName && (hasFiles || stagingHasEntries);

        logger?.LogDebug(
            "Validate: CanAdd={CanAdd} (HasName={HasName}, HasFiles={HasFiles}, StagingExists={StagingExists}, StagingHasEntries={StagingHasEntries})", CanAdd, hasName, hasFiles, stagingExists, stagingHasEntries);

        if (!CanAdd)
        {
            if (!hasName) logger?.LogDebug("Validate failed: ContentName is empty.");
            if (!hasFiles && !stagingHasEntries) logger?.LogDebug("Validate failed: No files in tree or staging directory.");
        }
    }

    partial void OnContentNameChanged(string value) => Validate();

    partial void OnFileTreeChanged(ObservableCollection<FileTreeItem> value) => Validate();
}
