using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services.Hosting;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the Publish and Share tab.
/// Handles catalog validation, export, hosting provider selection, and subscription link generation.
/// </summary>
/// <remarks>
/// This ViewModel enables publishers to:
/// 1. Validate their catalog before publishing
/// 2. Export the catalog JSON for manual hosting
/// 3. Upload to integrated hosting providers (GitHub, etc.)
/// 4. Generate subscription links for users.
/// </remarks>
public partial class PublishShareViewModel : ObservableObject
{
    private readonly PublisherStudioProject _project;
    private readonly IPublisherStudioService _publisherStudioService;
    private readonly IHostingProviderFactory? _hostingProviderFactory;
    private readonly IHostingStateManager _hostingStateManager;
    private readonly ILogger _logger;
    private readonly INotificationService? _notificationService;
    private HostingState? _currentHostingState;

    [ObservableProperty]
    private IHostingProvider? _selectedHostingProvider;

    [ObservableProperty]
    private string _catalogJson = string.Empty;

    [ObservableProperty]
    private string _catalogUrl = string.Empty;

    [ObservableProperty]
    private string _subscriptionUrl = string.Empty;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private int _uploadProgress;

    [ObservableProperty]
    private string _uploadStatusMessage = string.Empty;

    [ObservableProperty]
    private string _providerDefinitionUrl = string.Empty;

    [ObservableProperty]
    private string _providerDefinitionJson = string.Empty;

    [ObservableProperty]
    private string _primaryCatalogUrl = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _catalogMirrorUrls = new();

    [ObservableProperty]
    private bool _hasPreviouslyPublished;

    [ObservableProperty]
    private string _gitHubPersonalAccessToken = string.Empty;

    [ObservableProperty]
    private string _dropboxAccessToken = string.Empty;

    [ObservableProperty]
    private string _googleClientId = string.Empty;

    [ObservableProperty]
    private string _googleClientSecret = string.Empty;

    /// <summary>
    /// Gets the three-tier upload hierarchy (1. Definition / 2. Catalogs / 3. Content items and releases).
    /// </summary>
    public UploadHierarchyItemViewModel UploadHierarchy { get; } = new();

    [ObservableProperty]
    private bool _isAuthenticating;

    [ObservableProperty]
    private string _authenticationStatusMessage = string.Empty;

    [ObservableProperty]
    private int _currentPublishStep;

    [ObservableProperty]
    private bool _publishCompleted;

    [ObservableProperty]
    private string _publishSummary = string.Empty;

    /// <summary>
    /// Gets the collection of catalog publish statuses.
    /// </summary>
    public ObservableCollection<CatalogPublishStatus> CatalogStatuses { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the selected provider requires authentication.
    /// </summary>
    public bool RequiresAuthentication => SelectedHostingProvider?.RequiresAuthentication ?? false;

    /// <summary>
    /// Gets a value indicating whether the selected provider is authenticated.
    /// </summary>
    public bool IsProviderAuthenticated => SelectedHostingProvider?.IsAuthenticated ?? false;

    /// <summary>
    /// Gets a value indicating whether authentication is needed (provider requires it but is not authenticated).
    /// </summary>
    public bool NeedsAuthentication => RequiresAuthentication && !IsProviderAuthenticated;

    /// <summary>
    /// Gets a value indicating whether GitHub PAT input should be shown.
    /// </summary>
    public bool ShowGitHubPatInput => SelectedHostingProvider?.ProviderId == HostingConstants.GitHub && !IsProviderAuthenticated;

    /// <summary>
    /// Gets a value indicating whether Google OAuth button should be shown.
    /// </summary>
    public bool ShowGoogleOAuthButton => SelectedHostingProvider?.ProviderId == HostingConstants.GoogleDrive && !IsProviderAuthenticated;

    /// <summary>
    /// Gets a value indicating whether Dropbox token input should be shown.
    /// </summary>
    public bool ShowDropboxTokenInput => SelectedHostingProvider?.ProviderId == HostingConstants.Dropbox && !IsProviderAuthenticated;

    /// <summary>
    /// Gets the available catalogs in the project.
    /// </summary>
    public ObservableCollection<NamedCatalog> AvailableCatalogs { get; } = new();

    [ObservableProperty]
    private NamedCatalog? _activeCatalog;

    /// <summary>
    /// Gets the list of artifact URL statuses.
    /// </summary>
    public ObservableCollection<ArtifactUrlStatus> ArtifactStatuses { get; } = new();

    /// <summary>
    /// Gets the upload queue for tracking artifact uploads.
    /// </summary>
    public ObservableCollection<ArtifactUploadTask> UploadQueue { get; } = new();

    /// <summary>
    /// Gets the available hosting providers.
    /// </summary>
    public ObservableCollection<IHostingProvider> HostingProviders { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PublishShareViewModel"/> class.
    /// </summary>
    /// <param name="project">The publisher studio project.</param>
    /// <param name="publisherStudioService">The publisher studio service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="hostingProviderFactory">Optional hosting provider factory.</param>
    /// <param name="hostingStateManager">The hosting state manager.</param>
    /// <param name="notificationService">The notification service.</param>
    public PublishShareViewModel(
        PublisherStudioProject project,
        IPublisherStudioService publisherStudioService,
        ILogger logger,
        IHostingProviderFactory? hostingProviderFactory = null,
        IHostingStateManager? hostingStateManager = null,
        INotificationService? notificationService = null)
    {
        _project = project;
        _publisherStudioService = publisherStudioService;
        _hostingProviderFactory = hostingProviderFactory;
        _hostingStateManager = hostingStateManager ?? new HostingStateManager(Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<HostingStateManager>());
        _logger = logger;
        _notificationService = notificationService;

        // Load hosting providers
        if (_hostingProviderFactory != null)
        {
            foreach (var provider in _hostingProviderFactory.GetCatalogHostingProviders())
            {
                HostingProviders.Add(provider);
            }

            // Select first provider by default
            SelectedHostingProvider = HostingProviders.FirstOrDefault();
        }

        // Load available catalogs
        foreach (var catalog in _project.Catalogs)
        {
            AvailableCatalogs.Add(catalog);
        }

        // Select the first catalog by default
        ActiveCatalog = AvailableCatalogs.FirstOrDefault();

        // Initialize catalog statuses
        InitializeCatalogStatuses();
        RefreshUploadHierarchy();

        // Load existing hosting state if available
        _ = LoadHostingStateAsync();

        // Validate on load
        RefreshArtifactStatuses();
        _ = ValidateCatalogAsync();
    }

    partial void OnActiveCatalogChanged(NamedCatalog? value)
    {
        if (value == null) return;
        RefreshArtifactStatuses();
        _ = ValidateCatalogAsync();
        OnPropertyChanged(nameof(ContentItemCount));
        OnPropertyChanged(nameof(TotalReleaseCount));
    }

    partial void OnSelectedHostingProviderChanged(IHostingProvider? value)
    {
        // Notify computed properties that depend on selected provider
        OnPropertyChanged(nameof(RequiresAuthentication));
        OnPropertyChanged(nameof(IsProviderAuthenticated));
        OnPropertyChanged(nameof(NeedsAuthentication));
        OnPropertyChanged(nameof(ShowGitHubPatInput));
        OnPropertyChanged(nameof(ShowGoogleOAuthButton));
        OnPropertyChanged(nameof(ShowDropboxTokenInput));

        if (value == null) return;

        // Check if hosting state has saved credentials for this provider
        if (_currentHostingState != null
            && _currentHostingState.ProviderId == value.ProviderId
            && !string.IsNullOrEmpty(_currentHostingState.AuthToken))
        {
            // Restore saved authentication
            _ = RestoreAuthenticationAsync();
        }
        else
        {
            // Different provider - clear auth state
            AuthenticationStatusMessage = string.Empty;
            GitHubPersonalAccessToken = string.Empty;
            DropboxAccessToken = string.Empty;
        }
    }

    /// <summary>
    /// Authenticates with the selected hosting provider.
    /// </summary>
    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        if (SelectedHostingProvider == null)
        {
            return;
        }

        IsAuthenticating = true;
        AuthenticationStatusMessage = "Authenticating...";

        try
        {
            if (SelectedHostingProvider is GoogleDriveHostingProvider gdrive)
            {
                if (!string.IsNullOrWhiteSpace(GoogleClientId)) gdrive.CustomClientId = GoogleClientId.Trim();
                if (!string.IsNullOrWhiteSpace(GoogleClientSecret)) gdrive.CustomClientSecret = GoogleClientSecret.Trim();
            }

            OperationResult<bool> result;

            // Handle GitHub PAT authentication
            if (SelectedHostingProvider.ProviderId == HostingConstants.GitHub && SelectedHostingProvider is GitHubHostingProvider githubProvider)
            {
                if (string.IsNullOrWhiteSpace(GitHubPersonalAccessToken))
                {
                    AuthenticationStatusMessage = "Please enter your GitHub Personal Access Token";
                    return;
                }

                result = await githubProvider.AuthenticateWithTokenAsync(GitHubPersonalAccessToken);
            }

            // Handle Dropbox token authentication
            else if (SelectedHostingProvider.ProviderId == HostingConstants.Dropbox && SelectedHostingProvider is DropboxHostingProvider dropboxProvider)
            {
                if (string.IsNullOrWhiteSpace(DropboxAccessToken))
                {
                    AuthenticationStatusMessage = "Please enter your Dropbox Access Token";
                    return;
                }

                result = await dropboxProvider.AuthenticateWithTokenAsync(DropboxAccessToken);
            }
            else
            {
                result = await SelectedHostingProvider.AuthenticateAsync();
            }

            if (result.Success)
            {
                AuthenticationStatusMessage = "Authenticated successfully";
                _logger.LogInformation("Authenticated with {Provider}", SelectedHostingProvider.DisplayName);

                // Save token to hosting state for persistence
                await SaveAuthTokenAsync();

                _notificationService?.ShowSuccess(
                    "Connected",
                    $"Successfully connected to {SelectedHostingProvider.DisplayName}. You can now publish your catalog.",
                    autoDismissMs: 4000);
            }
            else
            {
                AuthenticationStatusMessage = $"Authentication failed: {result.FirstError}";
                _logger.LogWarning("Authentication failed for {Provider}: {Error}", SelectedHostingProvider.DisplayName, result.FirstError);

                _notificationService?.ShowError(
                    "Connection Failed",
                    result.FirstError ?? "Failed to authenticate with the hosting provider.");
            }

            // Notify computed properties
            OnPropertyChanged(nameof(IsProviderAuthenticated));
            OnPropertyChanged(nameof(NeedsAuthentication));
            OnPropertyChanged(nameof(ShowGitHubPatInput));
            OnPropertyChanged(nameof(ShowGoogleOAuthButton));
            OnPropertyChanged(nameof(ShowDropboxTokenInput));
        }
        catch (Exception ex)
        {
            AuthenticationStatusMessage = $"Authentication error: {ex.Message}";
            _logger.LogError(ex, "Authentication error for {Provider}", SelectedHostingProvider.DisplayName);
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    /// <summary>
    /// Signs out from the selected hosting provider.
    /// </summary>
    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (SelectedHostingProvider == null)
        {
            return;
        }

        try
        {
            await SelectedHostingProvider.SignOutAsync();
            AuthenticationStatusMessage = "Signed out";
            GitHubPersonalAccessToken = string.Empty;
            DropboxAccessToken = string.Empty;

            // Notify computed properties
            OnPropertyChanged(nameof(IsProviderAuthenticated));
            OnPropertyChanged(nameof(NeedsAuthentication));
            OnPropertyChanged(nameof(ShowGitHubPatInput));
            OnPropertyChanged(nameof(ShowGoogleOAuthButton));
            OnPropertyChanged(nameof(ShowDropboxTokenInput));

            _logger.LogInformation("Signed out from {Provider}", SelectedHostingProvider.DisplayName);
        }
        catch (Exception ex)
        {
            AuthenticationStatusMessage = $"Sign out error: {ex.Message}";
            _logger.LogError(ex, "Sign out error for {Provider}", SelectedHostingProvider.DisplayName);
        }
    }

    private void RefreshArtifactStatuses()
    {
        ArtifactStatuses.Clear();

        if (ActiveCatalog == null)
        {
            return;
        }

        foreach (var content in ActiveCatalog.Catalog.Content)
        {
            foreach (var release in content.Releases)
            {
                foreach (var artifact in release.Artifacts)
                {
                    ArtifactStatuses.Add(new ArtifactUrlStatus(artifact, content.Name, release.Version));
                }
            }
        }
    }

    private async Task LoadHostingStateAsync()
    {
        if (string.IsNullOrEmpty(_project.ProjectPath))
            return;

        try
        {
            var result = await _hostingStateManager.LoadStateAsync(_project.ProjectPath);
            if (result.Success && result.Data != null)
            {
                _currentHostingState = result.Data;
                HasPreviouslyPublished = true;

                // Restore URLs from hosting state
                if (_currentHostingState.Definition != null)
                {
                    ProviderDefinitionUrl = _currentHostingState.Definition.Url;
                }

                if (_currentHostingState.Catalogs.Count > 0)
                {
                    CatalogUrl = _currentHostingState.Catalogs[0].Url;
                    PrimaryCatalogUrl = _currentHostingState.Catalogs[0].Url;
                }

                GenerateSubscriptionUrl();
                InitializeCatalogStatuses();
                RefreshUploadHierarchy();
                _logger.LogInformation("Loaded hosting state with {CatalogCount} catalogs", _currentHostingState.Catalogs.Count);

                // After loading state, try to restore authentication
                if (!string.IsNullOrEmpty(_currentHostingState.AuthToken))
                {
                    await RestoreAuthenticationAsync();
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "Hosting state loading was canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load hosting state");
        }
    }

    /// <summary>
    /// Gets the content item count in the active catalog.
    /// </summary>
    public int ContentItemCount => ActiveCatalog?.Catalog.Content.Count ?? 0;

    /// <summary>
    /// Gets the total release count across all content items in the active catalog.
    /// </summary>
    public int TotalReleaseCount => ActiveCatalog?.Catalog.Content.Sum(c => c.Releases.Count) ?? 0;

    /// <summary>
    /// Refreshes the three-tier upload hierarchy (1. Definition / 2. Catalogs / 3. Content items and releases).
    /// </summary>
    public void RefreshUploadHierarchy()
    {
        try
        {
            // Tier 1: Provider Definition
            UploadHierarchy.PublisherName = _project.Catalog.Publisher?.Name ?? "Publisher";
            UploadHierarchy.PublisherId = _project.Catalog.Publisher?.Id ?? "publisher";
            UploadHierarchy.AvatarUrl = _project.Catalog.Publisher?.AvatarUrl;
            UploadHierarchy.Website = _project.Catalog.Publisher?.Website;
            UploadHierarchy.DefinitionUrl = ProviderDefinitionUrl;
            UploadHierarchy.SubscriptionUrl = SubscriptionUrl;
            UploadHierarchy.IsUploaded = !string.IsNullOrWhiteSpace(ProviderDefinitionUrl);
            UploadHierarchy.LastUpdated = _currentHostingState?.Definition?.LastUpdated;

            // Tier 2: Catalogs
            UploadHierarchy.Catalogs.Clear();
            foreach (var namedCat in _project.Catalogs)
            {
                var catNode = new UploadCatalogNodeViewModel
                {
                    Id = namedCat.Id,
                    Name = namedCat.Name,
                    Description = namedCat.Description ?? string.Empty,
                };

                var hostedInfo = _currentHostingState?.Catalogs?.FirstOrDefault(c => c.CatalogId == namedCat.Id);
                if (hostedInfo != null)
                {
                    catNode.DirectDownloadUrl = hostedInfo.Url;
                    catNode.IsPublished = true;
                    catNode.LastUpdated = hostedInfo.LastUpdated;
                }

                // Tier 3: Content Items and Releases
                if (namedCat.Catalog?.Content != null)
                {
                    foreach (var contentItem in namedCat.Catalog.Content)
                    {
                        var contentNode = new UploadContentNodeViewModel
                        {
                            Id = contentItem.Id,
                            Name = contentItem.Name,
                            ContentType = contentItem.ContentType.ToString(),
                            TargetGame = contentItem.TargetGame.ToString(),
                            Description = contentItem.Description ?? string.Empty,
                        };

                        if (contentItem.Releases != null)
                        {
                            foreach (var rel in contentItem.Releases)
                            {
                                var relNode = new UploadReleaseNodeViewModel
                                {
                                    Version = rel.Version,
                                    ReleaseDate = rel.ReleaseDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                                    ReleaseNotes = rel.Changelog ?? string.Empty,
                                    IsLatest = rel.IsLatest,
                                };

                                if (rel.Artifacts != null)
                                {
                                    foreach (var art in rel.Artifacts)
                                    {
                                        var artNode = new UploadArtifactNodeViewModel
                                        {
                                            FileName = art.Filename,
                                            DownloadUrl = art.DownloadUrl,
                                            FileSizeFormatted = GenHub.Core.Helpers.FileSizeFormatter.Format(art.Size),
                                            Sha256 = art.Sha256 ?? string.Empty,
                                            IsHosted = !string.IsNullOrEmpty(art.DownloadUrl),
                                        };

                                        var localArtifact = ArtifactStatuses.FirstOrDefault(a => a.ArtifactName == art.Filename);
                                        if (localArtifact != null)
                                        {
                                            artNode.HasLocalFile = localArtifact.HasLocalFile;
                                            artNode.LocalFilePath = localArtifact.LocalFilePath ?? string.Empty;
                                        }

                                        relNode.Artifacts.Add(artNode);
                                    }
                                }

                                contentNode.Releases.Add(relNode);
                            }
                        }

                        catNode.ContentItems.Add(contentNode);
                    }
                }

                UploadHierarchy.Catalogs.Add(catNode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh upload hierarchy");
        }
    }

    /// <summary>
    /// Validates the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task ValidateCatalogAsync()
    {
        try
        {
            if (ActiveCatalog == null)
            {
                IsValid = false;
                ValidationMessage = "No catalog selected";
                return;
            }

            // Update artifact validations
            foreach (var status in ArtifactStatuses)
            {
                status.Validate();
            }

            var artifactErrors = ArtifactStatuses.Where(s => !s.IsValid).ToList();
            if (artifactErrors.Any())
            {
                IsValid = false;
                ValidationMessage = $"Validation failed: {artifactErrors.Count} artifacts have invalid or missing URLs";
                return;
            }

            var result = await _publisherStudioService.ValidateCatalogAsync(ActiveCatalog.Catalog, allowPendingArtifacts: true);
            IsValid = result.Success;
            ValidationMessage = result.Success ? $"Catalog '{ActiveCatalog.Name}' is valid" : $"Validation failed: {result.FirstError}";

            _logger.LogInformation("Catalog '{CatalogName}' validation: {IsValid}", ActiveCatalog.Name, IsValid);
        }
        catch (Exception ex)
        {
            IsValid = false;
            ValidationMessage = $"Validation error: {ex.Message}";
            _logger.LogError(ex, "Error validating catalog");
        }
    }

    /// <summary>
    /// Exports the active catalog to JSON.
    /// </summary>
    [RelayCommand]
    private async Task ExportCatalogAsync()
    {
        try
        {
            if (ActiveCatalog == null)
            {
                _logger.LogWarning("Cannot export catalog: no active catalog selected");
                return;
            }

            var result = await _publisherStudioService.ExportCatalogAsync(_project, ActiveCatalog);
            if (result.Success && result.Data != null)
            {
                CatalogJson = result.Data;
                _logger.LogInformation("Exported catalog '{CatalogName}' JSON", ActiveCatalog.Name);
            }
            else
            {
                _logger.LogError("Failed to export catalog '{CatalogName}': {Error}", ActiveCatalog.Name, result.FirstError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting catalog");
        }
    }

    /// <summary>
    /// Uploads the catalog to the selected hosting provider.
    /// </summary>
    [RelayCommand]
    private async Task UploadCatalogAsync()
    {
        if (SelectedHostingProvider == null)
        {
            UploadStatusMessage = "Please select a hosting provider";
            return;
        }

        if (!IsValid)
        {
            UploadStatusMessage = "Please fix validation errors before uploading";
            return;
        }

        try
        {
            IsUploading = true;
            UploadProgress = 0;
            UploadStatusMessage = "Preparing to publish...";
            PublishCompleted = false;
            CurrentPublishStep = 0;
            PublishSummary = string.Empty;

            if (!await EnsureProviderAuthenticatedAsync())
            {
                return;
            }

            // 1. Upload Pending Artifacts
            CurrentPublishStep = 1;
            if (!await UploadPendingArtifactsAsync(SelectedHostingProvider))
            {
               return;
            }

            // 2. Export Active Catalog (Now includes new URLs)
            CurrentPublishStep = 2;
            if (ActiveCatalog == null)
            {
                UploadStatusMessage = "No catalog selected";
                return;
            }

            UploadStatusMessage = $"Generating catalog '{ActiveCatalog.Name}'...";
            var exportResult = await _publisherStudioService.ExportCatalogAsync(_project, ActiveCatalog);
            if (!exportResult.Success || string.IsNullOrEmpty(exportResult.Data))
            {
                UploadStatusMessage = $"Failed to export catalog: {exportResult.FirstError}";
                return;
            }

            CatalogJson = exportResult.Data;
            UploadProgress = 80;
            UploadStatusMessage = $"Uploading catalog '{ActiveCatalog.Name}' to {SelectedHostingProvider.DisplayName}...";

            // 3. Upload Catalog
            CurrentPublishStep = 3;
            var progress = new Progress<int>(p =>
            {
                UploadProgress = 80 + (int)(p * 0.2);
            });

            var uploadResult = await PerformCatalogUploadAsync(progress);
            if (uploadResult.Success && uploadResult.Data != null)
            {
                await CompletePublishSuccessAsync(uploadResult.Data);
            }
            else
            {
                UploadStatusMessage = $"Catalog upload failed: {uploadResult.FirstError}";
            }
        }
        catch (Exception ex)
        {
            UploadStatusMessage = $"Error: {ex.Message}";
            _logger.LogError(ex, "Error uploading catalog");
        }
        finally
        {
            IsUploading = false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "Accesses generated ObservableProperties")]
    private async Task<bool> EnsureProviderAuthenticatedAsync()
    {
        if (SelectedHostingProvider == null) return false;
        if (SelectedHostingProvider.RequiresAuthentication && !SelectedHostingProvider.IsAuthenticated)
        {
            UploadStatusMessage = "Authenticating...";
            var authResult = await SelectedHostingProvider.AuthenticateAsync();
            if (!authResult.Success)
            {
                UploadStatusMessage = $"Authentication failed: {authResult.FirstError}";
                return false;
            }
        }

        return true;
    }

    private async Task<OperationResult<HostingUploadResult>> PerformCatalogUploadAsync(IProgress<int> progress)
    {
        if (SelectedHostingProvider == null || ActiveCatalog == null)
        {
            return OperationResult<HostingUploadResult>.CreateFailure("Provider or catalog missing");
        }

        var existingCatalogFileId = _currentHostingState?.Catalogs
            .FirstOrDefault(c => c.CatalogId == ActiveCatalog.Id)?.FileId;

        var catalogFileName = string.IsNullOrEmpty(ActiveCatalog.FileName)
            ? $"catalog-{ActiveCatalog.Id}.json"
            : ActiveCatalog.FileName;

        if (!string.IsNullOrEmpty(existingCatalogFileId) && SelectedHostingProvider.SupportsUpdate)
        {
            UploadStatusMessage = $"Updating existing catalog '{ActiveCatalog.Name}'...";
            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(CatalogJson));
            return await SelectedHostingProvider.UpdateFileAsync(existingCatalogFileId, stream, catalogFileName, progress);
        }

        return await SelectedHostingProvider.UploadCatalogAsync(CatalogJson, _project.Catalog.Publisher.Id, progress);
    }

    private async Task CompletePublishSuccessAsync(HostingUploadResult data)
    {
        if (SelectedHostingProvider == null) return;

        CatalogUrl = data.DirectDownloadUrl;
        if (string.IsNullOrWhiteSpace(PrimaryCatalogUrl))
        {
            PrimaryCatalogUrl = CatalogUrl;
        }

        SubscriptionUrl = SelectedHostingProvider.GetSubscriptionLink(CatalogUrl);
        UploadProgress = 100;
        UploadStatusMessage = "Published successfully!";
        _logger.LogInformation("Catalog and artifacts uploaded to {Provider}: {Url}", SelectedHostingProvider.ProviderId, CatalogUrl);

        await SaveHostingStateAsync(data.FileId, data.DirectDownloadUrl);

        // 4. Generate and upload provider definition
        CurrentPublishStep = 4;
        UploadStatusMessage = "Generating provider definition...";
        await GenerateProviderDefinitionAsync();

        await UploadProviderDefinitionIfAvailableAsync();

        // 5. Generate subscription URL (uses definition URL if available)
        GenerateSubscriptionUrl();

        CurrentPublishStep = 6;
        PublishCompleted = true;
        PublishSummary = BuildPublishSummary(CatalogUrl, ProviderDefinitionUrl, SubscriptionUrl);
        UploadStatusMessage = "Published successfully!";
    }

    private async Task UploadProviderDefinitionIfAvailableAsync()
    {
        if (string.IsNullOrWhiteSpace(ProviderDefinitionJson) || SelectedHostingProvider == null)
        {
            return;
        }

        CurrentPublishStep = 5;
        UploadStatusMessage = "Uploading provider definition...";
        var defFileName = _project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName;
        var existingDefFileId = _currentHostingState?.Definition?.FileId;

        using var defStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(ProviderDefinitionJson));
        var defUploadResult = (!string.IsNullOrEmpty(existingDefFileId) && SelectedHostingProvider.SupportsUpdate)
            ? await SelectedHostingProvider.UpdateFileAsync(existingDefFileId, defStream, defFileName)
            : await SelectedHostingProvider.UploadFileAsync(defStream, defFileName);

        if (defUploadResult.Success && defUploadResult.Data != null)
        {
            ProviderDefinitionUrl = defUploadResult.Data.DirectDownloadUrl;
            if (_currentHostingState != null && !string.IsNullOrEmpty(_project.ProjectPath))
            {
                _currentHostingState.Definition = new HostedFileInfo
                {
                    FileId = defUploadResult.Data.FileId,
                    Url = defUploadResult.Data.DirectDownloadUrl,
                    LastUpdated = DateTime.UtcNow,
                };
                await _hostingStateManager.SaveStateAsync(_project.ProjectPath, _currentHostingState);
            }
        }
    }

    private async Task<bool> UploadPendingArtifactsAsync(IHostingProvider provider)
    {
        if (ActiveCatalog == null)
        {
            return true;
        }

        var allReleases = ActiveCatalog.Catalog.Content.SelectMany(c => c.Releases).ToList();
        var pendingArtifacts = allReleases
            .SelectMany(r => r.Artifacts)
            .Where(a => !string.IsNullOrEmpty(a.LocalFilePath) && string.IsNullOrEmpty(a.DownloadUrl))
            .ToList();

        if (pendingArtifacts.Count == 0)
        {
            return true;
        }

        if (!provider.SupportsArtifactHosting)
        {
             UploadStatusMessage = "Provider does not support artifact hosting. Please add URLs manually.";
             return false;
        }

        BuildUploadQueue(pendingArtifacts);

        int total = UploadQueue.Count;
        int current = 0;

        foreach (var task in UploadQueue)
        {
            current++;
            if (!await ExecuteSingleArtifactUploadAsync(provider, task, current, total))
            {
                return false;
            }
        }

        return true;
    }

    private void BuildUploadQueue(List<ReleaseArtifact> pendingArtifacts)
    {
        UploadQueue.Clear();
        if (ActiveCatalog == null) return;

        foreach (var artifact in pendingArtifacts)
        {
            var content = ActiveCatalog.Catalog.Content.FirstOrDefault(c =>
                c.Releases.Any(r => r.Artifacts.Contains(artifact)));
            var release = content?.Releases.FirstOrDefault(r => r.Artifacts.Contains(artifact));

            if (content != null && release != null)
            {
                UploadQueue.Add(new ArtifactUploadTask
                {
                    ContentId = content.Id,
                    Version = release.Version,
                    Artifact = artifact,
                    Status = UploadStatus.Pending,
                });
            }
        }
    }

    private async Task<bool> ExecuteSingleArtifactUploadAsync(IHostingProvider provider, ArtifactUploadTask task, int current, int total)
    {
        task.Status = UploadStatus.Uploading;
        UploadStatusMessage = $"Uploading artifact {current}/{total}: {task.Artifact.Filename}";
        UploadProgress = (int)((double)(current - 1) / total * 80);

        try
        {
            if (!System.IO.File.Exists(task.Artifact.LocalFilePath))
            {
                task.Status = UploadStatus.Failed;
                task.ErrorMessage = "File not found";
                UploadStatusMessage = $"File not found: {task.Artifact.LocalFilePath}";
                return false;
            }

            using var stream = System.IO.File.OpenRead(task.Artifact.LocalFilePath);
            var progress = new Progress<int>(p =>
            {
                task.Progress = p;
                UploadProgress = (int)(((double)(current - 1) / total * 80) + (p / total * 80.0 / 100.0));
            });

            var result = await provider.UploadFileAsync(stream, task.Artifact.Filename, null, progress);
            if (result.Success && result.Data != null)
            {
                task.Artifact.DownloadUrl = result.Data.DirectDownloadUrl;
                task.Status = UploadStatus.Uploaded;
                task.Progress = 100;
                _logger.LogInformation("Uploaded artifact {File} to {Url}", task.Artifact.Filename, task.Artifact.DownloadUrl);
                return true;
            }

            task.Status = UploadStatus.Failed;
            task.ErrorMessage = result.FirstError ?? "Upload failed";
            UploadStatusMessage = $"Failed to upload {task.Artifact.Filename}: {result.FirstError}";
            return false;
        }
        catch (Exception ex)
        {
            task.Status = UploadStatus.Failed;
            task.ErrorMessage = ex.Message;
            UploadStatusMessage = $"Error uploading {task.Artifact.Filename}: {ex.Message}";
            _logger.LogError(ex, "Error uploading artifact {Filename}", task.Artifact.Filename);
            return false;
        }
    }

    private async Task SaveHostingStateAsync(string catalogFileId, string catalogUrl)
    {
        if (string.IsNullOrEmpty(_project.ProjectPath))
            return;

        _currentHostingState ??= new HostingState
        {
            ProviderId = SelectedHostingProvider?.ProviderId ?? "unknown",
        };

        // Update or add catalog entry using the active catalog ID
        var catalogId = ActiveCatalog?.Id ?? "default";
        var catalogEntry = _currentHostingState.Catalogs.FirstOrDefault(c => c.CatalogId == catalogId);
        if (catalogEntry == null)
        {
            catalogEntry = new CatalogHostingInfo { CatalogId = catalogId };
            _currentHostingState.Catalogs.Add(catalogEntry);
        }

        catalogEntry.FileId = catalogFileId;
        catalogEntry.Url = catalogUrl;
        catalogEntry.LastUpdated = DateTime.UtcNow;

        _currentHostingState.LastPublished = DateTime.UtcNow;

        var result = await _hostingStateManager.SaveStateAsync(_project.ProjectPath, _currentHostingState);
        if (result.Success)
        {
            HasPreviouslyPublished = true;
            _logger.LogInformation("Saved hosting state");
        }
    }

    private async Task SaveAuthTokenAsync()
    {
        if (string.IsNullOrEmpty(_project.ProjectPath) || SelectedHostingProvider == null)
            return;

        _currentHostingState ??= new HostingState { ProviderId = SelectedHostingProvider.ProviderId };
        _currentHostingState.ProviderId = SelectedHostingProvider.ProviderId;

        // Store the token
        if (SelectedHostingProvider.ProviderId == HostingConstants.GitHub)
            _currentHostingState.AuthToken = GitHubPersonalAccessToken;
        else if (SelectedHostingProvider.ProviderId == HostingConstants.Dropbox)
            _currentHostingState.AuthToken = DropboxAccessToken;

        await _hostingStateManager.SaveStateAsync(_project.ProjectPath, _currentHostingState);
    }

    private async Task RestoreAuthenticationAsync()
    {
        if (_currentHostingState == null || string.IsNullOrEmpty(_currentHostingState.AuthToken))
            return;

        // Find the matching provider
        var provider = HostingProviders.FirstOrDefault(p => p.ProviderId == _currentHostingState.ProviderId);
        if (provider == null) return;

        SelectedHostingProvider = provider;

        try
        {
            if (provider.ProviderId == HostingConstants.GitHub && provider is GitHubHostingProvider githubProvider)
            {
                GitHubPersonalAccessToken = _currentHostingState.AuthToken;
                var result = await githubProvider.AuthenticateWithTokenAsync(_currentHostingState.AuthToken);
                if (result.Success)
                {
                    AuthenticationStatusMessage = "Restored connection";
                    _logger.LogInformation("Restored GitHub authentication from hosting state");
                }
            }
            else if (provider.ProviderId == HostingConstants.Dropbox && provider is DropboxHostingProvider dropboxProvider)
            {
                DropboxAccessToken = _currentHostingState.AuthToken;
                var result = await dropboxProvider.AuthenticateWithTokenAsync(_currentHostingState.AuthToken);
                if (result.Success)
                {
                    AuthenticationStatusMessage = "Restored connection";
                    _logger.LogInformation("Restored Dropbox authentication from hosting state");
                }
            }

            // Notify computed properties
            OnPropertyChanged(nameof(IsProviderAuthenticated));
            OnPropertyChanged(nameof(NeedsAuthentication));
            OnPropertyChanged(nameof(ShowGitHubPatInput));
            OnPropertyChanged(nameof(ShowGoogleOAuthButton));
            OnPropertyChanged(nameof(ShowDropboxTokenInput));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore authentication");
        }
    }

    /// <summary>
    /// Generates the provider definition JSON.
    /// </summary>
    [RelayCommand]
    private async Task GenerateProviderDefinitionAsync()
    {
        if (_currentHostingState == null || _currentHostingState.Catalogs.Count == 0)
        {
            UploadStatusMessage = "No catalogs have been published yet";
            return;
        }

        try
        {
            // Build catalog hosting info dictionary from hosting state
            var catalogHostingInfo = _currentHostingState.Catalogs
                .Where(c => !string.IsNullOrEmpty(c.Url))
                .GroupBy(c => c.CatalogId)
                .ToDictionary(g => g.Key, g => g.Last().Url);

            if (catalogHostingInfo.Count == 0)
            {
                UploadStatusMessage = "No catalog URLs available for definition";
                return;
            }

            var result = await _publisherStudioService.ExportProviderDefinitionAsync(
                _project,
                catalogHostingInfo,
                ProviderDefinitionUrl);

            if (result.Success && result.Data != null)
            {
                ProviderDefinitionJson = result.Data;
                _logger.LogInformation("Generated provider definition JSON with {CatalogCount} catalogs", catalogHostingInfo.Count);
            }
            else
            {
                _logger.LogError("Failed to generate provider definition: {Error}", result.FirstError);
                UploadStatusMessage = $"Failed to generate definition: {result.FirstError}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating provider definition");
            UploadStatusMessage = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Uploads the provider definition to the selected hosting provider.
    /// </summary>
    [RelayCommand]
    private async Task UploadProviderDefinitionAsync()
    {
        if (SelectedHostingProvider == null)
        {
            UploadStatusMessage = "Please select a hosting provider";
            return;
        }

        // Regenerate to ensure latest values
        await GenerateProviderDefinitionAsync();

        if (string.IsNullOrWhiteSpace(ProviderDefinitionJson))
        {
            return;
        }

        try
        {
            IsUploading = true;
            UploadStatusMessage = "Uploading provider definition...";

            var fileName = _project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName;

            // Upload as a file
            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(ProviderDefinitionJson));
            var result = await SelectedHostingProvider.UploadFileAsync(stream, fileName);

            if (result.Success && result.Data != null)
            {
                ProviderDefinitionUrl = result.Data.DirectDownloadUrl;
                GenerateSubscriptionUrl(); // Regenerate based on new definition URL
                RefreshUploadHierarchy();
                UploadStatusMessage = "Provider definition uploaded successfully.";
                _logger.LogInformation("Uploaded provider definition to {Url}", ProviderDefinitionUrl);
            }
            else
            {
                UploadStatusMessage = $"Upload failed: {result.FirstError}";
            }
        }
        catch (Exception ex)
        {
            UploadStatusMessage = $"Error uploading definition: {ex.Message}";
            _logger.LogError(ex, "Error uploading provider definition");
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand]
    private void AddCatalogMirror()
    {
        CatalogMirrorUrls.Add(HostingConstants.DefaultMirrorUrlPrefix);
    }

    [RelayCommand]
    private void RemoveCatalogMirror(string url)
    {
        if (CatalogMirrorUrls.Contains(url))
        {
            CatalogMirrorUrls.Remove(url);
        }
    }

    /// <summary>
    /// Generates the subscription URL.
    /// </summary>
    [RelayCommand]
    private void GenerateSubscriptionUrl()
    {
        // Always prefer Provider Definition URL (Tier 1)
        if (!string.IsNullOrWhiteSpace(ProviderDefinitionUrl))
        {
            SubscriptionUrl = _publisherStudioService.GenerateSubscriptionUrl(ProviderDefinitionUrl);
            _logger.LogInformation("Generated subscription URL using definition URL");
        }
        else
        {
            // No definition URL available - this should not happen in normal flow
            SubscriptionUrl = "Please publish to generate subscription URL";
            _logger.LogWarning("Cannot generate subscription URL: definition URL not available");
        }
    }

    /// <summary>
    /// Copies the subscription URL to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopySubscriptionUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(SubscriptionUrl))
        {
            return;
        }

        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(SubscriptionUrl);
                _logger.LogInformation("Copied subscription URL to clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy to clipboard");
        }
    }

    /// <summary>
    /// Copies the catalog JSON to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyCatalogJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(CatalogJson))
        {
            // Generate first if not already done
            await ExportCatalogAsync();
        }

        if (string.IsNullOrWhiteSpace(CatalogJson))
        {
            return;
        }

        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(CatalogJson);
                _logger.LogInformation("Copied catalog JSON to clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy catalog to clipboard");
        }
    }

    /// <summary>
    /// Copies the provider definition JSON to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyProviderDefinitionJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(ProviderDefinitionJson))
        {
            // Generate first if not already done
            await GenerateProviderDefinitionAsync();
        }

        if (string.IsNullOrWhiteSpace(ProviderDefinitionJson))
        {
            return;
        }

        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(ProviderDefinitionJson);
                _logger.LogInformation("Copied provider definition JSON to clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy provider definition to clipboard");
        }
    }

    /// <summary>
    /// Copies the provider definition URL to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyProviderDefinitionUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(ProviderDefinitionUrl))
        {
            return;
        }

        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(ProviderDefinitionUrl);
                _logger.LogInformation("Copied provider definition URL to clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy provider definition URL to clipboard");
        }
    }

    /// <summary>
    /// Copies the catalog URL to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyCatalogUrlAsync()
    {
        if (string.IsNullOrEmpty(CatalogUrl))
        {
            return;
        }

        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(CatalogUrl);
                _logger.LogInformation("Copied catalog URL to clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy catalog URL to clipboard");
        }
    }

    private string BuildPublishSummary(string catalogUrl, string providerDefinitionUrl, string subscriptionUrl)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(catalogUrl))
            sb.AppendLine($"Catalog URL: {catalogUrl}");
        if (!string.IsNullOrEmpty(providerDefinitionUrl))
            sb.AppendLine($"Definition URL: {providerDefinitionUrl}");
        if (!string.IsNullOrEmpty(subscriptionUrl))
            sb.AppendLine($"Subscription URL: {subscriptionUrl}");
        return sb.ToString();
    }

    /// <summary>
    /// Initializes catalog statuses from project and hosting state.
    /// </summary>
    private void InitializeCatalogStatuses()
    {
        var existingStatuses = CatalogStatuses.ToDictionary(s => s.Catalog.Id);

        foreach (var catalog in _project.Catalogs)
        {
            if (!existingStatuses.TryGetValue(catalog.Id, out var status))
            {
                status = new CatalogPublishStatus(catalog);
                CatalogStatuses.Add(status);
            }

            // Check if published
            var hostingInfo = _currentHostingState?.Catalogs
                .FirstOrDefault(c => c.CatalogId == catalog.Id);

            if (hostingInfo != null)
            {
                status.IsPublished = true;
                status.PublishedUrl = hostingInfo.Url;
                status.LastPublished = hostingInfo.LastUpdated;
            }
        }

        var projectCatalogIds = new HashSet<string>(_project.Catalogs.Select(c => c.Id));
        for (var i = CatalogStatuses.Count - 1; i >= 0; i--)
        {
            if (!projectCatalogIds.Contains(CatalogStatuses[i].Catalog.Id))
            {
                CatalogStatuses.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Publishes a specific catalog by its ID.
    /// </summary>
    [RelayCommand]
    private async Task PublishCatalogByIdAsync(string catalogId)
    {
        var catalog = _project.Catalogs.FirstOrDefault(c => c.Id == catalogId);
        if (catalog != null)
        {
            await PublishCatalogAsync(catalog);
        }
    }

    /// <summary>
    /// Publishes a specific catalog.
    /// </summary>
    [RelayCommand]
    private async Task PublishCatalogAsync(NamedCatalog catalog)
    {
        // Set as active catalog temporarily
        var previousActive = ActiveCatalog;
        ActiveCatalog = catalog;

        try
        {
            await UploadCatalogAsync();

            // Update status
            var status = CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == catalog.Id);
            if (status != null)
            {
                status.IsPublished = true;
                status.LastPublished = DateTime.UtcNow;
                status.HasChanges = false;
            }
        }
        finally
        {
            // Restore previous active catalog
            ActiveCatalog = previousActive;
        }
    }

    /// <summary>
    /// Publishes all catalogs in sequence.
    /// </summary>
    [RelayCommand]
    private async Task PublishAllCatalogsAsync()
    {
        if (SelectedHostingProvider == null || !IsValid)
        {
            return;
        }

        IsUploading = true;
        PublishCompleted = false;

        try
        {
            var totalCatalogs = _project.Catalogs.Count;
            var currentCatalog = 0;

            foreach (var catalog in _project.Catalogs)
            {
                currentCatalog++;
                UploadStatusMessage = $"Publishing catalog {currentCatalog}/{totalCatalogs}: {catalog.Name}";

                await PublishCatalogAsync(catalog);
            }

            // Generate provider definition with all catalogs
            await GenerateProviderDefinitionAsync();

            // Upload definition
            if (!string.IsNullOrWhiteSpace(ProviderDefinitionJson))
            {
                await UploadProviderDefinitionAsync();
            }

            GenerateSubscriptionUrl();
            RefreshUploadHierarchy();
            PublishCompleted = true;
            UploadStatusMessage = $"Successfully published {totalCatalogs} catalogs!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish all catalogs");
            UploadStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand]
    private async Task CopyCustomUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(url);
                _notificationService?.ShowSuccess("Copied", "Link copied to clipboard.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy URL to clipboard");
        }
    }
}
