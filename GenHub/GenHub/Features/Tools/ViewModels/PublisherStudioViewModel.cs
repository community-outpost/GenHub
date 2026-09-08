using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;
using GenHub.Features.Tools.Services.Hosting;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Main ViewModel for Publisher Studio.
/// </summary>
public partial class PublisherStudioViewModel : ObservableObject
{
    private readonly string _settingsPath;

    private readonly ILogger<PublisherStudioViewModel> _logger;
    private readonly IPublisherStudioService _publisherStudioService;
    private readonly IPublisherStudioDialogService _dialogService;
    private readonly IHostingProviderFactory? _hostingProviderFactory;
    private readonly IHostingStateManager _hostingStateManager;
    private readonly INotificationService? _notificationService;

    [ObservableProperty]
    private PublisherStudioProject? _currentProject;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<NamedCatalog> _catalogs = [];

    [ObservableProperty]
    private NamedCatalog? _selectedCatalog;

    [ObservableProperty]
    private bool _isRecoveryNeeded;

    /// <summary>
    /// Gets a value indicating whether the selected catalog can be removed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound in XAML")]
    public bool CanRemoveCatalog => Catalogs.Count > 1;

    /// <summary>
    /// Gets a value indicating whether the publisher setup is complete.
    /// Setup is complete when Publisher ID and Name are configured.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound in XAML")]
    public bool IsSetupComplete =>
        !string.IsNullOrWhiteSpace(CurrentProject?.Catalog?.Publisher?.Id) &&
        !string.IsNullOrWhiteSpace(CurrentProject?.Catalog?.Publisher?.Name);

    /// <summary>
    /// Gets a value indicating whether the setup overlay should be shown.
    /// </summary>
    public bool ShouldShowSetupOverlay => !IsSetupComplete && SelectedTabIndex != 0;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.PublisherProfileViewModel? _publisherProfileViewModel;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.ContentLibraryViewModel? _contentLibraryViewModel;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.PublishShareViewModel? _publishShareViewModel;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.ReferralsViewModel? _referralsViewModel;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShouldShowSetupOverlay));
    }

    /// <summary>
    /// Navigates to the Publisher Profile tab.
    /// </summary>
    [RelayCommand]
    private void GoToProfileTab()
    {
        SelectedTabIndex = 0;
    }

    partial void OnSelectedCatalogChanged(NamedCatalog? value)
    {
        if (value != null && CurrentProject != null)
        {
            ContentLibraryViewModel = new GenHub.Features.Tools.ViewModels.ContentLibraryViewModel(CurrentProject, value, this, _logger, _dialogService);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherStudioViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="publisherStudioService">The publisher studio service.</param>
    /// <param name="dialogService">The dialog service.</param>
    /// <param name="hostingProviderFactory">The hosting provider factory.</param>
    /// <param name="hostingStateManager">The hosting state manager.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="configurationProvider">The configuration provider service.</param>
    public PublisherStudioViewModel(
        ILogger<PublisherStudioViewModel> logger,
        IPublisherStudioService publisherStudioService,
        IPublisherStudioDialogService dialogService,
        IHostingProviderFactory? hostingProviderFactory = null,
        IHostingStateManager? hostingStateManager = null,
        INotificationService? notificationService = null,
        IConfigurationProviderService? configurationProvider = null)
    {
        _logger = logger;
        _publisherStudioService = publisherStudioService;
        _dialogService = dialogService;
        _hostingProviderFactory = hostingProviderFactory;
        _hostingStateManager = hostingStateManager ?? new HostingStateManager(Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<HostingStateManager>());
        _notificationService = notificationService;
        _settingsPath = Path.Combine(
            configurationProvider?.GetApplicationDataPath() ?? Path.GetTempPath(),
            "GenHub",
            "publisher_studio_settings.json");

        // Initialize: auto-load last project or create a new one
        _ = InitializeAsync();
    }

    /// <summary>
    /// Marks the current project as dirty (having unsaved changes).
    /// </summary>
    public void MarkDirty()
    {
        if (CurrentProject != null)
        {
            CurrentProject.IsDirty = true;
            HasUnsavedChanges = true;
            OnPropertyChanged(nameof(IsSetupComplete));
            OnPropertyChanged(nameof(ShouldShowSetupOverlay));
        }
    }

    /// <summary>
    /// Saves the current project.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task SaveProjectAsync()
    {
        if (CurrentProject == null)
        {
            return;
        }

        try
        {
            // If path is missing, treat as "Save As"
            if (string.IsNullOrEmpty(CurrentProject.ProjectPath))
            {
                var promptResult = await _dialogService.ShowProjectSavePromptAsync("Save Project");
                if (promptResult != null)
                {
                    CurrentProject.ProjectPath = promptResult;
                }
                else
                {
                    // User cancelled
                    return;
                }
            }

            var result = await _publisherStudioService.SaveProjectAsync(CurrentProject);
            if (result.Success)
            {
                HasUnsavedChanges = false;
                StatusMessage = "Project saved. Go to 'Publish & Share' to export and release.";
                _logger.LogInformation("Saved project: {ProjectName}", CurrentProject.ProjectName);

                // Persist the project path for auto-load on next launch
                if (!string.IsNullOrEmpty(CurrentProject.ProjectPath))
                {
                    await SaveLastProjectPathAsync(CurrentProject.ProjectPath);
                }

                _notificationService?.ShowSuccess(
                    "Project Saved",
                    $"Your publisher project '{CurrentProject.ProjectName}' has been saved successfully.",
                    autoDismissMs: 4000);

                // Force a dirty state update to refresh UI
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
            else
            {
                StatusMessage = $"Failed to save: {result.FirstError}";
                _logger.LogError("Failed to save project: {Error}", result.FirstError);

                _notificationService?.ShowError(
                    "Save Failed",
                    result.FirstError ?? "An unknown error occurred while saving the project.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
            _logger.LogError(ex, "Error saving project");

            _notificationService?.ShowError(
                "Save Error",
                $"An error occurred while saving: {ex.Message}");
        }
    }

    private async Task InitializeAsync()
    {
        // Check if this is a first-time user (no previous project)
        var lastPath = await LoadLastProjectPathAsync();
        var isFirstTime = string.IsNullOrEmpty(lastPath) || !File.Exists(lastPath);

        if (isFirstTime)
        {
            // Show welcome screen for first-time users
            var welcomeResult = await _dialogService.ShowWelcomeScreenAsync();

            if (welcomeResult == null || welcomeResult.Action == WelcomeAction.Skip)
            {
                // User skipped or cancelled - create default project
                await CreateNewProjectInternalAsync(showWizard: false);
                return;
            }

            if (welcomeResult.Action == WelcomeAction.Import && !string.IsNullOrEmpty(welcomeResult.ImportPath))
            {
                // User wants to import existing profile
                await LoadProjectFromPathAsync(welcomeResult.ImportPath);
                return;
            }

            if (welcomeResult.Action == WelcomeAction.CreateNew)
            {
                // User wants to create new profile - show setup wizard
                await CreateNewProjectInternalAsync(showWizard: true);
                return;
            }
        }

        // Returning user - load last project
        if (!string.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
        {
            await LoadProjectFromPathAsync(lastPath);
        }
        else
        {
            await CreateNewProjectInternalAsync(showWizard: false);
        }
    }

    [RelayCommand]
    private async Task LoadProjectAsync()
    {
        try
        {
            var filePath = await _dialogService.ShowProjectOpenPromptAsync("Load Project");
            if (string.IsNullOrEmpty(filePath))
                return;

            await LoadProjectFromPathAsync(filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading project: {ex.Message}";
            _logger.LogError(ex, "Error loading project");
        }
    }

    private async Task LoadProjectFromPathAsync(string filePath)
    {
        var result = await _publisherStudioService.LoadProjectAsync(filePath);
        if (result.Success && result.Data != null)
        {
            CurrentProject = result.Data;
            HasUnsavedChanges = false;
            await InitializeChildViewModelsAsync();
            StatusMessage = $"Loaded project: {CurrentProject.ProjectName}";
            _logger.LogInformation("Loaded project from: {Path}", filePath);

            // Save as last opened project
            await SaveLastProjectPathAsync(filePath);

            _notificationService?.ShowSuccess(
                "Project Loaded",
                $"Publisher project '{CurrentProject.ProjectName}' loaded successfully.",
                autoDismissMs: 4000);
        }
        else
        {
            StatusMessage = $"Failed to load project: {result.FirstError}";
            _logger.LogError("Failed to load project: {Error}", result.FirstError);
            _notificationService?.ShowError("Load Failed", result.FirstError ?? "Unknown error");
        }
    }

    private async Task SaveLastProjectPathAsync(string projectPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var settings = new { LastProjectPath = projectPath, LastOpened = DateTime.UtcNow };
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save publisher studio settings");
        }
    }

    private async Task<string?> LoadLastProjectPathAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;
            var json = await File.ReadAllTextAsync(_settingsPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("LastProjectPath", out var prop) ? prop.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load last project path from settings");
            return null;
        }
    }

    /// <summary>
    /// Creates a new publisher project (Interactive).
    /// </summary>
    [RelayCommand]
    private async Task CreateNewProjectAsync()
    {
        // Check for unsaved changes and auto-save before creating new if project has been saved before
        if (HasUnsavedChanges && CurrentProject != null && !string.IsNullOrEmpty(CurrentProject.ProjectPath))
        {
            await SaveProjectAsync();
        }

        await CreateNewProjectInternalAsync(showWizard: true);
    }

    private async Task CreateNewProjectInternalAsync(bool showWizard)
    {
        try
        {
            var result = await _publisherStudioService.CreateProjectAsync("New Publisher");
            if (result.Success && result.Data != null)
            {
                CurrentProject = result.Data;
                await InitializeChildViewModelsAsync();
                StatusMessage = showWizard ? "New project created - configure your publisher profile to get started" : "New project created";
                _logger.LogInformation("Created new publisher project");
            }
            else
            {
                StatusMessage = $"Failed to create project: {result.FirstError}";
                _logger.LogError("Failed to create new project: {Error}", result.FirstError);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _logger.LogError(ex, "Error creating new project");
        }
    }

    /// <summary>
    /// Adds a new catalog to the project.
    /// </summary>
    [RelayCommand]
    private void AddCatalog()
    {
        if (CurrentProject == null) return;

        CurrentProject.Catalogs ??= [];
        CurrentProject.Catalog ??= new();
        CurrentProject.Catalog.Publisher ??= new();
        CurrentProject.Catalog.Content ??= [];

        var newId = $"catalog-{CurrentProject.Catalogs.Count + 1}";
        var newCatalog = new NamedCatalog
        {
            Id = newId,
            Name = $"Catalog {CurrentProject.Catalogs.Count + 1}",
            FileName = $"catalog-{newId}.json",
            Catalog = new() { Publisher = CurrentProject.Catalog.Publisher },
        };

        CurrentProject.Catalogs.Add(newCatalog);
        Catalogs.Add(newCatalog);
        SelectedCatalog = newCatalog;
        MarkDirty();
        OnPropertyChanged(nameof(CanRemoveCatalog));
        _logger.LogInformation("Added new catalog: {CatalogId}", newId);
    }

    /// <summary>
    /// Removes a catalog from the project.
    /// </summary>
    [RelayCommand]
    private void RemoveCatalog(NamedCatalog catalog)
    {
        if (CurrentProject == null || catalog == null) return;
        if (CurrentProject.Catalogs.Count <= 1)
        {
            StatusMessage = "Cannot remove the last catalog";
            return;
        }

        CurrentProject.Catalogs.Remove(catalog);
        Catalogs.Remove(catalog);
        SelectedCatalog = Catalogs.FirstOrDefault();
        MarkDirty();
        OnPropertyChanged(nameof(CanRemoveCatalog));
        _logger.LogInformation("Removed catalog: {CatalogId}", catalog.Id);
    }

    /// <summary>
    /// Migrates a single-catalog project to multi-catalog format.
    /// </summary>
    private void MigrateProjectToMultiCatalog()
    {
        if (CurrentProject == null) return;

        CurrentProject.Catalogs ??= [];
        CurrentProject.Catalog ??= new();
        CurrentProject.Catalog.Publisher ??= new();
        CurrentProject.Catalog.Content ??= [];

        // If project has no catalogs list, ensure a default catalog exists
        if (CurrentProject.Catalogs.Count == 0)
        {
            var defaultCatalog = new NamedCatalog
            {
                Id = "default",
                Name = "Content",
                Catalog = CurrentProject.Catalog,
                FileName = CurrentProject.CatalogFileName ?? HostingConstants.DefaultCatalogFileName,
            };
            CurrentProject.Catalogs.Add(defaultCatalog);
            if (CurrentProject.Catalog?.Content?.Count > 0)
            {
                _logger.LogInformation("Migrated single catalog to multi-catalog format");
            }
        }
    }

    /// <summary>
    /// Checks if hosting state recovery is needed for the project.
    /// </summary>
    private void CheckHostingStateRecovery()
    {
        if (CurrentProject == null || string.IsNullOrEmpty(CurrentProject.ProjectPath))
            return;

        // Check if hosting state file exists
        if (!_hostingStateManager.StateFileExists(CurrentProject.ProjectPath))
        {
            CurrentProject.Catalogs ??= [];

            // If this project has previously been published (has catalogs with URLs), prompt recovery
            var hasPublishedUrls = CurrentProject.Catalogs.Any(c =>
                c.Catalog?.Content != null &&
                c.Catalog.Content.Any(item =>
                    item.Releases != null &&
                    item.Releases.Any(r =>
                        r.Artifacts != null &&
                        r.Artifacts.Any(a => !string.IsNullOrEmpty(a.DownloadUrl)))));

            if (hasPublishedUrls)
            {
                IsRecoveryNeeded = true;
                StatusMessage = "Hosting state missing - recovery may be needed. Use Publish & Share tab to reconnect.";
                _logger.LogWarning("Project appears to have been published but hosting state is missing");
            }
        }
    }

    private async Task InitializeChildViewModelsAsync()
    {
        if (CurrentProject == null)
        {
            return;
        }

        CurrentProject.Catalogs ??= [];
        CurrentProject.Catalog ??= new();
        CurrentProject.Catalog.Publisher ??= new();
        CurrentProject.Catalog.Content ??= [];

        // Ensure multi-catalog migration
        MigrateProjectToMultiCatalog();

        // Populate catalogs collection
        Catalogs.Clear();
        foreach (var catalog in CurrentProject.Catalogs)
        {
            if (catalog.Catalog != null)
            {
                catalog.Catalog.Publisher = CurrentProject.Catalog.Publisher;
                catalog.Catalog.Content ??= [];
            }

            Catalogs.Add(catalog);
        }

        var selectedCatalog = Catalogs.FirstOrDefault();
        if (selectedCatalog == null)
        {
            selectedCatalog = new NamedCatalog
            {
                Id = "main",
                Name = "Main Catalog",
                Catalog = CurrentProject.Catalog,
                FileName = CurrentProject.CatalogFileName ?? HostingConstants.DefaultCatalogFileName,
            };
            Catalogs.Add(selectedCatalog);
        }

        SelectedCatalog = selectedCatalog;

        PublisherProfileViewModel = new GenHub.Features.Tools.ViewModels.PublisherProfileViewModel(CurrentProject, this, _logger);
        ContentLibraryViewModel = new GenHub.Features.Tools.ViewModels.ContentLibraryViewModel(CurrentProject, selectedCatalog, this, _logger, _dialogService);
        PublishShareViewModel = new GenHub.Features.Tools.ViewModels.PublishShareViewModel(CurrentProject, _publisherStudioService, _logger, _hostingProviderFactory, _hostingStateManager);
        ReferralsViewModel = new GenHub.Features.Tools.ViewModels.ReferralsViewModel(CurrentProject, this, _logger, _dialogService);

        // Check for hosting state recovery
        CheckHostingStateRecovery();

        OnPropertyChanged(nameof(IsSetupComplete));
        OnPropertyChanged(nameof(ShouldShowSetupOverlay));

        await Task.CompletedTask;
    }
}
