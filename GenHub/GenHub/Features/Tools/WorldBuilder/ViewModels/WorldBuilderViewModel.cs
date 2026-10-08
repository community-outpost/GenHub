using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Messages;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Models.Validation;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.ViewModels;

/// <summary>
/// ViewModel for the WorldBuilder map tool. Opens, validates, generates, and saves
/// Generals and Zero Hour map documents with their sidecar files.
/// </summary>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required services for WorldBuilder tool orchestrator.")]
public sealed partial class WorldBuilderViewModel(
    IWorldBuilderMapService mapService,
    IMapValidationService validationService,
    IMapGenerationService generationService,
    IMapPreviewService previewService,
    ITeamExchangeService teamExchangeService,
    IWorldBuilderProjectService projectService,
    IWorldBuilderSidecarService sidecarService,
    INotificationService notificationService,
    ILocalizationService localizationService,
    IDialogService dialogService,
    IWorldBuilderContentService contentService,
    ILogger<WorldBuilderViewModel> logger) : ObservableObject, IDisposable
{
    private WorldBuilderMap? _map;
    private WorldBuilderProject? _project;
    private bool _adoptingDocument;
    private bool _contentWarningShown;
    private bool _disposed;

    /// <summary>
    /// Gets the adopted map document, if one is open.
    /// </summary>
    public WorldBuilderMap? CurrentMap => _map;

    /// <summary>
    /// Gets validation issues from the most recent validation run.
    /// </summary>
    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = [];

    /// <summary>
    /// Gets map files belonging to the open project.
    /// </summary>
    public ObservableCollection<string> ProjectFiles { get; } = [];

    [ObservableProperty]
    private string? filePath;

    [ObservableProperty]
    private bool hasDocument;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private string mapName = string.Empty;

    [ObservableProperty]
    private MapSummaryReport? summary;

    [ObservableProperty]
    private Bitmap? previewImage;

    [ObservableProperty]
    private string? projectName;

    [ObservableProperty]
    private bool hasProject;

    [ObservableProperty]
    private int seed = WorldBuilderConstants.MapGen.DefaultSeed;

    [ObservableProperty]
    private int mapWidth = WorldBuilderConstants.MapGen.DefaultWidth;

    [ObservableProperty]
    private int mapHeight = WorldBuilderConstants.MapGen.DefaultHeight;

    [ObservableProperty]
    private int playerCount = WorldBuilderConstants.MapGen.DefaultPlayers;

    /// <summary>
    /// Opens a map file, asking to discard unsaved changes first.
    /// </summary>
    /// <param name="mapPath">The full path of the .map file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the map was opened successfully.</returns>
    public async Task<bool> OpenMapAsync(string mapPath, CancellationToken cancellationToken = default)
    {
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            var result = await mapService.LoadAsync(mapPath, cancellationToken).ConfigureAwait(false);
            if (!result.Success || result.Data == null)
            {
                notificationService.ShowError(
                    localizationService.GetString("Tools.WorldBuilder.Open.FailureTitle"),
                    localizationService.GetString("Tools.WorldBuilder.Open.FailureMessage", result.FirstError ?? mapPath),
                    NotificationDurations.Long);
                return false;
            }

            var preview = result.Data.Preview ?? await ReadSidecarPreviewAsync(mapPath, cancellationToken).ConfigureAwait(false);
            await InvokeOnUIThreadAsync(() => AdoptDocument(result.Data, mapPath, preview)).ConfigureAwait(false);
            logger.LogInformation("Opened map file {Path}", mapPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or OverflowException)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Open.FailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.Open.FailureMessage", ex.Message),
                NotificationDurations.Long);
            logger.LogWarning(ex, "Failed to open map file {Path}", mapPath);
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CanvasBitmap?.Dispose();
        CanvasBitmap = null;
        PreviewImage?.Dispose();
        PreviewImage = null;
    }

    private static string GetTeamName(MapTeamEntry team)
    {
        return team.Properties.GetString(WorldBuilderConstants.DictKeys.TeamName, string.Empty);
    }

    private static Bitmap? BuildPreviewImage(MapPreviewData? preview)
    {
        if (preview == null || preview.Width <= 0 || preview.Height <= 0)
        {
            return null;
        }

        if (preview.Pixels.Count != preview.Width * preview.Height)
        {
            return null;
        }

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(preview.Width, preview.Height),
                new Vector(96, 96),
                PixelFormats.Bgra8888);
            using var frame = bitmap.Lock();
            var pixels = preview.Pixels is int[] ready ? ready : [.. preview.Pixels];
            Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
            return bitmap;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (OutOfMemoryException)
        {
            return null;
        }
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return null;
        }

        return lifetime.MainWindow is null ? null : TopLevel.GetTopLevel(lifetime.MainWindow);
    }

    private static async Task InvokeOnUIThreadAsync(Action action)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            await Task.CompletedTask.ConfigureAwait(false);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    private static async Task<T> InvokeOnUIThreadAsync<T>(Func<T> function)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            var result = function();
            await Task.CompletedTask.ConfigureAwait(false);
            return result;
        }

        return await Dispatcher.UIThread.InvokeAsync(function);
    }

    /// <summary>
    /// Opens a map chosen with a file dialog.
    /// </summary>
    [RelayCommand]
    private async Task OpenMapWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localizationService.GetString("Tools.WorldBuilder.FileDialog.OpenTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WorldBuilder.FileDialog.FilterName"))
                {
                    Patterns = [MapManagerConstants.MapFilePattern],
                },
            ],
        }).ConfigureAwait(false);
        var localPath = files.Count == 0 ? null : files[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            await OpenMapAsync(localPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Generates a new map from the configured seed and dimensions.
    /// </summary>
    [RelayCommand]
    private async Task GenerateNewMapAsync(CancellationToken cancellationToken = default)
    {
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var settings = new MapGenSettings
        {
            Seed = Seed,
            PlayableWidth = MapWidth,
            PlayableHeight = MapHeight,
            NumPlayers = PlayerCount,
        };
        var result = await Task.Run(() => generationService.Generate(settings, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Generate.FailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.Generate.FailureMessage", result.FirstError ?? string.Empty),
                NotificationDurations.Long);
            return;
        }

        var preview = await Task.Run(() => previewService.BuildPreview(result.Data), cancellationToken).ConfigureAwait(false);
        result.Data.IsDirty = true;
        await InvokeOnUIThreadAsync(() =>
        {
            var hadPrevious = _map != null;
            if (hadPrevious)
            {
                _undoService.Checkpoint(_map!);
            }

            AdoptDocument(result.Data, null, preview, clearUndo: !hadPrevious);
        }).ConfigureAwait(false);
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Generate.SuccessTitle"),
            localizationService.GetString("Tools.WorldBuilder.Generate.SuccessMessage", MapWidth, MapHeight, PlayerCount),
            NotificationDurations.Medium);
        logger.LogInformation("Generated new map with seed {Seed}", Seed);
    }

    /// <summary>
    /// Saves the open map to its current path, prompting for a path when needed.
    /// </summary>
    [RelayCommand]
    private async Task SaveMapAsync(CancellationToken cancellationToken = default)
    {
        if (_map == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(FilePath))
        {
            await SaveMapAsWithDialogAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteMapToFileAsync(FilePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves the open map to a path chosen with a file dialog.
    /// </summary>
    [RelayCommand]
    private async Task SaveMapAsWithDialogAsync(CancellationToken cancellationToken = default)
    {
        if (_map == null)
        {
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = localizationService.GetString("Tools.WorldBuilder.FileDialog.SaveTitle"),
            SuggestedFileName = FilePath == null ? null : Path.GetFileName(FilePath),
            FileTypeChoices =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WorldBuilder.FileDialog.FilterName"))
                {
                    Patterns = [MapManagerConstants.MapFilePattern],
                },
            ],
        }).ConfigureAwait(false);
        var localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            await WriteMapToFileAsync(localPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the open map, asking to discard unsaved changes first.
    /// </summary>
    [RelayCommand]
    private async Task CloseMapAsync(CancellationToken cancellationToken = default)
    {
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await InvokeOnUIThreadAsync(ClearDocument).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates the open map and refreshes the reported issues.
    /// </summary>
    [RelayCommand]
    private async Task ValidateMapAsync(CancellationToken cancellationToken = default)
    {
        if (_map == null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = validationService.Validate(_map);
        await InvokeOnUIThreadAsync(() => RefreshValidationIssues(result)).ConfigureAwait(false);
        if (result.IsValid)
        {
            notificationService.ShowSuccess(
                localizationService.GetString("Tools.WorldBuilder.Validate.ValidTitle"),
                localizationService.GetString("Tools.WorldBuilder.Validate.ValidMessage"),
                NotificationDurations.Medium);
        }
        else
        {
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WorldBuilder.Validate.IssuesTitle"),
                localizationService.GetString("Tools.WorldBuilder.Validate.IssuesMessage", result.CriticalIssueCount, result.WarningIssueCount),
                NotificationDurations.Long);
        }
    }

    /// <summary>
    /// Opens the map.ini companion file in the INI editor tool.
    /// </summary>
    [RelayCommand]
    private void EditMapIniInEditor()
    {
        if (_map == null || string.IsNullOrEmpty(FilePath))
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.MapIni.NoDocumentTitle"),
                localizationService.GetString("Tools.WorldBuilder.MapIni.NoDocumentMessage"),
                NotificationDurations.Medium);
            return;
        }

        var iniPath = Path.ChangeExtension(FilePath, WorldBuilderConstants.FileExtensions.MapIni);
        if (!File.Exists(iniPath))
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.MapIni.MissingTitle"),
                localizationService.GetString("Tools.WorldBuilder.MapIni.MissingMessage", Path.GetFileName(iniPath)),
                NotificationDurations.Medium);
            return;
        }

        WeakReferenceMessenger.Default.Send(new OpenFileInToolMessage(WorldBuilderConstants.Tool.IniEditorToolId, iniPath));
    }

    /// <summary>
    /// Tidies the map.ini companion file beside the open map.
    /// </summary>
    [RelayCommand]
    private async Task TidyMapIniAsync(CancellationToken cancellationToken = default)
    {
        if (_map == null || string.IsNullOrEmpty(FilePath))
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.MapIni.NoDocumentTitle"),
                localizationService.GetString("Tools.WorldBuilder.MapIni.NoDocumentMessage"),
                NotificationDurations.Medium);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var iniPath = Path.ChangeExtension(FilePath, WorldBuilderConstants.FileExtensions.MapIni);
        var text = string.Empty;
        try
        {
            text = await File.ReadAllTextAsync(iniPath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.MapIni.MissingTitle"),
                localizationService.GetString("Tools.WorldBuilder.MapIni.MissingMessage", Path.GetFileName(iniPath)),
                NotificationDurations.Medium);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.MapIni.MissingTitle"),
                localizationService.GetString("Tools.WorldBuilder.MapIni.MissingMessage", Path.GetFileName(iniPath)),
                NotificationDurations.Medium);
            return;
        }

        var sanitized = MapIniSanitizer.Sanitize(text);
        if (string.Equals(sanitized.Text, text, StringComparison.Ordinal))
        {
            notificationService.ShowSuccess(
                localizationService.GetString("Tools.WorldBuilder.MapIni.CleanTitle"),
                localizationService.GetString("Tools.WorldBuilder.MapIni.CleanMessage", sanitized.Warnings.Count),
                NotificationDurations.Medium);
            return;
        }

        try
        {
            var tempPath = $"{iniPath}.tmp.{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllTextAsync(tempPath, sanitized.Text, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, iniPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (IOException ex)
                    {
                        logger.LogDebug(ex, "Best-effort temp file cleanup failed for {Path}", tempPath);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        logger.LogDebug(ex, "Best-effort temp file cleanup failed for {Path}", tempPath);
                    }
                }
            }
        }
        catch (IOException ex)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.MapIni.FailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.MapIni.FailureMessage", ex.Message),
                NotificationDurations.Long);
            logger.LogWarning(ex, "Failed to tidy map INI file {Path}", iniPath);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.MapIni.FailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.MapIni.FailureMessage", ex.Message),
                NotificationDurations.Long);
            logger.LogWarning(ex, "Failed to tidy map INI file {Path}", iniPath);
            return;
        }

        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.MapIni.SuccessTitle"),
            localizationService.GetString("Tools.WorldBuilder.MapIni.SuccessMessage", sanitized.Warnings.Count),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Imports teams from a .teams exchange file into the open map.
    /// </summary>
    [RelayCommand]
    private async Task ImportTeamsWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var map = _map;
        if (map == null)
        {
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localizationService.GetString("Tools.WorldBuilder.Teams.ImportTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WorldBuilder.Teams.FilterName"))
                {
                    Patterns = ["*" + WorldBuilderConstants.FileExtensions.TeamsFile],
                },
            ],
        }).ConfigureAwait(false);
        var localPath = files.Count == 0 ? null : files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            return;
        }

        var existingNames = new HashSet<string>(map.Teams.Select(GetTeamName), StringComparer.OrdinalIgnoreCase);
        var result = await teamExchangeService.ImportAsync(localPath, ResolveOwnerName(), existingNames, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Teams.ImportFailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.Teams.ImportFailureMessage", result.FirstError ?? localPath),
                NotificationDurations.Long);
            return;
        }

        var applied = await InvokeOnUIThreadAsync(() =>
        {
            if (_map == null || _map != map)
            {
                return false;
            }

            _undoService.Checkpoint(_map);
            foreach (var team in result.Data)
            {
                map.Teams.Add(team);
            }

            SyncSidesAndTeams();
            Summary = mapService.Summarize(map);
            map.IsDirty = true;
            IsDirty = true;
            UpdateUndoState();
            return true;
        }).ConfigureAwait(false);

        if (!applied)
        {
            return;
        }

        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Teams.ImportSuccessTitle"),
            localizationService.GetString("Tools.WorldBuilder.Teams.ImportSuccessMessage", result.Data.Count),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Exports the open map teams to a .teams exchange file.
    /// </summary>
    [RelayCommand]
    private async Task ExportTeamsWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var map = _map;
        if (map == null)
        {
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = localizationService.GetString("Tools.WorldBuilder.Teams.ExportTitle"),
            SuggestedFileName = FilePath == null
                ? null
                : Path.GetFileNameWithoutExtension(FilePath) + WorldBuilderConstants.FileExtensions.TeamsFile,
            FileTypeChoices =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WorldBuilder.Teams.FilterName"))
                {
                    Patterns = ["*" + WorldBuilderConstants.FileExtensions.TeamsFile],
                },
            ],
        }).ConfigureAwait(false);
        var localPath = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            return;
        }

        if (_map == null || _map != map)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.Teams.ExportCancelledTitle"),
                localizationService.GetString("Tools.WorldBuilder.Teams.ExportCancelledMessage"),
                NotificationDurations.Short);
            return;
        }

        var result = await teamExchangeService.ExportAsync(localPath, map.Teams, ResolveOwnerName(), ResolveDefaultTeams(), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Teams.ExportFailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.Teams.ExportFailureMessage", result.FirstError ?? localPath),
                NotificationDurations.Long);
            return;
        }

        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Teams.ExportSuccessTitle"),
            localizationService.GetString("Tools.WorldBuilder.Teams.ExportSuccessMessage", result.Data),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Creates a project folder with the name of the chosen directory.
    /// </summary>
    [RelayCommand]
    private async Task NewProjectWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var folder = await PickFolderAsync(
            localizationService.GetString("Tools.WorldBuilder.Project.NewTitle")).ConfigureAwait(false);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var result = await projectService.CreateAsync(folder, Path.GetFileName(folder), null, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Project.FailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.Project.FailureMessage", result.FirstError ?? folder),
                NotificationDurations.Long);
            return;
        }

        await InvokeOnUIThreadAsync(() => AdoptProject(result.Data)).ConfigureAwait(false);
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Project.CreatedTitle"),
            localizationService.GetString("Tools.WorldBuilder.Project.CreatedMessage", result.Data.Name),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Opens an existing project folder.
    /// </summary>
    [RelayCommand]
    private async Task OpenProjectWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var folder = await PickFolderAsync(
            localizationService.GetString("Tools.WorldBuilder.Project.OpenTitle")).ConfigureAwait(false);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var result = await projectService.OpenAsync(folder, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Project.FailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.Project.FailureMessage", result.FirstError ?? folder),
                NotificationDurations.Long);
            return;
        }

        await InvokeOnUIThreadAsync(() => AdoptProject(result.Data)).ConfigureAwait(false);
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Project.OpenedTitle"),
            localizationService.GetString("Tools.WorldBuilder.Project.OpenedMessage", result.Data.Name, result.Data.MapFiles.Count),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Imports map files from a game-data folder into the open project.
    /// </summary>
    [RelayCommand]
    private async Task ImportGameDataWithDialogAsync(CancellationToken cancellationToken = default)
    {
        if (_project == null)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.Project.NoProjectTitle"),
                localizationService.GetString("Tools.WorldBuilder.Project.NoProjectMessage"),
                NotificationDurations.Medium);
            return;
        }

        var folder = await PickFolderAsync(
            localizationService.GetString("Tools.WorldBuilder.Project.ImportTitle")).ConfigureAwait(false);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var result = await projectService.ImportFromFolderAsync(_project, folder, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Project.FailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.Project.FailureMessage", result.FirstError ?? folder),
                NotificationDurations.Long);
            return;
        }

        await InvokeOnUIThreadAsync(() => AdoptProject(_project)).ConfigureAwait(false);
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Project.ImportedTitle"),
            localizationService.GetString("Tools.WorldBuilder.Project.ImportedMessage", result.Data.Count),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Launches the native WorldBuilder for the open map when one is installed.
    /// </summary>
    [RelayCommand]
    private async Task LaunchNativeWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var folder = await PickFolderAsync(
            localizationService.GetString("Tools.WorldBuilder.Native.GameFolderTitle")).ConfigureAwait(false);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var found = await sidecarService.FindNativeAsync(folder, cancellationToken).ConfigureAwait(false);
        if (!found.Success || string.IsNullOrEmpty(found.Data))
        {
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WorldBuilder.Native.NotFoundTitle"),
                localizationService.GetString("Tools.WorldBuilder.Native.NotFoundMessage", found.FirstError ?? folder),
                NotificationDurations.Long);
            return;
        }

        var launched = await sidecarService.LaunchAsync(found.Data, FilePath, cancellationToken).ConfigureAwait(false);
        if (!launched.Success)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Native.FailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.Native.FailureMessage", launched.FirstError ?? found.Data),
                NotificationDurations.Long);
            return;
        }

        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Native.SuccessTitle"),
            localizationService.GetString("Tools.WorldBuilder.Native.SuccessMessage"),
            NotificationDurations.Medium);
    }

    partial void OnMapNameChanged(string value)
    {
        if (_adoptingDocument || _map == null || _disposed)
        {
            return;
        }

        _map.World.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.MapName,
            WorldBuilderConstants.DictValueType.UnicodeString,
            StringValue: value));
        _map.IsDirty = true;
        IsDirty = true;
    }

    private async Task<bool> ConfirmDiscardUnsavedAsync(CancellationToken cancellationToken)
    {
        if (!IsDirty || !HasDocument)
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            return await dialogService.ShowConfirmationAsync(
                localizationService.GetString("Tools.WorldBuilder.UnsavedChanges.Title"),
                localizationService.GetString("Tools.WorldBuilder.UnsavedChanges.Message"),
                localizationService.GetString("Tools.WorldBuilder.UnsavedChanges.Discard"),
                localizationService.GetString("Tools.WorldBuilder.UnsavedChanges.Cancel")).ConfigureAwait(false);
        }

        return await Dispatcher.UIThread.InvokeAsync(() => dialogService.ShowConfirmationAsync(
            localizationService.GetString("Tools.WorldBuilder.UnsavedChanges.Title"),
            localizationService.GetString("Tools.WorldBuilder.UnsavedChanges.Message"),
            localizationService.GetString("Tools.WorldBuilder.UnsavedChanges.Discard"),
            localizationService.GetString("Tools.WorldBuilder.UnsavedChanges.Cancel"))).ConfigureAwait(false);
    }

    private async Task<MapPreviewData?> ReadSidecarPreviewAsync(string mapPath, CancellationToken cancellationToken)
    {
        var result = await previewService.ReadTgaAsync(mapPath, cancellationToken).ConfigureAwait(false);
        return result.Success ? result.Data : null;
    }

    private void AdoptDocument(WorldBuilderMap map, string? filePath, MapPreviewData? preview, bool clearUndo = true)
    {
        _adoptingDocument = true;
        try
        {
            _map = map;
            FilePath = filePath;
            HasDocument = true;
            IsDirty = map.IsDirty;
            MapWidth = map.Terrain.Width;
            MapHeight = map.Terrain.Height;
            MapTerrainHeights = map.Terrain.Heights;
            MapName = map.World.GetString(WorldBuilderConstants.DictKeys.MapName, filePath == null ? string.Empty : Path.GetFileNameWithoutExtension(filePath));
            OnPropertyChanged(nameof(MapName));
            Summary = mapService.Summarize(map);
            ValidationIssues.Clear();
            PreviewImage?.Dispose();
            PreviewImage = BuildPreviewImage(preview);
            if (clearUndo)
            {
                _undoService.Clear();
            }

            UpdateUndoState();
            SyncAllCollections();
            SyncSunFromLighting();
            UpdateViewportVisibility();
            RefreshCanvasBitmap();
            RequestResetView?.Invoke(map);
            _ = Task.Run(EnsureGameContentAsync);
        }
        finally
        {
            _adoptingDocument = false;
        }
    }

    private async Task EnsureGameContentAsync()
    {
        try
        {
            var result = await contentService.EnsureContentLoadedAsync().ConfigureAwait(false);
            if (result.Success && result.Data)
            {
                await InvokeOnUIThreadAsync(RaiseRefreshView).ConfigureAwait(false);
            }
            else if (!result.Success && !_contentWarningShown)
            {
                _contentWarningShown = true;
                await InvokeOnUIThreadAsync(() => notificationService.ShowWarning(
                    localizationService.GetString("Tools.WorldBuilder.Content.NoInstallation.Title"),
                    localizationService.GetString("Tools.WorldBuilder.Content.NoInstallation.Message"),
                    NotificationDurations.Medium)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Content loading was superseded; a newer pass wins.
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Background game content load for WorldBuilder failed.");
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Background game content load for WorldBuilder failed.");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Background game content load for WorldBuilder failed.");
        }
    }

    private void ClearDocument()
    {
        _map = null;
        FilePath = null;
        HasDocument = false;
        IsDirty = false;
        MapWidth = 120;
        MapHeight = 120;
        MapTerrainHeights = null;
        Summary = null;
        ValidationIssues.Clear();
        PreviewImage?.Dispose();
        PreviewImage = null;
        _undoService.Clear();
        UpdateUndoState();
        CanvasBitmap?.Dispose();
        CanvasBitmap = null;
        SyncAllCollections();
    }

    private void RefreshValidationIssues(ValidationResult result)
    {
        ValidationIssues.Clear();
        foreach (var issue in result.Issues)
        {
            ValidationIssues.Add(issue);
        }
    }

    private void AdoptProject(WorldBuilderProject project)
    {
        _project = project;
        ProjectName = project.Name;
        HasProject = true;
        ProjectFiles.Clear();
        foreach (var mapFile in project.MapFiles)
        {
            ProjectFiles.Add(mapFile);
        }
    }

    private string ResolveOwnerName()
    {
        if (_map == null)
        {
            return string.Empty;
        }

        var names = _map.Sides
            .Select(s => s.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName, string.Empty))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        var skirmish = names.FirstOrDefault(n =>
            !string.Equals(n, WorldBuilderConstants.Sides.CivilianPlayerName, StringComparison.OrdinalIgnoreCase));
        return skirmish ?? names.FirstOrDefault() ?? string.Empty;
    }

    private HashSet<string> ResolveDefaultTeams()
    {
        var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            WorldBuilderConstants.Teams.DefaultRootTeam,
            WorldBuilderConstants.Teams.DefaultTeam,
        };

        var owner = ResolveOwnerName();
        if (!string.IsNullOrEmpty(owner))
        {
            defaults.Add($"{WorldBuilderConstants.Teams.DefaultRootTeam}{owner}");
        }

        if (_map != null)
        {
            foreach (var side in _map.Sides)
            {
                var sideName = side.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName, string.Empty);
                if (!string.IsNullOrEmpty(sideName))
                {
                    defaults.Add($"{WorldBuilderConstants.Teams.DefaultRootTeam}{sideName}");
                }
            }
        }

        return defaults;
    }

    private async Task<bool> WriteMapToFileAsync(string path, CancellationToken cancellationToken)
    {
        var map = _map;
        if (map == null)
        {
            return false;
        }

        var previousPath = map.FilePath;
        map.FilePath = path;
        var result = await mapService.SaveAsync(map, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            map.FilePath = previousPath;
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Save.FailureTitle"),
                localizationService.GetString("Tools.WorldBuilder.Save.FailureMessage", result.FirstError ?? path),
                NotificationDurations.Long);
            logger.LogWarning("Failed to save map file {Path}: {Error}", path, result.FirstError);
            return false;
        }

        await InvokeOnUIThreadAsync(() =>
        {
            if (_map == null || _map != map)
            {
                return;
            }

            FilePath = path;
            map.IsDirty = false;
            IsDirty = false;
        }).ConfigureAwait(false);
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Save.SuccessTitle"),
            localizationService.GetString("Tools.WorldBuilder.Save.SuccessMessage", Path.GetFileName(path)),
            NotificationDurations.Medium);
        logger.LogInformation("Saved map file {Path}", path);
        return true;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        }).ConfigureAwait(false);
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
