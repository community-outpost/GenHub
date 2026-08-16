using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for ModBuilder tool with complete build pipeline integration.
/// </summary>
public partial class ModBuilderViewModel : ObservableObject, IDisposable
{
    private readonly IBuildEngineService _buildEngineService;
    private readonly IProjectConfigService _projectConfigService;
    private readonly IConfigurationLoaderService _configurationLoaderService;
    private readonly IProjectStructureGenerator _projectStructureGenerator;
    private readonly INotificationService _notificationService;
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
    public ModBuilderViewModel(
        IBuildEngineService buildEngineService,
        IProjectConfigService projectConfigService,
        IConfigurationLoaderService configurationLoaderService,
        IProjectStructureGenerator projectStructureGenerator,
        INotificationService notificationService,
        FileManagerViewModel fileManager,
        ILoggerFactory loggerFactory,
        ILogger<ModBuilderViewModel> logger)
    {
        _buildEngineService = buildEngineService;
        _projectConfigService = projectConfigService;
        _configurationLoaderService = configurationLoaderService;
        _projectStructureGenerator = projectStructureGenerator;
        _notificationService = notificationService;
        FileManager = fileManager;
        _loggerFactory = loggerFactory;
        _logger = logger;

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
    public ObservableCollection<string> RecentProjects { get; } = [];

    /// <summary>
    /// Gets or sets the search query for filtering projects.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

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
    public string ProgressText => $"{PercentComplete:F1}%";

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
    private string _buildStatus = "Ready";

    /// <summary>
    /// Gets the current build stage (alias for BuildStage).
    /// </summary>
    public string CurrentStage => BuildStage;

    /// <summary>
    /// Gets or sets a value indicating whether clean action is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _cleanEnabled = true;

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
    private string _statusMessage = "Ready";

    /// <summary>
    /// Gets or sets the status text for the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusText = "Ready";

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
            var result = await _projectConfigService.GetRecentProjectsAsync(10).ConfigureAwait(false);
            if (result.Success && result.Data != null)
            {
                await InvokeOnUIThreadAsync(() =>
                {
                    RecentProjects.Clear();
                    foreach (var projectPath in result.Data)
                    {
                        RecentProjects.Add(projectPath);
                    }

                    // Notify property changes for dashboard
                    OnPropertyChanged(nameof(HasRecentProjects));
                    OnPropertyChanged(nameof(TotalProjects));
                });

                _logger.LogInformation("Loaded {Count} recent projects", result.Data.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recent projects");
        }
    }

    /// <summary>
    /// Creates a new project.
    /// </summary>
    [RelayCommand]
    private async Task NewProjectAsync()
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(lifetime?.MainWindow);
        if (topLevel == null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create New ModBuilder Project",
            SuggestedFileName = "MyMod.mbproj",
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
                    await _projectConfigService.AddToRecentProjectsAsync(projectPath).ConfigureAwait(false);

                    _notificationService.ShowSuccess(
                        "Project Created",
                        $"Created project: {projectName}\nProject structure ready. Edit files in GameFilesEdited folder.");
                    AppendBuildLog($"Created new project: {projectPath}");
                    AppendBuildLog("Generated project structure with folders and config files");
                }
                else
                {
                    _notificationService.ShowError("Creation Failed", result.FirstError ?? "Unknown error");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create project");
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
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(lifetime?.MainWindow);
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open ModBuilder Project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ModBuilder Project") { Patterns = ["*.mbproj",], }
            ],
        }).ConfigureAwait(false);

        if (files.Any())
        {
            await LoadProjectFromPathAsync(files[0].Path.LocalPath).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens a recent project from its file path.
    /// </summary>
    /// <param name="path">The file path of the project to open.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task OpenRecentProjectAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _notificationService.ShowWarning("Project Not Found", $"Could not find project file at: {path}");
            return;
        }

        await LoadProjectFromPathAsync(path).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the sample project for testing.
    /// </summary>
    [RelayCommand]
    private async Task LoadSampleProjectAsync()
    {
        try
        {
            var samplePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "SampleProjects",
                "ModBuilder",
                "BasicMod",
                "BasicMod.mbproj");

            if (!File.Exists(samplePath))
            {
                _notificationService.ShowWarning(
                    "Sample Not Found",
                    "Sample project not found. It may not be included in this build.");
                AppendBuildLog($"Sample project not found at: {samplePath}");
                return;
            }

            // Ensure sample TGA exists
            await EnsureSampleTgaExistsAsync(Path.GetDirectoryName(samplePath)!).ConfigureAwait(false);

            await LoadProjectFromPathAsync(samplePath).ConfigureAwait(false);

            _notificationService.ShowSuccess(
                "Sample Loaded",
                "Sample project loaded. Click 'Execute Build' to test ModBuilder.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sample project");
            _notificationService.ShowError("Load Failed", $"Failed to load sample project: {ex.Message}");
        }
    }

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

        Directory.CreateDirectory(Path.GetDirectoryName(tgaPath)!);
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

                await LoadProjectDataAsync().ConfigureAwait(false);
                await _projectConfigService.AddToRecentProjectsAsync(projectPath).ConfigureAwait(false);

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
            }
            else
            {
                _notificationService.ShowError("Save Failed", result.FirstError ?? "Unknown error");
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
            return;
        }

        try
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            if (mainWindow == null)
            {
                return;
            }

            // Create the ConfigEditorViewModel
            var configEditorViewModel = new ConfigEditorViewModel(
                _configurationLoaderService,
                _notificationService,
                _loggerFactory.CreateLogger<ConfigEditorViewModel>());

            // Initialize with current project
            await configEditorViewModel.InitializeAsync(CurrentProject).ConfigureAwait(false);

            // Show the dialog
            await InvokeOnUIThreadAsync(async () =>
            {
                var dialog = new Views.ConfigEditorDialog(configEditorViewModel);
                await dialog.ShowDialog(mainWindow).ConfigureAwait(false);

                // Reload bundles after dialog closes
                await LoadBundlesAsync().ConfigureAwait(false);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open configuration editor");
            _notificationService.ShowError("Configuration Editor Error", ex.Message);
        }
    }

    private bool CanOpenConfigEditor() => CurrentProject != null && !IsBuildRunning;

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
        if (CurrentProject == null)
        {
            return;
        }

        // TODO: Prompt to save if there are unsaved changes
        await InvokeOnUIThreadAsync(() =>
        {
            CurrentProject = null;
            ProjectPath = string.Empty;
            ProjectName = string.Empty;
            IsProjectLoaded = false;
            Bundles.Clear();
            BuildLog.Clear();
            StatusMessage = "Ready";
        });

        _logger.LogInformation("Project closed");
    }

    private bool CanCloseProject() => IsProjectLoaded && !IsBuildRunning;

    /// <summary>
    /// Adds a new bundle.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddBundle))]
    private async Task AddBundleAsync()
    {
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

        // TODO: Open bundle editor dialog
        await Task.CompletedTask;
        _logger.LogInformation("Edit bundle: {BundleName}", SelectedBundle.Name);
    }

    private bool CanEditBundle() => IsProjectLoaded && SelectedBundle != null && !IsBuildRunning;

    /// <summary>
    /// Executes the build.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        if (CurrentProject == null)
        {
            _notificationService.ShowWarning("No Project", "Please load or create a project first");
            return;
        }

        // VALIDATE: Check if there are files to build
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
            // Load or use existing configuration
            var buildConfig = CurrentProject.Configuration;
            if (buildConfig == null && CurrentProject.ConfigFiles.Count > 0)
            {
                var configPath = Path.Combine(CurrentProject.ProjectDir, CurrentProject.ConfigFiles[0]);
                buildConfig = await _configurationLoaderService.LoadConfigurationAsync(
                    configPath,
                    _buildCancellationTokenSource.Token).ConfigureAwait(false);
                CurrentProject.Configuration = buildConfig;
            }

            if (buildConfig == null)
            {
                buildConfig = new BuildConfiguration();
            }

            // Update compression level
            buildConfig.ZipCompressionLevel = SelectedCompressionLevel;

            // Get selected bundle packs
            var selectedPacks = Bundles
                .Where(b => b.IsSelected)
                .Select(b => b.Name)
                .ToList();

            var progress = new Progress<string>(message =>
            {
                AppendBuildLog(message);

                // Track processed files and bundles
                if (message.Contains("Processing file:") || message.Contains("Converted"))
                {
                    filesProcessed++;
                }

                if (message.Contains("Created bundle:") || message.Contains(".big"))
                {
                    bundlesCreated++;
                }
            });

            // Build the BuildStep flags from enabled checkboxes
            var buildSteps = BuildStep.Zero;
            if (CleanEnabled) buildSteps |= BuildStep.Clean;
            if (BuildEnabled) buildSteps |= BuildStep.Build;
            if (ReleaseEnabled) buildSteps |= BuildStep.Release;
            if (InstallEnabled) buildSteps |= BuildStep.Install;
            if (RunGameEnabled) buildSteps |= BuildStep.Run;
            if (UninstallEnabled) buildSteps |= BuildStep.Uninstall;

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
                AppendBuildLog($"\n=== Build Completed Successfully in {LastBuildTime:mm\\:ss\\.fff} ===");

                // Show build summary
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
                        var outputPath = Path.Combine(CurrentProject.ProjectDir, CurrentProject.Directories.Build);
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

                // Update last build time in project
                await _projectConfigService.UpdateLastBuildTimeAsync(ProjectPath).ConfigureAwait(false);
            }
            else
            {
                AppendBuildLog($"\n=== Build Failed ===");
                AppendBuildLog(result.FirstError ?? "Unknown error");
                _notificationService.ShowError("Build Failed", result.FirstError ?? "Unknown error");
                StatusMessage = "Build failed";
            }
        }
        catch (OperationCanceledException)
        {
            _buildStopwatch.Stop();
            _logger.LogInformation("Build cancelled by user");
            AppendBuildLog("\n=== Build Cancelled ===");
            await InvokeOnUIThreadAsync(() =>
            {
                _notificationService.ShowInfo(
                    "Build Cancelled",
                    "Build operation was cancelled");
            });
            StatusMessage = "Build cancelled";
        }
        catch (Exception ex)
        {
            _buildStopwatch.Stop();
            _logger.LogError(ex, "Build execution failed");
            AppendBuildLog($"\n=== Build Error ===");
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

    /// <summary>
    /// Counts the number of files that will be built.
    /// </summary>
    private async Task<int> CountFilesToBuildAsync()
    {
        if (CurrentProject == null)
        {
            return 0;
        }

        try
        {
            var projectDir = Path.GetDirectoryName(ProjectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                return 0;
            }

            var editFolder = Path.Combine(projectDir, "GameFilesEdited");
            if (!Directory.Exists(editFolder))
            {
                return 0;
            }

            // Count all files in GameFilesEdited folder recursively
            var fileCount = await Task.Run(() =>
            {
                try
                {
                    return Directory.GetFiles(editFolder, "*.*", SearchOption.AllDirectories).Length;
                }
                catch
                {
                    return 0;
                }
            }).ConfigureAwait(false);

            return fileCount;
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
        if (CurrentProject == null)
        {
            return;
        }

        try
        {
            var buildDir = CurrentProject.Directories.Build;
            if (!string.IsNullOrEmpty(buildDir) && Directory.Exists(buildDir))
            {
                await Task.Run(() => Directory.Delete(buildDir, recursive: true)).ConfigureAwait(false);
                AppendBuildLog($"Cleaned build directory: {buildDir}");
                _notificationService.ShowSuccess("Clean Complete", "Build directory cleaned");
                StatusMessage = "Build directory cleaned";
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
        if (string.IsNullOrEmpty(ProjectPath))
        {
            _notificationService.ShowWarning("No Project", "Please load or create a project first");
            return;
        }

        try
        {
            var projectDir = Path.GetDirectoryName(ProjectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                _notificationService.ShowWarning("Invalid Path", "Project path is invalid");
                return;
            }

            if (!Directory.Exists(projectDir))
            {
                _notificationService.ShowWarning("Folder Not Found", "Project folder does not exist");
                return;
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
        if (string.IsNullOrEmpty(ProjectPath))
        {
            _notificationService.ShowWarning("No Project", "Please load or create a project first");
            return;
        }

        try
        {
            var projectDir = Path.GetDirectoryName(ProjectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                return;
            }

            var editFolder = Path.Combine(projectDir, "GameFilesEdited");
            if (Directory.Exists(editFolder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = editFolder,
                    UseShellExecute = true,
                });
            }
            else
            {
                _notificationService.ShowWarning("Folder Not Found",
                    "GameFilesEdited folder does not exist. It will be created during the first build.");
            }
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
        if (CurrentProject == null || string.IsNullOrEmpty(ProjectPath))
        {
            _notificationService.ShowWarning("No Project", "Please load or create a project first");
            return;
        }

        try
        {
            var projectDir = Path.GetDirectoryName(ProjectPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                return;
            }

            var buildPath = Path.Combine(projectDir, CurrentProject.Directories.Build);
            if (Directory.Exists(buildPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = buildPath,
                    UseShellExecute = true,
                });
            }
            else
            {
                _notificationService.ShowWarning("Folder Not Found",
                    "Build folder does not exist. Run a build first to create it.");
            }
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
        if (CurrentProject == null || string.IsNullOrEmpty(ProjectPath))
        {
            return;
        }

        var projectDir = Path.GetDirectoryName(ProjectPath);
        if (string.IsNullOrEmpty(projectDir))
        {
            return;
        }

        var releaseDir = CurrentProject.Directories.Release ?? ModBuilderConstants.DefaultReleaseDir;
        var releasePath = Path.Combine(projectDir, releaseDir);
        if (Directory.Exists(releasePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = releasePath,
                UseShellExecute = true,
            });
        }
    }

    /// <summary>
    /// Clears the build output log.
    /// </summary>
    [RelayCommand]
    private void ClearOutput()
    {
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
            var projectDir = Path.GetDirectoryName(ProjectPath) ?? CurrentProject.ProjectDir;

            // Load configuration if not already loaded
            if (CurrentProject.Configuration == null && !string.IsNullOrEmpty(projectDir))
            {
                CurrentProject.Configuration = await _configurationLoaderService.LoadProjectConfigurationAsync(
                    projectDir,
                    CancellationToken.None).ConfigureAwait(false);
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
                        });
                    }
                }

                // Update properties
                GameDirectory = CurrentProject.GameDir;
                OutputDirectory = CurrentProject.Directories.Build;

                if (CurrentProject.Configuration != null)
                {
                    SelectedCompressionLevel = CurrentProject.Configuration.ZipCompressionLevel;
                }

                // Update file count
                FileCount = Bundles.Sum(b => b.FileCount);
            });

            // Initialize file manager with project path
            if (!string.IsNullOrEmpty(projectDir))
            {
                await FileManager.InitializeAsync(projectDir).ConfigureAwait(false);
            }

            // Notify command state changes on UI thread
            PostToUIThread(() =>
            {
                SaveProjectCommand.NotifyCanExecuteChanged();
                CloseProjectCommand.NotifyCanExecuteChanged();
                BuildCommand.NotifyCanExecuteChanged();
                CleanCommand.NotifyCanExecuteChanged();
                AddBundleCommand.NotifyCanExecuteChanged();
            });

            // Refresh file count
            await RefreshFileCountAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load project data");
            await InvokeOnUIThreadAsync(() =>
            {
                _notificationService.ShowError(
                    "Load Error",
                    $"Failed to load project data: {ex.Message}");
            });
        }
    }

    /// <summary>
    /// Appends a message to the build log.
    /// </summary>
    private void AppendBuildLog(string message)
    {
        PostToUIThread(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
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
        BuildStatus = string.IsNullOrEmpty(value) ? "Ready" : value;
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
            SaveProjectCommand.NotifyCanExecuteChanged();
            CloseProjectCommand.NotifyCanExecuteChanged();
            BuildCommand.NotifyCanExecuteChanged();
            CleanCommand.NotifyCanExecuteChanged();
            AddBundleCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CurrentProjectPath));
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
        if (_disposed)
        {
            return;
        }

        _buildCancellationTokenSource?.Cancel();
        _buildCancellationTokenSource?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
