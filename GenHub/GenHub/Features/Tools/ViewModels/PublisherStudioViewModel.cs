using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;
using GenHub.Features.Tools.Services.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    IHostingCredentialStore? credentialStore = null,
    IPublisherCatalogParser? catalogParser = null) : ObservableObject, IDisposable
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

    private const string NewPublisherName = "New Publisher";

    private static readonly JsonSerializerOptions CatalogImportOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _settingsPath = Path.Combine(
        configurationProvider?.GetApplicationDataPath() ?? Path.GetTempPath(),
        AppConstants.AppName,
        PublisherStudioConstants.SettingsFileName);

    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private bool _statusLocalizationHooked;

    [ObservableProperty]
    private PublisherStudioProject? _currentProject;

    [ObservableProperty]
    private int _selectedTabIndex = TabHostingStorage;

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
        (!string.IsNullOrWhiteSpace(CurrentProject?.Catalog?.Publisher?.Id) &&
        !string.IsNullOrWhiteSpace(CurrentProject?.Catalog?.Publisher?.Name)) ||
        (PublishShareViewModel?.IsProviderAuthenticated ?? false);

    /// <summary>
    /// Gets a value indicating whether the Referrals tab is visible/enabled.
    /// Temporarily disabled.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound in XAML")]
    public bool IsReferralsTabVisible => false;

    /// <summary>
    /// Gets a value indicating whether the setup overlay should be shown.
    /// In the cloud-first workflow, users land on Hosting &amp; Storage to connect and sync their
    /// definition or can choose manual profile configuration. The setup overlay only blocks
    /// Content Library and Publish tabs when setup has not been completed.
    /// </summary>
    public bool ShouldShowSetupOverlay => !IsSetupComplete && SelectedTabIndex != TabHostingStorage && SelectedTabIndex != TabProfile;

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
            if (PublishShareViewModel != null)
            {
                PublishShareViewModel.HasDefinitionChanges = true;
            }

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

        // A dropped catalog JSON definition is imported instead of staged as content
        if (IsJsonCatalogPath(path) && await ImportCatalogFromFileAsync(path))
        {
            return;
        }

        // Switch to Content Library tab
        SelectedTabIndex = TabCatalogs;

        if (ContentLibraryViewModel != null)
        {
            await ContentLibraryViewModel.AddContentWithPathAsync(path);
        }
    }

    /// <summary>
    /// Attempts to import a catalog from a JSON file path, prompting the user for confirmation.
    /// </summary>
    /// <param name="filePath">The file path to the catalog JSON.</param>
    /// <param name="announceFailures">True to toast parse failures to the user; false to fail silently for incidental drops.</param>
    /// <returns>True if the file was recognized as a catalog and processed; false otherwise.</returns>
    public async Task<bool> ImportCatalogFromFileAsync(string filePath, bool announceFailures = false)
    {
        var project = CurrentProject;
        if (project == null || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var catalog = ParseImportCatalog(content, filePath, announceFailures);
            if (catalog == null)
            {
                return false;
            }

            var itemCount = catalog.Content?.Count ?? 0;
            if (itemCount == 0 && string.IsNullOrWhiteSpace(catalog.Publisher?.Id))
            {
                return false;
            }

            var publisherName = ResolvePublisherDisplayName(catalog);
            var fileName = Path.GetFileName(filePath);
            var catalogName = BuildImportedCatalogName(catalog, filePath);

            var promptTitle = localizationService?.GetString("Tools.PublisherStudio.Studio.ImportCatalogPromptTitle") ?? "Add Catalog to Provider?";
            var promptTemplate = localizationService?.GetString("Tools.PublisherStudio.Studio.ImportCatalogPromptMessage") ??
                "A catalog file '{0}' was detected containing {1} content items by '{2}'.\n\nWould you like to add this catalog to your provider project?";
            var promptMessage = string.Format(promptTemplate, fileName, itemCount, publisherName);

            var confirmText = localizationService?.GetString("Tools.PublisherStudio.Studio.AddCatalogConfirm") ?? "Add Catalog";
            var cancelText = localizationService?.GetString("Common.Cancel") ?? "Cancel";

            var confirmed = await dialogService.ShowConfirmationAsync(
                promptTitle,
                promptMessage,
                confirmText: confirmText,
                cancelText: cancelText);

            if (!confirmed)
            {
                return true;
            }

            var namedCatalog = CreateImportedCatalogEntry(project, catalog, catalogName, fileName);
            AttachImportedCatalog(project, namedCatalog);

            await SaveProjectAsync();

            var successTemplate = localizationService?.GetString("Tools.PublisherStudio.Studio.CatalogImportedFormat") ??
                "Added catalog '{0}' with {1} content items.";
            var successMessage = string.Format(successTemplate, catalogName, itemCount);

            StatusMessage = successMessage;
            notificationService?.ShowSuccess(StudioNotificationTitle, successMessage, NotificationDurations.Medium);
            logger.LogInformation("Imported catalog {CatalogId} from {FilePath} with {ItemCount} items", namedCatalog.Id, filePath, itemCount);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import catalog from {FilePath}", filePath);
            var errTemplate = localizationService?.GetString("Tools.PublisherStudio.Studio.ImportCatalogErrorFormat") ??
                "Failed to import catalog: {0}";
            var errMessage = string.Format(errTemplate, ex.Message);
            notificationService?.ShowError(StudioNotificationTitle, errMessage, NotificationDurations.Long);
            return false;
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
                if (!IsSetupComplete)
                {
                    SelectedTabIndex = TabHostingStorage;
                }

                return;
            }

            var defaultPath = GetDefaultProjectPath();
            if (File.Exists(defaultPath))
            {
                await LoadProjectFromPathAsync(defaultPath, announce: false);
                if (!IsSetupComplete)
                {
                    SelectedTabIndex = TabHostingStorage;
                }

                return;
            }

            await CreateNewProjectInternalAsync(showWizard: false);
            if (CurrentProject != null)
            {
                CurrentProject.ProjectPath = defaultPath;
                await SaveProjectAsync();
            }

            SelectedTabIndex = TabHostingStorage;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize PublisherStudioViewModel");
        }
    }

    /// <summary>
    /// Reloads the studio UI state from the current project after an external definition or catalog is loaded.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task ReloadFromCurrentProjectAsync()
    {
        if (CurrentProject == null)
        {
            return;
        }

        SyncReloadCatalogsCollection();

        PublisherProfileViewModel?.LoadFromProject();
        ReferralsViewModel?.LoadFromProject();

        if (SelectedCatalog != null)
        {
            ContentLibraryViewModel = new GenHub.Features.Tools.ViewModels.ContentLibraryViewModel(CurrentProject, SelectedCatalog, this, logger, dialogService, notificationService, localizationService);
        }

        SyncPublishShareCatalogs();

        RefreshSetupState();
        OnPropertyChanged(nameof(ProjectStatusText));
        OnPropertyChanged(nameof(CatalogSummaryText));

        await Task.CompletedTask;
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

    private static bool IsJsonCatalogPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    private static string ResolvePublisherDisplayName(PublisherCatalog catalog)
    {
        if (!string.IsNullOrWhiteSpace(catalog.Publisher?.Name))
        {
            return catalog.Publisher.Name;
        }

        return !string.IsNullOrWhiteSpace(catalog.Publisher?.Id)
            ? catalog.Publisher.Id
            : "Unknown Publisher";
    }

    private static string BuildImportedCatalogName(PublisherCatalog catalog, string filePath)
    {
        var rawName = !string.IsNullOrWhiteSpace(catalog.Publisher?.Name)
            && !string.Equals(catalog.Publisher.Name, NewPublisherName, StringComparison.OrdinalIgnoreCase)
            ? $"{catalog.Publisher.Name} Catalog"
            : Path.GetFileNameWithoutExtension(filePath).Replace(".catalog", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!rawName.Contains('-') && !rawName.Contains('_'))
        {
            return rawName;
        }

        var words = rawName.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
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

    private PublisherCatalog? ParseImportCatalog(string content, string filePath, bool announceFailures)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            ReportImportFailure(filePath, "The file is empty.", announceFailures);
            return null;
        }

        PublisherCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<PublisherCatalog>(content, CatalogImportOptions);
        }
        catch (JsonException ex)
        {
            ReportImportFailure(filePath, ex.Message, announceFailures);
            return null;
        }

        if (catalog == null || (catalog.Content == null && catalog.Publisher == null))
        {
            ReportImportFailure(filePath, "The file is not a valid catalog.", announceFailures);
            return null;
        }

        NormalizeImportedPublisherTypes(catalog, filePath);

        if (catalogParser != null)
        {
            var validation = catalogParser.ValidateCatalog(catalog);
            if (!validation.Success)
            {
                ReportImportFailure(filePath, validation.FirstError ?? "The file is not a valid catalog.", announceFailures);
                return null;
            }
        }

        return catalog;
    }

    private void NormalizeImportedPublisherTypes(PublisherCatalog catalog, string filePath)
    {
        if (catalog.Content == null)
        {
            return;
        }

        // Imported items are adopted into the user's catalog. Publisher types outside the
        // native-pipeline allowlist would fail validation everywhere, so fall back to generic.
        foreach (var item in catalog.Content)
        {
            if (string.IsNullOrWhiteSpace(item.PublisherType))
            {
                continue;
            }

            var declared = CatalogManifestIdentity.ResolveDeclaredPublisherType(item.PublisherType);
            if (!item.PublisherType.Equals(declared, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "Catalog import from {FilePath}: publisherType '{PublisherType}' on '{ContentId}' is not a native pipeline; using generic.",
                    filePath,
                    item.PublisherType,
                    item.Id);
                item.PublisherType = string.Empty;
            }
        }
    }

    private void ReportImportFailure(string filePath, string reason, bool announceFailures)
    {
        logger.LogWarning("Catalog import skipped for {FilePath}: {Reason}", filePath, reason);
        if (!announceFailures)
        {
            return;
        }

        var errTemplate = localizationService?.GetString("Tools.PublisherStudio.Studio.ImportCatalogErrorFormat") ??
            "Failed to import catalog: {0}";
        notificationService?.ShowError(StudioNotificationTitle, string.Format(errTemplate, reason), NotificationDurations.Long);
    }

    private NamedCatalog CreateImportedCatalogEntry(PublisherStudioProject project, PublisherCatalog catalog, string catalogName, string fileName)
    {
        project.Catalogs ??= [];

        var baseSlug = Path.GetFileNameWithoutExtension(fileName)
            .Replace(".catalog", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant()
            .Replace(" ", "-");
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = "imported-catalog";
        }

        var existingIds = new HashSet<string>(project.Catalogs.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var newId = baseSlug;
        var counter = 2;
        while (existingIds.Contains(newId))
        {
            newId = $"{baseSlug}-{counter++}";
        }

        return new NamedCatalog
        {
            Id = newId,
            Name = catalogName,
            FileName = fileName,
            Catalog = catalog,
        };
    }

    private void AttachImportedCatalog(PublisherStudioProject project, NamedCatalog namedCatalog)
    {
        project.Catalogs ??= [];
        var defaultEmpty = project.Catalogs.FirstOrDefault(c =>
            c.Id == "default" && (c.Catalog?.Content == null || c.Catalog.Content.Count == 0));
        if (defaultEmpty != null && project.Catalogs.Count == 1)
        {
            project.Catalogs.Remove(defaultEmpty);
            Catalogs.Remove(defaultEmpty);
        }

        AdoptImportedPublisher(project, namedCatalog.Catalog.Publisher);

        project.Catalogs.Add(namedCatalog);
        Catalogs.Add(namedCatalog);
        SelectedCatalog = namedCatalog;

        MarkDirty();
        OnPropertyChanged(nameof(CanRemoveCatalog));
        PublishShareViewModel?.SyncAvailableCatalogs();
    }

    private void AdoptImportedPublisher(PublisherStudioProject project, PublisherProfile? publisher)
    {
        if (project.Catalog?.Publisher != null
            && !string.IsNullOrWhiteSpace(project.Catalog.Publisher.Id)
            && !string.Equals(project.Catalog.Publisher.Name, NewPublisherName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (publisher == null || string.IsNullOrWhiteSpace(publisher.Id))
        {
            return;
        }

        project.Catalog ??= new();
        project.Catalog.Publisher = publisher;
        if (string.IsNullOrWhiteSpace(project.ProjectName) || project.ProjectName == NewPublisherName)
        {
            project.ProjectName = publisher.Name ?? publisher.Id;
        }
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
        int target = -1;
        if (parameter is int i)
        {
            target = i;
        }
        else if (parameter != null && int.TryParse(parameter.ToString(), out var parsed))
        {
            target = parsed;
        }

        if (target < 0)
        {
            return;
        }

        // Temporarily disallow selecting Referrals tab
        if (target == TabReferrals)
        {
            return;
        }

        // When setup is incomplete, only HostingStorage and Profile (for manual setup) are accessible
        if (!IsSetupComplete && target != TabHostingStorage && target != TabProfile)
        {
            SelectedTabIndex = TabHostingStorage;
            return;
        }

        SelectedTabIndex = target;
    }

    /// <summary>
    /// Navigates to the Publisher Profile tab.
    /// </summary>
    [RelayCommand]
    private void GoToProfileTab()
    {
        SelectedTabIndex = TabProfile;
    }

    /// <summary>
    /// Navigates to the Hosting &amp; Storage tab.
    /// </summary>
    [RelayCommand]
    private void GoToHostingTab()
    {
        SelectedTabIndex = TabHostingStorage;
    }

    /// <summary>
    /// Gets the default project file path in user AppData.
    /// </summary>
    private string GetDefaultProjectPath()
    {
        var baseDir = configurationProvider?.GetApplicationDataPath()
            ?? Path.Combine(Path.GetTempPath(), AppConstants.AppName);
        var projectDir = Path.Combine(baseDir, PublisherStudioConstants.StudioFolderName, PublisherStudioConstants.ProjectsFolderName);
        if (!Directory.Exists(projectDir))
        {
            Directory.CreateDirectory(projectDir);
        }

        return Path.Combine(projectDir, PublisherStudioConstants.DefaultProjectFileName);
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
            var result = await publisherStudioService.CreateProjectAsync(NewPublisherName);
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
    /// Prompts the user to pick a catalog JSON file and imports it into the current project.
    /// </summary>
    [RelayCommand]
    private async Task ImportCatalogAsync()
    {
        if (CurrentProject == null)
        {
            return;
        }

        var title = localizationService?.GetString("Tools.PublisherStudio.Studio.ImportCatalogTitle") ?? "Import Catalog (.json)";
        var filePath = await dialogService.ShowCatalogFilePickerAsync(title);
        if (!string.IsNullOrEmpty(filePath))
        {
            await ImportCatalogFromFileAsync(filePath, announceFailures: true);
        }
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
        if (PublishShareViewModel != null)
        {
            var remotesCleaned = await PublishShareViewModel.DeleteCatalogRemotesAsync(catalog.Id);
            PublishShareViewModel.SyncAvailableCatalogs();
            if (!remotesCleaned)
            {
                var warnMessage = string.Format(
                    localizationService?.GetString("Tools.PublisherStudio.Studio.RemoteCatalogDeleteFailedFormat") ?? "Catalog removed locally, but the published file for '{0}' could not be deleted. It may still be live.",
                    catalog.Name);
                notificationService?.ShowWarning(StudioNotificationTitle, warnMessage, NotificationDurations.Medium);
            }
        }

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
            PublishShareViewModel.MarkCatalogChanged(target.Id);
        }

        await SaveProjectAsync();
        StatusMessage = GetStatusString("Tools.PublisherStudio.Studio.CatalogRenamedFormat", "Renamed catalog to '{0}'", target.Name);
        notificationService?.ShowSuccess(StudioNotificationTitle, StatusMessage, NotificationDurations.Short);
        logger.LogInformation("Renamed catalog to {CatalogName} ({CatalogId})", target.Name, target.Id);
    }

    private void SyncReloadCatalogsCollection()
    {
        if (CurrentProject == null)
        {
            return;
        }

        MigrateProjectToMultiCatalog();

        Catalogs.Clear();
        foreach (var catalog in CurrentProject.Catalogs)
        {
            if (catalog?.Catalog != null)
            {
                catalog.Catalog.Publisher = CurrentProject.Catalog?.Publisher ?? new();
                catalog.Catalog.Content ??= [];
            }

            if (catalog != null)
            {
                Catalogs.Add(catalog);
            }
        }

        if (Catalogs.Count > 0 && (SelectedCatalog == null || !Catalogs.Contains(SelectedCatalog)))
        {
            SelectedCatalog = Catalogs.FirstOrDefault();
        }
    }

    private void SyncPublishShareCatalogs()
    {
        if (PublishShareViewModel == null || CurrentProject == null)
        {
            return;
        }

        PublishShareViewModel.AvailableCatalogs.Clear();
        foreach (var c in CurrentProject.Catalogs)
        {
            PublishShareViewModel.AvailableCatalogs.Add(c);
        }

        PublishShareViewModel.CatalogStatuses.Clear();
        foreach (var c in CurrentProject.Catalogs)
        {
            PublishShareViewModel.CatalogStatuses.Add(new CatalogPublishStatus(c, localizationService));
        }

        if (SelectedCatalog != null)
        {
            PublishShareViewModel.ActiveCatalog = SelectedCatalog;
        }

        PublishShareViewModel.RefreshHostedAssets();
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
        PublishShareViewModel.DefinitionUploadedCallback = () =>
        {
            HasDefinitionChanges = false;
            if (PublishShareViewModel != null)
            {
                PublishShareViewModel.HasDefinitionChanges = false;
            }
        };
        PublishShareViewModel.DefinitionStaleCallback = () =>
        {
            HasDefinitionChanges = true;
            if (PublishShareViewModel != null)
            {
                PublishShareViewModel.HasDefinitionChanges = true;
            }
        };
        PublishShareViewModel.ProjectReloadCallback = ReloadFromCurrentProjectAsync;
        PublishShareViewModel.NavigateToTabCallback = tabIndex => SelectedTabIndex = tabIndex;
        PublishShareViewModel.AuthenticationChangedCallback = RefreshSetupState;
        dialogService.DuplicateAssetLookup = sha => PublishShareViewModel?.FindHostedAssetBySha256(sha);
        await PublishShareViewModel.InitializeAsync();
        HasDefinitionChanges = !PublishShareViewModel.IsDefinitionPublished;
        PublishShareViewModel.HasDefinitionChanges = HasDefinitionChanges;
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
