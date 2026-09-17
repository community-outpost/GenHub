using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.Common;
using GenHub.Infrastructure.Converters;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// View model for the "Add Local Content" dialog.
/// </summary>
/// <param name="localContentService">Service for handling local content operations.</param>
/// <param name="contentStorageService">Service for content storage operations.</param>
/// <param name="genLauncherNormalizationService">Service for GenLauncher file normalization.</param>
/// <param name="dialogService">Service for showing dialogs.</param>
/// <param name="archivePayloadProcessor">Service for archive extraction and payload structure normalization.</param>
/// <param name="logger">Logger instance.</param>
/// <param name="localizationService">Localization service for user-facing status and progress messages.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel instance methods access CommunityToolkit MVVM generated properties.")]
public partial class AddLocalContentViewModel(
    ILocalContentService localContentService,
    IContentStorageService? contentStorageService,
    IGenLauncherNormalizationService? genLauncherNormalizationService,
    IDialogService? dialogService,
    IArchivePayloadProcessor? archivePayloadProcessor = null,
    ILogger<AddLocalContentViewModel>? logger = null,
    ILocalizationService? localizationService = null) : ObservableObject, IDisposable
{
    private const string StatusImportSkippedCollisionKey = "Profiles.AddLocalContent.StatusImportSkippedCollision";
    private const string StatusImportSkippedCollisionFallback = "Import skipped due to file collisions.";

    /// <summary>
    /// Gets the list of available game types.
    /// </summary>
    public static IReadOnlyList<GameType> AvailableGameTypes { get; } =
    [
        GameType.Generals,
        GameType.ZeroHour,
    ];

    /// <summary>
    /// Gets the list of allowed content types for the dialog.
    /// </summary>
    public static IReadOnlyList<ContentType> AllowedContentTypes { get; } =
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

    private readonly ILocalizationService? _localizationService = localizationService ?? LocalizationConverterHelper.ResolveLocalizationService();
    private readonly string _stagingPath = Path.Combine(Path.GetTempPath(), "GenHub_Staging_" + Guid.NewGuid());
    private string? _originalManifestId;
    private string? _pendingEntryPoint;
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
    /// Gets a value indicating whether we are editing existing content.
    /// </summary>
    public bool IsEditing => _originalManifestId != null;

    /// <summary>
    /// Gets the title for the dialog.
    /// </summary>
    public string DialogTitle => IsEditing
        ? GetLocalizedString("Profiles.AddLocalContent.DialogTitleEdit", "Edit Local Content")
        : GetLocalizedString("Profiles.AddLocalContent.DialogTitleAdd", "Add Local Content");

    /// <summary>
    /// Gets the text to display on the action button.
    /// </summary>
    public string ActionButtonText => IsEditing
        ? GetLocalizedString("Profiles.AddLocalContent.ActionButtonSave", "Save Changes")
        : GetLocalizedString("Profiles.AddLocalContent.ActionButtonAdd", "Add to Library");

    /// <summary>
    /// Gets a value indicating whether the loading overlay should be visible.
    /// Virtual to allow demos to suppress it.
    /// </summary>
    public virtual bool ShowLoadingOverlay => IsBusy;

    /// <summary>
    /// Gets or sets the progress percentage (0-100).
    /// </summary>
    [ObservableProperty]
    private double _progressPercentage;

    /// <summary>
    /// Gets or sets a value indicating whether the progress is indeterminate.
    /// </summary>
    [ObservableProperty]
    private bool _isProgressIndeterminate = true;

    /// <summary>
    /// Gets or sets a detailed progress subtitle message.
    /// </summary>
    [ObservableProperty]
    private string _progressDetailMessage = string.Empty;

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
    /// Gets or sets the selected executable item (for GameClient/Executable/ModdingTool content type).
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
    public bool ShowExecutableSelection => RequiresExecutable(SelectedContentType) && ExecutableCount > 0;

    /// <summary>
    /// Gets or sets a value indicating whether inactive mod archives (.ctr, .gib, .skw) are present in the staging area.
    /// </summary>
    [ObservableProperty]
    private bool _hasInactiveArchives;

    /// <summary>
    /// Gets the text to display in the preview area when no content is loaded.
    /// </summary>
    public string PreviewIdleText => SelectedContentType switch
    {
        ContentType.Mod => GetLocalizedString("Profiles.AddLocalContent.IdleMod", "Import mod content (e.g. .big, .zip)"),
        ContentType.GameClient => GetLocalizedString("Profiles.AddLocalContent.IdleGameClient", "Import GameClient"),
        ContentType.Executable => GetLocalizedString("Profiles.AddLocalContent.IdleExecutable", "Import executable"),
        ContentType.ModdingTool => GetLocalizedString("Profiles.AddLocalContent.IdleModdingTool", "Import tool executable"),
        ContentType.Patch => GetLocalizedString("Profiles.AddLocalContent.IdlePatch", "Import patch"),
        ContentType.Addon => GetLocalizedString("Profiles.AddLocalContent.IdleAddon", "Import addon content"),
        ContentType.Map => GetLocalizedString("Profiles.AddLocalContent.IdleMap", "Import map files"),
        ContentType.MapPack => GetLocalizedString("Profiles.AddLocalContent.IdleMapPack", "Import map pack files"),
        ContentType.Mission => GetLocalizedString("Profiles.AddLocalContent.IdleMission", "Import mission content"),
        _ => GetLocalizedString("Profiles.AddLocalContent.IdleDefault", "Drag and drop content to begin"),
    };

    /// <summary>
    /// Gets the temporary staging directory path for local content.
    /// </summary>
    internal string StagingPath => _stagingPath;

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
    /// Gets or sets an optional custom action to execute during AddContentAsync in demo mode.
    /// </summary>
    public Func<Task>? DemoAddAction { get; set; }

    /// <summary>
    /// Loads existing content for editing.
    /// </summary>
    /// <param name="item">The item to load.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task LoadFromManifestAsync(ContentDisplayItem item)
    {
        if (contentStorageService == null)
        {
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusStorageUnavailable", "Storage service unavailable.");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusLoadingContent", "Loading existing content...");

            _originalManifestId = item.ManifestId.Value;
            _pendingEntryPoint = item.Manifest?.EntryPoint;
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
                _stagingPath,
                _cts?.Token ?? CancellationToken.None);

            if (result.Success)
            {
                StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusLoadSuccess", "Success!");
                await RefreshStagingTreeAsync();
            }
            else
            {
                StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusLoadFailed", "Failed to load content: {0}", result.FirstError ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error loading content for editing");
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusLoadError", "Error loading content: {0}", ex.Message);
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

        if (string.IsNullOrEmpty(SourcePath))
        {
            SourcePath = path;
        }

        SetDefaultContentName(path);

        _cts ??= new CancellationTokenSource();
        if (_cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }

        var cancellationToken = _cts.Token;

        try
        {
            IsBusy = true;
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusImportingFile", "Importing {0}...", Path.GetFileName(path));
            logger?.LogInformation("Importing content from {Path} to staging {Staging}", path, _stagingPath);

            if (!Directory.Exists(_stagingPath))
            {
                Directory.CreateDirectory(_stagingPath);
            }

            var staged = await StageContentFromPathAsync(path, cancellationToken);
            if (!staged)
            {
                return;
            }

            CreateMapFoldersIfNeeded();

            var normalizationSetStatus = await HandleGenLauncherNormalizationAsync(cancellationToken);
            if (normalizationSetStatus == null)
            {
                return;
            }

            await RefreshStagingTreeAsync();

            if (!normalizationSetStatus.Value)
            {
                StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusImportSuccessful", "Import successful.");
            }

            Validate();
        }
        catch (OperationCanceledException ex)
        {
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusImportCancelled", "Import cancelled.");
            logger?.LogInformation(ex, "Import cancelled by user");
        }
        catch (Exception ex)
        {
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusImportError", "Import Error: {0}", ex.Message);
            logger?.LogError(ex, "Error importing content to staging");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
        CleanupStaging();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Counts the total number of executables in the given file tree items recursively.
    /// </summary>
    /// <param name="items">The file tree items to inspect.</param>
    /// <returns>The total number of executable files found.</returns>
    internal static int CountExecutables(IEnumerable<FileTreeItem> items)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item.IsExecutable) count++;
            count += CountExecutables(item.Children);
        }

        return count;
    }

    private static bool RequiresExecutable(ContentType contentType) =>
        contentType is ContentType.GameClient or ContentType.ModdingTool or ContentType.Executable;

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

    private static bool FilesHaveIdenticalContent(string file1, string file2) =>
        ArchivePayloadProcessor.FilesHaveIdenticalContent(file1, file2);

    private static bool IsBigArchiveFile(string filePath) =>
        ArchivePayloadProcessor.IsBigArchiveFile(filePath);

    private static bool IsExecutableFile(string filePath) =>
        ExecutableFileClassifier.HasExecutableMagicBytes(filePath);

    private static List<FileTreeItem> BuildDirectoryTree(DirectoryInfo dir)
        => BuildDirectoryTree(dir, CollectExecutableDirectories(dir));

    private static HashSet<string> CollectExecutableDirectories(DirectoryInfo root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (!ExecutableFileClassifier.IsLegacyLaunchCandidate(file.Name, file.FullName)
                    && !file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (var d = file.Directory; d != null; d = d.Parent)
                {
                    if (!result.Add(d.FullName))
                    {
                        break;
                    }
                }
            }
        }
        catch
        {
            // ignore inaccessible directories
        }

        return result;
    }

    private static List<FileTreeItem> BuildDirectoryTree(DirectoryInfo dir, HashSet<string> executableDirs)
    {
        var items = new List<FileTreeItem>();

        if (!dir.Exists)
        {
            return items;
        }

        var subDirs = dir.GetDirectories();
        var prioritizedDirs = subDirs
            .OrderByDescending(d => executableDirs.Contains(d.FullName))
            .ThenBy(d => d.Name)
            .Take(20);

        foreach (var d in prioritizedDirs)
        {
            items.Add(new FileTreeItem
            {
                Name = d.Name,
                IsFile = false,
                FullPath = d.FullName,
                Children = new ObservableCollection<FileTreeItem>(BuildDirectoryTree(d, executableDirs)),
            });
        }

        var files = dir.GetFiles();
        var prioritizedFiles = files
            .OrderByDescending(f => ExecutableFileClassifier.IsLegacyLaunchCandidate(f.Name, f.FullName) || f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f.Name)
            .Take(50);

        foreach (var f in prioritizedFiles)
        {
            items.Add(new FileTreeItem { Name = f.Name, IsFile = true, FullPath = f.FullName });
        }

        return items;
    }

    private static void CopyDirectory(DirectoryInfo source, DirectoryInfo target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!target.Exists)
        {
            Directory.CreateDirectory(target.FullName);
        }

        foreach (var file in source.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            file.CopyTo(Path.Combine(target.FullName, file.Name), true);
        }

        foreach (var subDirectory in source.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextTargetSubDir = target.CreateSubdirectory(subDirectory.Name);
            CopyDirectory(subDirectory, nextTargetSubDir, cancellationToken);
        }
    }

    private static List<string> DetectDirectoryCollisions(DirectoryInfo source, DirectoryInfo target, CancellationToken cancellationToken = default)
    {
        var collisions = new List<string>();
        cancellationToken.ThrowIfCancellationRequested();
        if (!target.Exists)
        {
            return collisions;
        }

        foreach (var file in source.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetFilePath = Path.Combine(target.FullName, file.Name);
            if (File.Exists(targetFilePath) && !FilesHaveIdenticalContent(file.FullName, targetFilePath))
            {
                collisions.Add(file.Name);
            }
        }

        foreach (var subDir in source.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetSubDirPath = Path.Combine(target.FullName, subDir.Name);
            if (Directory.Exists(targetSubDirPath))
            {
                var subCollisions = DetectDirectoryCollisions(subDir, new DirectoryInfo(targetSubDirPath), cancellationToken);
                foreach (var sc in subCollisions)
                {
                    collisions.Add(Path.Combine(subDir.Name, sc).Replace('\\', '/'));
                }
            }
        }

        return collisions;
    }

    private void SetDefaultContentName(string path)
    {
        if (string.IsNullOrWhiteSpace(ContentName))
        {
            ContentName = Path.GetFileNameWithoutExtension(path);
        }
    }

    private async Task<bool> StageContentFromPathAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(path))
        {
            return await StageFileAsync(path, cancellationToken);
        }

        if (Directory.Exists(path))
        {
            return await StageDirectoryAsync(path, cancellationToken);
        }

        return true;
    }

    private async Task<bool> StageFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var destFile = Path.Combine(_stagingPath, Path.GetFileName(filePath));

        if (File.Exists(destFile))
        {
            var hasCollision = await Task.Run(
                () => !FilesHaveIdenticalContent(filePath, destFile),
                cancellationToken);

            if (hasCollision)
            {
                var canOverwrite = await ConfirmFileCollisionAsync(filePath, destFile);
                if (!canOverwrite)
                {
                    return false;
                }
            }
        }

        await Task.Run(() => File.Copy(filePath, destFile, true), cancellationToken);

        if (archivePayloadProcessor != null)
        {
            await archivePayloadProcessor.ProcessPayloadAsync(
                _stagingPath,
                SelectedContentType,
                SelectedGameType,
                normalizeInactiveArchives: false,
                cancellationToken: cancellationToken);
        }
        else if (Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(
                () =>
                {
                    ZipFile.ExtractToDirectory(destFile, _stagingPath, true);
                    try
                    {
                        File.Delete(destFile);
                    }
                    catch
                    {
                        // Best effort cleanup of source zip in staging
                    }
                },
                cancellationToken);
        }

        return true;
    }

    private async Task<bool> StageDirectoryAsync(string dirPath, CancellationToken cancellationToken)
    {
        var dirInfo = new DirectoryInfo(dirPath);
        logger?.LogDebug("ImportContentAsync: Copying folder contents from source {Source} to staging root {Staging}", dirPath, _stagingPath);

        var collisions = await Task.Run(
            () => DetectDirectoryCollisions(dirInfo, new DirectoryInfo(_stagingPath), cancellationToken),
            cancellationToken);

        if (collisions.Count > 0)
        {
            var canOverwrite = await ConfirmDirectoryCollisionsAsync(dirPath, collisions);
            if (!canOverwrite)
            {
                return false;
            }
        }

        await Task.Run(() => CopyDirectory(dirInfo, new DirectoryInfo(_stagingPath), cancellationToken), cancellationToken);

        if (archivePayloadProcessor != null)
        {
            await archivePayloadProcessor.ProcessPayloadAsync(
                _stagingPath,
                SelectedContentType,
                SelectedGameType,
                normalizeInactiveArchives: false,
                cancellationToken: cancellationToken);
        }

        return true;
    }

    private async Task<bool> ConfirmFileCollisionAsync(string path, string destFile)
    {
        logger?.LogWarning("Detected file collision when importing {Source}: file {Dest} already exists with different content.", path, destFile);
        if (dialogService == null)
        {
            logger?.LogInformation("No dialog service available; skipping import of {Path} to avoid overwriting staged content without confirmation.", path);
            SetImportSkippedCollisionStatus();
            return false;
        }

        var confirmMessage = string.Format(
            GetLocalizedString("Profiles.AddLocalContent.SingleCollisionDialogMessage", "The file '{0}' already exists in staging with different content. Overwrite?"),
            Path.GetFileName(path));

        var overwrite = await dialogService.ShowConfirmationAsync(
            GetLocalizedString("Profiles.AddLocalContent.CollisionDialogTitle", "File Collisions Detected"),
            confirmMessage,
            GetLocalizedString("Profiles.AddLocalContent.CollisionDialogOverwrite", "Overwrite"),
            GetLocalizedString("Profiles.AddLocalContent.CollisionDialogCancel", "Skip"));

        if (!overwrite)
        {
            logger?.LogInformation("User skipped importing {Path} due to detected file collision.", path);
            SetImportSkippedCollisionStatus();
            return false;
        }

        return true;
    }

    private async Task<bool> ConfirmDirectoryCollisionsAsync(string path, IReadOnlyList<string> collisions)
    {
        logger?.LogWarning(
            "Detected {Count} file collision(s) when importing from {Source} into staging {Staging}: {Collisions}",
            collisions.Count,
            path,
            _stagingPath,
            string.Join(", ", collisions));

        if (dialogService == null)
        {
            logger?.LogInformation("No dialog service available; skipping import of {Path} to avoid overwriting staged content without confirmation.", path);
            SetImportSkippedCollisionStatus();
            return false;
        }

        var confirmMessage = string.Format(
            GetLocalizedString("Profiles.AddLocalContent.CollisionDialogMessage", "Importing '{0}' conflicts with {1} existing file(s). Overwrite existing files?"),
            Path.GetFileName(path),
            collisions.Count);

        var overwrite = await dialogService.ShowConfirmationAsync(
            GetLocalizedString("Profiles.AddLocalContent.CollisionDialogTitle", "File Collisions Detected"),
            confirmMessage,
            GetLocalizedString("Profiles.AddLocalContent.CollisionDialogOverwrite", "Overwrite"),
            GetLocalizedString("Profiles.AddLocalContent.CollisionDialogCancel", "Skip"));

        if (!overwrite)
        {
            logger?.LogInformation("User skipped importing {Path} due to detected file collisions.", path);
            SetImportSkippedCollisionStatus();
            return false;
        }

        return true;
    }

    private async Task<bool?> HandleGenLauncherNormalizationAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (genLauncherNormalizationService == null || dialogService == null)
            {
                return false;
            }

            var detectionResult = await genLauncherNormalizationService.DetectGenLauncherFilesAsync(_stagingPath, cancellationToken);
            if (!detectionResult.HasGenLauncherFiles)
            {
                return false;
            }

            logger?.LogInformation("GenLauncher files detected: {Summary}", detectionResult.GetSummary());

            var normalizationPrompt = GetLocalizedString(
                "Profiles.AddLocalContent.GenLauncherPrompt",
                "This content contains GenLauncher-modified files:\n\n{0}\n\nWould you like to normalize these files to standard format?\n\nThis will:\n• Convert {1} files to {2}\n• Convert {3} files to {2} or {4} based on content\n• Remove {5} suffixes\n• Remove symbolic links",
                detectionResult.GetSummary(),
                GenLauncherConstants.GibExtension,
                GenLauncherConstants.BigExtension,
                GenLauncherConstants.CtrExtension,
                GenLauncherConstants.ExeExtension,
                string.Join(", ", GenLauncherConstants.AllSuffixes));

            var shouldNormalize = await dialogService.ShowConfirmationAsync(
                GetLocalizedString("Profiles.AddLocalContent.GenLauncherDialogTitle", "GenLauncher Files Detected"),
                normalizationPrompt,
                GetLocalizedString("Profiles.AddLocalContent.GenLauncherDialogNormalize", "Normalize"),
                GetLocalizedString("Profiles.AddLocalContent.GenLauncherDialogSkip", "Skip"),
                sessionKey: GenLauncherConstants.NormalizationDialogSessionKey);

            if (shouldNormalize)
            {
                return await ExecuteGenLauncherNormalizationAsync(cancellationToken);
            }

            logger?.LogInformation("User skipped normalization");
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusNormalizedSkipped", "Import successful (GenLauncher files not normalized).");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger?.LogInformation("GenLauncher detection/normalization was cancelled");
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusOperationCancelled", "Operation cancelled");
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during GenLauncher detection/normalization");
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusNormalizationCheckFailed", "Import successful (normalization check failed).");
            return true;
        }
    }

    private async Task<bool> ExecuteGenLauncherNormalizationAsync(CancellationToken cancellationToken)
    {
        if (genLauncherNormalizationService == null)
        {
            return false;
        }

        StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusNormalizingGenLauncher", "Normalizing GenLauncher files...");
        logger?.LogInformation("User confirmed normalization");

        var normalizationResult = await genLauncherNormalizationService.NormalizeFilesAsync(
            _stagingPath,
            cancellationToken);

        if (normalizationResult.Success && normalizationResult.Data != null)
        {
            var result = normalizationResult.Data;
            StatusMessage = FormatNormalizationSuccessMessage(result);
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
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusNormalizationWarning", "Normalization warning: {0}. Import will continue.", normalizationResult.FirstError ?? string.Empty);
            logger?.LogWarning("Normalization failed: {Error}", normalizationResult.FirstError);
        }

        return true;
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
            if (paths is { Count: > 0 })
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
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusRemovingItem", "Removing {0}...", item.Name);
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
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusRemovedItem", "Removed {0}.", item.Name);
            logger?.LogInformation("Item successfully deleted: {Name}", item.Name);
            Validate();
        }
        catch (Exception ex)
        {
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusRemovalError", "Removal Error: {0}", ex.Message);
            logger?.LogError(ex, "Error deleting item from staging: {Path}", item.FullPath);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NormalizeArchivesAsync()
    {
        if (!Directory.Exists(_stagingPath))
        {
            return;
        }

        try
        {
            IsBusy = true;
            IsProgressIndeterminate = true;
            ProgressDetailMessage = GetLocalizedString("Profiles.AddLocalContent.ProgressValidatingArchive", "Validating and converting archive formats...");
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.ProgressNormalizingArchives", "Normalizing inactive archives (.ctr / .gib / .skw) to .big...");
            logger?.LogInformation("User triggered archive normalization in staging: {StagingPath}", _stagingPath);

            var token = _cts?.Token ?? CancellationToken.None;
            await Task.Run(() => NormalizeStagingDirectory(token), token);

            await RefreshStagingTreeAsync();
            HasInactiveArchives = CheckForInactiveArchives();
            if (!HasInactiveArchives)
            {
                StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusNormalizedSuccess", "Inactive archives normalized to .big successfully.");
            }
            else
            {
                StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusPartialNormalization", "Some inactive archives could not be normalized.");
            }

            Validate();
        }
        catch (OperationCanceledException ex)
        {
            logger?.LogInformation(ex, "Archive normalization cancelled by user");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error normalizing inactive archives in staging");
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusNormalizationFailed", "Normalization failed: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NormalizeStagingDirectory(CancellationToken cancellationToken = default)
    {
        foreach (var extension in GenLauncherConstants.InactiveBigExtensions)
        {
            var searchPattern = "*" + extension;
            foreach (var inactiveFile in Directory.GetFiles(_stagingPath, searchPattern, SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                NormalizeSingleInactiveFile(inactiveFile);
            }
        }
    }

    private void NormalizeSingleInactiveFile(string inactiveFile)
    {
        if (IsExecutableFile(inactiveFile))
        {
            var exeFile = Path.ChangeExtension(inactiveFile, GenLauncherConstants.ExeExtension);
            if (!File.Exists(exeFile))
            {
                File.Move(inactiveFile, exeFile);
                logger?.LogInformation("Normalized disguised executable '{InactiveFile}' to '{ExeFile}'", inactiveFile, exeFile);
            }
            else if (FilesHaveIdenticalContent(inactiveFile, exeFile))
            {
                File.Delete(inactiveFile);
                logger?.LogInformation("Removed duplicate identical inactive executable '{InactiveFile}' as '{ExeFile}' already exists", inactiveFile, exeFile);
            }
            else
            {
                var nonCollidingExePath = ArchivePayloadProcessor.GetNonCollidingDestinationPath(exeFile);
                File.Move(inactiveFile, nonCollidingExePath);
                logger?.LogInformation("Preserved differing inactive executable '{InactiveFile}' by renaming to '{NewExeFile}'", inactiveFile, nonCollidingExePath);
            }

            return;
        }

        if (!IsBigArchiveFile(inactiveFile))
        {
            logger?.LogDebug("Skipping non-BIG inactive file '{InactiveFile}' during archive normalization", inactiveFile);
            return;
        }

        ArchivePayloadProcessor.NormalizeInactiveBigArchive(inactiveFile, logger);
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
        if (IsDemoMode)
        {
            if (DemoAddAction != null)
            {
                await DemoAddAction();
            }
            else
            {
                StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusDemoRegistered", "'{0}' registered in library. Ready to link to any game profile!", ContentName);
                CanAdd = true;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(ContentName))
        {
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusEnterName", "Please enter a name for the content.");
            return;
        }

        if (!Directory.Exists(_stagingPath) || !Directory.EnumerateFileSystemEntries(_stagingPath).Any())
        {
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusNoContent", "No content to add. Please import files or folders.");
            return;
        }

        try
        {
            IsBusy = true;
            IsProgressIndeterminate = true;
            ProgressPercentage = 0;
            ProgressDetailMessage = GetLocalizedString("Profiles.AddLocalContent.ProgressAnalyzingFiles", "Analyzing files and computing hashes...");
            StatusMessage = IsEditing
                ? GetLocalizedString("Profiles.AddLocalContent.StatusUpdatingManifest", "Updating content manifest...")
                : GetLocalizedString("Profiles.AddLocalContent.StatusScanningContent", "Scanning local content...");

            var targetGame = SelectedGameType;

            var progress = new Progress<Core.Models.Content.ContentStorageProgress>(p =>
            {
                if (p.TotalCount > 0)
                {
                    ProgressPercentage = p.Percentage;
                    IsProgressIndeterminate = false;
                    var fileLabel = !string.IsNullOrWhiteSpace(p.CurrentFileName) ? $" ({Path.GetFileName(p.CurrentFileName)})" : string.Empty;
                    ProgressDetailMessage = GetLocalizedString("Profiles.AddLocalContent.ProgressCasStorage", "{0} of {1} files stored in CAS pool{2}", p.ProcessedCount, p.TotalCount, fileLabel);
                    var actionKey = IsEditing ? "Profiles.AddLocalContent.StatusCasProgressUpdating" : "Profiles.AddLocalContent.StatusCasProgressImporting";
                    var fallback = IsEditing ? "Updating {0}: {1:0}% ({2}/{3} files)" : "Importing {0}: {1:0}% ({2}/{3} files)";
                    StatusMessage = string.Format(
                        GetLocalizedString(actionKey, fallback),
                        ContentName,
                        p.Percentage,
                        p.ProcessedCount,
                        p.TotalCount);
                }
                else
                {
                    IsProgressIndeterminate = true;
                    ProgressDetailMessage = !string.IsNullOrWhiteSpace(p.CurrentFileName)
                        ? p.CurrentFileName
                        : GetLocalizedString("Profiles.AddLocalContent.ProgressWritingMetadata", "Writing metadata...");
                    StatusMessage = IsEditing
                        ? GetLocalizedString("Profiles.AddLocalContent.StatusUpdatingFiles", "Updating content files...")
                        : GetLocalizedString("Profiles.AddLocalContent.StatusImportingFiles", "Importing content files...");
                }
            });

            _cts = new CancellationTokenSource();

            string? entryPoint = null;
            if (RequiresExecutable(SelectedContentType) && SelectedExecutableItem != null && !string.IsNullOrWhiteSpace(SelectedExecutableItem.FullPath))
            {
                try
                {
                    entryPoint = Path.GetRelativePath(_stagingPath, SelectedExecutableItem.FullPath).Replace('\\', '/');
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to determine relative path for selected executable '{FullPath}'. Falling back to file name '{Name}'", SelectedExecutableItem.FullPath, SelectedExecutableItem.Name);
                    entryPoint = SelectedExecutableItem.Name;
                }
            }

            // Preserve SourcePath metadata if available
            // Note: We no longer write to "source.path" file to avoid polluting the content.
            // Instead we pass the SourcePath directly to the service.
            var options = new LocalContentOptions
            {
                SourcePath = SourcePath,
                Progress = progress,
                CancellationToken = _cts.Token,
                EntryPoint = entryPoint,
                NormalizeInactiveArchives = false,
            };

            var result = IsEditing && _originalManifestId != null
                ? await localContentService.UpdateLocalContentManifestAsync(
                    _originalManifestId,
                    ContentName,
                    _stagingPath,
                    SelectedContentType,
                    targetGame,
                    options)
                : await localContentService.CreateLocalContentManifestAsync(
                    _stagingPath,
                    ContentName,
                    SelectedContentType,
                    targetGame,
                    options);

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
                    Manifest = manifest,
                    GameClient = Services.ProfileContentLoader.CreateGameClientFromManifest(manifest),
                };

                // CleanupStaging(); // Moved to finally block
                ContentAdded?.Invoke(this, EventArgs.Empty);
                RequestClose?.Invoke(this, true);
            }
            else
            {
                StatusMessage = string.Format(GetLocalizedString("Profiles.AddLocalContent.StatusGenericError", "Error: {0}"), result.FirstError);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusOperationCancelled", "Operation cancelled");
            logger?.LogInformation("Content creation/update cancelled by user");
        }
        catch (Exception ex)
        {
            StatusMessage = GetLocalizedString("Profiles.AddLocalContent.StatusGenericError", "Error: {0}", ex.Message);
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

    private FileTreeItem? FindFileItemByRelativePath(IEnumerable<FileTreeItem> items, string relativePath)
    {
        var normalizedTarget = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var item in items)
        {
            if (item.IsFile)
            {
                var itemRel = Path.GetRelativePath(_stagingPath, item.FullPath).Replace('\\', '/').TrimStart('/');
                if (ManifestVariantResolver.PathsMatch(itemRel, normalizedTarget))
                {
                    return item;
                }
            }
            else
            {
                var found = FindFileItemByRelativePath(item.Children, relativePath);
                if (found != null) return found;
            }
        }

        return null;
    }

    private async Task RefreshStagingTreeAsync()
    {
        bool wasBusy = IsBusy;
        try
        {
            if (!wasBusy) IsBusy = true;

            string? previousRelativePath = null;
            if (SelectedExecutableItem != null && !string.IsNullOrWhiteSpace(SelectedExecutableItem.FullPath))
            {
                try
                {
                    previousRelativePath = Path.GetRelativePath(_stagingPath, SelectedExecutableItem.FullPath).Replace('\\', '/');
                }
                catch
                {
                    // Ignore path calculation error
                }
            }
            else if (!string.IsNullOrWhiteSpace(_pendingEntryPoint))
            {
                previousRelativePath = _pendingEntryPoint;
            }

            FileTree.Clear();
            SelectedExecutableItem = null; // Clear previous selection on refresh
            if (Directory.Exists(_stagingPath))
            {
                var dirInfo = new DirectoryInfo(_stagingPath);
                var items = await Task.Run(() => BuildDirectoryTree(dirInfo), _cts?.Token ?? CancellationToken.None);
                foreach (var item in items)
                {
                    FileTree.Add(item);
                }
            }

            ExecutableCount = CountExecutables(FileTree);

            // Reselect previously selected executable or auto-select first if content type requires it
            if (RequiresExecutable(SelectedContentType))
            {
                FileTreeItem? matchedItem = null;
                if (!string.IsNullOrWhiteSpace(previousRelativePath))
                {
                    matchedItem = FindFileItemByRelativePath(FileTree, previousRelativePath);
                }

                if (matchedItem != null && matchedItem.IsExecutable)
                {
                    SelectedExecutableItem = matchedItem;
                    _pendingEntryPoint = null;
                }
                else
                {
                    _pendingEntryPoint = null;
                    AutoSelectFirstExecutable();
                }
            }
            else
            {
                SelectedExecutableItem = null;
            }

            HasInactiveArchives = CheckForInactiveArchives();
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

        // For GameClient, ModdingTool (Tool), and Executable, we also need an executable selected
        var requiresExecutable = RequiresExecutable(SelectedContentType);
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

    private void SetImportSkippedCollisionStatus() =>
        StatusMessage = GetLocalizedString(StatusImportSkippedCollisionKey, StatusImportSkippedCollisionFallback);

    private string GetLocalizedString(string key, string fallback) =>
        _localizationService?[key] ?? fallback;

    private string GetLocalizedString(string key, string fallback, params object[] args) =>
        _localizationService != null ? _localizationService.GetString(key, args) : string.Format(fallback, args);

    private string FormatNormalizationSuccessMessage(GenLauncherNormalizationResult result)
    {
        if (result.FailedFiles.Count > 0 && result.SkippedFiles.Count > 0)
        {
            return string.Format(
                GetLocalizedString("Profiles.AddLocalContent.NormalizationPartial", "Normalized {0} file(s); {1} skipped, {2} failed. Import completed."),
                result.NormalizedCount,
                result.SkippedFiles.Count,
                result.FailedFiles.Count);
        }

        if (result.FailedFiles.Count > 0)
        {
            return string.Format(
                GetLocalizedString("Profiles.AddLocalContent.NormalizationFailed", "Normalized {0} file(s); {1} failed. Import completed."),
                result.NormalizedCount,
                result.FailedFiles.Count);
        }

        if (result.SkippedFiles.Count > 0)
        {
            return string.Format(
                GetLocalizedString("Profiles.AddLocalContent.NormalizationSkipped", "Normalized {0} file(s); {1} skipped. Import completed."),
                result.NormalizedCount,
                result.SkippedFiles.Count);
        }

        return string.Format(
            GetLocalizedString("Profiles.AddLocalContent.NormalizationSuccess", "Normalized {0} file(s). Import successful."),
            result.NormalizedCount);
    }

    partial void OnContentNameChanged(string value) => Validate();

    partial void OnFileTreeChanged(ObservableCollection<FileTreeItem> value) => Validate();

    partial void OnSelectedContentTypeChanged(ContentType value)
    {
        OnPropertyChanged(nameof(ShowExecutableSelection));
        OnPropertyChanged(nameof(PreviewIdleText));

        // Auto-select first executable if switching to a content type that requires it,
        // or clear selection when switching to a non-executable content type
        if (RequiresExecutable(value))
        {
            if (SelectedExecutableItem == null)
            {
                FileTreeItem? matchedItem = null;
                if (!string.IsNullOrWhiteSpace(_pendingEntryPoint))
                {
                    matchedItem = FindFileItemByRelativePath(FileTree, _pendingEntryPoint);
                }

                if (matchedItem != null && matchedItem.IsExecutable)
                {
                    SelectedExecutableItem = matchedItem;
                    _pendingEntryPoint = null;
                }
                else
                {
                    AutoSelectFirstExecutable();
                }
            }
        }
        else
        {
            if (SelectedExecutableItem != null && !string.IsNullOrWhiteSpace(SelectedExecutableItem.FullPath))
            {
                try
                {
                    _pendingEntryPoint = Path.GetRelativePath(_stagingPath, SelectedExecutableItem.FullPath).Replace('\\', '/');
                }
                catch
                {
                    // Ignore path calculation error
                }
            }

            SelectedExecutableItem = null;
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

    private bool CheckForInactiveArchives()
    {
        if (!Directory.Exists(_stagingPath))
        {
            return false;
        }

        try
        {
            return GenLauncherConstants.InactiveBigExtensions
                .Any(extension => Directory.EnumerateFiles(_stagingPath, "*" + extension, SearchOption.AllDirectories).Any());
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to enumerate inactive archives in staging path");
            return false;
        }
    }
}
