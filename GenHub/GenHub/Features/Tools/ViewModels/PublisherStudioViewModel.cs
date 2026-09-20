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
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Main ViewModel for Publisher Studio.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods mutate CommunityToolkit generated instance properties.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel properties and methods mutate CommunityToolkit generated instance properties.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required dependencies for Publisher Studio operations.")]
[method: System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required dependencies for Publisher Studio operations.")]
public partial class PublisherStudioViewModel(
    ILogger<PublisherStudioViewModel> logger,
    IPublisherStudioService publisherStudioService,
    IPublisherStudioDialogService dialogService,
    IHostingProviderFactory? hostingProviderFactory = null,
    IHostingStateManager? hostingStateManager = null,
    INotificationService? notificationService = null,
    IConfigurationProviderService? configurationProvider = null,
    ILocalizationService? localizationService = null,
    IHostingCredentialStore? credentialStore = null) : ObservableObject, IDisposable
{
    /// <summary>Tab index for the Profile tab.</summary>
    public const int TabProfile = 0;

    /// <summary>Tab index for the Catalogs tab.</summary>
    public const int TabCatalogs = 1;

    /// <summary>Tab index for the Hosting &amp; Storage tab.</summary>
    public const int TabHostingStorage = 2;

    /// <summary>Tab index for the Referrals tab.</summary>
    public const int TabReferrals = 3;

    /// <summary>Tab index for the Publish &amp; Share tab.</summary>
    public const int TabPublishShare = 4;

    private readonly string _settingsPath = Path.Combine(
        configurationProvider?.GetApplicationDataPath() ?? Path.GetTempPath(),
        "GenHub",
        "publisher_studio_settings.json");

    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private bool _statusLocalizationHooked;

    [ObservableProperty]
    private PublisherStudioProject? _currentProject;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private bool _hasDefinitionChanges = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<NamedCatalog> _catalogs = [];

    [ObservableProperty]
    private NamedCatalog? _selectedCatalog;

    [ObservableProperty]
    private bool _isRecoveryNeeded;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.PublisherProfileViewModel? _publisherProfileViewModel;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.ContentLibraryViewModel? _contentLibraryViewModel;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.PublishShareViewModel? _publishShareViewModel;

    [ObservableProperty]
    private GenHub.Features.Tools.ViewModels.ReferralsViewModel? _referralsViewModel;

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

    /// <summary>
    /// Gets localized status-bar text for the current project.
    /// </summary>
    public string ProjectStatusText =>
        CurrentProject?.ProjectName
        ?? GetStatusString("Tools.PublisherStudio.Studio.NoProjectLoaded", "No project loaded");

    /// <summary>
    /// Gets localized status-bar summary of the catalog count.
    /// </summary>
    public string CatalogSummaryText =>
        GetStatusString("Tools.PublisherStudio.Studio.CatalogCountFormat", "{0} catalogs", Catalogs.Count);

    private string StudioNotificationTitle =>
        localizationService?.GetString("Tools.PublisherStudio.Title")
        ?? localizationService?.GetString("Tools.PublisherStudio.Studio.Title")
        ?? "Publisher Studio";

    /// <summary>
    /// Marks the current project as dirty (having unsaved changes).
    /// Any project edit may affect the published provider definition, so definition
    /// change tracking is raised together with the unsaved flag.
    /// </summary>
    public void MarkDirty()
    {
        if (CurrentProject != null)
        {
            CurrentProject.IsDirty = true;
            HasUnsavedChanges = true;
            HasDefinitionChanges = true;
            RefreshSetupState();
        }
    }

    /// <summary>
    /// Re-evaluates the publisher setup state and notifies bindings so tabs unlock once a profile is saved.
    /// </summary>
    public void RefreshSetupState()
    {
        OnPropertyChanged(nameof(IsSetupComplete));
        OnPropertyChanged(nameof(ShouldShowSetupOverlay));
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

        await _saveLock.WaitAsync();
        try
        {
            await SaveProjectCoreAsync(CurrentProject, silent: false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Uploads the provider definition to the connected hosting provider.
    /// Bound to the header action button; enabled only when definition changes are pending.
    /// The project is saved silently first so the uploaded definition always matches disk.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task UploadDefinitionAsync()
    {
        if (CurrentProject == null || PublishShareViewModel == null)
        {
            return;
        }

        if (PublishShareViewModel.IsUploading)
        {
            var busyTitle = localizationService?.GetString("Tools.PublisherStudio.Publish.UploadInProgressTitle") ?? "Upload In Progress";
            var busyMessage = localizationService?.GetString("Tools.PublisherStudio.Hosting.UploadInProgress") ?? "Another upload is already in progress.";
            notificationService?.ShowWarning(busyTitle, busyMessage, NotificationDurations.Medium);
            return;
        }

        await SaveProjectSilentAsync();
        await PublishShareViewModel.UploadProviderDefinitionAsync();
    }

    /// <summary>
    /// Saves the current project without a success toast.
    /// Used by operations that already report their own outcome to the user.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SaveProjectSilentAsync()
    {
        if (CurrentProject == null)
        {
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            await SaveProjectCoreAsync(CurrentProject, silent: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Handles a file or directory path dropped into Publisher Studio.
    /// Switches to the Content Library tab and opens the Add Content dialog prefilled with the item details.
    /// </summary>
    /// <param name="path">The dropped file or directory path.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates CommunityToolkit generated instance properties in partial view model")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "Mutates CommunityToolkit generated instance properties in partial view model")]
    public async Task HandleDroppedPathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        logger.LogInformation("Handling dropped path: {Path}", path);

        // Switch to Content Library tab
        SelectedTabIndex = TabCatalogs;

        if (ContentLibraryViewModel != null)
        {
            await ContentLibraryViewModel.AddContentWithPathAsync(path);
        }
    }

    /// <summary>
    /// Initializes the view model by loading the last used or default project.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            var lastPath = await LoadLastProjectPathAsync();
            if (!string.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
            {
                await LoadProjectFromPathAsync(lastPath, announce: false);
                return;
            }

            var defaultPath = GetDefaultProjectPath();
            if (File.Exists(defaultPath))
            {
                await LoadProjectFromPathAsync(defaultPath, announce: false);
                return;
            }

            await CreateNewProjectInternalAsync(showWizard: false);
            if (CurrentProject != null)
            {
                CurrentProject.ProjectPath = defaultPath;
                await SaveProjectAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize PublisherStudioViewModel");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Catalogs.CollectionChanged -= OnStatusCatalogsChanged;
            if (localizationService != null)
            {
                localizationService.PropertyChanged -= OnStatusCultureChanged;
            }

            PublishShareViewModel?.Dispose();
            PublishShareViewModel = null;
            _saveLock.Dispose();
        }
    }

    private static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "catalog";
        var slug = text.ToLowerInvariant().Trim();
        slug = Regex.Replace(slug, @"\s+", "-", RegexOptions.None, TimeSpan.FromSeconds(1));
        slug = Regex.Replace(slug, @"[^a-z0-9-]", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(1));
        slug = Regex.Replace(slug, @"-+", "-", RegexOptions.None, TimeSpan.FromSeconds(1));
        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? "catalog" : slug;
    }

    private async Task SaveProjectCoreAsync(PublisherStudioProject project, bool silent)
    {
        try
        {
            // Auto-assign default project path if empty to guarantee persistence
            if (string.IsNullOrEmpty(project.ProjectPath))
            {
                project.ProjectPath = GetDefaultProjectPath();
            }

            var result = await publisherStudioService.SaveProjectAsync(project);
            if (result.Success)
            {
                HasUnsavedChanges = false;
                StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.ProjectSavedHint", "Project saved. Go to 'Publish & Share' to export and release.");
                logger.LogInformation("Saved project: {ProjectName}", project.ProjectName);

                // Persist the project path for auto-load on next launch
                if (!string.IsNullOrEmpty(project.ProjectPath))
                {
                    await SaveLastProjectPathAsync(project.ProjectPath);
                }

                if (!silent)
                {
                    var savedTitle = localizationService?.GetString("Tools.PublisherStudio.Notification.ProjectSavedTitle") ?? "Project Saved";
                    var savedMsgTemplate = localizationService?.GetString("Tools.PublisherStudio.Notification.ProjectSavedMessage") ?? "Your publisher project '{0}' has been saved successfully.";
                    notificationService?.ShowSuccess(
                        savedTitle,
                        string.Format(savedMsgTemplate, project.ProjectName),
                        autoDismissMs: 4000);
                }

                // Force a dirty state update to refresh UI
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
            else
            {
                StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.SaveProjectFailedFormat", "Failed to save: {0}", result.FirstError);
                logger.LogError("Failed to save project: {Error}", result.FirstError);

                var saveFailedTitle = localizationService?.GetString("Tools.PublisherStudio.Notification.SaveFailedTitle") ?? "Save Failed";
                notificationService?.ShowError(
                    saveFailedTitle,
                    result.FirstError ?? localizationService?.GetString("Tools.PublisherStudio.Notification.SaveUnknownError") ?? "An unknown error occurred while saving the project.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.SaveProjectErrorFormat", "Error saving: {0}", ex.Message);
            logger.LogError(ex, "Error saving project");

            var saveErrorTitle = localizationService?.GetString("Tools.PublisherStudio.Notification.SaveErrorTitle") ?? "Save Error";
            var saveErrorTemplate = localizationService?.GetString("Tools.PublisherStudio.Notification.SaveErrorMessageFormat") ?? "An error occurred while saving: {0}";
            notificationService?.ShowError(
                saveErrorTitle,
                string.Format(saveErrorTemplate, ex.Message));
        }
        finally
        {
            // Profile data is written to the in-memory project before saving, so refresh
            // the setup state even when persistence fails to keep tab bindings accurate.
            RefreshSetupState();
        }
    }

    private async Task SaveProjectAfterPublishAsync()
    {
        MarkDirty();
        await SaveProjectSilentAsync();
    }

    private string GetStatusString(string key, string fallback, params object?[] args)
    {
        var template = localizationService?.GetString(key);
        if (string.IsNullOrEmpty(template) || template == key)
        {
            template = fallback;
        }

        return args.Length == 0 ? template : string.Format(template, args);
    }

    private void EnsureStatusLocalizationHooked()
    {
        if (_statusLocalizationHooked)
        {
            return;
        }

        _statusLocalizationHooked = true;
        Catalogs.CollectionChanged += OnStatusCatalogsChanged;
        if (localizationService != null)
        {
            localizationService.PropertyChanged += OnStatusCultureChanged;
        }
    }

    private void OnStatusCatalogsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(CatalogSummaryText));

    private void OnStatusCultureChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ILocalizationService.CurrentCulture))
        {
            OnPropertyChanged(nameof(ProjectStatusText));
            OnPropertyChanged(nameof(CatalogSummaryText));
            ContentLibraryViewModel?.RefreshLocalizedText();
            PublishShareViewModel?.RefreshLocalizedText();
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShouldShowSetupOverlay));
        if (value == TabCatalogs)
        {
            ContentLibraryViewModel?.RefreshHostingHint();
        }

        if (value is TabHostingStorage or TabPublishShare && PublishShareViewModel != null)
        {
            if (PublishShareViewModel.HostingProviders.Count == 0)
            {
                PublishShareViewModel.ReloadHostingProviders();
            }

            PublishShareViewModel.RefreshUploadHierarchy();
            PublishShareViewModel.RefreshHostedAssets();
        }
    }

    partial void OnSelectedCatalogChanged(NamedCatalog? value)
    {
        if (value != null && CurrentProject != null)
        {
            ContentLibraryViewModel = new GenHub.Features.Tools.ViewModels.ContentLibraryViewModel(CurrentProject, value, this, logger, dialogService, notificationService, localizationService);
        }
    }

    partial void OnCurrentProjectChanged(PublisherStudioProject? value)
    {
        _ = value;
        OnPropertyChanged(nameof(ProjectStatusText));
    }

    /// <summary>
    /// Navigates to a specific tab index.
    /// </summary>
    [RelayCommand]
    private void SelectTab(object? parameter)
    {
        if (parameter is int i)
        {
            SelectedTabIndex = i;
        }
        else if (parameter != null && int.TryParse(parameter.ToString(), out var parsed))
        {
            SelectedTabIndex = parsed;
        }
    }

    /// <summary>
    /// Navigates to the Publisher Profile tab.
    /// </summary>
    [RelayCommand]
    private void GoToProfileTab()
    {
        SelectedTabIndex = 0;
    }

    /// <summary>
    /// Gets the default project file path in user AppData.
    /// </summary>
    private string GetDefaultProjectPath()
    {
        var baseDir = configurationProvider?.GetApplicationDataPath()
            ?? Path.Combine(Path.GetTempPath(), "GenHub");
        var projectDir = Path.Combine(baseDir, "PublisherStudio", "projects");
        if (!Directory.Exists(projectDir))
        {
            Directory.CreateDirectory(projectDir);
        }

        return Path.Combine(projectDir, "default-publisher.json");
    }

    [RelayCommand]
    private async Task LoadProjectAsync()
    {
        try
        {
            var filePath = await dialogService.ShowProjectOpenPromptAsync("Load Project");
            if (string.IsNullOrEmpty(filePath))
                return;

            await LoadProjectFromPathAsync(filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.LoadProjectErrorFormat", "Error loading project: {0}", ex.Message);
            notificationService?.ShowError(StudioNotificationTitle, StatusMessage, NotificationDurations.Long);
            logger.LogError(ex, "Error loading project");
        }
    }

    private async Task LoadProjectFromPathAsync(string filePath, bool announce = true)
    {
        try
        {
            var result = await publisherStudioService.LoadProjectAsync(filePath);
            if (result.Success && result.Data != null)
            {
                CurrentProject = result.Data;
                CurrentProject.ProjectPath = filePath;
                await InitializeChildViewModelsAsync();
                await SaveLastProjectPathAsync(filePath);
                HasUnsavedChanges = false;
                StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.ProjectLoadedFormat", "Project loaded: {0}", CurrentProject.ProjectName);
                if (announce)
                {
                    notificationService?.ShowSuccess(StudioNotificationTitle, StatusMessage, NotificationDurations.Short);
                }

                logger.LogInformation("Loaded publisher project from {Path}", filePath);
            }
            else
            {
                StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.LoadProjectFailedFormat", "Failed to load project: {0}", result.FirstError);
                notificationService?.ShowError(StudioNotificationTitle, StatusMessage, NotificationDurations.Long);
                logger.LogError("Failed to load project: {Error}", result.FirstError);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.LoadProjectErrorFormat", "Error loading project: {0}", ex.Message);
            notificationService?.ShowError(StudioNotificationTitle, StatusMessage, NotificationDurations.Long);
            logger.LogError(ex, "Error loading project from {Path}", filePath);
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
            logger.LogWarning(ex, "Failed to save publisher studio settings");
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
            logger.LogWarning(ex, "Failed to load last project path from settings");
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

        await CreateNewProjectInternalAsync(showWizard: false);
    }

    private async Task CreateNewProjectInternalAsync(bool showWizard)
    {
        try
        {
            var result = await publisherStudioService.CreateProjectAsync("New Publisher");
            if (result.Success && result.Data != null)
            {
                CurrentProject = result.Data;
                CurrentProject.ProjectPath = GetDefaultProjectPath();
                await InitializeChildViewModelsAsync();
                StatusMessage = showWizard
                    ? GetStatusString("Tools.PublisherStudio.Studio.ProjectCreatedSetupHint", "New project created - configure your publisher profile to get started")
                    : GetStatusString("Tools.PublisherStudio.Studio.ProjectCreated", "New project created");
                notificationService?.ShowSuccess(StudioNotificationTitle, StatusMessage, NotificationDurations.Medium);
                logger.LogInformation("Created new publisher project");
            }
            else
            {
                StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.CreateProjectFailedFormat", "Failed to create project: {0}", result.FirstError);
                notificationService?.ShowError(StudioNotificationTitle, StatusMessage, NotificationDurations.Long);
                logger.LogError("Failed to create new project: {Error}", result.FirstError);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.CreateProjectErrorFormat", "Error creating project: {0}", ex.Message);
            notificationService?.ShowError(StudioNotificationTitle, StatusMessage, NotificationDurations.Long);
            logger.LogError(ex, "Error creating new project");
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

        var existingIds = new System.Collections.Generic.HashSet<string>(
            CurrentProject.Catalogs.Select(c => c.Id),
            System.StringComparer.OrdinalIgnoreCase);
        int index = CurrentProject.Catalogs.Count + 1;
        while (existingIds.Contains($"catalog-{index}"))
        {
            index++;
        }

        var newId = $"catalog-{index}";
        var newCatalog = new NamedCatalog
        {
            Id = newId,
            Name = $"Catalog {index}",
            FileName = $"{newId}.json",
            Catalog = new() { Publisher = CurrentProject.Catalog.Publisher },
        };

        CurrentProject.Catalogs.Add(newCatalog);
        Catalogs.Add(newCatalog);
        SelectedCatalog = newCatalog;
        MarkDirty();
        OnPropertyChanged(nameof(CanRemoveCatalog));
        PublishShareViewModel?.SyncAvailableCatalogs();
        logger.LogInformation("Added new catalog: {CatalogId}", newId);
    }

    /// <summary>
    /// Removes a catalog from the project.
    /// </summary>
    [RelayCommand]
    private async Task RemoveCatalogAsync(NamedCatalog catalog)
    {
        if (CurrentProject == null || catalog == null) return;
        if (CurrentProject.Catalogs.Count <= 1)
        {
            StatusMessage = localizationService?.GetString("Tools.PublisherStudio.Studio.CannotRemoveLastCatalog") ?? "Cannot remove the last catalog";
            notificationService?.ShowWarning(StudioNotificationTitle, StatusMessage, NotificationDurations.Medium);
            return;
        }

        var deleteTitle = localizationService?.GetString("Tools.PublisherStudio.Studio.DeleteCatalogTitle") ?? "Delete Catalog";
        var deleteMsgTemplate = localizationService?.GetString("Tools.PublisherStudio.Studio.DeleteCatalogMessage") ?? "Are you sure you want to delete catalog '{0}'? This cannot be undone.";
        var deleteConfirm = localizationService?.GetString("Tools.PublisherStudio.Studio.DeleteConfirm") ?? "Delete";

        var confirmed = await dialogService.ShowConfirmationAsync(
            deleteTitle,
            string.Format(deleteMsgTemplate, catalog.Name),
            confirmText: deleteConfirm,
            sessionKey: "DeleteCatalogConfirmation");

        if (!confirmed)
        {
            return;
        }

        CurrentProject.Catalogs.Remove(catalog);
        Catalogs.Remove(catalog);
        SelectedCatalog = Catalogs.FirstOrDefault();
        MarkDirty();
        OnPropertyChanged(nameof(CanRemoveCatalog));
        PublishShareViewModel?.SyncAvailableCatalogs();
        logger.LogInformation("Removed catalog: {CatalogId}", catalog.Id);
    }

    /// <summary>
    /// Renames a catalog in the project.
    /// </summary>
    /// <param name="catalog">Optional catalog to rename. If null, the currently selected catalog is renamed.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task RenameCatalogAsync(NamedCatalog? catalog = null)
    {
        var target = catalog ?? SelectedCatalog;
        if (target == null) return;

        var newName = await dialogService.ShowRenameCatalogDialogAsync(target.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == target.Name) return;

        var newId = Slugify(newName);
        if (CurrentProject?.Catalogs != null && CurrentProject.Catalogs.Any(c => c != target && string.Equals(c.Id, newId, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = string.Format(
                localizationService?.GetString("Tools.PublisherStudio.Studio.CatalogAlreadyExists") ?? "A catalog with ID '{0}' already exists.",
                newId);
            var dupTitle = localizationService?.GetString("Tools.PublisherStudio.Notification.DuplicateCatalogIdTitle") ?? "Duplicate Catalog ID";
            notificationService?.ShowWarning(dupTitle, StatusMessage);
            return;
        }

        var oldId = target.Id;
        target.Name = newName.Trim();
        target.Id = newId;
        target.FileName = target.Id.StartsWith("catalog-", StringComparison.OrdinalIgnoreCase)
            ? $"{target.Id}.json"
            : $"catalog-{target.Id}.json";

        var idx = Catalogs.IndexOf(target);
        if (idx >= 0)
        {
            Catalogs[idx] = target;
            SelectedCatalog = target;
        }

        MarkDirty();
        if (PublishShareViewModel != null)
        {
            await PublishShareViewModel.RenameCatalogInHostingStateAsync(oldId, target.Id, target.Name, target.FileName);
            PublishShareViewModel.SyncAvailableCatalogs();
        }

        await SaveProjectAsync();
        StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.CatalogRenamedFormat", "Renamed catalog to '{0}'", target.Name);
        notificationService?.ShowSuccess(StudioNotificationTitle, StatusMessage, NotificationDurations.Short);
        logger.LogInformation("Renamed catalog to {CatalogName} ({CatalogId})", target.Name, target.Id);
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
                logger.LogInformation("Migrated single catalog to multi-catalog format");
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
        if (hostingStateManager == null || !hostingStateManager.StateFileExists(CurrentProject.ProjectPath))
        {
            CurrentProject.Catalogs ??= [];

            // If this project has previously been published (has catalogs with URLs), prompt recovery
            var hasPublishedUrls = CurrentProject.Catalogs.Any(c =>
                c?.Catalog?.Content is { } content &&
                content.Any(item =>
                    item?.Releases is { } releases &&
                    releases.Any(r =>
                        r?.Artifacts is { } artifacts &&
                        artifacts.Any(a => a != null && !string.IsNullOrEmpty(a.DownloadUrl)))));

            if (hasPublishedUrls)
            {
                IsRecoveryNeeded = true;
                StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.HostingRecoveryNeeded", "Hosting state missing - recovery may be needed. Use Publish & Share tab to reconnect.");
                notificationService?.ShowWarning(StudioNotificationTitle, StatusMessage, NotificationDurations.VeryLong);
                logger.LogWarning("Project appears to have been published but hosting state is missing");
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
            if (catalog?.Catalog != null)
            {
                catalog.Catalog.Publisher = CurrentProject.Catalog.Publisher;
                catalog.Catalog.Content ??= [];
            }

            if (catalog != null)
            {
                Catalogs.Add(catalog);
            }
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

        PublisherProfileViewModel = new GenHub.Features.Tools.ViewModels.PublisherProfileViewModel(CurrentProject, this, logger, notificationService, localizationService);
        ContentLibraryViewModel = new GenHub.Features.Tools.ViewModels.ContentLibraryViewModel(CurrentProject, selectedCatalog, this, logger, dialogService, notificationService, localizationService);
        PublishShareViewModel?.Dispose();
        PublishShareViewModel = new GenHub.Features.Tools.ViewModels.PublishShareViewModel(CurrentProject, publisherStudioService, logger, hostingProviderFactory, hostingStateManager, notificationService, localizationService, credentialStore);
        PublishShareViewModel.SaveProjectCallback = SaveProjectAfterPublishAsync;
        PublishShareViewModel.LibraryRefreshCallback = () => ContentLibraryViewModel?.RefreshContentDisplay();
        PublishShareViewModel.DefinitionUploadedCallback = () => HasDefinitionChanges = false;
        await PublishShareViewModel.InitializeAsync();
        HasDefinitionChanges = !PublishShareViewModel.IsDefinitionPublished;
        ReferralsViewModel = new GenHub.Features.Tools.ViewModels.ReferralsViewModel(CurrentProject, this, logger, dialogService, notificationService, localizationService);

        // Check for hosting state recovery
        CheckHostingStateRecovery();

        OnPropertyChanged(nameof(IsSetupComplete));
        OnPropertyChanged(nameof(ShouldShowSetupOverlay));
        EnsureStatusLocalizationHooked();
        OnPropertyChanged(nameof(ProjectStatusText));
        OnPropertyChanged(nameof(CatalogSummaryText));

        await Task.CompletedTask;
    }
}
