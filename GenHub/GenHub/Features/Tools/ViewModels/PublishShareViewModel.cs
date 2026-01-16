using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
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
    private bool _isValid;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private string _catalogJson = string.Empty;

    [ObservableProperty]
    private string _catalogUrl = string.Empty;

    [ObservableProperty]
    private string _subscriptionUrl = string.Empty;

    [ObservableProperty]
    private IHostingProvider? _selectedHostingProvider;

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private int _uploadProgress;

    [ObservableProperty]
    private string? _uploadStatusMessage;

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
    private bool _isAuthenticating;

    [ObservableProperty]
    private string _authenticationStatusMessage = string.Empty;

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
    public bool ShowGitHubPatInput => SelectedHostingProvider?.ProviderId == "github" && !IsProviderAuthenticated;

    /// <summary>
    /// Gets a value indicating whether Google OAuth button should be shown.
    /// </summary>
    public bool ShowGoogleOAuthButton => SelectedHostingProvider?.ProviderId == "google_drive" && !IsProviderAuthenticated;

    /// <summary>
    /// Gets a value indicating whether Dropbox token input should be shown.
    /// </summary>
    public bool ShowDropboxTokenInput => SelectedHostingProvider?.ProviderId == "dropbox" && !IsProviderAuthenticated;

    /// <summary>
    /// Gets the list of artifact URL statuses.
    /// </summary>
    public ObservableCollection<ArtifactUrlStatus> ArtifactStatuses { get; } = new();

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

        // Load existing hosting state if available
        _ = LoadHostingStateAsync();

        // Validate on load
        RefreshArtifactStatuses();
        _ = ValidateCatalogAsync();
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

        // Clear authentication status when switching providers
        AuthenticationStatusMessage = string.Empty;
        GitHubPersonalAccessToken = string.Empty;
        DropboxAccessToken = string.Empty;
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
            OperationResult<bool> result;

            // Handle GitHub PAT authentication
            if (SelectedHostingProvider.ProviderId == "github" && SelectedHostingProvider is GitHubHostingProvider githubProvider)
            {
                if (string.IsNullOrWhiteSpace(GitHubPersonalAccessToken))
                {
                    AuthenticationStatusMessage = "Please enter your GitHub Personal Access Token";
                    return;
                }

                result = await githubProvider.AuthenticateWithTokenAsync(GitHubPersonalAccessToken);
            }
            // Handle Dropbox token authentication
            else if (SelectedHostingProvider.ProviderId == "dropbox" && SelectedHostingProvider is DropboxHostingProvider dropboxProvider)
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
                AuthenticationStatusMessage = "✓ Authenticated successfully";
                _logger.LogInformation("Authenticated with {Provider}", SelectedHostingProvider.DisplayName);

                _notificationService?.ShowSuccess(
                    "Connected",
                    $"Successfully connected to {SelectedHostingProvider.DisplayName}. You can now publish your catalog.",
                    autoDismissMs: 4000);
            }
            else
            {
                AuthenticationStatusMessage = $"✗ {result.FirstError}";
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
            AuthenticationStatusMessage = $"✗ Error: {ex.Message}";
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
        foreach (var content in _project.Catalog.Content)
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
            _logger.LogInformation("Loaded hosting state with {CatalogCount} catalogs", _currentHostingState.Catalogs.Count);
        }
    }

    /// <summary>
    /// Gets the content item count in the catalog.
    /// </summary>
    public int ContentItemCount => _project.Catalog.Content.Count;

    /// <summary>
    /// Gets the total release count across all content items.
    /// </summary>
    public int TotalReleaseCount => _project.Catalog.Content.Sum(c => c.Releases.Count);

    /// <summary>
    /// Validates the catalog.
    /// </summary>
    [RelayCommand]
    private async Task ValidateCatalogAsync()
    {
        try
        {
            // Update artifact validations
            foreach (var status in ArtifactStatuses)
            {
                status.Validate();
            }

            var artifactErrors = ArtifactStatuses.Where(s => !s.IsValid).ToList();
            if (artifactErrors.Any())
            {
                IsValid = false;
                ValidationMessage = $"✗ {artifactErrors.Count} artifacts have invalid or missing URLs";
                return;
            }

            var result = await _publisherStudioService.ValidateCatalogAsync(_project.Catalog);
            IsValid = result.Success;
            ValidationMessage = result.Success ? "✓ Catalog is valid" : $"✗ {result.FirstError}";

            _logger.LogInformation("Catalog validation: {IsValid}", IsValid);
        }
        catch (Exception ex)
        {
            IsValid = false;
            ValidationMessage = $"✗ Validation error: {ex.Message}";
            _logger.LogError(ex, "Error validating catalog");
        }
    }

    /// <summary>
    /// Exports the catalog to JSON.
    /// </summary>
    [RelayCommand]
    private async Task ExportCatalogAsync()
    {
        try
        {
            var result = await _publisherStudioService.ExportCatalogAsync(_project);
            if (result.Success && result.Data != null)
            {
                CatalogJson = result.Data;
                _logger.LogInformation("Exported catalog JSON");
            }
            else
            {
                _logger.LogError("Failed to export catalog: {Error}", result.FirstError);
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

            // Check authentication first
            if (SelectedHostingProvider.RequiresAuthentication && !SelectedHostingProvider.IsAuthenticated)
            {
                UploadStatusMessage = "Authenticating...";
                var authResult = await SelectedHostingProvider.AuthenticateAsync();
                if (!authResult.Success)
                {
                    UploadStatusMessage = $"Authentication failed: {authResult.FirstError}";
                    return;
                }
            }

            // 1. Upload Pending Artifacts
            var artifactsUploaded = await UploadPendingArtifactsAsync(SelectedHostingProvider);
            if (!artifactsUploaded)
            {
               // Error message already set in helper
               return;
            }

            // 2. Export Catalog (Now includes new URLs)
            UploadStatusMessage = "Generatng catalog...";
            var exportResult = await _publisherStudioService.ExportCatalogAsync(_project);
            if (!exportResult.Success || string.IsNullOrEmpty(exportResult.Data))
            {
                UploadStatusMessage = $"Failed to export catalog: {exportResult.FirstError}";
                return;
            }

            CatalogJson = exportResult.Data;
            UploadProgress = 80;
            UploadStatusMessage = "Uploading catalog to " + SelectedHostingProvider.DisplayName + "...";

            // 3. Upload Catalog
            var progress = new Progress<int>(p =>
            {
                // Map 0-100 to 80-100
                UploadProgress = 80 + (int)(p * 0.2);
            });

            // Check if we should update existing file or create new
            var existingCatalogFileId = _currentHostingState?.Catalogs.FirstOrDefault()?.FileId;
            OperationResult<HostingUploadResult> uploadResult;

            if (!string.IsNullOrEmpty(existingCatalogFileId) && SelectedHostingProvider.SupportsUpdate)
            {
                UploadStatusMessage = "Updating existing catalog...";
                using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(CatalogJson));
                uploadResult = await SelectedHostingProvider.UpdateFileAsync(existingCatalogFileId, stream, "catalog.json", progress);
            }
            else
            {
                uploadResult = await SelectedHostingProvider.UploadCatalogAsync(CatalogJson, _project.Catalog.Publisher.Id, progress);
            }

            if (uploadResult.Success && uploadResult.Data != null)
            {
                CatalogUrl = uploadResult.Data.DirectDownloadUrl;

                // Also set PrimaryCatalogUrl if user hasn't manually entered one
                if (string.IsNullOrWhiteSpace(PrimaryCatalogUrl))
                {
                    PrimaryCatalogUrl = CatalogUrl;
                }

                SubscriptionUrl = SelectedHostingProvider.GetSubscriptionLink(CatalogUrl);
                UploadProgress = 100;
                UploadStatusMessage = "Published successfully!";
                _logger.LogInformation("Catalog and artifacts uploaded to {Provider}: {Url}", SelectedHostingProvider.ProviderId, CatalogUrl);

                // Save hosting state
                await SaveHostingStateAsync(uploadResult.Data.FileId, uploadResult.Data.DirectDownloadUrl);
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

    private async Task<bool> UploadPendingArtifactsAsync(IHostingProvider provider)
    {
        var allReleases = _project.Catalog.Content.SelectMany(c => c.Releases).ToList();
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

        int total = pendingArtifacts.Count;
        int current = 0;

        foreach (var artifact in pendingArtifacts)
        {
            current++;
            UploadStatusMessage = $"Uploading artifact {current}/{total}: {artifact.Filename}";

            // Scale progress from 0 to 80
            UploadProgress = (int)((double)(current - 1) / total * 80);

            try
            {
                if (!System.IO.File.Exists(artifact.LocalFilePath))
                {
                    UploadStatusMessage = $"File not found: {artifact.LocalFilePath}";
                    return false;
                }

                using var stream = System.IO.File.OpenRead(artifact.LocalFilePath);
                var result = await provider.UploadFileAsync(stream, artifact.Filename);

                if (result.Success && result.Data != null)
                {
                    artifact.DownloadUrl = result.Data.DirectDownloadUrl;

                    // Optional: Update Hash if provider returns it, or size
                    _logger.LogInformation("Uploaded artifact {File} to {Url}", artifact.Filename, artifact.DownloadUrl);
                }
                else
                {
                    UploadStatusMessage = $"Failed to upload {artifact.Filename}: {result.FirstError}";
                    return false;
                }
            }
            catch (Exception ex)
            {
                UploadStatusMessage = $"Error uploading {artifact.Filename}: {ex.Message}";
                return false;
            }
        }

        return true;
    }

    private async Task SaveHostingStateAsync(string catalogFileId, string catalogUrl)
    {
        if (string.IsNullOrEmpty(_project.ProjectPath))
            return;

        _currentHostingState ??= new HostingState
        {
            ProviderId = SelectedHostingProvider?.ProviderId ?? "unknown",
        };

        // Update or add catalog entry
        var catalogEntry = _currentHostingState.Catalogs.FirstOrDefault(c => c.CatalogId == "default");
        if (catalogEntry == null)
        {
            catalogEntry = new CatalogHostingInfo { CatalogId = "default" };
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

    /// <summary>
    /// Generates the provider definition JSON.
    /// </summary>
    [RelayCommand]
    private async Task GenerateProviderDefinitionAsync()
    {
        if (string.IsNullOrWhiteSpace(CatalogUrl) && string.IsNullOrWhiteSpace(PrimaryCatalogUrl))
        {
            UploadStatusMessage = "Catalog URL is required to generate definition";
            return;
        }

        // Use CatalogUrl from upload if PrimaryCatalogUrl is not set
        var catalogUrlToUse = !string.IsNullOrWhiteSpace(PrimaryCatalogUrl) ? PrimaryCatalogUrl : CatalogUrl;

        try
        {
            var result = await _publisherStudioService.ExportProviderDefinitionAsync(
                _project,
                catalogUrlToUse,
                CatalogMirrorUrls.ToList(),
                ProviderDefinitionUrl);

            if (result.Success && result.Data != null)
            {
                ProviderDefinitionJson = result.Data;
                _logger.LogInformation("Generated provider definition JSON");
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

            // Use 'provider.json' as filename
            var fileName = "provider.json";

            // Upload as a file
            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(ProviderDefinitionJson));
            var result = await SelectedHostingProvider.UploadFileAsync(stream, fileName);

            if (result.Success && result.Data != null)
            {
                ProviderDefinitionUrl = result.Data.DirectDownloadUrl;
                GenerateSubscriptionUrl(); // Regenerate based on new definition URL
                UploadStatusMessage = "✓ Provider definition uploaded!";
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
        CatalogMirrorUrls.Add("https://");
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
        // Prefer Provider Definition URL if available
        if (!string.IsNullOrWhiteSpace(ProviderDefinitionUrl))
        {
            SubscriptionUrl = $"genhub://subscribe?url={Uri.EscapeDataString(ProviderDefinitionUrl)}";
        }
        else if (!string.IsNullOrWhiteSpace(CatalogUrl))
        {
             // Fallback to direct catalog URL
             if (SelectedHostingProvider != null)
             {
                 SubscriptionUrl = SelectedHostingProvider.GetSubscriptionLink(CatalogUrl);
             }
             else
             {
                 SubscriptionUrl = $"genhub://subscribe?url={Uri.EscapeDataString(CatalogUrl)}";
             }
        }
        else
        {
            SubscriptionUrl = "Please upload catalog or definition first";
        }

        _logger.LogInformation("Generated subscription URL: {Url}", SubscriptionUrl);
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
}
