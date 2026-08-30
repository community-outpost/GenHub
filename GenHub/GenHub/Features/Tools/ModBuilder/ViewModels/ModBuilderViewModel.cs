using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for ModBuilder tool with complete build pipeline integration.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarCloud", "S107:Methods should not have too many parameters", Justification = "ViewModel requires multiple injected services")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarCloud", "S2325:Methods and properties that don't access instance data should be static", Justification = "RelayCommand and XAML bindings require instance members")]
public partial class ModBuilderViewModel : ObservableObject, IDisposable
{
    private const string ModBuilderLiteral = "ModBuilder";
    private const string BasicModLiteral = "BasicMod";
    private const string BasicModProjectFileLiteral = "BasicMod.mbproj";
    private const string SampleProjectsDirLiteral = "SampleProjects";
    private const string NoProjectTitle = "No Project";
    private const string NoProjectMessage = "Please load or create a project first";
    private const string ReadyStatusLiteral = "Ready";
    private const string UnknownErrorLiteral = "Unknown error";

    private readonly IBuildEngineService _buildEngineService;
    private readonly IProjectConfigService _projectConfigService;
    private readonly IConfigurationLoaderService _configurationLoaderService;
    private readonly IProjectStructureGenerator _projectStructureGenerator;
    private readonly INotificationService _notificationService;
    private readonly IDialogService? _dialogService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ModBuilderViewModel> _logger;
    private readonly Stopwatch _buildStopwatch = new();
    private CancellationTokenSource? _buildCancellationTokenSource;

    /// <summary>
    /// Gets the file manager view model.
    /// </summary>
    public FileManagerViewModel FileManager { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModBuilderViewModel"/> class.
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
    public ModBuilderViewModel(
        IBuildEngineService buildEngineService,
        IProjectConfigService projectConfigService,
        IConfigurationLoaderService configurationLoaderService,
        IProjectStructureGenerator projectStructureGenerator,
        INotificationService notificationService,
        FileManagerViewModel fileManager,
        ILoggerFactory loggerFactory,
        ILogger<ModBuilderViewModel> logger,
        IDialogService? dialogService = null)
    {
        _buildEngineService = buildEngineService;
        _projectConfigService = projectConfigService;
        _configurationLoaderService = configurationLoaderService;
        _projectStructureGenerator = projectStructureGenerator;
        _notificationService = notificationService;
        FileManager = fileManager;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _dialogService = dialogService;

        // Initialize compression levels
        CompressionLevels.Add(CompressionLevel.NoCompression);
        CompressionLevels.Add(CompressionLevel.Fastest);
        CompressionLevels.Add(CompressionLevel.Optimal);
        CompressionLevels.Add(CompressionLevel.SmallestSize);
        SelectedCompressionLevel = CompressionLevel.Fastest;

        // Initialize build configurations
        BuildConfigurations.Add("Debug");
        BuildConfigurations.Add("Release");
        SelectedConfiguration = "Debug";
    }

    /// <summary>
    /// Gets or sets the current project.
    /// </summary>
    [ObservableProperty]
    private ModBuilderProject? _currentProject;

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

    private readonly List<RecentProjectInfo> _allRecentProjects = [];

    /// <summary>
    /// Gets or sets a value indicating whether the quick start guide is visible.
    /// </summary>
    [ObservableProperty]
    private bool _showQuickStartGuide = true;

    /// <summary>
    /// Dismisses the quick start guide.
    /// </summary>
    [RelayCommand]
    private void DismissQuickStartGuide()
    {
        ShowQuickStartGuide = false;
    }

    /// <summary>
    /// Gets or sets the search query for filtering projects.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        ApplyProjectFilter();
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
    public ObservableCollection<string> BuildConfigurations { get; } = [];

    /// <summary>
    /// Gets or sets the selected configuration.
    /// </summary>
    [ObservableProperty]
    private string _selectedConfiguration = "Debug";

    /// <summary>
    /// Gets the list of compression levels.
    /// </summary>
    public ObservableCollection<CompressionLevel> CompressionLevels { get; } = [];

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
    public string BuildOutput => string.Join(Environment.NewLine, BuildLog);

    /// <summary>
    /// Gets or sets the build status text.
    /// </summary>
    [ObservableProperty]
    private string _buildStatus = ReadyStatusLiteral;

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
    private bool _releaseEnabled;

    /// <summary>
    /// Gets or sets a value indicating whether install action is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _installEnabled;

    /// <summary>
    /// Gets or sets a value indicating whether run game action is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _runGameEnabled;

    /// <summary>
    /// Gets or sets a value indicating whether uninstall action is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _uninstallEnabled;

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
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = ReadyStatusLiteral;

    /// <summary>
    /// Gets or sets the status text for the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusText = ReadyStatusLiteral;

    /// <summary>
    /// Gets or sets the status color for the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusColor = "#10FFFFFF";

    /// <summary>
    /// Gets or sets the status text color for the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusTextColor = "White";

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
            var result = await _projectConfigService.GetRecentProjectsAsync(10, CancellationToken.None).ConfigureAwait(false);
            if (result.Success && result.Data != null)
            {
                var projectInfos = result.Data.Select(CreateRecentProjectInfo).ToList();

                await InvokeOnUIThreadAsync(() =>
                {
                    _allRecentProjects.Clear();
                    _allRecentProjects.AddRange(projectInfos);
                    ApplyProjectFilter();
                });

                _logger.LogInformation("Loaded {Count} recent projects", projectInfos.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recent projects");
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
            name = "Untitled Project";
        }

        DateTime? lastWriteTime = null;
        try
        {
            if (File.Exists(path))
            {
                lastWriteTime = File.GetLastWriteTime(path);
            }
            else if (Directory.Exists(path))
            {
                lastWriteTime = Directory.GetLastWriteTime(path);
            }
        }
        catch (Exception)
        {
            // Ignore I/O errors reading timestamp
        }

        return new RecentProjectInfo
        {
            Name = name,
            Path = path,
            LastBuildTime = lastWriteTime,
            Version = "1.0.0",
        };
    }

    /// <summary>
    /// Creates a new project.
    /// </summary>
    [RelayCommand]
    private async Task NewProjectAsync()
    {
        _logger.LogInformation("NewProjectAsync requested");
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(lifetime?.MainWindow);
        if (topLevel == null)
        {
            return;
        }

        var defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ModBuilder");
        if (!Directory.Exists(defaultFolder))
        {
            try
            {
                Directory.CreateDirectory(defaultFolder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create default ModBuilder directory at {Folder}", defaultFolder);
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
                new FilePickerFileType("ModBuilder Project") { Patterns = ["*.mbproj",], }
            ],
        }).ConfigureAwait(false);

        if (file != null)
        {
            var projectPath = file.Path.LocalPath;

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                _notificationService.ShowWarning(
                    "Invalid Path",
                    "Please select a valid project location");
                return;
            }

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            _logger.LogInformation("Creating new project '{ProjectName}' at {ProjectPath}", projectName, projectPath);

            try
            {
                var result = await _projectConfigService.CreateProjectAsync(
                    projectPath,
                    projectName,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                if (result.Success && result.Data != null)
                {
                    CurrentProject = result.Data;
                    ProjectPath = projectPath;
                    ProjectName = projectName;
                    IsProjectLoaded = true;

                    // Generate complete project structure
                    await _projectStructureGenerator.GenerateProjectStructureAsync(
                        projectPath,
                        CancellationToken.None).ConfigureAwait(false);

                    await LoadProjectDataAsync().ConfigureAwait(false);
                    await _projectConfigService.AddToRecentProjectsAsync(projectPath, CancellationToken.None).ConfigureAwait(false);
                    await LoadRecentProjectsAsync().ConfigureAwait(false);

                    _notificationService.ShowSuccess(
                        "Project Created",
                        $"Created project: {projectName}\nProject structure ready. Edit files in GameFilesEdited folder.");
                    AppendBuildLog($"Created new project: {projectPath}");
                    AppendBuildLog("Generated project structure with folders and config files");
                    _logger.LogInformation("Project created successfully at {ProjectPath}", projectPath);
                }
                else
                {
                    _notificationService.ShowError("Creation Failed", result.FirstError ?? UnknownErrorLiteral);
                    _logger.LogWarning("Project creation failed: {Error}", result.FirstError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create project at {ProjectPath}", projectPath);
                _notificationService.ShowError("Creation Error", ex.Message);
            }
        }
    }

    /// <summary>
    /// Opens an existing project.
    /// </summary>
    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        _logger.LogInformation("OpenProjectAsync requested");
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(lifetime?.MainWindow);
        if (topLevel == null)
        {
            return;
        }

        var defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ModBuilder");
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
                new FilePickerFileType("ModBuilder Project") { Patterns = ["*.mbproj",], }
            ],
        }).ConfigureAwait(false);

        if (files.Any())
        {
            _logger.LogInformation("Selected project to open: {Path}", files[0].Path.LocalPath);
            await LoadProjectFromPathAsync(files[0].Path.LocalPath).ConfigureAwait(false);
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

        _logger.LogInformation("OpenRecentProjectAsync requested for: {Path}", path);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _notificationService.ShowWarning("Project Not Found", $"Could not find project file at: {path}");
            return;
        }

        await LoadProjectFromPathAsync(path).ConfigureAwait(false);
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

        await _projectConfigService.RemoveFromRecentProjectsAsync(path, CancellationToken.None).ConfigureAwait(false);
        await LoadRecentProjectsAsync().ConfigureAwait(false);
        _notificationService.ShowInfo("Project Removed", $"Removed '{name}' from recent projects.");
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

        var confirmed = _dialogService == null || await _dialogService.ShowConfirmationAsync(
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
            var projectDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(projectDir) && Directory.Exists(projectDir))
            {
                Directory.Delete(projectDir, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }

            await _projectConfigService.RemoveFromRecentProjectsAsync(path, CancellationToken.None).ConfigureAwait(false);

            if (ProjectPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                await CloseProjectAsync().ConfigureAwait(false);
            }

            await LoadRecentProjectsAsync().ConfigureAwait(false);
            _notificationService.ShowSuccess("Project Deleted", $"Successfully deleted '{name}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete project at {Path}", path);
            _notificationService.ShowError("Delete Failed", $"Failed to delete project: {ex.Message}");
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
            "Generals" => ("avares://GenHub/Assets/Icons/generals-icon.png", "Generals"),
            "ZeroHour" => ("avares://GenHub/Assets/Icons/zerohour-icon.png", "Zero Hour"),
            _ => (string.Empty, string.Empty)
        };
    }

    /// <summary>
    /// Loads the sample project for testing.
    /// </summary>
    [RelayCommand]
    private async Task LoadSampleProjectAsync()
    {
        _logger.LogInformation("LoadSampleProjectAsync requested");
        try
        {
            var samplePath = await ResolveSampleProjectPathAsync().ConfigureAwait(false);

            if (string.IsNullOrEmpty(samplePath))
            {
                _notificationService.ShowWarning(
                    "Sample Not Found",
                    "Sample project not found and could not be created automatically.");
                AppendBuildLog("Sample project not found in search paths and could not be created.");
                return;
            }

            _logger.LogInformation("Found sample project at: {SamplePath}", samplePath);

            var sampleDir = Path.GetDirectoryName(samplePath);
            if (!string.IsNullOrEmpty(sampleDir))
            {
                await EnsureSampleTgaExistsAsync(sampleDir).ConfigureAwait(false);
            }

            await LoadProjectFromPathAsync(samplePath).ConfigureAwait(false);

            _notificationService.ShowSuccess(
                "Sample Loaded",
                "Sample project loaded. Click 'Build' to test ModBuilder.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sample project");
            _notificationService.ShowError("Load Failed", $"Failed to load sample project: {ex.Message}");
        }
    }

    private async Task<string?> ResolveSampleProjectPathAsync()
    {
        var candidatePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SampleProjectsDirLiteral, ModBuilderLiteral, BasicModLiteral, BasicModProjectFileLiteral),
            Path.Combine(AppContext.BaseDirectory, SampleProjectsDirLiteral, ModBuilderLiteral, BasicModLiteral, BasicModProjectFileLiteral),
            Path.Combine(Directory.GetCurrentDirectory(), SampleProjectsDirLiteral, ModBuilderLiteral, BasicModLiteral, BasicModProjectFileLiteral),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", SampleProjectsDirLiteral, ModBuilderLiteral, BasicModLiteral, BasicModProjectFileLiteral)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", SampleProjectsDirLiteral, ModBuilderLiteral, BasicModLiteral, BasicModProjectFileLiteral)),
        };

        var samplePath = candidatePaths.FirstOrDefault(File.Exists);
        if (samplePath != null)
        {
            return samplePath;
        }

        var defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ModBuilderLiteral,
            BasicModLiteral);
        Directory.CreateDirectory(defaultFolder);
        var generatedPath = Path.Combine(defaultFolder, BasicModProjectFileLiteral);

        var createResult = await _projectConfigService.CreateProjectAsync(
            generatedPath,
            BasicModLiteral,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        if (createResult.Success)
        {
            await _projectStructureGenerator.GenerateProjectStructureAsync(generatedPath, CancellationToken.None).ConfigureAwait(false);
            return generatedPath;
        }

        return null;
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
        _logger.LogInformation("OpenFileManagerAsync requested");
        if (CurrentProject == null)
        {
            _notificationService.ShowWarning(NoProjectTitle, NoProjectMessage);
            return;
        }

        try
        {
            var projectDir = GetEffectiveProjectDir();
            if (!string.IsNullOrEmpty(projectDir))
            {
                await FileManager.InitializeAsync(projectDir, CancellationToken.None).ConfigureAwait(false);
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

                await RefreshFileCountAsync();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open File Manager dialog");
            _notificationService.ShowError("File Manager Error", ex.Message);
        }
    }

    private bool CanOpenFileManager() => CurrentProject != null && !IsBuildRunning;

    /// <summary>
    /// Ensures the sample TGA file exists by creating it if needed.
    /// </summary>
    private static async Task EnsureSampleTgaExistsAsync(string projectRoot)
    {
        var tgaPath = Path.Combine(projectRoot, "GameFilesEdited", "Art", "Textures", "sample.tga");

        if (File.Exists(tgaPath))
        {
            var fileInfo = new FileInfo(tgaPath);
            if (fileInfo.Length > 100) // Already a valid TGA
            {
                return;
            }
        }

        // Create a simple 64x64 gradient TGA using ImageSharp
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(64, 64);

        // Create gradient pattern
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                byte r = (byte)((x / 64.0) * 255);
                byte g = (byte)((y / 64.0) * 255);
                byte b = 128;
                byte a = 255;
                image[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(r, g, b, a);
            }
        }

        var tgaDir = Path.GetDirectoryName(tgaPath);
        if (!string.IsNullOrEmpty(tgaDir))
        {
            Directory.CreateDirectory(tgaDir);
        }

        using var fileStream = File.Create(tgaPath);
        await image.SaveAsync(fileStream, new SixLabors.ImageSharp.Formats.Tga.TgaEncoder()).ConfigureAwait(false);
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
                _notificationService.ShowError("Invalid Path", "Project path cannot be empty");
                return;
            }

            if (!File.Exists(projectPath))
            {
                _notificationService.ShowError("File Not Found", $"Project file does not exist: {projectPath}");
                return;
            }

            var result = await _projectConfigService.LoadProjectAsync(
                projectPath,
                validateIntegrity: true,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            if (result.Success && result.Data != null)
            {
                CurrentProject = result.Data;
                ProjectPath = projectPath;
                ProjectName = result.Data.Name;
                IsProjectLoaded = true;
                ShowQuickStartGuide = true;

                await LoadProjectDataAsync().ConfigureAwait(false);
                await _projectConfigService.AddToRecentProjectsAsync(projectPath, CancellationToken.None).ConfigureAwait(false);

                _notificationService.ShowSuccess("Project Loaded", $"Loaded: {Path.GetFileName(projectPath)}");
                AppendBuildLog($"Loaded project: {projectPath}");
                StatusMessage = $"Project loaded: {ProjectName}";
            }
            else
            {
                var errorMessage = result.FirstError ?? "Unknown error occurred while loading project";
                _notificationService.ShowError("Load Failed", errorMessage);
                AppendBuildLog($"Failed to load project: {errorMessage}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied loading project");
            _notificationService.ShowError("Access Denied", "You don't have permission to access this project file");
            AppendBuildLog($"Access denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error loading project");
            _notificationService.ShowError("File Error", "Could not read project file. It may be in use by another program.");
            AppendBuildLog($"I/O error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load project");
            _notificationService.ShowError("Load Error", $"Unexpected error: {ex.Message}");
            AppendBuildLog($"Error loading project: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the current project.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private async Task SaveProjectAsync()
    {
        _logger.LogInformation("SaveProjectAsync requested for: {Path}", ProjectPath);
        if (CurrentProject == null || string.IsNullOrEmpty(ProjectPath))
        {
            return;
        }

        try
        {
            // Update compression level in configuration
            if (CurrentProject.Configuration != null)
            {
                CurrentProject.Configuration.ZipCompressionLevel = SelectedCompressionLevel;
            }

            var result = await _projectConfigService.SaveProjectAsync(
                ProjectPath,
                CurrentProject,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            if (result.Success)
            {
                _notificationService.ShowSuccess("Project Saved", "Project saved successfully");
                AppendBuildLog($"Saved project: {ProjectPath}");
                StatusMessage = "Project saved";
                _logger.LogInformation("Project saved successfully to {Path}", ProjectPath);
            }
            else
            {
                _notificationService.ShowError("Save Failed", result.FirstError ?? UnknownErrorLiteral);
                _logger.LogWarning("Failed to save project: {Error}", result.FirstError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save project");
            _notificationService.ShowError("Save Error", ex.Message);
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
            _notificationService.ShowWarning(NoProjectTitle, NoProjectMessage);
            return;
        }

        try
        {
            var configEditorViewModel = new ConfigEditorViewModel(
                _configurationLoaderService,
                _notificationService,
                _loggerFactory.CreateLogger<ConfigEditorViewModel>());

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
            _logger.LogError(ex, "Failed to open configuration editor");
            _notificationService.ShowError("Configuration Editor", $"Failed to open configuration editor: {ex.Message}");
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
            Bundles.Clear();

            // Load bundles from configuration
            if (CurrentProject.Configuration?.Items != null)
            {
                foreach (var item in CurrentProject.Configuration.Items)
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

            _logger.LogInformation("Loaded {Count} bundles", Bundles.Count);
        });
    }

    /// <summary>
    /// Closes the current project.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCloseProject))]
    private async Task CloseProjectAsync()
    {
        _logger.LogInformation("CloseProjectAsync requested for: {Name}", CurrentProject?.Name);
        if (CurrentProject == null)
        {
            return;
        }

        CurrentProject = null;
        ProjectPath = string.Empty;
        ProjectName = string.Empty;
        IsProjectLoaded = false;
        Bundles.Clear();
        BuildLog.Clear();
        StatusMessage = ReadyStatusLiteral;

        _logger.LogInformation("Project closed successfully");
        await Task.CompletedTask;
    }

    private bool CanCloseProject() => IsProjectLoaded && !IsBuildRunning;

    /// <summary>
    /// Adds a new bundle.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddBundle))]
    private async Task AddBundleAsync()
    {
        _logger.LogInformation("AddBundleAsync requested");
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
        _logger.LogInformation("RemoveBundleAsync requested for: {BundleName}", SelectedBundle?.Name);
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

        _logger.LogInformation("Editing bundle: {BundleName}", SelectedBundle.Name);
        await Task.CompletedTask;
    }

    private bool CanEditBundle() => IsProjectLoaded && SelectedBundle != null && !IsBuildRunning;

    private BuildStep DetermineBuildSteps()
    {
        var buildSteps = BuildStep.None;
        if (CleanEnabled) buildSteps |= BuildStep.Clean;
        if (BuildEnabled) buildSteps |= BuildStep.Build;
        if (ReleaseEnabled) buildSteps |= BuildStep.Release;
        if (InstallEnabled) buildSteps |= BuildStep.Install;
        if (RunGameEnabled) buildSteps |= BuildStep.Run;
        if (UninstallEnabled) buildSteps |= BuildStep.Uninstall;
        return buildSteps;
    }

    private async Task<BuildConfiguration> PrepareBuildConfigurationAsync(CancellationToken cancellationToken)
    {
        var buildConfig = CurrentProject?.Configuration;
        if (buildConfig == null && CurrentProject != null && CurrentProject.ConfigFiles.Count > 0)
        {
            var configPath = Path.Combine(CurrentProject.ProjectDir, CurrentProject.ConfigFiles[0]);
            buildConfig = await _configurationLoaderService.LoadConfigurationAsync(
                configPath,
                cancellationToken).ConfigureAwait(false);
            CurrentProject.Configuration = buildConfig;
        }

        buildConfig ??= new BuildConfiguration();

        var resolvedGameDir = ResolveGameDirectory(buildConfig);
        if (!string.IsNullOrEmpty(resolvedGameDir))
        {
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

        buildConfig.ZipCompressionLevel = SelectedCompressionLevel;
        return buildConfig;
    }

    private async Task HandleBuildSuccessAsync(int filesProcessed, int bundlesCreated)
    {
        AppendBuildLog($"\n=== Build Completed Successfully in {LastBuildTime:mm\\:ss\\.fff} ===");

        await InvokeOnUIThreadAsync(() =>
        {
            if (filesProcessed == 0)
            {
                const string noFilesMessage = "Build completed but no files were processed.\n" +
                    "Check that:\n" +
                    "- Files exist in GameFilesEdited folder\n" +
                    "- Bundles are configured in config/ModBundleItems.json\n" +
                    "- File paths in config match actual files";
                _notificationService.ShowInfo(
                    "Build Complete (No Files)",
                    noFilesMessage,
                    autoDismissMs: 8000);
            }
            else
            {
                var outputPath = CurrentProject != null
                    ? Path.Combine(CurrentProject.ProjectDir, CurrentProject.Directories.Build)
                    : string.Empty;
                var summaryMessage = $"Processed {filesProcessed} files\n" +
                    $"Created {bundlesCreated} bundles\n" +
                    $"Time: {LastBuildTime:mm\\:ss}\n" +
                    $"Output: {outputPath}";
                _notificationService.ShowSuccess(
                    "Build Complete",
                    summaryMessage);
            }
        });

        StatusMessage = "Build completed successfully";

        if (!string.IsNullOrEmpty(ProjectPath))
        {
            await _projectConfigService.UpdateLastBuildTimeAsync(ProjectPath).ConfigureAwait(false);
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
            _notificationService.ShowWarning(NoProjectTitle, NoProjectMessage);
            return;
        }

        var fileCount = await CountFilesToBuildAsync().ConfigureAwait(false);
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
                _notificationService.ShowWarning(
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

        int filesProcessed = 0;
        int bundlesCreated = 0;

        await InvokeOnUIThreadAsync(() =>
        {
            BuildLog.Clear();
            ProcessedFiles = 0;
            TotalFiles = 0;
            PercentComplete = 0;
            EstimatedTimeRemaining = null;
        });

        AppendBuildLog("=== Build Started ===");
        AppendBuildLog($"Files to process: {fileCount}");
        StatusMessage = "Building...";

        try
        {
            var buildConfig = await PrepareBuildConfigurationAsync(_buildCancellationTokenSource.Token).ConfigureAwait(false);
            var selectedPacks = Bundles.Where(b => b.IsSelected).Select(b => b.Name).ToList();

            var progress = new Progress<string>(message =>
            {
                AppendBuildLog(message);
                if (message.Contains("Processing file:") || message.Contains("Converted"))
                {
                    Interlocked.Increment(ref filesProcessed);
                }

                if (message.Contains("Created bundle:") || message.Contains(".big"))
                {
                    Interlocked.Increment(ref bundlesCreated);
                }
            });

            var buildSteps = DetermineBuildSteps();
            _logger.LogInformation("Build steps configured: {BuildSteps} (RunGameEnabled={RunGameEnabled})", buildSteps, RunGameEnabled);

            var result = await _buildEngineService.ExecuteBuildAsync(
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
                await HandleBuildSuccessAsync(filesProcessed, bundlesCreated).ConfigureAwait(false);
            }
            else
            {
                AppendBuildLog("\n=== Build Failed ===");
                AppendBuildLog(result.FirstError ?? "Unknown error");
                _notificationService.ShowError("Build Failed", result.FirstError ?? "Unknown error");
                StatusMessage = "Build failed";
            }
        }
        catch (OperationCanceledException ex)
        {
            _buildStopwatch.Stop();
            _logger.LogInformation(ex, "Build cancelled by user");
            AppendBuildLog("\n=== Build Cancelled ===");
            await InvokeOnUIThreadAsync(() => _notificationService.ShowInfo("Build Cancelled", "Build operation was cancelled"));
            StatusMessage = "Build cancelled";
        }
        catch (Exception ex)
        {
            _buildStopwatch.Stop();
            _logger.LogError(ex, "Build execution failed");
            AppendBuildLog("\n=== Build Error ===");
            AppendBuildLog(ex.Message);
            _notificationService.ShowError("Build Error", ex.Message);
            StatusMessage = "Build error";
        }
        finally
        {
            IsBuildRunning = false;
            _buildCancellationTokenSource?.Dispose();
            _buildCancellationTokenSource = null;
        }
    }

    private bool CanBuild() => IsProjectLoaded && !IsBuildRunning;

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
    private async Task<int> CountFilesToBuildAsync()
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
                    () =>
                    {
                        try
                        {
                            return Directory.GetFiles(editFolder, "*.*", SearchOption.AllDirectories)
                                .Count(f => !Path.GetFileName(f).Equals("README.txt", StringComparison.OrdinalIgnoreCase));
                        }
                        catch
                        {
                            return 0;
                        }
                    },
                    CancellationToken.None).ConfigureAwait(false);

                if (fileCount > 0)
                {
                    return fileCount;
                }
            }

            return Bundles.Sum(b => b.FileCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count files to build");
            return 0;
        }
    }

    /// <summary>
    /// Refreshes the file count.
    /// </summary>
    [RelayCommand]
    private async Task RefreshFileCountAsync()
    {
        FilesToBuildCount = await CountFilesToBuildAsync().ConfigureAwait(false);
        StatusMessage = $"Files to build: {FilesToBuildCount}";
    }

    /// <summary>
    /// Cleans the build output.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        _logger.LogInformation("CleanAsync requested for project: {Name}", CurrentProject?.Name);
        if (CurrentProject == null)
        {
            return;
        }

        try
        {
            var buildDir = CurrentProject.Directories.Build;
            if (!string.IsNullOrEmpty(buildDir) && Directory.Exists(buildDir))
            {
                await Task.Run(() => Directory.Delete(buildDir, recursive: true), CancellationToken.None).ConfigureAwait(false);
                AppendBuildLog($"Cleaned build directory: {buildDir}");
                _notificationService.ShowSuccess("Clean Complete", "Build directory cleaned");
                StatusMessage = "Build directory cleaned";
                _logger.LogInformation("Cleaned build directory: {Dir}", buildDir);
            }

            _buildEngineService.InvalidateBuildStructureCache();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean build directory");
            _notificationService.ShowError("Clean Failed", ex.Message);
        }
    }

    private bool CanClean() => IsProjectLoaded && !IsBuildRunning;

    /// <summary>
    /// Aborts the current build.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAbortBuild))]
    private void AbortBuild()
    {
        _logger.LogInformation("AbortBuild requested");
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
        _logger.LogInformation("OpenProjectFolder requested for: {Path}", ProjectPath);
        var projectDir = !string.IsNullOrEmpty(ProjectPath) ? Path.GetDirectoryName(ProjectPath) : CurrentProject?.ProjectDir;
        if (string.IsNullOrEmpty(projectDir))
        {
            _notificationService.ShowWarning(NoProjectTitle, NoProjectMessage);
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
            _logger.LogError(ex, "Failed to open project folder");
            _notificationService.ShowError("Open Failed", "Could not open project folder");
        }
    }

    /// <summary>
    /// Opens the GameFilesEdited folder in file explorer.
    /// </summary>
    [RelayCommand]
    private void OpenEditFolder()
    {
        _logger.LogInformation("OpenEditFolder requested for project: {Path}", ProjectPath);
        var projectDir = !string.IsNullOrEmpty(ProjectPath) ? Path.GetDirectoryName(ProjectPath) : CurrentProject?.ProjectDir;
        if (string.IsNullOrEmpty(projectDir))
        {
            _notificationService.ShowWarning(NoProjectTitle, NoProjectMessage);
            return;
        }

        try
        {
            var editFolder = Path.Combine(projectDir, "GameFilesEdited");
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
            _logger.LogError(ex, "Failed to open edit folder");
            _notificationService.ShowError("Open Failed", "Could not open GameFilesEdited folder");
        }
    }

    /// <summary>
    /// Opens the build folder in file explorer.
    /// </summary>
    [RelayCommand]
    private void OpenBuildFolder()
    {
        _logger.LogInformation("OpenBuildFolder requested for: {Path}", ProjectPath);
        if (CurrentProject == null || string.IsNullOrEmpty(ProjectPath))
        {
            _notificationService.ShowWarning(NoProjectTitle, NoProjectMessage);
            return;
        }

        try
        {
            var projectDir = Path.GetDirectoryName(ProjectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                return;
            }

            var buildDir = CurrentProject.Directories.Build ?? ModBuilderConstants.DefaultBuildDir;
            var buildPath = Path.IsPathRooted(buildDir) ? buildDir : Path.Combine(projectDir, buildDir);
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
            _logger.LogError(ex, "Failed to open build folder");
            _notificationService.ShowError("Open Failed", "Could not open build folder");
        }
    }

    /// <summary>
    /// Opens the release folder in file explorer.
    /// </summary>
    [RelayCommand]
    private void OpenReleaseFolder()
    {
        _logger.LogInformation("OpenReleaseFolder requested for: {Path}", ProjectPath);
        if (CurrentProject == null || string.IsNullOrEmpty(ProjectPath))
        {
            return;
        }

        var projectDir = Path.GetDirectoryName(ProjectPath);
        if (string.IsNullOrEmpty(projectDir))
        {
            return;
        }

        try
        {
            var releaseDir = CurrentProject.Directories.Release ?? ModBuilderConstants.DefaultReleaseDir;
            var releasePath = Path.IsPathRooted(releaseDir) ? releaseDir : Path.Combine(projectDir, releaseDir);
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
            _logger.LogError(ex, "Failed to open release folder");
            _notificationService.ShowError("Open Folder Failed", $"Failed to open release folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the build output log.
    /// </summary>
    [RelayCommand]
    private void ClearOutput()
    {
        _logger.LogInformation("ClearOutput requested");
        PostToUIThread(() =>
        {
            BuildLog.Clear();
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

            if (CurrentProject.Configuration == null && !string.IsNullOrEmpty(projectDir))
            {
                CurrentProject.Configuration = await _configurationLoaderService.LoadProjectConfigurationAsync(
                    projectDir,
                    CancellationToken.None).ConfigureAwait(false);
            }

            await InvokeOnUIThreadAsync(() => PopulateProjectBundlesAndProperties(CurrentProject.Configuration)).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(projectDir))
            {
                await InitializeFileManagerAndGameDirectoryAsync(projectDir).ConfigureAwait(false);
            }

            PostToUIThread(NotifyAllProjectCommands);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load project data");
            _notificationService.ShowError("Load Error", $"Failed to load project data: {ex.Message}");
        }
    }

    private void PopulateProjectBundlesAndProperties(BuildConfiguration? config)
    {
        Bundles.Clear();

        if (config?.Items != null)
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
            OutputDirectory = CurrentProject.Directories.Build;
        }

        if (config != null)
        {
            SelectedCompressionLevel = config.ZipCompressionLevel;
        }

        FileCount = Bundles.Sum(b => b.FileCount);
    }

    private async Task InitializeFileManagerAndGameDirectoryAsync(string projectDir)
    {
        await FileManager.InitializeAsync(projectDir, CancellationToken.None).ConfigureAwait(false);

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
            BuildLog.Add($"[{timestamp}] {message}");
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
        BuildStatus = string.IsNullOrEmpty(value) ? ReadyStatusLiteral : value;
    }

    partial void OnProjectPathChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentProjectPath));
    }

    partial void OnCurrentProjectChanged(ModBuilderProject? value)
    {
        IsProjectLoaded = value != null;

        // Dispatch UI updates to UI thread
        PostToUIThread(() =>
        {
            OpenFileManagerCommand.NotifyCanExecuteChanged();
            OpenConfigEditorCommand.NotifyCanExecuteChanged();
            SaveProjectCommand.NotifyCanExecuteChanged();
            CloseProjectCommand.NotifyCanExecuteChanged();
            BuildCommand.NotifyCanExecuteChanged();
            CleanCommand.NotifyCanExecuteChanged();
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
            await Dispatcher.UIThread.InvokeAsync(action);
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

    private bool _disposed;

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
            _buildCancellationTokenSource?.Cancel();
            _buildCancellationTokenSource?.Dispose();
            _buildCancellationTokenSource = null;
        }

        _disposed = true;
    }
}
