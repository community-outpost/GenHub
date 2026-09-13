using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Models;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// Main view model for the ModBuilder tool plugin.
/// Coordinates project state, build pipeline, file management, and sub-view models.
/// </summary>
/// <param name="buildEngineService">The build engine service.</param>
/// <param name="projectConfigService">The project configuration service.</param>
/// <param name="configurationLoaderService">The configuration loader service.</param>
/// <param name="projectStructureGenerator">The project structure generator.</param>
/// <param name="notificationService">The notification service.</param>
/// <param name="fileManager">The file manager view model.</param>
/// <param name="loggerFactory">The logger factory.</param>
/// <param name="logger">The logger.</param>
/// <param name="dialogService">Optional dialog service for user confirmations.</param>
/// <param name="sampleProjectService">Optional sample project service for asset acquisition.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarCloud", "S107:Methods should not have too many parameters", Justification = "ViewModel requires multiple injected services")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarCloud", "S2325:Methods and properties that don't access instance data should be static", Justification = "RelayCommand and XAML bindings require instance members")]
public partial class ModBuilderViewModel(
    IBuildEngineService buildEngineService,
    IProjectConfigService projectConfigService,
    IConfigurationLoaderService configurationLoaderService,
    IProjectStructureGenerator projectStructureGenerator,
    INotificationService notificationService,
    FileManagerViewModel fileManager,
    ILoggerFactory loggerFactory,
    ILogger<ModBuilderViewModel> logger,
    IDialogService? dialogService = null,
    ISampleProjectService? sampleProjectService = null) : ObservableObject, IDisposable
{
    private const string DefaultStatusColor = UiConstants.DefaultStatusBackgroundColor;

    private readonly Stopwatch _buildStopwatch = new();
    private readonly Dictionary<string, (bool? Big, string? OutputFile)> _originalPackStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RecentProjectInfo> _allRecentProjects = [];
    private readonly StringBuilder _buildOutputBuilder = new();
    private CancellationTokenSource? _buildCancellationTokenSource;
    private CancellationTokenSource? _importCancellationTokenSource;
    private bool _isPopulatingBundles;
    private bool _disposed;

    /// <summary>
    /// Gets the file manager view model.
    /// </summary>
    public FileManagerViewModel FileManager => fileManager;

    /// <summary>
    /// Gets or sets the current project.
    /// </summary>
    [ObservableProperty]
    private ModBuilderProject? _currentProject;

    /// <summary>
    /// Gets the available target game options.
    /// </summary>
    public IReadOnlyList<GameType> AvailableTargetGames { get; } = [GameType.ZeroHour, GameType.Generals];

    /// <summary>
    /// Gets or sets the target game for the current project.
    /// </summary>
    [ObservableProperty]
    private GameType _selectedTargetGame = GameType.ZeroHour;

    partial void OnSelectedTargetGameChanged(GameType value)
    {
        if (CurrentProject is { } project && project.TargetGame != value)
        {
            project.TargetGame = value;
            logger.LogInformation("Project '{Name}' TargetGame changed to {TargetGame}", project.Name, value);
        }
    }

    /// <summary>
    /// Gets the available content type options (e.g. Mod, Patch, Addon).
    /// </summary>
    public IReadOnlyList<ContentType> AvailableContentTypes { get; } =
    [
        ContentType.Mod,
        ContentType.Patch,
        ContentType.Addon,
        ContentType.MapPack,
        ContentType.LanguagePack,
        ContentType.ModdingTool,
    ];

    /// <summary>
    /// Gets or sets the content type for the current project.
    /// </summary>
    [ObservableProperty]
    private ContentType _selectedContentType = ContentType.Mod;

    partial void OnSelectedContentTypeChanged(ContentType value)
    {
        if (CurrentProject is { } project && project.ContentType != value)
        {
            project.ContentType = value;
            logger.LogInformation("Project '{Name}' ContentType changed to {ContentType}", project.Name, value);
        }
    }

    /// <summary>
    /// Gets or sets the project name.
    /// </summary>
    [ObservableProperty]
    private string _projectName = string.Empty;

    /// <summary>
    /// Gets or sets the project path.
    /// </summary>
    [ObservableProperty]
    private string _projectPath = string.Empty;

    /// <summary>
    /// Gets the list of recent projects.
    /// </summary>
    public ObservableCollection<RecentProjectInfo> RecentProjects { get; } = [];

    /// <summary>
    /// Gets the collection of curated publisher sample projects showcased in the dashboard.
    /// </summary>
    public ObservableCollection<SampleProjectShowcaseItem> PublisherSampleProjects { get; } =
    [
        new SampleProjectShowcaseItem
        {
            Id = "GeneralsGamePatch2",
            Name = "Generals Community Patch 2.0",
            Publisher = "TheSuperHackers",
            Description = "Comprehensive balance and bugfix INI rules for C&C Generals. Demonstrates multi-directory INI rules and balance tuning.",
            TargetGame = "Generals",
            OutputFileName = "500_900_CommunityPatch_CoreINI.big",
            Tag = "Balance & Bugfix",
            IsReproducibleVerified = true,
            ExpectedSha256 = ModBuilderConstants.SampleProjects.GeneralsGamePatch2Sha256,
        },
        new SampleProjectShowcaseItem
        {
            Id = "ImprovedMenus",
            Name = "Improved Menus Widescreen",
            Publisher = "eliorata",
            Description = "16:9 widescreen UI layout overhaul with custom .wnd window definitions and high-definition menu textures.",
            TargetGame = "Zero Hour",
            OutputFileName = "0_ImprovedMenusEnglish.big",
            Tag = "Widescreen UI",
            IsReproducibleVerified = true,
            ExpectedSha256 = ModBuilderConstants.SampleProjects.ImprovedMenusSha256,
        },
        new SampleProjectShowcaseItem
        {
            Id = "LemonControlBar",
            Name = "Lemon Control Bar (1080p)",
            Publisher = "L3-M (Lemon)",
            Description = "Lemon Edition 1080p widescreen command & control bar overhaul with custom art textures and window layouts.",
            TargetGame = "Zero Hour",
            OutputFileName = "340_ControlBarProLemonEdition1080ZH.big",
            Tag = "Control Bar",
            IsReproducibleVerified = true,
            ExpectedSha256 = ModBuilderConstants.SampleProjects.LemonControlBarSha256,
        },
        new SampleProjectShowcaseItem
        {
            Id = "LeikezeHotkeys",
            Name = "Leikeze Competitive Hotkeys",
            Publisher = "Leikeze",
            Description = "Tournament-standard QWERTY CSF string tables. Demonstrates language string packaging into a fast release BIG archive.",
            TargetGame = "Zero Hour",
            OutputFileName = "!HotkeysLeikezeENZH.big",
            Tag = "Competitive Hotkeys",
            IsReproducibleVerified = true,
            ExpectedSha256 = ModBuilderConstants.SampleProjects.LeikezeHotkeysSha256,
        },
    ];

    /// <summary>
    /// Gets or sets the search query for filtering projects.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        ApplyProjectFilter();
    }

    /// <summary>
    /// Gets a value indicating whether there are recent projects.
    /// </summary>
    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>
    /// Gets the total number of projects.
    /// </summary>
    public int TotalProjects => RecentProjects.Count;

    /// <summary>
    /// Gets the total number of builds (placeholder).
    /// </summary>
    public int TotalBuilds => 0;

    /// <summary>
    /// Gets or sets a value indicating whether a project is loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isProjectLoaded;

    /// <summary>
    /// Gets the list of build configurations.
    /// </summary>
    public ObservableCollection<string> BuildConfigurations { get; } = [ModBuilderConstants.BuildConfigurationDebug, ModBuilderConstants.BuildConfigurationRelease];

    /// <summary>
    /// Gets or sets the selected configuration.
    /// </summary>
    [ObservableProperty]
    private string _selectedConfiguration = ModBuilderConstants.BuildConfigurationDebug;

    /// <summary>
    /// Gets the list of compression levels.
    /// </summary>
    public ObservableCollection<CompressionLevel> CompressionLevels { get; } =
    [
        CompressionLevel.NoCompression,
        CompressionLevel.Fastest,
        CompressionLevel.Optimal,
        CompressionLevel.SmallestSize,
    ];

    /// <summary>
    /// Gets or sets the selected compression level.
    /// </summary>
    [ObservableProperty]
    private CompressionLevel _selectedCompressionLevel;

    /// <summary>
    /// Gets or sets the output directory.
    /// </summary>
    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    /// <summary>
    /// Gets or sets the game directory.
    /// </summary>
    [ObservableProperty]
    private string _gameDirectory = string.Empty;

    /// <summary>
    /// Gets the list of bundles.
    /// </summary>
    public ObservableCollection<BundleItemViewModel> Bundles { get; } = [];

    /// <summary>
    /// Gets the list of bundle packs (alias for Bundles).
    /// </summary>
    public ObservableCollection<BundleItemViewModel> BundlePacks => Bundles;

    /// <summary>
    /// Gets or sets the selected bundle.
    /// </summary>
    [ObservableProperty]
    private BundleItemViewModel? _selectedBundle;

    /// <summary>
    /// Gets or sets a value indicating whether a build is running.
    /// </summary>
    [ObservableProperty]
    private bool _isBuildRunning;

    /// <summary>
    /// Gets a value indicating whether a build is running (alias for IsBuildRunning).
    /// </summary>
    public bool IsBuilding => IsBuildRunning;

    /// <summary>
    /// Gets or sets the current build progress.
    /// </summary>
    [ObservableProperty]
    private BuildProgress? _buildProgress;

    /// <summary>
    /// Gets or sets the current build stage.
    /// </summary>
    [ObservableProperty]
    private string _buildStage = string.Empty;

    /// <summary>
    /// Gets or sets the current file being processed.
    /// </summary>
    [ObservableProperty]
    private string _currentFile = string.Empty;

    /// <summary>
    /// Gets or sets the number of processed files.
    /// </summary>
    [ObservableProperty]
    private int _processedFiles;

    /// <summary>
    /// Gets or sets the total number of files.
    /// </summary>
    [ObservableProperty]
    private int _totalFiles;

    /// <summary>
    /// Gets or sets the percent complete.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double _percentComplete;

    /// <summary>
    /// Gets the progress text for display.
    /// </summary>
    public string ProgressText => $"{PercentComplete.ToString("F1", CultureInfo.InvariantCulture)}%";

    /// <summary>
    /// Gets or sets the estimated time remaining.
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _estimatedTimeRemaining;

    /// <summary>
    /// Gets the build log.
    /// </summary>
    public ObservableCollection<string> BuildLog { get; } = [];

    /// <summary>
    /// Gets the build output as a formatted string for display.
    /// </summary>
    public string BuildOutput => _buildOutputBuilder.ToString();

    /// <summary>
    /// Gets or sets the build status text.
    /// </summary>
    [ObservableProperty]
    private string _buildStatus = ModBuilderConstants.ReadyStatus;

    /// <summary>
    /// Gets the current build stage (alias for BuildStage).
    /// </summary>
    public string CurrentStage => BuildStage;

    /// <summary>
    /// Gets or sets a value indicating whether clean action is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _cleanEnabled;

    /// <summary>
    /// Gets or sets a value indicating whether build action is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _buildEnabled = true;

    /// <summary>
    /// Gets or sets a value indicating whether release action is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _releaseEnabled = true;

    /// <summary>
    /// Gets or sets a value indicating whether manifest creation action is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _createManifestEnabled = true;

    /// <summary>
    /// Gets or sets a value indicating whether verbose logging is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _verboseLogging;

    /// <summary>
    /// Gets or sets a value indicating whether multi-processing is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _multiProcessing = true;

    /// <summary>
    /// Gets or sets a value indicating whether configuration should be printed before build.
    /// </summary>
    [ObservableProperty]
    private bool _printConfig;

    /// <summary>
    /// Gets or sets a value indicating whether bundle packs should be packaged into single .big archives instead of zip files.
    /// </summary>
    [ObservableProperty]
    private bool _singleBigPackMode;

    partial void OnSingleBigPackModeChanged(bool value)
    {
        if (_isPopulatingBundles)
        {
            return;
        }

        UpdatePacksForSingleBigMode(CurrentProject?.Configuration?.Packs, value);

        foreach (var bundle in Bundles)
        {
            var matchingPack = CurrentProject?.Configuration?.Packs?.FirstOrDefault(p =>
                string.Equals(p.Name, bundle.Name, StringComparison.OrdinalIgnoreCase));
            bundle.IsBig = matchingPack?.IsBigPack ?? value;
        }
    }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = ModBuilderConstants.ReadyStatus;

    /// <summary>
    /// Gets or sets the status text for the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusText = ModBuilderConstants.ReadyStatus;

    /// <summary>
    /// Default status color value for the status bar.
    /// </summary>
    /// <summary>
    /// Gets or sets the status color for the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusColor = DefaultStatusColor;

    /// <summary>
    /// Gets or sets the status text color for the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusTextColor = UiConstants.DefaultStatusTextColor;

    /// <summary>
    /// Gets or sets the file count.
    /// </summary>
    [ObservableProperty]
    private int _fileCount;

    /// <summary>
    /// Gets or sets the total size.
    /// </summary>
    [ObservableProperty]
    private long _totalSize;

    /// <summary>
    /// Gets or sets the last build time.
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _lastBuildTime;

    /// <summary>
    /// Gets or sets the count of files to build.
    /// </summary>
    [ObservableProperty]
    private int _filesToBuildCount;

    /// <summary>
    /// Gets the execute build command (alias for BuildCommand).
    /// </summary>
    public IRelayCommand ExecuteBuildCommand => BuildCommand;

    /// <summary>
    /// Gets the load project command (alias for OpenProjectCommand).
    /// </summary>
    public IRelayCommand LoadProjectCommand => OpenProjectCommand;

    /// <summary>
    /// Gets the current project path for display.
    /// </summary>
    public string CurrentProjectPath => string.IsNullOrEmpty(ProjectPath) ? string.Empty : ProjectPath;

    /// <summary>
    /// Initializes the ViewModel.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await LoadRecentProjectsAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Loads recent projects.
    /// </summary>
    private async Task LoadRecentProjectsAsync()
    {
        try
        {
            var result = await projectConfigService.GetRecentProjectsAsync(10, CancellationToken.None).ConfigureAwait(false);
            var rawPaths = result.Success && result.Data != null ? result.Data : (IReadOnlyList<string>)[];
            var projectPaths = await SanitizeAndMigrateRecentPathsAsync(rawPaths).ConfigureAwait(false);

            await PrependDiscoveredSampleProjectsAsync(projectPaths).ConfigureAwait(false);

            var projectInfos = projectPaths.Select(CreateRecentProjectInfo).ToList();

            await InvokeOnUIThreadAsync(() =>
            {
                _allRecentProjects.Clear();
                _allRecentProjects.AddRange(projectInfos);
                ApplyProjectFilter();
            });

            logger.LogInformation("Loaded {Count} recent projects", projectInfos.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load recent projects");
        }
    }

    private async Task<List<string>> SanitizeAndMigrateRecentPathsAsync(IEnumerable<string> rawPaths)
    {
        var projectPaths = new List<string>();

        // Sanitize recent projects: if any project is located inside the application installation directory,
        // migrate it to user documents and scrub old app-dir paths so Velopack updates won't be blocked.
        foreach (var rawPath in rawPaths)
        {
            if (IsDeprecatedSamplePath(rawPath))
            {
                continue;
            }

            if (IsPathInsideAppDirectory(rawPath))
            {
                var migrated = await MigrateProjectOutOfAppDirectoryAsync(rawPath).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(migrated) &&
                    !IsDeprecatedSamplePath(migrated) &&
                    !projectPaths.Contains(migrated, StringComparer.OrdinalIgnoreCase))
                {
                    projectPaths.Add(migrated);
                }
            }
            else if (!string.IsNullOrWhiteSpace(rawPath) && !projectPaths.Contains(rawPath, StringComparer.OrdinalIgnoreCase))
            {
                projectPaths.Add(rawPath);
            }
        }

        return projectPaths;
    }

    private static bool IsDeprecatedSamplePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        var dirName = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
        return ModBuilderConstants.DeprecatedSampleNames.Any(deprecated =>
            string.Equals(name, deprecated, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dirName, deprecated, StringComparison.OrdinalIgnoreCase));
    }

    private async Task PrependDiscoveredSampleProjectsAsync(List<string> projectPaths)
    {
        IReadOnlyList<string> samplePaths = [];
        try
        {
            samplePaths = await DiscoverSampleProjectPathsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to discover sample project paths");
        }

        for (var i = samplePaths.Count - 1; i >= 0; i--)
        {
            var samplePath = samplePaths[i];
            if (!string.IsNullOrEmpty(samplePath) && File.Exists(samplePath) && !projectPaths.Contains(samplePath, StringComparer.OrdinalIgnoreCase))
            {
                projectPaths.Insert(0, samplePath);
            }
        }
    }

    private static RecentProjectInfo CreateRecentProjectInfo(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = ModBuilderConstants.UntitledProjectName;
        }

        DateTime? lastWriteTime = null;
        var contentType = ContentType.Mod;
        try
        {
            if (File.Exists(path))
            {
                lastWriteTime = File.GetLastWriteTime(path);
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("contentType", out var ctProp) &&
                    Enum.TryParse<ContentType>(ctProp.GetString(), true, out var parsed))
                {
                    contentType = parsed;
                }
            }
            else if (Directory.Exists(path))
            {
                lastWriteTime = Directory.GetLastWriteTime(path);
            }
        }
        catch (IOException)
        {
            // Ignore I/O errors reading timestamp and content type
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore access errors reading timestamp and content type
        }
        catch (System.Text.Json.JsonException)
        {
            // Ignore JSON errors reading timestamp and content type
        }

        return new RecentProjectInfo
        {
            Name = name,
            Path = path,
            LastBuildTime = lastWriteTime,
            Version = "1.0.0",
            ContentType = contentType,
        };
    }

    /// <summary>
    /// Creates a new project.
    /// </summary>
    [RelayCommand]
    private async Task NewProjectAsync()
    {
        logger.LogInformation("NewProjectAsync requested");
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(lifetime?.MainWindow);
        if (topLevel == null)
        {
            return;
        }

        var defaultFolder = GetUserModBuilderDirectory();
        if (!Directory.Exists(defaultFolder))
        {
            try
            {
                Directory.CreateDirectory(defaultFolder);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not create default ModBuilder directory at {Folder}", defaultFolder);
            }
        }

        var suggestedFolder = Directory.Exists(defaultFolder)
            ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(defaultFolder).ConfigureAwait(false)
            : null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create New ModBuilder Project",
            SuggestedFileName = "MyMod.mbproj",
            SuggestedStartLocation = suggestedFolder,
            FileTypeChoices =
            [
                new FilePickerFileType("ModBuilder Project") { Patterns = [ModBuilderConstants.ProjectFilePattern,], }
            ],
        }).ConfigureAwait(false);

        if (file != null)
        {
            var projectPath = file.Path.LocalPath;

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                notificationService.ShowWarning(
                    "Invalid Path",
                    "Please select a valid project location");
                return;
            }

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            logger.LogInformation("Creating new project '{ProjectName}' at {ProjectPath}", projectName, projectPath);

            try
            {
                var result = await projectConfigService.CreateProjectAsync(
                    projectPath,
                    projectName,
                    contentType: SelectedContentType,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                if (result.Success && result.Data != null)
                {
                    await HandleNewProjectCreatedAsync(projectPath, projectName, result.Data).ConfigureAwait(false);
                }
                else
                {
                    notificationService.ShowError("Creation Failed", result.FirstError ?? ModBuilderConstants.UnknownError);
                    logger.LogWarning("Project creation failed: {Error}", result.FirstError);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create project at {ProjectPath}", projectPath);
                notificationService.ShowError("Creation Error", ex.Message);
            }
        }
    }

    /// <summary>
    /// Opens an existing project.
    /// </summary>
    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        logger.LogInformation("OpenProjectAsync requested");
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(lifetime?.MainWindow);
        if (topLevel == null)
        {
            return;
        }

        var defaultFolder = GetUserModBuilderDirectory();
        var suggestedFolder = Directory.Exists(defaultFolder)
            ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(defaultFolder).ConfigureAwait(false)
            : null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open ModBuilder Project",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedFolder,
            FileTypeFilter =
            [
                new FilePickerFileType("ModBuilder Project") { Patterns = [ModBuilderConstants.ProjectFilePattern,], }
            ],
        }).ConfigureAwait(false);

        if (files.Any())
        {
            logger.LogInformation("Selected project to open: {Path}", files[0].Path.LocalPath);
            await LoadProjectFromPathAsync(files[0].Path.LocalPath).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports one or more .BIG archives into the current project's GameFilesEdited directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task ImportBigFilesAsync()
    {
        if (CurrentProject == null || string.IsNullOrWhiteSpace(ProjectPath))
        {
            notificationService.ShowWarning(
                "No Project Open",
                "Please open or create a project first before importing .BIG files.");
            return;
        }

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(lifetime?.MainWindow);
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import .BIG Archives into Project",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Command & Conquer BIG Archive (*.big)") { Patterns = ["*.big"], },
                new FilePickerFileType("All Files (*.*)") { Patterns = ["*.*"], },
            ],
        }).ConfigureAwait(false);

        if (files == null || files.Count == 0)
        {
            return;
        }

        var selectedPaths = files.Select(f => f.Path.LocalPath).Where(File.Exists).ToList();
        if (selectedPaths.Count == 0)
        {
            return;
        }

        if (IsBuildRunning)
        {
            notificationService.ShowWarning("Build in Progress", "Cannot import files while a build is running.");
            return;
        }

        logger.LogInformation("Importing {Count} .BIG file(s) into current project: {ProjectPath}", selectedPaths.Count, ProjectPath);
        AppendBuildLog($"Importing {selectedPaths.Count} .BIG archive(s) into project...");

        if (_importCancellationTokenSource != null)
        {
            await _importCancellationTokenSource.CancelAsync().ConfigureAwait(false);
            _importCancellationTokenSource.Dispose();
            _importCancellationTokenSource = null;
        }

        var cts = new CancellationTokenSource();
        _importCancellationTokenSource = cts;

        try
        {
            var result = await projectConfigService.ImportBigFilesAsync(
                ProjectPath,
                selectedPaths,
                createBundlePackForBig: true,
                cancellationToken: cts.Token).ConfigureAwait(false);

            if (result.Success)
            {
                AppendBuildLog($"Successfully imported {result.Data} files from {selectedPaths.Count} BIG archive(s).");
                notificationService.ShowSuccess(
                    "Import Complete",
                    $"Imported {result.Data} file(s) from {selectedPaths.Count} .BIG archive(s) into GameFilesEdited.");

                await LoadProjectDataAsync().ConfigureAwait(false);
                if (CurrentProject != null)
                {
                    var editedDir = CurrentProject.Directories?.GameFilesEdited ?? ModBuilderConstants.GameFilesEditedDir;
                    await FileManager.InitializeAsync(CurrentProject.ProjectDir, editedDir).ConfigureAwait(false);
                }
            }
            else
            {
                notificationService.ShowError("Import Failed", result.FirstError ?? ModBuilderConstants.UnknownError);
                AppendBuildLog($"Import failed: {result.FirstError}");
            }
        }
        catch (OperationCanceledException)
        {
            AppendBuildLog("Import cancelled.");
            notificationService.ShowInfo("Import Cancelled", "The BIG import operation was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import BIG file(s)");
            notificationService.ShowError("Import Error", ex.Message);
            AppendBuildLog($"Error importing BIG archive: {ex.Message}");
        }
        finally
        {
            if (_importCancellationTokenSource == cts)
            {
                _importCancellationTokenSource.Dispose();
                _importCancellationTokenSource = null;
            }
        }
    }

    /// <summary>
    /// Creates a new project initialized from one or more existing .BIG files.
    /// Extracts all files into GameFilesEdited and automatically creates bundle pack configurations.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task ImportBigModAsync()
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(lifetime?.MainWindow);
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select .BIG Mod Archives to Import",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Command & Conquer BIG Archive (*.big)") { Patterns = ["*.big"], },
                new FilePickerFileType("All Files (*.*)") { Patterns = ["*.*"], },
            ],
        }).ConfigureAwait(false);

        if (files == null || files.Count == 0)
        {
            return;
        }

        var selectedPaths = files.Select(f => f.Path.LocalPath).Where(File.Exists).ToList();
        if (selectedPaths.Count == 0)
        {
            return;
        }

        var primaryBigName = Path.GetFileNameWithoutExtension(selectedPaths[0]);
        var defaultFolder = GetUserModBuilderDirectory();
        if (!Directory.Exists(defaultFolder))
        {
            try
            {
                Directory.CreateDirectory(defaultFolder);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not create default ModBuilder directory at {Folder}", defaultFolder);
            }
        }

        var suggestedFolder = Directory.Exists(defaultFolder)
            ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(defaultFolder).ConfigureAwait(false)
            : null;

        var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Imported ModBuilder Project",
            SuggestedFileName = $"{primaryBigName}.mbproj",
            SuggestedStartLocation = suggestedFolder,
            FileTypeChoices =
            [
                new FilePickerFileType("ModBuilder Project") { Patterns = [ModBuilderConstants.ProjectFilePattern], },
            ],
        }).ConfigureAwait(false);

        if (saveFile == null)
        {
            return;
        }

        var projectPath = saveFile.Path.LocalPath;
        var projectName = Path.GetFileNameWithoutExtension(projectPath);

        logger.LogInformation("Creating imported project '{ProjectName}' at {ProjectPath} from {BigCount} BIG archives", projectName, projectPath, selectedPaths.Count);
        AppendBuildLog($"Creating project '{projectName}' from {selectedPaths.Count} .BIG archive(s)...");

        try
        {
            var result = await projectConfigService.CreateProjectFromBigFilesAsync(
                projectPath,
                projectName,
                selectedPaths,
                contentType: SelectedContentType,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            if (result.Success && result.Data != null)
            {
                CurrentProject = result.Data;
                ProjectPath = projectPath;
                ProjectName = projectName;
                SelectedContentType = result.Data.ContentType;
                IsProjectLoaded = true;

                await LoadProjectDataAsync().ConfigureAwait(false);
                await projectConfigService.AddToRecentProjectsAsync(projectPath, CancellationToken.None).ConfigureAwait(false);
                await LoadRecentProjectsAsync().ConfigureAwait(false);

                if (CurrentProject != null)
                {
                    var editedDir = CurrentProject.Directories?.GameFilesEdited ?? ModBuilderConstants.GameFilesEditedDir;
                    await FileManager.InitializeAsync(CurrentProject.ProjectDir, editedDir).ConfigureAwait(false);
                }

                notificationService.ShowSuccess(
                    "BIG Mod Imported",
                    $"Project '{projectName}' created from {selectedPaths.Count} .BIG archive(s).\nExtracted to GameFilesEdited and bundle packs configured.");
                AppendBuildLog($"Imported project created successfully: {projectPath}");
            }
            else
            {
                notificationService.ShowError("Import Failed", result.FirstError ?? ModBuilderConstants.UnknownError);
                AppendBuildLog($"Failed to create imported project: {result.FirstError}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create project from BIG archive(s)");
            notificationService.ShowError("Import Error", ex.Message);
            AppendBuildLog($"Error importing BIG archive(s): {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a recent project from its file path or info object.
    /// </summary>
    /// <param name="parameter">The file path or recent project info to open.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task OpenRecentProjectAsync(object? parameter)
    {
        var path = parameter switch
        {
            RecentProjectInfo info => info.Path,
            string s => s,
            _ => null,
        };

        logger.LogInformation("OpenRecentProjectAsync requested for: {Path}", path);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            notificationService.ShowWarning("Project Not Found", $"Could not find project file at: {path}");
            return;
        }

        await LoadProjectFromPathAsync(path).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens or selects a recent project. Alias for <see cref="OpenRecentProjectCommand"/>.
    /// </summary>
    /// <param name="parameter">The recent project info or path.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task SelectRecentProjectAsync(object? parameter)
    {
        await OpenRecentProjectAsync(parameter).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a project from the recent projects list without deleting files.
    /// </summary>
    /// <param name="parameter">The file path or recent project info to remove.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task RemoveRecentProjectAsync(object? parameter)
    {
        var (path, name) = ExtractProjectInfo(parameter);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await projectConfigService.RemoveFromRecentProjectsAsync(path, CancellationToken.None).ConfigureAwait(false);
        await LoadRecentProjectsAsync().ConfigureAwait(false);
        notificationService.ShowInfo("Project Removed", $"Removed '{name}' from recent projects.");
    }

    /// <summary>
    /// Deletes a project from disk after user confirmation.
    /// </summary>
    /// <param name="parameter">The file path or recent project info to delete.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task DeleteRecentProjectAsync(object? parameter)
    {
        var (path, name) = ExtractProjectInfo(parameter);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (dialogService == null)
        {
            logger.LogWarning("Cannot confirm project deletion: dialog service unavailable");
            notificationService.ShowError("Error", "Confirmation dialog service is unavailable.");
            return;
        }

        var confirmed = await dialogService.ShowConfirmationAsync(
            "Delete Project",
            $"Are you sure you want to permanently delete '{name}'?\n\nThis will delete the project file and its directory from disk:\n{path}",
            confirmText: "Delete",
            cancelText: "Cancel",
            sessionKey: "ModBuilder_DeleteProject_Confirmation").ConfigureAwait(false);

        if (!confirmed)
        {
            return;
        }

        try
        {
            DeleteProjectFilesFromDisk(path);

            await projectConfigService.RemoveFromRecentProjectsAsync(path, CancellationToken.None).ConfigureAwait(false);

            if (ProjectPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                await CloseProjectAsync().ConfigureAwait(false);
            }

            await LoadRecentProjectsAsync().ConfigureAwait(false);
            notificationService.ShowSuccess("Project Deleted", $"Successfully deleted '{name}'.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete project at {Path}", path);
            notificationService.ShowError("Delete Failed", $"Failed to delete project: {ex.Message}");
        }
    }

    private string GetEffectiveProjectDir()
    {
        if (CurrentProject != null && !string.IsNullOrEmpty(CurrentProject.ProjectDir) && Directory.Exists(CurrentProject.ProjectDir))
        {
            return CurrentProject.ProjectDir;
        }

        if (!string.IsNullOrEmpty(ProjectPath))
        {
            if (Directory.Exists(ProjectPath))
            {
                return ProjectPath;
            }

            var dir = Path.GetDirectoryName(ProjectPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                return dir;
            }
        }

        return string.Empty;
    }

    private static (string Path, string Name) ExtractProjectInfo(object? parameter)
    {
        return parameter switch
        {
            RecentProjectInfo info => (info.Path, info.Name),
            string s => (s, Path.GetFileNameWithoutExtension(s)),
            _ => (string.Empty, string.Empty)
        };
    }

    private static (string IconPath, string DisplayType) GetInstallationDisplayInfo(string? installationType)
    {
        return installationType switch
        {
            ModBuilderConstants.GeneralsInstallationType => (UriConstants.GeneralsIconUri, ModBuilderConstants.GeneralsDisplayName),
            ModBuilderConstants.ZeroHourInstallationType => (UriConstants.ZeroHourIconUri, ModBuilderConstants.ZeroHourDisplayName),
            _ => (string.Empty, string.Empty)
        };
    }

    /// <summary>
    /// Loads the sample project for testing.
    /// </summary>
    /// <summary>
    /// Loads or provisions a specific publisher sample project by its showcase item.
    /// </summary>
    [RelayCommand]
    private async Task OpenSampleProjectAsync(SampleProjectShowcaseItem? item)
    {
        if (item == null)
        {
            return;
        }

        logger.LogInformation("OpenSampleProjectAsync requested for {SampleId} ({SampleName})", item.Id, item.Name);
        try
        {
            var userSamplesDir = Path.Combine(GetUserModBuilderDirectory(), ModBuilderConstants.SamplesDirectoryName);
            var projectDir = Path.Combine(userSamplesDir, item.Id);
            var projectFile = Path.Combine(projectDir, $"{item.Id}{ModBuilderConstants.ProjectFileExtension}");

            if (!File.Exists(projectFile))
            {
                var baseTemplateDir = FindBaseSampleTemplateDirectory(item.Id);
                if (!string.IsNullOrEmpty(baseTemplateDir) && Directory.Exists(baseTemplateDir))
                {
                    Directory.CreateDirectory(projectDir);
                    await CopyDirectoryAsync(baseTemplateDir, projectDir).ConfigureAwait(false);
                }
            }

            if (!File.Exists(projectFile))
            {
                var discovered = await DiscoverSampleProjectPathsAsync().ConfigureAwait(false);
                var found = discovered.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals(item.Id, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(found) && File.Exists(found))
                {
                    projectFile = found;
                }
            }

            if (File.Exists(projectFile))
            {
                await LoadProjectFromPathAsync(projectFile).ConfigureAwait(false);
            }
            else
            {
                notificationService.ShowWarning(
                    "Sample Not Found",
                    $"Could not locate template files for {item.Name}.");
                AppendBuildLog($"Sample template {item.Id} not found in search paths.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load sample project {SampleId}", item.Id);
            notificationService.ShowError("Load Failed", $"Failed to load sample project: {ex.Message}");
        }
    }

    private static string? FindBaseSampleTemplateDirectory(string sampleId)
    {
        var sampleBaseDirs = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName),
            Path.Combine(AppContext.BaseDirectory, ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName),
            Path.Combine(Directory.GetCurrentDirectory(), ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName)),
        };

        foreach (var dir in sampleBaseDirs.Where(Directory.Exists))
        {
            var candidate = Path.Combine(dir, sampleId);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    [RelayCommand]
    private async Task LoadSampleProjectAsync()
    {
        logger.LogInformation("LoadSampleProjectAsync requested");
        try
        {
            var samplePath = await ResolveSampleProjectPathAsync().ConfigureAwait(false);

            if (string.IsNullOrEmpty(samplePath))
            {
                notificationService.ShowWarning(
                    "Sample Not Found",
                    "Sample project not found.");
                AppendBuildLog("Sample project not found in search paths.");
                return;
            }

            logger.LogInformation("Found sample project at: {SamplePath}", samplePath);
            await LoadProjectFromPathAsync(samplePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load sample project");
            notificationService.ShowError("Load Failed", $"Failed to load sample project: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<string>> DiscoverSampleProjectPathsAsync()
    {
        var sampleBaseDirs = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName),
            Path.Combine(AppContext.BaseDirectory, ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName),
            Path.Combine(Directory.GetCurrentDirectory(), ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ModBuilderConstants.SampleProjectsDirectoryName, ModBuilderConstants.ModBuilderDirName)),
        };

        var userSamplesDir = Path.Combine(GetUserModBuilderDirectory(), ModBuilderConstants.SamplesDirectoryName);
        CleanDeprecatedSampleDirectories(userSamplesDir);

        var userProjectPaths = new List<string>();

        foreach (var baseDir in sampleBaseDirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await ProvisionSampleTemplatesFromDirectoryAsync(baseDir, userSamplesDir, userProjectPaths).ConfigureAwait(false);
        }

        return userProjectPaths;
    }

    private void CleanDeprecatedSampleDirectories(string userSamplesDir)
    {
        if (!Directory.Exists(userSamplesDir))
        {
            return;
        }

        foreach (var name in ModBuilderConstants.DeprecatedSampleNames)
        {
            var staleDir = Path.Combine(userSamplesDir, name);
            if (Directory.Exists(staleDir))
            {
                try
                {
                    Directory.Delete(staleDir, recursive: true);
                    logger.LogInformation("Removed deprecated sample directory: {Dir}", staleDir);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to clean deprecated sample directory {Dir}", staleDir);
                }
            }
        }
    }

    private async Task ProvisionSampleTemplatesFromDirectoryAsync(string baseDir, string userSamplesDir, List<string> userProjectPaths)
    {
        try
        {
            var files = Directory.GetFiles(baseDir, ModBuilderConstants.ProjectFilePattern, SearchOption.AllDirectories);
            foreach (var templateFile in files)
            {
                await ProvisionSingleSampleTemplateAsync(templateFile, userSamplesDir, userProjectPaths).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to provision sample templates from {Dir}", baseDir);
        }
    }

    private async Task ProvisionSingleSampleTemplateAsync(string templateFile, string userSamplesDir, List<string> userProjectPaths)
    {
        var templateDir = Path.GetDirectoryName(templateFile);
        if (string.IsNullOrEmpty(templateDir))
        {
            return;
        }

        var projectName = Path.GetFileName(templateDir);
        if (!ModBuilderConstants.AllowedSampleTemplateNames.Contains(projectName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var userProjectDir = Path.Combine(userSamplesDir, projectName);
        var userProjectFile = Path.Combine(userProjectDir, Path.GetFileName(templateFile));

        // Provision template to user directory if not present
        if (!File.Exists(userProjectFile))
        {
            await CopyDirectoryAsync(templateDir, userProjectDir).ConfigureAwait(false);
        }

        var buildDir = Path.Combine(userProjectDir, ModBuilderConstants.DefaultBuildDir);
        var releaseDir = Path.Combine(userProjectDir, ModBuilderConstants.DefaultReleaseDir);
        if (!Directory.Exists(buildDir))
        {
            Directory.CreateDirectory(buildDir);
        }

        if (!Directory.Exists(releaseDir))
        {
            Directory.CreateDirectory(releaseDir);
        }

        if (File.Exists(userProjectFile) && !userProjectPaths.Contains(userProjectFile, StringComparer.OrdinalIgnoreCase))
        {
            userProjectPaths.Add(userProjectFile);
        }
    }

    private async Task<string?> ResolveSampleProjectPathAsync()
    {
        var samplePaths = await DiscoverSampleProjectPathsAsync().ConfigureAwait(false);
        return samplePaths.FirstOrDefault();
    }

    /// <summary>
    /// Determines whether the specified path is located inside the application installation directory.
    /// </summary>
    /// <param name="path">The file or directory path to check.</param>
    /// <returns><c>true</c> if the path is inside the application directory; otherwise, <c>false</c>.</returns>
    internal static bool IsPathInsideAppDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fullPath, baseDir, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (NotSupportedException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }

    private static void DeleteProjectFilesFromDisk(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var projectDir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir) || IsProtectedProjectDirectory(projectDir))
        {
            return;
        }

        // If other project files exist in this folder, do not delete sibling projects or shared folders
        var siblingProjects = Directory.GetFiles(projectDir, ModBuilderConstants.ProjectFilePattern);
        if (siblingProjects.Length > 0)
        {
            return;
        }

        // Delete known project-owned subdirectories if they exist
        var knownDirs = new[]
        {
            Path.Combine(projectDir, ModBuilderConstants.DefaultBuildDir),
            Path.Combine(projectDir, ModBuilderConstants.DefaultReleaseDir),
            Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir),
            Path.Combine(projectDir, ModBuilderConstants.ConfigDir),
            Path.Combine(projectDir, ModBuilderConstants.LowercaseConfigDir),
            Path.Combine(projectDir, ModBuilderConstants.CacheDirectoryName),
        };

        foreach (var dir in knownDirs.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Ignore deletion errors on subdirectories
            }
        }

        // Only delete the project directory itself if it is completely empty
        try
        {
            if (Directory.Exists(projectDir) && !Directory.EnumerateFileSystemEntries(projectDir).Any())
            {
                Directory.Delete(projectDir, recursive: false);
            }
        }
        catch
        {
            // Ignore directory deletion errors
        }
    }

    private static bool IsProtectedProjectDirectory(string projectDir)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var specialFolders = new[]
        {
            userProfile,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.Templates),
            !string.IsNullOrEmpty(userProfile) ? Path.Combine(userProfile, "Downloads") : null,
            Path.GetPathRoot(projectDir),
        }.Where(p => !string.IsNullOrEmpty(p))
         .Select(p => Path.GetFullPath(p!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
         .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalizedProjectDir = Path.GetFullPath(projectDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return specialFolders.Contains(normalizedProjectDir) || IsPathInsideAppDirectory(normalizedProjectDir);
    }

    private static string GetUserModBuilderDirectory()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(docs) && Directory.Exists(docs))
        {
            return Path.Combine(docs, ModBuilderConstants.ModBuilderDirName);
        }

        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApp))
        {
            return Path.Combine(localApp, AppConstants.AppName, ModBuilderConstants.ModBuilderDirName);
        }

        return Path.Combine(Path.GetTempPath(), AppConstants.AppName, ModBuilderConstants.ModBuilderDirName);
    }

    private async Task<string?> MigrateProjectOutOfAppDirectoryAsync(string oldProjectPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(oldProjectPath) || !IsPathInsideAppDirectory(oldProjectPath))
            {
                return oldProjectPath;
            }

            var oldProjectDir = Path.GetDirectoryName(oldProjectPath);
            if (string.IsNullOrEmpty(oldProjectDir))
            {
                return null;
            }

            var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedOldDir = Path.GetFullPath(oldProjectDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedOldDir, baseDir, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Project directory {Dir} is the application root directory; cannot migrate entire app directory.", oldProjectDir);
                await projectConfigService.RemoveFromRecentProjectsAsync(oldProjectPath, CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            var projectName = Path.GetFileName(oldProjectDir);
            var userSamplesDir = Path.Combine(GetUserModBuilderDirectory(), ModBuilderConstants.SamplesDirectoryName);
            var targetDir = Path.Combine(userSamplesDir, projectName);
            var targetProjectPath = Path.Combine(targetDir, Path.GetFileName(oldProjectPath));

            if (Directory.Exists(oldProjectDir))
            {
                await CopyDirectoryAsync(oldProjectDir, targetDir).ConfigureAwait(false);
                TryCleanAppDirectoryBuildArtifacts(oldProjectDir);
            }

            await projectConfigService.RemoveFromRecentProjectsAsync(oldProjectPath, CancellationToken.None).ConfigureAwait(false);
            if (File.Exists(targetProjectPath))
            {
                await projectConfigService.AddToRecentProjectsAsync(targetProjectPath, CancellationToken.None).ConfigureAwait(false);
            }

            logger.LogInformation("Successfully migrated project from {Old} to {New}", oldProjectPath, targetProjectPath);
            return targetProjectPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to migrate project from app directory: {Path}", oldProjectPath);
            return null;
        }
    }

    private static async Task CopyDirectoryAsync(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".msgpack", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destFile = Path.Combine(destinationDir, fileName);
            if (!File.Exists(destFile))
            {
                await using var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                await using var destinationStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await sourceStream.CopyToAsync(destinationStream).ConfigureAwait(false);
            }
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var subDirName = Path.GetFileName(subDir);
            if (subDirName.Equals(ModBuilderConstants.DefaultBuildDir, StringComparison.OrdinalIgnoreCase) ||
                subDirName.Equals(ModBuilderConstants.DefaultReleaseDir, StringComparison.OrdinalIgnoreCase) ||
                subDirName.StartsWith(ModBuilderConstants.StagingDirectoryPrefix, StringComparison.OrdinalIgnoreCase) ||
                subDirName.Equals(ModBuilderConstants.CacheDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destSubDir = Path.Combine(destinationDir, subDirName);
            await CopyDirectoryAsync(subDir, destSubDir).ConfigureAwait(false);
        }
    }

    private void TryCleanAppDirectoryBuildArtifacts(string dir)
    {
        try
        {
            if (!IsPathInsideAppDirectory(dir))
            {
                return;
            }

            var buildDir = Path.Combine(dir, ModBuilderConstants.DefaultBuildDir);
            if (Directory.Exists(buildDir))
            {
                Directory.Delete(buildDir, recursive: true);
            }

            var releaseDir = Path.Combine(dir, ModBuilderConstants.DefaultReleaseDir);
            if (Directory.Exists(releaseDir))
            {
                Directory.Delete(releaseDir, recursive: true);
            }

            var cacheDir = Path.Combine(dir, ModBuilderConstants.CacheDirectoryName);
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }

            foreach (var f in Directory.GetFiles(dir, "*.msgpack", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(f);
                }
                catch (Exception ex)
                {
                    // Best-effort cleanup of temporary msgpack files; ignore locked or inaccessible files
                    logger.LogTrace(ex, "Failed to delete temporary file {FilePath}", f);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not clean app directory build artifacts in {Dir}", dir);
        }
    }

    /// <summary>
    /// Gets the current active window or main application window.
    /// </summary>
    private static Window? GetOwnerWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return lifetime.Windows.FirstOrDefault(w => w.IsActive) ?? lifetime.MainWindow ?? lifetime.Windows.FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// Opens the dedicated File and Asset Manager dialog.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenFileManager))]
    private async Task OpenFileManagerAsync()
    {
        logger.LogInformation("OpenFileManagerAsync requested");
        if (CurrentProject == null)
        {
            notificationService.ShowWarning(ModBuilderConstants.NoProjectTitle, ModBuilderConstants.NoProjectMessage);
            return;
        }

        try
        {
            fileManager.ImportBigFilesRequested -= ImportBigFilesAsync;
            fileManager.ImportBigFilesRequested += ImportBigFilesAsync;

            var projectDir = GetEffectiveProjectDir();
            if (!string.IsNullOrEmpty(projectDir))
            {
                var editedDir = CurrentProject?.Directories?.GameFilesEdited ?? ModBuilderConstants.GameFilesEditedDir;
                await FileManager.InitializeAsync(projectDir, editedDir, CancellationToken.None).ConfigureAwait(false);
            }

            await InvokeOnUIThreadAsync(async () =>
            {
                var dialog = new Views.FileManagerDialog(FileManager);
                var owner = GetOwnerWindow();
                if (owner != null)
                {
                    await dialog.ShowDialog(owner);
                }
                else
                {
                    dialog.Show();
                }

                await RefreshFileCountAsync(CancellationToken.None);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open File Manager dialog");
            notificationService.ShowError("File Manager Error", ex.Message);
        }
    }

    private bool CanOpenFileManager() => CurrentProject != null && !IsBuildRunning;

    private async Task HandleNewProjectCreatedAsync(string projectPath, string projectName, ModBuilderProject project)
    {
        CurrentProject = project;
        ProjectPath = projectPath;
        ProjectName = projectName;
        SelectedContentType = project.ContentType;
        IsProjectLoaded = true;

        // Generate complete project structure
        await projectStructureGenerator.GenerateProjectStructureAsync(
            projectPath,
            CancellationToken.None).ConfigureAwait(false);

        var newProjectDir = Path.GetDirectoryName(projectPath);
        if (!string.IsNullOrEmpty(newProjectDir))
        {
            await EnsureSampleAssetsIfRequiredAsync(projectPath, newProjectDir, projectName).ConfigureAwait(false);
        }

        await LoadProjectDataAsync().ConfigureAwait(false);
        await projectConfigService.AddToRecentProjectsAsync(projectPath, CancellationToken.None).ConfigureAwait(false);
        await LoadRecentProjectsAsync().ConfigureAwait(false);

        notificationService.ShowSuccess(
            "Project Created",
            $"Created project: {projectName}\nProject structure ready. Edit files in GameFilesEdited folder.");
        AppendBuildLog($"Created new project: {projectPath}");
        AppendBuildLog("Generated project structure with folders and config files");
        logger.LogInformation("Project created successfully at {ProjectPath}", projectPath);
    }

    private async Task EnsureSampleAssetsIfRequiredAsync(string projectPath, string projectDir, string projectName)
    {
        if (sampleProjectService is not { } sps ||
            !sps.IsSampleProject(projectPath) ||
            sps.HasSampleAssets(projectDir))
        {
            return;
        }

        StatusMessage = $"Acquiring sample assets for {projectName}...";
        AppendBuildLog($"Sample assets missing for {projectName}. Downloading and extracting authentic game files on-demand...");
        notificationService.ShowInfo("Downloading Sample Assets", $"Downloading sample assets for {projectName}...");

        var progressReporter = new Progress<string>(AppendBuildLog);
        var acquireResult = await sps.EnsureSampleAssetsAsync(
            projectDir,
            projectName,
            progressReporter,
            CancellationToken.None).ConfigureAwait(false);

        if (acquireResult.Success)
        {
            AppendBuildLog($"Successfully acquired sample assets for {projectName}.");
            notificationService.ShowSuccess("Sample Assets Ready", "Authentic game files extracted into GameFilesEdited.");
        }
        else
        {
            AppendBuildLog($"Warning: Failed to acquire sample assets: {acquireResult.FirstError}");
            notificationService.ShowWarning("Sample Assets Incomplete", acquireResult.FirstError ?? "Failed to acquire sample assets.");
        }
    }

    /// <summary>
    /// Loads a project from a specific path.
    /// </summary>
    private async Task LoadProjectFromPathAsync(string projectPath)
    {
        try
        {
            if (string.IsNullOrEmpty(projectPath))
            {
                notificationService.ShowError("Invalid Path", "Project path cannot be empty");
                return;
            }

            if (IsPathInsideAppDirectory(projectPath))
            {
                logger.LogInformation("Project path is inside app directory. Auto-migrating to user space: {Path}", projectPath);
                var migrated = await MigrateProjectOutOfAppDirectoryAsync(projectPath).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(migrated))
                {
                    projectPath = migrated;
                }
            }

            if (!File.Exists(projectPath))
            {
                notificationService.ShowError("File Not Found", $"Project file does not exist: {projectPath}");
                return;
            }

            var result = await projectConfigService.LoadProjectAsync(
                projectPath,
                validateIntegrity: true,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            if (result.Success && result.Data != null)
            {
                CurrentProject = result.Data;
                ProjectPath = projectPath;
                ProjectName = result.Data.Name;
                IsProjectLoaded = true;

                var projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;
                await EnsureSampleAssetsIfRequiredAsync(projectPath, projectDir, ProjectName).ConfigureAwait(false);

                await LoadProjectDataAsync().ConfigureAwait(false);
                await projectConfigService.AddToRecentProjectsAsync(projectPath, CancellationToken.None).ConfigureAwait(false);

                notificationService.ShowSuccess("Project Loaded", $"Loaded: {Path.GetFileName(projectPath)}");
                AppendBuildLog($"Loaded project: {projectPath}");
                StatusMessage = $"Project loaded: {ProjectName}";
            }
            else
            {
                var errorMessage = result.FirstError ?? "Unknown error occurred while loading project";
                notificationService.ShowError("Load Failed", errorMessage);
                AppendBuildLog($"Failed to load project: {errorMessage}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied loading project");
            notificationService.ShowError("Access Denied", "You don't have permission to access this project file");
            AppendBuildLog($"Access denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "I/O error loading project");
            notificationService.ShowError("File Error", "Could not read project file. It may be in use by another program.");
            AppendBuildLog($"I/O error: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load project");
            notificationService.ShowError("Load Error", $"Unexpected error: {ex.Message}");
            AppendBuildLog($"Error loading project: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the current project.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private async Task SaveProjectAsync()
    {
        logger.LogInformation("SaveProjectAsync requested for: {Path}", ProjectPath);
        if (CurrentProject == null || string.IsNullOrEmpty(ProjectPath))
        {
            return;
        }

        try
        {
            CurrentProject.TargetGame = SelectedTargetGame;
            CurrentProject.ContentType = SelectedContentType;

            // Update compression level in configuration
            if (CurrentProject.Configuration != null)
            {
                CurrentProject.Configuration.ZipCompressionLevel = SelectedCompressionLevel;
            }

            var result = await projectConfigService.SaveProjectAsync(
                ProjectPath,
                CurrentProject,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            if (result.Success)
            {
                notificationService.ShowSuccess("Project Saved", "Project saved successfully");
                AppendBuildLog($"Saved project: {ProjectPath}");
                StatusMessage = "Project saved";
                logger.LogInformation("Project saved successfully to {Path}", ProjectPath);
            }
            else
            {
                notificationService.ShowError("Save Failed", result.FirstError ?? ModBuilderConstants.UnknownError);
                logger.LogWarning("Failed to save project: {Error}", result.FirstError);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save project");
            notificationService.ShowError("Save Error", ex.Message);
        }
    }

    private bool CanSaveProject() => CurrentProject != null && !string.IsNullOrEmpty(ProjectPath);

    /// <summary>
    /// Opens the configuration editor dialog.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenConfigEditor))]
    private async Task OpenConfigEditorAsync()
    {
        if (CurrentProject == null)
        {
            notificationService.ShowWarning(ModBuilderConstants.NoProjectTitle, ModBuilderConstants.NoProjectMessage);
            return;
        }

        try
        {
            var configEditorViewModel = new ConfigEditorViewModel(
                configurationLoaderService,
                notificationService,
                loggerFactory.CreateLogger<ConfigEditorViewModel>());

            await configEditorViewModel.InitializeAsync(CurrentProject).ConfigureAwait(false);

            await InvokeOnUIThreadAsync(async () =>
            {
                var dialog = new Views.ConfigEditorDialog(configEditorViewModel);
                var owner = GetOwnerWindow();
                if (owner != null)
                {
                    await dialog.ShowDialog(owner);
                }
                else
                {
                    dialog.Show();
                }

                await LoadBundlesAsync().ConfigureAwait(false);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open configuration editor");
            notificationService.ShowError("Configuration Editor", $"Failed to open configuration editor: {ex.Message}");
        }
    }

    private bool CanOpenConfigEditor() => IsProjectLoaded && !IsBuildRunning;

    /// <summary>
    /// Loads bundles from the current project configuration.
    /// </summary>
    private async Task LoadBundlesAsync()
    {
        if (CurrentProject?.Configuration == null)
        {
            return;
        }

        await InvokeOnUIThreadAsync(() =>
        {
            PopulateProjectBundlesAndProperties(CurrentProject.Configuration);
            logger.LogInformation("Loaded {Count} bundles", Bundles.Count);
        });
    }

    /// <summary>
    /// Closes the current project.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCloseProject))]
    private async Task CloseProjectAsync()
    {
        logger.LogInformation("CloseProjectAsync requested for: {Name}", CurrentProject?.Name);
        if (CurrentProject == null)
        {
            return;
        }

        _originalPackStates.Clear();
        await InvokeOnUIThreadAsync(() =>
        {
            CurrentProject = null;
            ProjectPath = string.Empty;
            ProjectName = string.Empty;
            IsProjectLoaded = false;
            Bundles.Clear();
            BuildLog.Clear();
            StatusMessage = ModBuilderConstants.ReadyStatus;
        }).ConfigureAwait(false);

        logger.LogInformation("Project closed successfully");
    }

    private bool CanCloseProject() => IsProjectLoaded && !IsBuildRunning;

    /// <summary>
    /// Adds a new bundle.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddBundle))]
    private async Task AddBundleAsync()
    {
        logger.LogInformation("AddBundleAsync requested");
        if (CurrentProject?.Configuration == null)
        {
            return;
        }

        await InvokeOnUIThreadAsync(() =>
        {
            var newBundle = new BundleItem
            {
                Name = $"Bundle{Bundles.Count + 1}",
                IsBig = true,
            };

            CurrentProject.Configuration.Items.Add(newBundle);

            var viewModel = new BundleItemViewModel
            {
                Name = newBundle.Name,
                IsSelected = true,
                IsBig = newBundle.IsBig,
            };

            Bundles.Add(viewModel);
            SelectedBundle = viewModel;
        });

        StatusMessage = "Bundle added";
    }

    private bool CanAddBundle() => IsProjectLoaded && !IsBuildRunning;

    /// <summary>
    /// Removes the selected bundle.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveBundle))]
    private async Task RemoveBundleAsync()
    {
        logger.LogInformation("RemoveBundleAsync requested for: {BundleName}", SelectedBundle?.Name);
        if (SelectedBundle == null || CurrentProject?.Configuration == null)
        {
            return;
        }

        await InvokeOnUIThreadAsync(() =>
        {
            var bundleToRemove = CurrentProject.Configuration.Items
                .FirstOrDefault(b => b.Name == SelectedBundle.Name);

            if (bundleToRemove != null)
            {
                CurrentProject.Configuration.Items.Remove(bundleToRemove);
            }

            Bundles.Remove(SelectedBundle);
            SelectedBundle = null;
        });

        StatusMessage = "Bundle removed";
    }

    private bool CanRemoveBundle() => IsProjectLoaded && SelectedBundle != null && !IsBuildRunning;

    /// <summary>
    /// Edits the selected bundle.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditBundle))]
    private async Task EditBundleAsync()
    {
        if (SelectedBundle == null)
        {
            return;
        }

        logger.LogInformation("Editing bundle: {BundleName}", SelectedBundle.Name);
        await Task.CompletedTask;
    }

    private bool CanEditBundle() => IsProjectLoaded && SelectedBundle != null && !IsBuildRunning;

    private BuildStep DetermineBuildSteps()
    {
        var buildSteps = BuildStep.None;
        if (CleanEnabled) buildSteps |= BuildStep.Clean;
        if (BuildEnabled) buildSteps |= BuildStep.Build;
        if (CreateManifestEnabled) buildSteps |= BuildStep.CreateManifest;
        if (ReleaseEnabled) buildSteps |= BuildStep.Release;
        return buildSteps;
    }

    private async Task<BuildConfiguration> PrepareBuildConfigurationAsync(CancellationToken cancellationToken)
    {
        var buildConfig = await EnsureProjectConfigurationLoadedAsync(cancellationToken).ConfigureAwait(false);
        ApplyResolvedGameDirectory(buildConfig);
        ApplyPackSelections(buildConfig);
        buildConfig.ZipCompressionLevel = SelectedCompressionLevel;
        return buildConfig;
    }

    private async Task<BuildConfiguration> EnsureProjectConfigurationLoadedAsync(CancellationToken cancellationToken)
    {
        var buildConfig = CurrentProject?.Configuration;
        var projectDir = GetEffectiveProjectDir();

        if ((buildConfig == null || buildConfig.Items.Count == 0) && !string.IsNullOrEmpty(projectDir))
        {
            buildConfig = await configurationLoaderService.LoadProjectConfigurationAsync(
                projectDir,
                cancellationToken).ConfigureAwait(false);
            if (CurrentProject != null)
            {
                CurrentProject.Configuration = buildConfig;
            }
        }

        return buildConfig ?? new BuildConfiguration();
    }

    private void ApplyResolvedGameDirectory(BuildConfiguration buildConfig)
    {
        var resolvedGameDir = ResolveGameDirectory(buildConfig);
        if (string.IsNullOrEmpty(resolvedGameDir))
        {
            return;
        }

        buildConfig.Folders.AbsGameDir = resolvedGameDir;
        if (CurrentProject != null && string.IsNullOrEmpty(CurrentProject.GameDir))
        {
            CurrentProject.GameDir = resolvedGameDir;
        }

        if (string.IsNullOrEmpty(GameDirectory))
        {
            GameDirectory = resolvedGameDir;
        }
    }

    private void ApplyPackSelections(BuildConfiguration buildConfig)
    {
        if (buildConfig.Packs == null)
        {
            return;
        }

        foreach (var pack in buildConfig.Packs)
        {
            var bundleVm = Bundles.FirstOrDefault(b => string.Equals(b.Name, pack.Name, StringComparison.OrdinalIgnoreCase));
            if (bundleVm != null)
            {
                pack.AllowBuild = bundleVm.IsSelected;
                pack.Big = bundleVm.IsBig;
            }
            else
            {
                pack.Big = SingleBigPackMode;
            }
        }
    }

    private async Task HandleBuildSuccessAsync(int filesProcessed, int bundlesCreated)
    {
        AppendBuildLog($"\n=== Build Completed Successfully in {LastBuildTime:mm\\:ss\\.fff} ===");

        await InvokeOnUIThreadAsync(() =>
        {
            ProcessedFiles = filesProcessed;
            PercentComplete = 100.0;
            if (filesProcessed == 0)
            {
                const string noFilesMessage = "Build completed but no files were processed.\n" +
                    "Check that:\n" +
                    "- Files exist in GameFilesEdited folder\n" +
                    "- Bundles are configured in config/ModBundleItems.json\n" +
                    "- File paths in config match actual files";
                notificationService.ShowInfo(
                    "Build Complete (No Files)",
                    noFilesMessage,
                    autoDismissMs: 8000);
            }
            else
            {
                var outputPath = CurrentProject != null
                    ? Path.Combine(CurrentProject.ProjectDir, CurrentProject.Directories?.Build ?? ModBuilderConstants.DefaultBuildDir)
                    : string.Empty;
                var summaryMessage = $"Processed {filesProcessed} files\n" +
                    $"Created {bundlesCreated} bundles\n" +
                    $"Time: {LastBuildTime:mm\\:ss}\n" +
                    $"Output: {outputPath}";
                notificationService.ShowSuccess(
                    "Build Complete",
                    summaryMessage);
            }
        });

        StatusMessage = "Build completed successfully";

        if (!string.IsNullOrEmpty(ProjectPath))
        {
            var updateResult = await projectConfigService.UpdateLastBuildTimeAsync(ProjectPath).ConfigureAwait(false);
            if (!updateResult.Success)
            {
                logger.LogWarning("Failed to update last build time for project {ProjectPath}: {Error}", ProjectPath, updateResult.FirstError);
            }
        }
    }

    /// <summary>
    /// Executes the build.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        if (CurrentProject == null)
        {
            notificationService.ShowWarning(ModBuilderConstants.NoProjectTitle, ModBuilderConstants.NoProjectMessage);
            return;
        }

        var fileCount = await CountFilesToBuildAsync(CancellationToken.None).ConfigureAwait(false);
        if (fileCount == 0)
        {
            await InvokeOnUIThreadAsync(() =>
            {
                const string warningMessage = "Your GameFilesEdited folder is empty or no bundles are configured.\n\n" +
                    "Steps:\n" +
                    "1. Click 'Open GameFilesEdited Folder'\n" +
                    "2. Copy game files to appropriate folders\n" +
                    "3. Edit config/ModBundleItems.json to configure bundles\n" +
                    "4. Try building again";
                notificationService.ShowWarning(
                    "No Files to Build",
                    warningMessage,
                    autoDismissMs: 10000);
            });
            AppendBuildLog("Build aborted: No files to build");
            return;
        }

        IsBuildRunning = true;
        _buildCancellationTokenSource = new CancellationTokenSource();
        _buildStopwatch.Restart();

        await InvokeOnUIThreadAsync(() =>
        {
            BuildLog.Clear();
            ProcessedFiles = 0;
            TotalFiles = fileCount;
            PercentComplete = 0;
            EstimatedTimeRemaining = null;
        });

        AppendBuildLog("=== Build Started ===");
        AppendBuildLog($"Files to process: {fileCount}");
        StatusMessage = "Building...";

        try
        {
            var buildConfig = await PrepareBuildConfigurationAsync(_buildCancellationTokenSource.Token).ConfigureAwait(false);
            var selectedPacks = GetResolvedSelectedPacks(buildConfig);

            var progress = new Progress<BuildProgress>(OnBuildProgress);

            var buildSteps = DetermineBuildSteps();
            logger.LogInformation("Build steps configured: {BuildSteps} (CreateManifestEnabled={CreateManifestEnabled})", buildSteps, CreateManifestEnabled);

            var result = await buildEngineService.ExecuteBuildAsync(
                CurrentProject,
                buildConfig,
                selectedPacks,
                buildSteps,
                progress,
                _buildCancellationTokenSource.Token).ConfigureAwait(false);

            _buildStopwatch.Stop();
            LastBuildTime = _buildStopwatch.Elapsed;

            if (result.Success)
            {
                var totalProcessed = result.FilesProcessed;
                var totalBundles = selectedPacks.Count;
                await HandleBuildSuccessAsync(totalProcessed, totalBundles).ConfigureAwait(false);
            }
            else
            {
                AppendBuildLog("\n=== Build Failed ===");
                AppendBuildLog(result.FirstError ?? ModBuilderConstants.UnknownError);
                notificationService.ShowError("Build Failed", result.FirstError ?? ModBuilderConstants.UnknownError);
                StatusMessage = "Build failed";
            }
        }
        catch (OperationCanceledException ex)
        {
            _buildStopwatch.Stop();
            logger.LogInformation(ex, "Build cancelled by user");
            AppendBuildLog("\n=== Build Cancelled ===");
            await InvokeOnUIThreadAsync(() => notificationService.ShowInfo("Build Cancelled", "Build operation was cancelled"));
            StatusMessage = "Build cancelled";
        }
        catch (Exception ex)
        {
            _buildStopwatch.Stop();
            logger.LogError(ex, "Build execution failed");
            AppendBuildLog("\n=== Build Error ===");
            AppendBuildLog(ex.Message);
            notificationService.ShowError("Build Error", ex.Message);
            StatusMessage = "Build error";
        }
        finally
        {
            IsBuildRunning = false;
            _buildCancellationTokenSource?.Dispose();
            _buildCancellationTokenSource = null;
        }
    }

    private List<string> GetResolvedSelectedPacks(BuildConfiguration buildConfig)
    {
        var selectedNames = Bundles.Where(b => b.IsSelected).Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolvedPacks = new List<string>();

        if (buildConfig.Packs is { Count: > 0 })
        {
            var matchingPacks = buildConfig.Packs
                .Where(pack => selectedNames.Contains(pack.Name) || (pack.ItemNames is { } itemNames && itemNames.Any(selectedNames.Contains)))
                .Select(pack => pack.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            resolvedPacks.AddRange(matchingPacks);
        }
        else
        {
            resolvedPacks.AddRange(selectedNames);
        }

        return resolvedPacks;
    }

    private bool CanBuild() => IsProjectLoaded && !IsBuildRunning;

    /// <summary>
    /// Stores built bundles in CAS and creates a local ContentManifest in the GenHub library.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateManifest))]
    private async Task CreateManifestAsync()
    {
        if (CurrentProject == null)
        {
            notificationService.ShowWarning(ModBuilderConstants.NoProjectTitle, ModBuilderConstants.NoProjectMessage);
            return;
        }

        IsBuildRunning = true;
        _buildCancellationTokenSource = new CancellationTokenSource();
        StatusMessage = "Creating ContentManifest...";

        AppendBuildLog("\n=== Creating Local ContentManifest ===");

        try
        {
            var buildConfig = await PrepareBuildConfigurationAsync(_buildCancellationTokenSource.Token).ConfigureAwait(false);
            var selectedPacks = GetResolvedSelectedPacks(buildConfig);

            var progress = new Progress<BuildProgress>(OnBuildProgress);

            var result = await buildEngineService.ExecuteBuildAsync(
                CurrentProject,
                buildConfig,
                selectedPacks,
                BuildStep.CreateManifest,
                progress,
                _buildCancellationTokenSource.Token).ConfigureAwait(false);

            if (result.Success)
            {
                AppendBuildLog("\n=== Manifest Created Successfully ===");
                await InvokeOnUIThreadAsync(() =>
                {
                    var typeName = CurrentProject.ContentType != ContentType.UnknownContentType
                        ? CurrentProject.ContentType.ToString().ToLowerInvariant()
                        : "mod";
                    notificationService.ShowSuccess(
                        "Manifest Created",
                        $"Local ContentManifest registered in GenHub for '{CurrentProject.Name}'. You can now enable this {typeName} in Game Profiles.");
                });
                StatusMessage = "Manifest created successfully";
            }
            else
            {
                AppendBuildLog("\n=== Manifest Creation Failed ===");
                AppendBuildLog(result.FirstError ?? ModBuilderConstants.UnknownError);
                await InvokeOnUIThreadAsync(() =>
                    notificationService.ShowError("Manifest Creation Failed", result.FirstError ?? "Failed to create manifest"));
                StatusMessage = "Manifest creation failed";
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Manifest creation cancelled by user");
            AppendBuildLog("\n=== Manifest Creation Cancelled ===");
            StatusMessage = "Manifest creation cancelled";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manifest creation failed");
            AppendBuildLog($"\n=== Manifest Creation Error: {ex.Message} ===");
            await InvokeOnUIThreadAsync(() =>
                notificationService.ShowError("Manifest Creation Error", ex.Message));
            StatusMessage = "Manifest creation error";
        }
        finally
        {
            IsBuildRunning = false;
            _buildCancellationTokenSource?.Dispose();
            _buildCancellationTokenSource = null;
        }
    }

    private bool CanCreateManifest() => IsProjectLoaded && !IsBuildRunning;

    private string ResolveGameDirectory(BuildConfiguration buildConfig)
    {
        if (!string.IsNullOrEmpty(buildConfig.Folders.AbsGameDir))
        {
            return buildConfig.Folders.AbsGameDir;
        }

        if (!string.IsNullOrEmpty(CurrentProject?.GameDir))
        {
            return CurrentProject.GameDir;
        }

        if (!string.IsNullOrEmpty(GameDirectory))
        {
            return GameDirectory;
        }

        var detectedGameDir = FileManager.SelectedInstallationPath ?? FileManager.AvailableInstallations.FirstOrDefault()?.Path;
        return detectedGameDir ?? string.Empty;
    }

    /// <summary>
    /// Counts total files to build.
    /// </summary>
    private async Task<int> CountFilesToBuildAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var projectDir = GetEffectiveProjectDir();
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            {
                return 0;
            }

            var editFolder = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir);
            if (Directory.Exists(editFolder))
            {
                var fileCount = await Task.Run(
                    () => Directory.EnumerateFiles(editFolder, "*.*", SearchOption.AllDirectories)
                        .Count(f => !Path.GetFileName(f).Equals("README.txt", StringComparison.OrdinalIgnoreCase)),
                    cancellationToken).ConfigureAwait(false);

                if (fileCount > 0)
                {
                    return fileCount;
                }
            }

            return Bundles.Sum(b => b.FileCount);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Counting files to build was cancelled");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to count files to build");
            return 0;
        }
    }

    /// <summary>
    /// Refreshes the file count.
    /// </summary>
    [RelayCommand]
    private async Task RefreshFileCountAsync(CancellationToken cancellationToken = default)
    {
        var count = await CountFilesToBuildAsync(cancellationToken).ConfigureAwait(false);
        PostToUIThread(() =>
        {
            FilesToBuildCount = count;
            StatusMessage = $"Files to build: {FilesToBuildCount}";
        });
    }

    /// <summary>
    /// Cleans the build output.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        logger.LogInformation("CleanAsync requested for project: {Name}", CurrentProject?.Name);
        if (CurrentProject == null)
        {
            return;
        }

        try
        {
            var projectDir = GetEffectiveProjectDir();
            var buildDir = CurrentProject?.Directories?.Build ?? ModBuilderConstants.DefaultBuildDir;
            string? buildPath = null;
            if (Path.IsPathRooted(buildDir))
            {
                buildPath = buildDir;
            }
            else if (!string.IsNullOrEmpty(projectDir))
            {
                buildPath = Path.Combine(projectDir, buildDir);
            }

            if (!string.IsNullOrEmpty(buildPath) && Directory.Exists(buildPath))
            {
                await Task.Run(() => Directory.Delete(buildPath, recursive: true), CancellationToken.None).ConfigureAwait(false);
                AppendBuildLog($"Cleaned build directory: {buildPath}");
                notificationService.ShowSuccess("Clean Complete", "Build directory cleaned");
                StatusMessage = "Build directory cleaned";
                logger.LogInformation("Cleaned build directory: {Dir}", buildPath);
            }

            buildEngineService.InvalidateBuildStructureCache();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clean build directory");
            notificationService.ShowError("Clean Failed", ex.Message);
        }
    }

    private bool CanClean() => IsProjectLoaded && !IsBuildRunning;

    /// <summary>
    /// Aborts the current build.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAbortBuild))]
    private void AbortBuild()
    {
        logger.LogInformation("AbortBuild requested");
        _buildCancellationTokenSource?.Cancel();
        AppendBuildLog("\nAborting build...");
        StatusMessage = "Aborting build...";
    }

    private bool CanAbortBuild() => IsBuildRunning;

    /// <summary>
    /// Opens the project folder in file explorer.
    /// </summary>
    [RelayCommand]
    private void OpenProjectFolder()
    {
        logger.LogInformation("OpenProjectFolder requested for: {Path}", ProjectPath);
        var projectDir = !string.IsNullOrEmpty(ProjectPath) ? Path.GetDirectoryName(ProjectPath) : CurrentProject?.ProjectDir;
        if (string.IsNullOrEmpty(projectDir))
        {
            notificationService.ShowWarning(ModBuilderConstants.NoProjectTitle, ModBuilderConstants.NoProjectMessage);
            return;
        }

        try
        {
            if (!Directory.Exists(projectDir))
            {
                Directory.CreateDirectory(projectDir);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = projectDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open project folder");
            notificationService.ShowError("Open Failed", "Could not open project folder");
        }
    }

    /// <summary>
    /// Opens the GameFilesEdited folder in file explorer.
    /// </summary>
    [RelayCommand]
    private void OpenEditFolder()
    {
        logger.LogInformation("OpenEditFolder requested for project: {Path}", ProjectPath);
        var projectDir = !string.IsNullOrEmpty(ProjectPath) ? Path.GetDirectoryName(ProjectPath) : CurrentProject?.ProjectDir;
        if (string.IsNullOrEmpty(projectDir))
        {
            notificationService.ShowWarning(ModBuilderConstants.NoProjectTitle, ModBuilderConstants.NoProjectMessage);
            return;
        }

        try
        {
            var editedDir = CurrentProject?.Directories?.GameFilesEdited ?? ModBuilderConstants.GameFilesEditedDir;
            var editFolder = Path.IsPathRooted(editedDir) ? editedDir : Path.Combine(projectDir, editedDir);
            if (IsPathInsideAppDirectory(editFolder))
            {
                logger.LogWarning("Refusing to open edit folder inside app directory: {Path}", editFolder);
                notificationService.ShowWarning("Folder Restricted", "Cannot open folder located inside application installation directory.");
                return;
            }

            if (!Directory.Exists(editFolder))
            {
                Directory.CreateDirectory(editFolder);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = editFolder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open edit folder");
            notificationService.ShowError("Open Failed", "Could not open GameFilesEdited folder");
        }
    }

    /// <summary>
    /// Opens the build folder in file explorer.
    /// </summary>
    [RelayCommand]
    private void OpenBuildFolder()
    {
        logger.LogInformation("OpenBuildFolder requested for: {Path}", ProjectPath);
        if (CurrentProject == null || string.IsNullOrEmpty(ProjectPath))
        {
            notificationService.ShowWarning(ModBuilderConstants.NoProjectTitle, ModBuilderConstants.NoProjectMessage);
            return;
        }

        try
        {
            var projectDir = Path.GetDirectoryName(ProjectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                return;
            }

            var buildDir = CurrentProject?.Directories?.Build ?? ModBuilderConstants.DefaultBuildDir;
            var buildPath = Path.IsPathRooted(buildDir) ? buildDir : Path.Combine(projectDir, buildDir);
            if (IsPathInsideAppDirectory(buildPath))
            {
                logger.LogWarning("Refusing to open build folder inside app directory: {Path}", buildPath);
                notificationService.ShowWarning("Folder Restricted", "Cannot open build folder located inside application installation directory.");
                return;
            }

            if (!Directory.Exists(buildPath))
            {
                Directory.CreateDirectory(buildPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = buildPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open build folder");
            notificationService.ShowError("Open Failed", "Could not open build folder");
        }
    }

    /// <summary>
    /// Opens the release folder in file explorer.
    /// </summary>
    [RelayCommand]
    private void OpenReleaseFolder()
    {
        logger.LogInformation("OpenReleaseFolder requested for: {Path}", ProjectPath);
        if (CurrentProject == null)
        {
            return;
        }

        var projectDir = GetEffectiveProjectDir();
        if (string.IsNullOrEmpty(projectDir))
        {
            return;
        }

        try
        {
            var releaseDir = ModBuilderConstants.DefaultReleaseDir;
            if (!string.IsNullOrWhiteSpace(CurrentProject.Directories?.Release))
            {
                var configuredRelease = CurrentProject.Directories.Release.Trim();
                if (configuredRelease.EndsWith($"/{CurrentProject.Name}", StringComparison.OrdinalIgnoreCase) ||
                    configuredRelease.EndsWith($"\\{CurrentProject.Name}", StringComparison.OrdinalIgnoreCase))
                {
                    configuredRelease = Path.GetDirectoryName(configuredRelease) ?? ModBuilderConstants.DefaultReleaseDir;
                }

                releaseDir = configuredRelease;
            }

            var releasePath = Path.IsPathRooted(releaseDir) ? releaseDir : Path.Combine(projectDir, releaseDir);
            if (IsPathInsideAppDirectory(releasePath))
            {
                logger.LogWarning("Refusing to open release folder inside app directory: {Path}", releasePath);
                notificationService.ShowWarning("Folder Restricted", "Cannot open release folder located inside application installation directory.");
                return;
            }

            if (!Directory.Exists(releasePath))
            {
                Directory.CreateDirectory(releasePath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = releasePath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open release folder");
            notificationService.ShowError("Open Folder Failed", $"Failed to open release folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the build output log.
    /// </summary>
    [RelayCommand]
    private void ClearOutput()
    {
        logger.LogInformation("ClearOutput requested");
        PostToUIThread(() =>
        {
            BuildLog.Clear();
            _buildOutputBuilder.Clear();
            OnPropertyChanged(nameof(BuildOutput));
        });
        StatusMessage = "Build output cleared";
    }

    /// <summary>
    /// Loads project data (bundles, configuration, etc.).
    /// </summary>
    private async Task LoadProjectDataAsync()
    {
        if (CurrentProject == null)
        {
            return;
        }

        try
        {
            var projectDir = GetEffectiveProjectDir();

            if (!string.IsNullOrEmpty(projectDir) && CurrentProject != null)
            {
                var loadedConfig = await configurationLoaderService.LoadProjectConfigurationAsync(
                    projectDir,
                    CancellationToken.None).ConfigureAwait(false);
                if (CurrentProject != null)
                {
                    CurrentProject.Configuration = loadedConfig;
                }
            }

            await InvokeOnUIThreadAsync(() => PopulateProjectBundlesAndProperties(CurrentProject?.Configuration)).ConfigureAwait(false);

            var countedFiles = await CountFilesToBuildAsync(CancellationToken.None).ConfigureAwait(false);
            if (countedFiles > 0)
            {
                await InvokeOnUIThreadAsync(() =>
                {
                    FilesToBuildCount = countedFiles;
                    FileCount = countedFiles;
                    StatusMessage = $"Project loaded: {ProjectName} ({FilesToBuildCount} files to build)";
                }).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(projectDir))
            {
                await InitializeFileManagerAndGameDirectoryAsync(projectDir).ConfigureAwait(false);
            }

            PostToUIThread(NotifyAllProjectCommands);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load project data");
            notificationService.ShowError("Load Error", $"Failed to load project data: {ex.Message}");
        }
    }

    private void PopulateProjectBundlesAndProperties(BuildConfiguration? config)
    {
        _originalPackStates.Clear();
        Bundles.Clear();

        if (config?.Packs != null && config.Packs.Count > 0)
        {
            var anyBig = false;
            foreach (var pack in config.Packs)
            {
                if (pack.IsBigPack)
                {
                    anyBig = true;
                }

                Bundles.Add(new BundleItemViewModel
                {
                    Name = pack.Name,
                    IsSelected = pack.AllowBuild,
                    IsBig = pack.IsBigPack,
                    FileCount = pack.ItemNames?.Count ?? 0,
                });
            }

            try
            {
                _isPopulatingBundles = true;
                SingleBigPackMode = anyBig;
            }
            finally
            {
                _isPopulatingBundles = false;
            }
        }
        else if (config?.Items != null)
        {
            foreach (var item in config.Items)
            {
                Bundles.Add(new BundleItemViewModel
                {
                    Name = item.Name,
                    IsSelected = true,
                    IsBig = item.IsBig,
                    FileCount = item.Files?.Count ?? 0,
                });
            }
        }

        if (CurrentProject != null)
        {
            GameDirectory = CurrentProject.GameDir;
            OutputDirectory = CurrentProject.Directories?.Build ?? ModBuilderConstants.DefaultBuildDir;
        }

        if (config != null)
        {
            SelectedCompressionLevel = config.ZipCompressionLevel;
        }

        FileCount = Bundles.Sum(b => b.FileCount);
        FilesToBuildCount = FileCount;
    }

    private async Task InitializeFileManagerAndGameDirectoryAsync(string projectDir)
    {
        fileManager.ImportBigFilesRequested -= ImportBigFilesAsync;
        fileManager.ImportBigFilesRequested += ImportBigFilesAsync;

        var editedDir = CurrentProject?.Directories?.GameFilesEdited ?? ModBuilderConstants.GameFilesEditedDir;
        await FileManager.InitializeAsync(projectDir, editedDir, CancellationToken.None).ConfigureAwait(false);

        if (CurrentProject != null && string.IsNullOrEmpty(CurrentProject.GameDir))
        {
            var fallbackGameDir = FileManager.SelectedInstallationPath ?? FileManager.AvailableInstallations.FirstOrDefault()?.Path;
            if (!string.IsNullOrEmpty(fallbackGameDir))
            {
                CurrentProject.GameDir = fallbackGameDir;
                await InvokeOnUIThreadAsync(() => GameDirectory = fallbackGameDir);
            }
        }
    }

    private void NotifyAllProjectCommands()
    {
        SaveProjectCommand.NotifyCanExecuteChanged();
        CloseProjectCommand.NotifyCanExecuteChanged();
        BuildCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        CreateManifestCommand.NotifyCanExecuteChanged();
        AddBundleCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Appends a message to the build log.
    /// </summary>
    private void AppendBuildLog(string message)
    {
        PostToUIThread(() =>
        {
            var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
            var line = $"[{timestamp}] {message}";
            BuildLog.Add(line);
            if (_buildOutputBuilder.Length > 0)
            {
                _buildOutputBuilder.AppendLine();
            }

            _buildOutputBuilder.Append(line);
            OnPropertyChanged(nameof(BuildOutput));
        });
    }

    /// <summary>
    /// Handles build progress updates.
    /// </summary>
    private void OnBuildProgress(BuildProgress progress)
    {
        PostToUIThread(() =>
        {
            BuildProgress = progress;
            BuildStage = progress.CurrentStage.ToString();
            CurrentFile = progress.CurrentFile;
            ProcessedFiles = progress.ProcessedFiles;
            TotalFiles = progress.TotalFiles;
            PercentComplete = progress.PercentComplete;
            EstimatedTimeRemaining = progress.EstimatedTimeRemaining;

            if (!string.IsNullOrEmpty(progress.CurrentFile))
            {
                AppendBuildLog($"{progress.CurrentStage}: {progress.CurrentFile}");
            }
            else if (!string.IsNullOrEmpty(progress.CurrentStep))
            {
                AppendBuildLog(progress.CurrentStep);
            }
        });
    }

    partial void OnIsBuildRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBuilding));

        PostToUIThread(() =>
        {
            OpenFileManagerCommand.NotifyCanExecuteChanged();
            OpenConfigEditorCommand.NotifyCanExecuteChanged();
            SaveProjectCommand.NotifyCanExecuteChanged();
            BuildCommand.NotifyCanExecuteChanged();
            CleanCommand.NotifyCanExecuteChanged();
            CreateManifestCommand.NotifyCanExecuteChanged();
            AbortBuildCommand.NotifyCanExecuteChanged();
            CloseProjectCommand.NotifyCanExecuteChanged();
            AddBundleCommand.NotifyCanExecuteChanged();
            RemoveBundleCommand.NotifyCanExecuteChanged();
            EditBundleCommand.NotifyCanExecuteChanged();
        });
    }

    partial void OnPercentCompleteChanged(double value)
    {
        OnPropertyChanged(nameof(ProgressText));
    }

    partial void OnBuildStageChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentStage));
        BuildStatus = string.IsNullOrEmpty(value) ? ModBuilderConstants.ReadyStatus : value;
    }

    partial void OnProjectPathChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentProjectPath));
    }

    partial void OnCurrentProjectChanged(ModBuilderProject? value)
    {
        IsProjectLoaded = value != null;

        if (value != null)
        {
            SelectedTargetGame = value.TargetGame;
            SelectedContentType = value.ContentType != ContentType.UnknownContentType
                ? value.ContentType
                : ContentType.Mod;
        }

        // Dispatch UI updates to UI thread
        PostToUIThread(() =>
        {
            OpenFileManagerCommand.NotifyCanExecuteChanged();
            OpenConfigEditorCommand.NotifyCanExecuteChanged();
            SaveProjectCommand.NotifyCanExecuteChanged();
            CloseProjectCommand.NotifyCanExecuteChanged();
            BuildCommand.NotifyCanExecuteChanged();
            CleanCommand.NotifyCanExecuteChanged();
            CreateManifestCommand.NotifyCanExecuteChanged();
            AddBundleCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CurrentProjectPath));
            OnPropertyChanged(nameof(IsProjectLoaded));
        });
    }

    partial void OnSelectedBundleChanged(BundleItemViewModel? value)
    {
        PostToUIThread(() =>
        {
            RemoveBundleCommand.NotifyCanExecuteChanged();
            EditBundleCommand.NotifyCanExecuteChanged();
        });
    }

    private static async Task InvokeOnUIThreadAsync(Action action)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            await Task.CompletedTask;
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    private static async Task InvokeOnUIThreadAsync(Func<Task> action)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            await action().ConfigureAwait(false);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action).ConfigureAwait(false);
        }
    }

    private static void PostToUIThread(Action action)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private void ApplyProjectFilter()
    {
        RecentProjects.Clear();
        var query = SearchQuery?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(query)
            ? _allRecentProjects
            : _allRecentProjects.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Path.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var project in filtered)
        {
            RecentProjects.Add(project);
        }

        OnPropertyChanged(nameof(HasRecentProjects));
        OnPropertyChanged(nameof(TotalProjects));
    }

    private void UpdatePacksForSingleBigMode(IEnumerable<BundlePack>? packs, bool singleBigMode)
    {
        if (packs == null)
        {
            return;
        }

        foreach (var pack in packs)
        {
            ApplyPackSingleBigMode(pack, singleBigMode, _originalPackStates);
        }
    }

    private static void ApplyPackSingleBigMode(
        BundlePack pack,
        bool singleBigMode,
        IDictionary<string, (bool? Big, string? OutputFile)>? originalStates = null)
    {
        if (singleBigMode)
        {
            if (pack.Big != false)
            {
                if (originalStates != null && !originalStates.ContainsKey(pack.Name))
                {
                    originalStates[pack.Name] = (pack.Big, pack.OutputFile);
                }

                pack.Big = true;
                pack.OutputFile = ReplaceExtension(pack.OutputFile, ".zip", ".big");
            }
        }
        else
        {
            if (pack.Big is false)
            {
                return;
            }

            if (originalStates is not null && originalStates.TryGetValue(pack.Name, out var original))
            {
                originalStates.Remove(pack.Name);
                pack.Big = original.Big;
                pack.OutputFile = original.OutputFile;
            }
            else if (pack.IsBigPack)
            {
                pack.Big = null;
                pack.OutputFile = ReplaceExtension(pack.OutputFile, ".big", ".zip");
            }
        }
    }

    private static string? ReplaceExtension(string? filePath, string oldExt, string newExt)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && filePath.EndsWith(oldExt, StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(filePath, newExt).Replace('\\', '/');
        }

        return filePath;
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    /// <param name="disposing">Whether called from Dispose().</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            fileManager.ImportBigFilesRequested -= ImportBigFilesAsync;

            _buildCancellationTokenSource?.Cancel();
            _buildCancellationTokenSource?.Dispose();
            _buildCancellationTokenSource = null;

            _importCancellationTokenSource?.Cancel();
            _importCancellationTokenSource?.Dispose();
            _importCancellationTokenSource = null;
        }

        _disposed = true;
    }
}
