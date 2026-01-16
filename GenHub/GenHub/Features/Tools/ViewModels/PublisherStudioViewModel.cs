using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    /// Gets a value indicating whether the publisher setup is complete.
    /// Setup is complete when Publisher ID and Name are configured.
    /// </summary>
    public bool IsSetupComplete =>
        CurrentProject != null &&
        !string.IsNullOrWhiteSpace(CurrentProject.Catalog.Publisher.Id) &&
        !string.IsNullOrWhiteSpace(CurrentProject.Catalog.Publisher.Name);

    /// <summary>
    /// Gets a value indicating whether the setup overlay should be shown.
    /// </summary>
    public bool ShouldShowSetupOverlay => !IsSetupComplete && SelectedTabIndex != 0;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShouldShowSetupOverlay));
    }

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.PublisherProfileViewModel? _publisherProfileViewModel;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.ContentLibraryViewModel? _contentLibraryViewModel;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.PublishShareViewModel? _publishShareViewModel;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.ReferralsViewModel? _referralsViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherStudioViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="publisherStudioService">The publisher studio service.</param>
    /// <param name="dialogService">The dialog service.</param>
    /// <param name="hostingProviderFactory">The hosting provider factory.</param>
    /// <param name="hostingStateManager">The hosting state manager.</param>
    /// <param name="notificationService">The notification service.</param>
    public PublisherStudioViewModel(
        ILogger<PublisherStudioViewModel> logger,
        IPublisherStudioService publisherStudioService,
        IPublisherStudioDialogService dialogService,
        IHostingProviderFactory? hostingProviderFactory = null,
        IHostingStateManager? hostingStateManager = null,
        INotificationService? notificationService = null)
    {
        _logger = logger;
        _publisherStudioService = publisherStudioService;
        _dialogService = dialogService;
        _hostingProviderFactory = hostingProviderFactory;
        _hostingStateManager = hostingStateManager ?? new HostingStateManager(Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<HostingStateManager>());
        _notificationService = notificationService;

        // Initialize with a new project (Silent)
        _ = CreateNewProjectInternalAsync(showWizard: false);
    }

    /// <summary>
    /// Marks the project as having unsaved changes.
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

    partial void OnSelectedCatalogChanged(NamedCatalog? value)
    {
        if (value != null && CurrentProject != null)
        {
            ContentLibraryViewModel = new GenHub.Features.Tools.ViewModels.ContentLibraryViewModel(CurrentProject, value, this, _logger, _dialogService);
        }
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
    /// Creates a new publisher project (Interactive).
    /// </summary>
    [RelayCommand]
    private async Task CreateNewProjectAsync()
    {
        await CreateNewProjectInternalAsync(showWizard: true);
    }

    /// <summary>
    /// Adds a new catalog to the project.
    /// </summary>
    [RelayCommand]
    private void AddCatalog()
    {
        if (CurrentProject == null) return;

        var newId = $"catalog-{CurrentProject.Catalogs.Count + 1}";
        var newCatalog = new NamedCatalog
        {
            Id = newId,
            Name = $"Catalog {CurrentProject.Catalogs.Count + 1}",
            FileName = $"catalog-{newId}.json",
        };

        CurrentProject.Catalogs.Add(newCatalog);
        Catalogs.Add(newCatalog);
        SelectedCatalog = newCatalog;
        MarkDirty();
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
        _logger.LogInformation("Removed catalog: {CatalogId}", catalog.Id);
    }

    /// <summary>
    /// Migrates a single-catalog project to multi-catalog format.
    /// </summary>
    private void MigrateProjectToMultiCatalog()
    {
        if (CurrentProject == null) return;

        // If project has no catalogs list but has a single Catalog, migrate it
        if (CurrentProject.Catalogs.Count == 0 && CurrentProject.Catalog.Content.Count > 0)
        {
            var defaultCatalog = new NamedCatalog
            {
                Id = "default",
                Name = "Content",
                Catalog = CurrentProject.Catalog,
                FileName = CurrentProject.CatalogFileName,
            };
            CurrentProject.Catalogs.Add(defaultCatalog);
            _logger.LogInformation("Migrated single catalog to multi-catalog format");
        }
        else if (CurrentProject.Catalogs.Count == 0)
        {
            // Create an empty default catalog
            var defaultCatalog = new NamedCatalog
            {
                Id = "default",
                Name = "Content",
                FileName = "catalog.json",
            };
            CurrentProject.Catalogs.Add(defaultCatalog);
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
            // If this project has previously been published (has catalogs with URLs), prompt recovery
            var hasPublishedUrls = CurrentProject.Catalogs.Any(c =>
                c.Catalog.Content.Any(item =>
                    item.Releases.Any(r =>
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

        // Ensure multi-catalog migration
        MigrateProjectToMultiCatalog();

        // Populate catalogs collection
        Catalogs.Clear();
        foreach (var catalog in CurrentProject.Catalogs)
        {
            Catalogs.Add(catalog);
        }

        SelectedCatalog = Catalogs.FirstOrDefault();

        PublisherProfileViewModel = new GenHub.Features.Tools.ViewModels.PublisherProfileViewModel(CurrentProject, this, _hostingProviderFactory!, _logger);
        PublisherProfileViewModel.RefreshConnectionStatus();
        ContentLibraryViewModel = new GenHub.Features.Tools.ViewModels.ContentLibraryViewModel(CurrentProject, SelectedCatalog!, this, _logger, _dialogService);
        PublishShareViewModel = new GenHub.Features.Tools.ViewModels.PublishShareViewModel(CurrentProject, _publisherStudioService, _logger, _hostingProviderFactory, _hostingStateManager);
        ReferralsViewModel = new GenHub.Features.Tools.ViewModels.ReferralsViewModel(CurrentProject, this, _logger, _dialogService);

        // Check for hosting state recovery
        CheckHostingStateRecovery();

        OnPropertyChanged(nameof(IsSetupComplete));
        OnPropertyChanged(nameof(ShouldShowSetupOverlay));
    }
}
