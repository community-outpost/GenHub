using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI and CommunityToolkit ObservableProperty generated properties.")]
public partial class PublishShareViewModel(
    PublisherStudioProject project,
    IPublisherStudioService publisherStudioService,
    ILogger logger,
    IHostingProviderFactory? hostingProviderFactory = null,
    IHostingStateManager? hostingStateManager = null,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null,
    IHostingCredentialStore? credentialStore = null) : ObservableObject, IDisposable
{
    private const string PleaseSelectHostingProviderMessage = "Please select a hosting provider";
    private const string StatusLiveOnline = "Live Online";
    private const string StatusPendingUpload = "Pending Upload";
    private const string StatusExternalCdn = "External CDN";
    private HostingState? _currentHostingState;

    [ObservableProperty]
    private IHostingProvider? _selectedHostingProvider = hostingProviderFactory?.GetCatalogHostingProviders().FirstOrDefault();

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

    private System.Threading.CancellationTokenSource? _authCts;
    private CancellationTokenSource? _uploadCts;
    private CancellationTokenSource? _scanCts;

    [RelayCommand]
    private void CancelUpload()
    {
        if (IsUploading)
        {
            var cts = _uploadCts;
            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Upload already completed or was disposed concurrently
            }
        }
    }

    /// <summary>
    /// Gets the collection of hosted assets across definition, catalogs, and releases.
    /// </summary>
    public ObservableCollection<HostedAssetItemViewModel> HostedAssets { get; } = new();

    [ObservableProperty]
    private string _totalStorageUsedFormatted = "0 B";

    [ObservableProperty]
    private int _totalHostedFilesCount;

    [ObservableProperty]
    private int _hostedDefinitionCount;

    [ObservableProperty]
    private int _hostedCatalogsCount;

    [ObservableProperty]
    private int _hostedArtifactsCount;

    [ObservableProperty]
    private int _externalCdnCount;

    [ObservableProperty]
    private bool _isScanningStorage;

    [ObservableProperty]
    private string _storageScanStatusMessage = string.Empty;

    [ObservableProperty]
    private string _hostingFolderPath = HostingConstants.DropboxDefaultPublisherFolder;

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
    public ObservableCollection<CatalogPublishStatus> CatalogStatuses { get; } = project?.Catalogs != null ? new ObservableCollection<CatalogPublishStatus>(project.Catalogs.Select(c => new CatalogPublishStatus(c))) : [];

    /// <summary>
    /// Gets a value indicating whether the selected provider requires authentication.
    /// </summary>
    public bool RequiresAuthentication => SelectedHostingProvider is { RequiresAuthentication: true };

    /// <summary>
    /// Gets a value indicating whether the selected provider is authenticated.
    /// </summary>
    public bool IsProviderAuthenticated => SelectedHostingProvider is { IsAuthenticated: true };

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
    /// Gets the text to display on the primary connect button.
    /// </summary>
    public string ConnectButtonText => SelectedHostingProvider != null
        ? FormatLocalizedString("Tools.PublisherStudio.Publish.ConnectToProvider", "Connect to {0}", SelectedHostingProvider.DisplayName)
        : GetLocalizedString("Tools.PublisherStudio.Publish.ConnectProvider", "Connect Provider");

    /// <summary>
    /// Gets the text to display on the primary publish button.
    /// </summary>
    public string PublishButtonText => SelectedHostingProvider != null
        ? FormatLocalizedString("Tools.PublisherStudio.Publish.PublishToProvider", "Publish to {0}", SelectedHostingProvider.DisplayName)
        : GetLocalizedString("Tools.PublisherStudio.Publish.PublishAllCatalogs", "Publish All Catalogs & Update Definition");

    /// <summary>
    /// Gets the human-readable description of where files will be uploaded.
    /// </summary>
    public string TargetDestinationDescription
    {
        get
        {
            if (SelectedHostingProvider == null)
            {
                return GetLocalizedString("Tools.PublisherStudio.Publish.NoProviderSelected", "No hosting provider selected");
            }

            return SelectedHostingProvider.ProviderId switch
            {
                HostingConstants.GoogleDrive => GetLocalizedString("Tools.PublisherStudio.Publish.DestinationGoogleDrive", $"Your Google Drive (inside '{HostingConstants.GoogleDriveDefaultPublisherFolder}' folder)"),
                HostingConstants.Dropbox => GetLocalizedString("Tools.PublisherStudio.Publish.DestinationDropbox", "Your Dropbox account (inside '/Apps/GenHub/' app folder)"),
                HostingConstants.GitHub => GetLocalizedString("Tools.PublisherStudio.Publish.DestinationGitHub", "Your GitHub Gists (manifests & definitions only; binaries require CDN URLs)"),
                _ => SelectedHostingProvider.DisplayName,
            };
        }
    }

    /// <summary>
    /// Gets the count of pending local artifacts awaiting upload.
    /// </summary>
    public int PendingArtifactsCount => project.Catalogs
        .SelectMany(c => c.Catalog.Content)
        .SelectMany(c => c.Releases)
        .SelectMany(r => r.Artifacts)
        .Count(a => !string.IsNullOrEmpty(a.LocalFilePath) && string.IsNullOrEmpty(a.DownloadUrl));

    /// <summary>
    /// Gets the count of artifacts served via external CDN or direct download links.
    /// </summary>
    public int ExternalCdnArtifactsCount => project.Catalogs
        .SelectMany(c => c.Catalog.Content)
        .SelectMany(c => c.Releases)
        .SelectMany(r => r.Artifacts)
        .Count(a => !string.IsNullOrEmpty(a.DownloadUrl) && !IsCloudProviderUrl(a.DownloadUrl));

    /// <summary>
    /// Gets a value indicating whether the selected provider cannot host binary artifacts but the project has pending local artifacts.
    /// </summary>
    public bool HasIncompatibleArtifactsForProvider =>
        SelectedHostingProvider != null &&
        !SelectedHostingProvider.SupportsArtifactHosting &&
        PendingArtifactsCount > 0;

    /// <summary>
    /// Gets the count of pending local artifacts awaiting upload in the active catalog.
    /// </summary>
    public int ActiveCatalogPendingArtifactsCount => ActiveCatalog?.Catalog.Content
        .SelectMany(c => c.Releases)
        .SelectMany(r => r.Artifacts)
        .Count(a => !string.IsNullOrEmpty(a.LocalFilePath) && string.IsNullOrEmpty(a.DownloadUrl)) ?? 0;

    /// <summary>
    /// Gets a value indicating whether the selected provider cannot host binary artifacts but the active catalog has pending local artifacts.
    /// </summary>
    public bool HasIncompatibleArtifactsForActiveCatalog =>
        SelectedHostingProvider != null &&
        !SelectedHostingProvider.SupportsArtifactHosting &&
        ActiveCatalogPendingArtifactsCount > 0;

    /// <summary>
    /// Updates the catalog ID, name, and file name in the hosting state if present and persists the change.
    /// </summary>
    /// <param name="oldCatalogId">The former catalog ID.</param>
    /// <param name="newCatalogId">The new catalog ID.</param>
    /// <param name="newCatalogName">The new catalog display name.</param>
    /// <param name="newFileName">The new catalog file name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RenameCatalogInHostingStateAsync(
        string oldCatalogId,
        string newCatalogId,
        string newCatalogName,
        string newFileName,
        CancellationToken cancellationToken = default)
    {
        if (_currentHostingState == null && !string.IsNullOrEmpty(project.ProjectPath))
        {
            var loadResult = hostingStateManager != null ? await hostingStateManager.LoadStateAsync(project.ProjectPath, cancellationToken).ConfigureAwait(false) : null;
            if (loadResult?.Success == true && loadResult.Data != null)
            {
                _currentHostingState = loadResult.Data;
            }
        }

        if (_currentHostingState == null)
        {
            return;
        }

        var entry = _currentHostingState.Catalogs.FirstOrDefault(c => c.CatalogId == oldCatalogId);
        if (entry != null)
        {
            entry.CatalogId = newCatalogId;
            entry.CatalogName = newCatalogName;
            entry.FileName = newFileName;

            if (!string.IsNullOrEmpty(project.ProjectPath))
            {
                if (hostingStateManager != null) await hostingStateManager.SaveStateAsync(project.ProjectPath, _currentHostingState, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Synchronizes available catalogs and refreshes state when catalogs are added, removed, or renamed.
    /// </summary>
    public void SyncAvailableCatalogs()
    {
        AvailableCatalogs.Clear();
        foreach (var catalog in project.Catalogs)
        {
            AvailableCatalogs.Add(catalog);
        }

        if (ActiveCatalog == null || !project.Catalogs.Contains(ActiveCatalog))
        {
            ActiveCatalog = AvailableCatalogs.FirstOrDefault();
        }

        InitializeCatalogStatuses();
        RefreshUploadHierarchy();
        RefreshHostedAssets();
        RefreshArtifactStatuses();
    }

    /// <summary>
    /// Gets an explanatory warning message when the provider cannot host the pending local files.
    /// </summary>
    public string IncompatibleArtifactsWarningMessage =>
        $"{SelectedHostingProvider?.DisplayName ?? "This provider"} only hosts catalog metadata (JSON). Your project has {PendingArtifactsCount} local file(s) pending upload. Either provide direct CDN URLs for those files, or switch to Google Drive or Dropbox to host binary archives.";

    /// <summary>
    /// Gets the available catalogs in the project.
    /// </summary>
    public ObservableCollection<NamedCatalog> AvailableCatalogs { get; } = project?.Catalogs != null ? new ObservableCollection<NamedCatalog>(project.Catalogs) : [];

    [ObservableProperty]
    private NamedCatalog? _activeCatalog = project?.Catalogs.FirstOrDefault();

    /// <summary>
    /// Gets the list of artifact URL statuses.
    /// </summary>
    public ObservableCollection<ArtifactUrlStatus> ArtifactStatuses { get; } = new();

    /// <summary>
    /// Gets the upload queue for tracking artifact uploads.
    /// </summary>
    public ObservableCollection<ArtifactUploadTask> UploadQueue { get; } = new();

    /// <summary>
    /// Gets the content item count in the active catalog.
    /// </summary>
    public int ContentItemCount => ActiveCatalog?.Catalog.Content.Count ?? 0;

    /// <summary>
    /// Gets the total release count across all content items in the active catalog.
    /// </summary>
    public int TotalReleaseCount => ActiveCatalog?.Catalog.Content.Sum(c => c.Releases.Count) ?? 0;

    /// <summary>
    /// Gets the available hosting providers.
    /// </summary>
    public ObservableCollection<IHostingProvider> HostingProviders { get; } = hostingProviderFactory != null ? new ObservableCollection<IHostingProvider>(hostingProviderFactory.GetCatalogHostingProviders()) : [];

    /// <summary>
    /// Asynchronously initializes hosting state, upload hierarchy, and validates catalogs.
    /// </summary>
    public async Task InitializeAsync()
    {
        InitializeCatalogStatuses();
        RefreshUploadHierarchy();
        RefreshHostedAssets();
        RefreshArtifactStatuses();
        await LoadHostingStateAsync().ConfigureAwait(false);
        await ValidateCatalogAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reloads the hosting providers from the factory.
    /// </summary>
    public void ReloadHostingProviders()
    {
        if (hostingProviderFactory == null) return;

        var existingSelectedId = SelectedHostingProvider?.ProviderId;
        HostingProviders.Clear();

        foreach (var provider in hostingProviderFactory.GetCatalogHostingProviders())
        {
            HostingProviders.Add(provider);
        }

        if (!string.IsNullOrEmpty(existingSelectedId))
        {
            SelectedHostingProvider = HostingProviders.FirstOrDefault(p => p.ProviderId == existingSelectedId)
                ?? HostingProviders.FirstOrDefault();
        }
        else if (SelectedHostingProvider == null)
        {
            SelectedHostingProvider = HostingProviders.FirstOrDefault();
        }
    }

    /// <summary>
    /// Refreshes the three-tier upload hierarchy (1. Definition / 2. Catalogs / 3. Content items and releases).
    /// </summary>
    public void RefreshUploadHierarchy()
    {
        try
        {
            PopulateUploadHierarchyHeader();
            UploadHierarchy.Catalogs.Clear();
            foreach (var namedCat in project.Catalogs)
            {
                UploadHierarchy.Catalogs.Add(BuildCatalogNode(namedCat));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh upload hierarchy");
        }
    }

    /// <summary>
    /// Rebuilds the hosted assets inventory list and recalculates storage metrics.
    /// </summary>
    public void RefreshHostedAssets()
    {
        HostedAssets.Clear();
        long totalBytes = 0;
        var defCount = 0;
        var catCount = 0;
        var artCount = 0;
        var cdnCount = 0;

        var providerName = SelectedHostingProvider?.DisplayName ?? "Cloud Storage";

        PopulatePublisherDefinitionAsset(providerName, ref totalBytes, ref defCount);
        PopulateCatalogAssets(providerName, ref totalBytes, ref catCount);
        PopulateArtifactAssets(providerName, ref totalBytes, ref artCount, ref cdnCount);
        PopulateCloudScanAssets(providerName, ref totalBytes, ref catCount, ref artCount);

        TotalStorageUsedFormatted = GenHub.Core.Helpers.FileSizeFormatter.Format(totalBytes);
        TotalHostedFilesCount = defCount + catCount + artCount;
        HostedDefinitionCount = defCount;
        HostedCatalogsCount = catCount;
        HostedArtifactsCount = artCount;
        ExternalCdnCount = cdnCount;
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
            _authCts?.Cancel();
            _authCts?.Dispose();
            _authCts = null;
            _uploadCts?.Cancel();
            _uploadCts?.Dispose();
            _uploadCts = null;
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private static string BuildPublishSummary(string catalogUrl, string providerDefinitionUrl, string subscriptionUrl)
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

    private static void CleanupTempZipFile(string? tempZipPath)
    {
        if (tempZipPath == null || !File.Exists(tempZipPath))
        {
            return;
        }

        try
        {
            File.Delete(tempZipPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup
        }
    }

    private string GetLocalizedString(string key, string defaultValue) =>
        localizationService?.GetString(key) ?? defaultValue;

    private string FormatLocalizedString(string key, string defaultValueFormat, params object[] args)
    {
        var template = localizationService?.GetString(key);
        if (!string.IsNullOrEmpty(template))
        {
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                // Fallback on format failure
            }
        }

        return string.Format(defaultValueFormat, args);
    }

    private void PopulatePublisherDefinitionAsset(string providerName, ref long totalBytes, ref int defCount)
    {
        var defUrl = ProviderDefinitionUrl;
        if (string.IsNullOrWhiteSpace(defUrl))
        {
            defUrl = _currentHostingState?.Definition?.Url;
        }

        var defSize = _currentHostingState?.Definition?.FileSize ?? 0;
        var defUpdated = _currentHostingState?.Definition?.LastUpdated ?? DateTime.MinValue;
        var isDefHosted = !string.IsNullOrWhiteSpace(defUrl);

        if (isDefHosted)
        {
            defCount = 1;
            totalBytes += defSize;
        }

        HostedAssets.Add(new HostedAssetItemViewModel
        {
            Name = project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName,
            Category = "Publisher Definition",
            Location = isDefHosted ? $"{providerName} ({HostingConstants.DropboxDefaultPublisherFolder})" : "Local only",
            FileSize = defSize,
            Url = defUrl ?? string.Empty,
            Status = isDefHosted ? StatusLiveOnline : StatusPendingUpload,
            IsOnline = isDefHosted,
            IsExternalCdn = false,
            LastUpdated = defUpdated,
        });
    }

    private void PopulateCatalogAssets(string providerName, ref long totalBytes, ref int catCount)
    {
        foreach (var catalog in project.Catalogs)
        {
            var catHosting = _currentHostingState?.Catalogs.FirstOrDefault(c => c.CatalogId == catalog.Id || c.FileName == catalog.FileName);
            var isCatHosted = catHosting != null && !string.IsNullOrWhiteSpace(catHosting.Url);
            var catSize = catHosting?.FileSize ?? 0;
            var catUrl = catHosting?.Url ?? string.Empty;
            var catUpdated = catHosting?.LastUpdated ?? DateTime.MinValue;

            if (isCatHosted)
            {
                catCount++;
                totalBytes += catSize;
            }

            HostedAssets.Add(new HostedAssetItemViewModel
            {
                Name = catalog.FileName,
                Category = $"Catalog Manifest ({catalog.Name})",
                Location = isCatHosted ? $"{providerName} ({HostingConstants.DropboxDefaultPublisherFolder})" : "Local only",
                FileSize = catSize,
                Url = catUrl,
                Status = isCatHosted ? StatusLiveOnline : StatusPendingUpload,
                IsOnline = isCatHosted,
                IsExternalCdn = false,
                LastUpdated = catUpdated,
            });
        }
    }

    private void PopulateArtifactAssets(string providerName, ref long totalBytes, ref int artCount, ref int cdnCount)
    {
        var allArtifacts = project.Catalogs
            .SelectMany(c => c.Catalog.Content)
            .SelectMany(content => content.Releases.SelectMany(release => release.Artifacts.Select(artifact => (content.Name, release.Version, artifact))));

        foreach (var (contentName, version, artifact) in allArtifacts)
        {
            ProcessArtifactAsset(artifact, contentName, version, providerName, ref totalBytes, ref artCount, ref cdnCount);
        }
    }

    private void ProcessArtifactAsset(
        ReleaseArtifact artifact,
        string contentName,
        string releaseVersion,
        string providerName,
        ref long totalBytes,
        ref int artCount,
        ref int cdnCount)
    {
        var isExternal = !string.IsNullOrEmpty(artifact.DownloadUrl) && !IsCloudProviderUrl(artifact.DownloadUrl);
        var isCloud = !string.IsNullOrEmpty(artifact.DownloadUrl) && IsCloudProviderUrl(artifact.DownloadUrl);
        var artHosting = _currentHostingState?.Artifacts.FirstOrDefault(a => a.FileName == artifact.Filename || a.Url == artifact.DownloadUrl);
        var artSize = artifact.Size > 0 ? artifact.Size : (artHosting?.FileSize ?? 0);
        var artUpdated = artHosting?.LastUpdated ?? DateTime.MinValue;

        string location = string.Empty;
        string status = string.Empty;
        if (isCloud)
        {
            artCount++;
            totalBytes += artSize;
            location = $"{providerName} ({HostingConstants.DropboxDefaultPublisherFolder})";
            status = StatusLiveOnline;
        }
        else if (isExternal)
        {
            cdnCount++;
            location = StatusExternalCdn;
            status = StatusExternalCdn;
        }
        else
        {
            location = "Local file";
            status = StatusPendingUpload;
        }

        HostedAssets.Add(new HostedAssetItemViewModel
        {
            Name = artifact.Filename,
            Category = $"Release Binary ({contentName} v{releaseVersion})",
            Location = location,
            FileSize = artSize,
            Url = artifact.DownloadUrl ?? string.Empty,
            Status = status,
            IsOnline = isCloud,
            IsExternalCdn = isExternal,
            LastUpdated = artUpdated,
            Sha256 = artifact.Sha256,
        });
    }

    private void PopulateCloudScanAssets(string providerName, ref long totalBytes, ref int catCount, ref int artCount)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        // skipcq: CS-R1033
        foreach (var cloudCat in _currentHostingState.Catalogs.Where(cloudCat => !HostedAssets.Any(a =>
            (!string.IsNullOrEmpty(a.Url) && !string.IsNullOrEmpty(cloudCat.Url) && string.Equals(a.Url, cloudCat.Url, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(cloudCat.FileName) && string.Equals(a.Name, cloudCat.FileName, StringComparison.OrdinalIgnoreCase)))))
        {
            catCount++;
            totalBytes += cloudCat.FileSize;
            HostedAssets.Add(new HostedAssetItemViewModel
            {
                Name = string.IsNullOrEmpty(cloudCat.FileName) ? $"catalog-{cloudCat.CatalogId}.json" : cloudCat.FileName,
                Category = $"Cloud Catalog ({cloudCat.CatalogId})",
                Location = $"{providerName} ({HostingConstants.DropboxDefaultPublisherFolder})",
                FileSize = cloudCat.FileSize,
                Url = cloudCat.Url,
                Status = StatusLiveOnline,
                IsOnline = true,
                IsExternalCdn = false,
                LastUpdated = cloudCat.LastUpdated,
            });
        }

        // skipcq: CS-R1033
        foreach (var cloudArt in _currentHostingState.Artifacts.Where(cloudArt => !HostedAssets.Any(a =>
            (!string.IsNullOrEmpty(a.Url) && !string.IsNullOrEmpty(cloudArt.Url) && string.Equals(a.Url, cloudArt.Url, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(cloudArt.FileName) && string.Equals(a.Name, cloudArt.FileName, StringComparison.OrdinalIgnoreCase)))))
        {
            artCount++;
            totalBytes += cloudArt.FileSize;
            HostedAssets.Add(new HostedAssetItemViewModel
            {
                Name = cloudArt.FileName,
                Category = "Cloud Artifact",
                Location = $"{providerName} ({HostingConstants.DropboxDefaultPublisherFolder})",
                FileSize = cloudArt.FileSize,
                Url = cloudArt.Url,
                Status = StatusLiveOnline,
                IsOnline = true,
                IsExternalCdn = false,
                LastUpdated = cloudArt.LastUpdated,
                Sha256 = cloudArt.Sha256,
            });
        }
    }

    partial void OnActiveCatalogChanged(NamedCatalog? value)
    {
        if (value == null) return;
        RefreshArtifactStatuses();
        _ = ValidateCatalogAsync();
        OnPropertyChanged(nameof(ContentItemCount));
        OnPropertyChanged(nameof(TotalReleaseCount));
        OnPropertyChanged(nameof(ActiveCatalogPendingArtifactsCount));
        OnPropertyChanged(nameof(HasIncompatibleArtifactsForActiveCatalog));
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
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(TargetDestinationDescription));
        OnPropertyChanged(nameof(HasIncompatibleArtifactsForProvider));
        OnPropertyChanged(nameof(IncompatibleArtifactsWarningMessage));

        HostingFolderPath = value?.ProviderId switch
        {
            HostingConstants.Dropbox => HostingConstants.DropboxDefaultPublisherFolder,
            HostingConstants.GoogleDrive => HostingConstants.GoogleDriveDefaultPublisherFolder,
            HostingConstants.GitHub => HostingConstants.GitHubGistsDestinationLabel,
            _ => HostingConstants.RemoteCloudDestinationLabel,
        };
        RefreshHostedAssets();

        if (value == null) return;

        // Check if hosting state has saved credentials for this provider
        if (_currentHostingState is { } hostingState
            && hostingState.ProviderId == value.ProviderId
            && !string.IsNullOrEmpty(hostingState.AuthToken))
        {
            // Restore saved authentication
            _ = RestoreAuthenticationAsync();
        }
        else
        {
            // Different provider - clear auth status but preserve entered credentials across UI switches
            AuthenticationStatusMessage = string.Empty;
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

        if (_authCts != null)
        {
            await _authCts.CancelAsync();
            _authCts.Dispose();
        }

        _authCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(HostingConstants.BrowserAuthTimeoutSeconds));

        IsAuthenticating = true;
        AuthenticationStatusMessage = "Authenticating...";

        try
        {
            var result = await ExecuteAuthenticationByProviderTypeAsync(_authCts.Token);
            if (result == null)
            {
                return;
            }

            if (result.Success)
            {
                await HandleAuthenticationSuccessAsync();
                if (SelectedHostingProvider.SupportsCatalogHosting)
                {
                    _ = ScanCloudStorageSilentlyAsync();
                }
            }
            else
            {
                HandleAuthenticationFailure(result);
            }

            NotifyAuthenticationStateChanged();
        }
        catch (OperationCanceledException ex)
        {
            AuthenticationStatusMessage = "Authentication was canceled or timed out. For Google Drive, ensure you selected 'Desktop app' (not 'Web application') in Google Cloud Console.";
            logger.LogInformation(ex, "Authentication canceled or timed out for {Provider}", SelectedHostingProvider.DisplayName);
            notificationService?.ShowWarning(GetLocalizedString("Tools.PublisherStudio.Publish.AuthCanceledTitle", "Authentication Canceled"), GetLocalizedString("Tools.PublisherStudio.Publish.AuthCanceledTimeout", "Authentication timed out or was canceled."));
        }
        catch (Exception ex)
        {
            AuthenticationStatusMessage = $"Authentication error: {ex.Message}";
            logger.LogError(ex, "Authentication error for {Provider}", SelectedHostingProvider.DisplayName);
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    /// <summary>
    /// Cancels an in-progress authentication attempt.
    /// </summary>
    [RelayCommand]
    private void CancelAuthentication()
    {
        if (IsAuthenticating)
        {
            var cts = _authCts;
            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already disposed
            }

            IsAuthenticating = false;
            AuthenticationStatusMessage = "Authentication canceled.";
            NotifyAuthenticationStateChanged();
            notificationService?.ShowInfo(GetLocalizedString("Tools.PublisherStudio.Publish.AuthCanceledTitle", "Authentication Canceled"), GetLocalizedString("Tools.PublisherStudio.Publish.AuthCanceledAborted", "Hosting provider connection was aborted."));
        }
    }

    private async Task<OperationResult<bool>?> ExecuteAuthenticationByProviderTypeAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        if (SelectedHostingProvider is GoogleDriveHostingProvider gdrive && !ConfigureGoogleDrive(gdrive))
        {
            return null;
        }

        if (SelectedHostingProvider is GitHubHostingProvider githubProvider)
        {
            if (string.IsNullOrWhiteSpace(GitHubPersonalAccessToken))
            {
                AuthenticationStatusMessage = "Please enter your GitHub Personal Access Token";
                return null;
            }

            return await githubProvider.AuthenticateWithTokenAsync(GitHubPersonalAccessToken, cancellationToken);
        }

        if (SelectedHostingProvider is DropboxHostingProvider dropboxProvider)
        {
            if (string.IsNullOrWhiteSpace(DropboxAccessToken))
            {
                AuthenticationStatusMessage = "Please enter your Dropbox Access Token";
                return null;
            }

            return await dropboxProvider.AuthenticateWithTokenAsync(DropboxAccessToken, cancellationToken);
        }

        return SelectedHostingProvider != null
            ? await SelectedHostingProvider.AuthenticateAsync(cancellationToken)
            : null;
    }

    private bool ConfigureGoogleDrive(GoogleDriveHostingProvider gdrive)
    {
        var hasCredentials = !string.IsNullOrWhiteSpace(GoogleClientId) && !string.IsNullOrWhiteSpace(GoogleClientSecret);

        if (!hasCredentials)
        {
            AuthenticationStatusMessage = "Google Drive requires client credentials. Enter your Client ID and Client Secret above.";
            notificationService?.ShowWarning(
                "Google Drive Credentials Needed",
                "Please enter your Google OAuth Client ID and Secret to connect to Google Drive. Follow the Project Configuration guide above.");
            return false;
        }

        gdrive.CustomClientId = GoogleClientId.Trim();
        gdrive.CustomClientSecret = GoogleClientSecret.Trim();
        return true;
    }

    private async Task HandleAuthenticationSuccessAsync()
    {
        AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.AuthenticatedSuccess", "Authenticated successfully");
        logger.LogInformation("Authenticated with {Provider}", SelectedHostingProvider?.DisplayName ?? "Provider");

        // Save token to hosting state for persistence
        await SaveAuthTokenAsync();

        OnPropertyChanged(nameof(IsProviderAuthenticated));
        OnPropertyChanged(nameof(NeedsAuthentication));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(TargetDestinationDescription));

        notificationService?.ShowSuccess(GetLocalizedString("Tools.PublisherStudio.Publish.ConnectedTitle", "Connected"), FormatLocalizedString("Tools.PublisherStudio.Publish.ConnectedMessage", "Successfully connected to {0}. You can now publish your catalog.", SelectedHostingProvider?.DisplayName ?? "Provider"), autoDismissMs: 4000);
    }

    private void HandleAuthenticationFailure(OperationResult<bool> result)
    {
        AuthenticationStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.AuthFailed", "Authentication failed: {0}", result.FirstError);
        logger.LogWarning("Authentication failed for {Provider}: {Error}", SelectedHostingProvider?.DisplayName ?? "Provider", result.FirstError);

        notificationService?.ShowError(GetLocalizedString("Tools.PublisherStudio.Publish.AuthError", "Authentication Error"), result.FirstError ?? "Failed to authenticate with the hosting provider.");
    }

    private void NotifyAuthenticationStateChanged()
    {
        OnPropertyChanged(nameof(IsProviderAuthenticated));
        OnPropertyChanged(nameof(NeedsAuthentication));
        OnPropertyChanged(nameof(ShowGitHubPatInput));
        OnPropertyChanged(nameof(ShowGoogleOAuthButton));
        OnPropertyChanged(nameof(ShowDropboxTokenInput));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(TargetDestinationDescription));
        OnPropertyChanged(nameof(HasIncompatibleArtifactsForProvider));
        OnPropertyChanged(nameof(IncompatibleArtifactsWarningMessage));
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
            if (credentialStore != null)
            {
                await credentialStore.DeleteCredentialAsync(SelectedHostingProvider.ProviderId).ConfigureAwait(false);
            }
            AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.SignedOut", "Signed out");
            GitHubPersonalAccessToken = string.Empty;
            DropboxAccessToken = string.Empty;

            // Notify computed properties
            OnPropertyChanged(nameof(IsProviderAuthenticated));
            OnPropertyChanged(nameof(NeedsAuthentication));
            OnPropertyChanged(nameof(ShowGitHubPatInput));
            OnPropertyChanged(nameof(ShowGoogleOAuthButton));
            OnPropertyChanged(nameof(ShowDropboxTokenInput));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(PublishButtonText));
            OnPropertyChanged(nameof(TargetDestinationDescription));

            logger.LogInformation("Signed out from {Provider}", SelectedHostingProvider.DisplayName);
        }
        catch (Exception ex)
        {
            AuthenticationStatusMessage = $"Sign out error: {ex.Message}";
            logger.LogError(ex, "Sign out error for {Provider}", SelectedHostingProvider.DisplayName);
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

        OnPropertyChanged(nameof(PendingArtifactsCount));
        OnPropertyChanged(nameof(ExternalCdnArtifactsCount));
        OnPropertyChanged(nameof(HasIncompatibleArtifactsForProvider));
        OnPropertyChanged(nameof(IncompatibleArtifactsWarningMessage));
    }

    private async Task LoadHostingStateAsync()
    {
        if (string.IsNullOrEmpty(project.ProjectPath))
            return;

        try
        {
            var result = hostingStateManager != null ? await hostingStateManager.LoadStateAsync(project.ProjectPath, CancellationToken.None) : null;
            if (result?.Success == true && result.Data != null)
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
                RefreshHostedAssets();
                logger.LogInformation("Loaded hosting state with {CatalogCount} catalogs", _currentHostingState.Catalogs.Count);

                // After loading state, try to restore authentication
                if (!string.IsNullOrEmpty(_currentHostingState.AuthToken))
                {
                    await RestoreAuthenticationAsync();
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Hosting state loading was canceled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load hosting state");
        }
    }

    private void PopulateUploadHierarchyHeader()
    {
        UploadHierarchy.PublisherName = project.Catalog.Publisher?.Name ?? "Publisher";
        UploadHierarchy.PublisherId = project.Catalog.Publisher?.Id ?? "publisher";
        UploadHierarchy.AvatarUrl = project.Catalog.Publisher?.AvatarUrl;
        UploadHierarchy.Website = project.Catalog.Publisher?.Website;
        UploadHierarchy.DefinitionUrl = ProviderDefinitionUrl;
        UploadHierarchy.SubscriptionUrl = SubscriptionUrl;
        UploadHierarchy.IsUploaded = !string.IsNullOrWhiteSpace(ProviderDefinitionUrl);
        UploadHierarchy.LastUpdated = _currentHostingState?.Definition?.LastUpdated;
    }

    private UploadArtifactNodeViewModel BuildArtifactNode(ReleaseArtifact art)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(art.DownloadUrl);
        var localArtifact = ArtifactStatuses.FirstOrDefault(a => a.ArtifactName == art.Filename);
        var hasLocal = localArtifact is { HasLocalFile: true } || !string.IsNullOrWhiteSpace(art.LocalFilePath);
        var localPath = localArtifact?.LocalFilePath ?? art.LocalFilePath ?? string.Empty;
        var isExternalCdn = hasUrl && !IsCloudProviderUrl(art.DownloadUrl);

        var artNode = new UploadArtifactNodeViewModel
        {
            FileName = art.Filename,
            DownloadUrl = art.DownloadUrl ?? string.Empty,
            FileSizeFormatted = GenHub.Core.Helpers.FileSizeFormatter.Format(art.Size),
            Sha256 = art.Sha256 ?? string.Empty,
            IsHosted = hasUrl,
            HasLocalFile = hasLocal,
            LocalFilePath = localPath,
            IsExternalCdn = isExternalCdn,
        };

        return artNode;
    }

    private UploadReleaseNodeViewModel BuildReleaseNode(ContentRelease rel)
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
                relNode.Artifacts.Add(BuildArtifactNode(art));
            }
        }

        return relNode;
    }

    private UploadContentNodeViewModel BuildContentNode(CatalogContentItem contentItem)
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
                contentNode.Releases.Add(BuildReleaseNode(rel));
            }
        }

        return contentNode;
    }

    private UploadCatalogNodeViewModel BuildCatalogNode(NamedCatalog namedCat)
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

        if (namedCat.Catalog?.Content != null)
        {
            foreach (var contentItem in namedCat.Catalog.Content)
            {
                catNode.ContentItems.Add(BuildContentNode(contentItem));
            }
        }

        return catNode;
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

            var result = await publisherStudioService.ValidateCatalogAsync(ActiveCatalog.Catalog, allowPendingArtifacts: true, cancellationToken: CancellationToken.None);
            IsValid = result.Success;
            ValidationMessage = result.Success ? $"Catalog '{ActiveCatalog.Name}' is valid" : $"Validation failed: {result.FirstError}";

            logger.LogInformation("Catalog '{CatalogName}' validation: {IsValid}", ActiveCatalog.Name, IsValid);
        }
        catch (Exception ex)
        {
            IsValid = false;
            ValidationMessage = $"Validation error: {ex.Message}";
            logger.LogError(ex, "Error validating catalog");
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
                logger.LogWarning("Cannot export catalog: no active catalog selected");
                return;
            }

            var result = await publisherStudioService.ExportCatalogAsync(project, ActiveCatalog, cancellationToken: CancellationToken.None);
            if (result.Success && result.Data != null)
            {
                CatalogJson = result.Data;
                logger.LogInformation("Exported catalog '{CatalogName}' JSON", ActiveCatalog.Name);
            }
            else
            {
                logger.LogError("Failed to export catalog '{CatalogName}': {Error}", ActiveCatalog.Name, result.FirstError);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error exporting catalog");
        }
    }

    /// <summary>
    /// Uploads the catalog to the selected hosting provider.
    /// </summary>
    [RelayCommand]
    private async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync()
    {
        if (IsUploading)
        {
            return OperationResult<HostingUploadResult>.CreateFailure("An upload is already in progress.");
        }

        _uploadCts?.Dispose();
        _uploadCts = new CancellationTokenSource();
        var cancellationToken = _uploadCts.Token;

        return await UploadCatalogCoreAsync(cancellationToken, manageUploadingState: true);
    }

    private async Task<OperationResult<HostingUploadResult>> UploadCatalogCoreAsync(
        CancellationToken cancellationToken,
        bool manageUploadingState)
    {
        if (SelectedHostingProvider == null)
        {
            UploadStatusMessage = PleaseSelectHostingProviderMessage;
            return OperationResult<HostingUploadResult>.CreateFailure(PleaseSelectHostingProviderMessage);
        }

        if (HasIncompatibleArtifactsForActiveCatalog)
        {
            var warningMsg = $"{SelectedHostingProvider?.DisplayName ?? "This provider"} only hosts catalog metadata (JSON). The active catalog '{ActiveCatalog?.Name}' has {ActiveCatalogPendingArtifactsCount} local file(s) pending upload. Either provide direct CDN URLs for those files, or switch to Google Drive or Dropbox to host binary archives.";
            UploadStatusMessage = warningMsg;
            notificationService?.ShowError(GetLocalizedString("Tools.PublisherStudio.Publish.IncompatibleProvider", "Incompatible Provider"), warningMsg);
            return OperationResult<HostingUploadResult>.CreateFailure(warningMsg);
        }

        await ValidateCatalogAsync();
        if (!IsValid)
        {
            UploadStatusMessage = "Please fix validation errors before uploading";
            return OperationResult<HostingUploadResult>.CreateFailure("Please fix validation errors before uploading");
        }

        try
        {
            if (manageUploadingState)
            {
                IsUploading = true;
            }

            UploadProgress = 0;
            UploadStatusMessage = "Preparing to publish...";
            PublishCompleted = false;
            CurrentPublishStep = 0;
            PublishSummary = string.Empty;

            if (!await EnsureProviderAuthenticatedAsync(cancellationToken))
            {
                return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
            }

            // 1. Upload Pending Artifacts
            CurrentPublishStep = 1;
            if (!await UploadPendingArtifactsAsync(SelectedHostingProvider, cancellationToken))
            {
                return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
            }

            // 2. Export Active Catalog (Now includes new URLs)
            CurrentPublishStep = 2;
            if (ActiveCatalog == null)
            {
                UploadStatusMessage = "No catalog selected";
                return OperationResult<HostingUploadResult>.CreateFailure("No catalog selected");
            }

            UploadStatusMessage = $"Generating catalog '{ActiveCatalog.Name}'...";
            var exportResult = await publisherStudioService.ExportCatalogAsync(project, ActiveCatalog, cancellationToken: cancellationToken);
            if (!exportResult.Success || string.IsNullOrEmpty(exportResult.Data))
            {
                UploadStatusMessage = $"Failed to export catalog: {exportResult.FirstError}";
                return OperationResult<HostingUploadResult>.CreateFailure(exportResult);
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

            var uploadResult = await PerformCatalogUploadAsync(progress, cancellationToken);
            if (uploadResult.Success && uploadResult.Data != null)
            {
                await CompletePublishSuccessAsync(uploadResult.Data, cancellationToken);
                return uploadResult;
            }
            else
            {
                UploadStatusMessage = $"Catalog upload failed: {uploadResult.FirstError}";
                return uploadResult;
            }
        }
        catch (OperationCanceledException ex)
        {
            UploadStatusMessage = "Upload canceled.";
            logger.LogInformation(ex, "Catalog upload was canceled.");
            return OperationResult<HostingUploadResult>.CreateFailure("Upload canceled");
        }
        catch (Exception ex)
        {
            UploadStatusMessage = $"Error: {ex.Message}";
            logger.LogError(ex, "Error uploading catalog");
            return OperationResult<HostingUploadResult>.CreateFailure($"Error uploading catalog: {ex.Message}");
        }
        finally
        {
            if (manageUploadingState)
            {
                IsUploading = false;
                _uploadCts?.Dispose();
                _uploadCts = null;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "Accesses generated ObservableProperties")]
    private async Task<bool> EnsureProviderAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedHostingProvider == null) return false;
        if (SelectedHostingProvider.RequiresAuthentication && !SelectedHostingProvider.IsAuthenticated)
        {
            UploadStatusMessage = "Authenticating...";
            var authResult = await ExecuteAuthenticationByProviderTypeAsync(cancellationToken);
            if (authResult == null || !authResult.Success)
            {
                UploadStatusMessage = $"Authentication failed: {authResult?.FirstError ?? AuthenticationStatusMessage}";
                return false;
            }
        }

        return true;
    }

    private async Task<OperationResult<HostingUploadResult>> PerformCatalogUploadAsync(IProgress<int> progress, CancellationToken cancellationToken = default)
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
            return await SelectedHostingProvider.UpdateFileAsync(existingCatalogFileId, stream, catalogFileName, progress, cancellationToken);
        }

        return await SelectedHostingProvider.UploadCatalogAsync(CatalogJson, project.Catalog.Publisher.Id, progress, cancellationToken);
    }

    private async Task CompletePublishSuccessAsync(HostingUploadResult data, CancellationToken cancellationToken = default)
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
        logger.LogInformation("Catalog and artifacts uploaded to {Provider}: {Url}", SelectedHostingProvider.ProviderId, CatalogUrl);

        await SaveHostingStateAsync(data.FileId, data.DirectDownloadUrl, data.FileSize, cancellationToken);

        // 4. Generate and upload provider definition
        CurrentPublishStep = 4;
        UploadStatusMessage = "Generating provider definition...";
        await GenerateProviderDefinitionAsync();

        var defResult = await UploadProviderDefinitionIfAvailableAsync(cancellationToken);

        // 5. Generate subscription URL (uses definition URL if available)
        GenerateSubscriptionUrl();

        CurrentPublishStep = 6;
        PublishCompleted = true;
        PublishSummary = BuildPublishSummary(CatalogUrl, ProviderDefinitionUrl, SubscriptionUrl);
        if (defResult != null && !defResult.Success)
        {
            UploadStatusMessage = $"Catalog published, but provider definition upload failed: {defResult.FirstError}";
            notificationService?.ShowWarning(GetLocalizedString("Tools.PublisherStudio.Publish.PublishWarning", "Publish Warning"), UploadStatusMessage);
        }
        else
        {
            UploadStatusMessage = "Published successfully!";
        }
    }

    private async Task<OperationResult<HostingUploadResult>?> UploadProviderDefinitionIfAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ProviderDefinitionJson) || SelectedHostingProvider == null)
        {
            return null;
        }

        CurrentPublishStep = 5;
        UploadStatusMessage = "Uploading provider definition...";
        var defFileName = project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName;
        var existingDefFileId = _currentHostingState?.Definition?.FileId;

        using var defStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(ProviderDefinitionJson));
        var defUploadResult = (!string.IsNullOrEmpty(existingDefFileId) && SelectedHostingProvider.SupportsUpdate)
            ? await SelectedHostingProvider.UpdateFileAsync(existingDefFileId, defStream, defFileName, cancellationToken: cancellationToken)
            : await SelectedHostingProvider.UploadFileAsync(defStream, defFileName, cancellationToken: cancellationToken);

        if (defUploadResult.Success && defUploadResult.Data != null)
        {
            ProviderDefinitionUrl = defUploadResult.Data.DirectDownloadUrl;
            if (_currentHostingState != null && !string.IsNullOrEmpty(project.ProjectPath))
            {
                _currentHostingState.Definition = new HostedFileInfo
                {
                    FileId = defUploadResult.Data.FileId,
                    Url = defUploadResult.Data.DirectDownloadUrl,
                    FileSize = defUploadResult.Data.FileSize,
                    LastUpdated = DateTime.UtcNow,
                };
                if (hostingStateManager != null) await hostingStateManager.SaveStateAsync(project.ProjectPath, _currentHostingState, cancellationToken);
            }

            RefreshHostedAssets();
            return defUploadResult;
        }

        logger.LogWarning("Provider definition upload failed: {Error}", defUploadResult.FirstError);
        return defUploadResult;
    }

    private async Task<bool> UploadPendingArtifactsAsync(IHostingProvider provider, CancellationToken cancellationToken = default)
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
            cancellationToken.ThrowIfCancellationRequested();
            current++;
            if (!await ExecuteSingleArtifactUploadAsync(provider, task, current, total, cancellationToken))
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

    private async Task<(Stream? Stream, string? TempZipPath, bool Success)> PrepareArtifactStreamAsync(ArtifactUploadTask task, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(task.Artifact.LocalFilePath))
        {
            UploadStatusMessage = $"Compressing folder '{Path.GetFileName(task.Artifact.LocalFilePath)}' into archive...";
            var tempDir = Path.Combine(Path.GetTempPath(), "GenHub", "ArtifactCache");
            Directory.CreateDirectory(tempDir);
            var archiveName = string.IsNullOrWhiteSpace(task.Artifact.Filename)
                ? $"{Path.GetFileName(task.Artifact.LocalFilePath)}.zip"
                : task.Artifact.Filename;
            if (!archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                archiveName += ".zip";
            }

            if (string.IsNullOrWhiteSpace(task.Artifact.Filename))
            {
                task.Artifact.Filename = archiveName;
            }

            var tempZipToCleanup = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{archiveName}");
            await Task.Run(() => ZipFile.CreateFromDirectory(task.Artifact.LocalFilePath, tempZipToCleanup), cancellationToken);

            using (var hashStream = File.OpenRead(tempZipToCleanup))
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = await sha256.ComputeHashAsync(hashStream, cancellationToken);
                task.Artifact.Sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            var stream = File.OpenRead(tempZipToCleanup);
            task.Artifact.Size = stream.Length;
            return (stream, tempZipToCleanup, true);
        }

        if (File.Exists(task.Artifact.LocalFilePath))
        {
            if (string.IsNullOrEmpty(task.Artifact.Sha256))
            {
                using var hashStream = File.OpenRead(task.Artifact.LocalFilePath);
                using var sha256 = SHA256.Create();
                var hashBytes = await sha256.ComputeHashAsync(hashStream, cancellationToken);
                task.Artifact.Sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            var stream = File.OpenRead(task.Artifact.LocalFilePath);
            task.Artifact.Size = stream.Length;
            return (stream, null, true);
        }

        task.Status = UploadStatus.Failed;
        task.ErrorMessage = "File or directory not found";
        UploadStatusMessage = $"File or directory not found: {task.Artifact.LocalFilePath}";
        return (null, null, false);
    }

    private async Task RecordUploadedArtifactHostingStateAsync(ArtifactUploadTask task, HostingUploadResult uploadData, CancellationToken cancellationToken = default)
    {
        task.Artifact.DownloadUrl = uploadData.DirectDownloadUrl;
        task.Status = UploadStatus.Uploaded;
        task.Progress = 100;
        logger.LogInformation("Uploaded artifact {File} to {Url}", task.Artifact.Filename, task.Artifact.DownloadUrl);

        if (_currentHostingState == null)
        {
            return;
        }

        var existingArt = _currentHostingState.Artifacts.FirstOrDefault(a => a.FileName == task.Artifact.Filename);
        if (existingArt != null)
        {
            existingArt.FileId = uploadData.FileId;
            existingArt.Url = uploadData.DirectDownloadUrl;
            existingArt.FileSize = uploadData.FileSize;
            existingArt.LastUpdated = DateTime.UtcNow;
        }
        else
        {
            _currentHostingState.Artifacts.Add(new ArtifactHostingInfo
            {
                FileName = task.Artifact.Filename,
                FileId = uploadData.FileId,
                Url = uploadData.DirectDownloadUrl,
                FileSize = uploadData.FileSize,
                ContentId = task.ContentId,
                Version = task.Version,
                Sha256 = task.Artifact.Sha256,
                LastUpdated = DateTime.UtcNow,
            });
        }

        if (!string.IsNullOrEmpty(project.ProjectPath))
        {
            if (hostingStateManager != null) await hostingStateManager.SaveStateAsync(project.ProjectPath, _currentHostingState, cancellationToken);
        }

        RefreshHostedAssets();
    }

    private async Task<bool> ExecuteSingleArtifactUploadAsync(IHostingProvider provider, ArtifactUploadTask task, int current, int total, CancellationToken cancellationToken = default)
    {
        task.Status = UploadStatus.Uploading;
        UploadStatusMessage = $"Uploading artifact {current}/{total}: {task.Artifact.Filename}";
        UploadProgress = (int)((double)(current - 1) / total * 80);

        string? tempZipToCleanup = null;
        try
        {
            var (stream, tempZip, success) = await PrepareArtifactStreamAsync(task, cancellationToken);
            if (!success || stream == null)
            {
                return false;
            }

            tempZipToCleanup = tempZip;
            try
            {
                var progress = new Progress<int>(p =>
                {
                    task.Progress = p;
                    UploadProgress = (int)(((double)(current - 1) / total * 80) + (p / total * 80.0 / 100.0));
                });

                var uploadFileName = task.Artifact.Filename;
                if (string.IsNullOrWhiteSpace(uploadFileName))
                {
                    var localFileName = Path.GetFileName(task.Artifact.LocalFilePath ?? string.Empty);
                    uploadFileName = !string.IsNullOrEmpty(localFileName) ? localFileName : "artifact.bin";
                }

                var result = await provider.UploadFileAsync(stream, uploadFileName, null, progress, cancellationToken);
                if (result.Success && result.Data != null)
                {
                    await RecordUploadedArtifactHostingStateAsync(task, result.Data, cancellationToken);
                    return true;
                }

                task.Status = UploadStatus.Failed;
                task.ErrorMessage = result.FirstError ?? "Upload failed";
                UploadStatusMessage = $"Failed to upload {task.Artifact.Filename}: {result.FirstError}";
                return false;
            }
            finally
            {
                await stream.DisposeAsync();
                CleanupTempZipFile(tempZipToCleanup);
            }
        }
        catch (OperationCanceledException)
        {
            task.Status = UploadStatus.Failed;
            task.ErrorMessage = "Upload canceled";
            throw;
        }
        catch (Exception ex)
        {
            task.Status = UploadStatus.Failed;
            task.ErrorMessage = ex.Message;
            UploadStatusMessage = $"Error uploading {task.Artifact.Filename}: {ex.Message}";
            logger.LogError(ex, "Error uploading artifact {Filename}", task.Artifact.Filename);
            return false;
        }
    }

    private async Task SaveHostingStateAsync(string catalogFileId, string catalogUrl, long catalogFileSize = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(project.ProjectPath))
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
        catalogEntry.FileSize = catalogFileSize;
        catalogEntry.FileName = ActiveCatalog?.FileName ?? $"catalog-{catalogId}.json";
        catalogEntry.CatalogName = ActiveCatalog?.Name ?? catalogId;
        catalogEntry.LastUpdated = DateTime.UtcNow;

        _currentHostingState.LastPublished = DateTime.UtcNow;

        if (hostingStateManager != null)
        {
            var result = await hostingStateManager.SaveStateAsync(project.ProjectPath, _currentHostingState, cancellationToken);
            if (result.Success)
            {
                HasPreviouslyPublished = true;
                logger.LogInformation("Saved hosting state");
            }
        }

        RefreshHostedAssets();
    }

    private async Task SaveAuthTokenAsync()
    {
        if (string.IsNullOrEmpty(project?.ProjectPath) || SelectedHostingProvider == null)
        {
            return;
        }

        _currentHostingState ??= new HostingState { ProviderId = SelectedHostingProvider.ProviderId };
        _currentHostingState.ProviderId = SelectedHostingProvider.ProviderId;
        _currentHostingState.AuthToken = null;

        string? tokenToStore = null;
        if (SelectedHostingProvider.ProviderId == HostingConstants.GitHub)
        {
            tokenToStore = GitHubPersonalAccessToken;
        }
        else if (SelectedHostingProvider.ProviderId == HostingConstants.Dropbox)
        {
            tokenToStore = DropboxAccessToken;
        }

        if (credentialStore != null && !string.IsNullOrEmpty(tokenToStore))
        {
            await credentialStore.SaveCredentialAsync(SelectedHostingProvider.ProviderId, tokenToStore).ConfigureAwait(false);
        }

        if (hostingStateManager != null)
        {
            await hostingStateManager.SaveStateAsync(project.ProjectPath, _currentHostingState, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RestoreAuthenticationAsync()
    {
        if (_currentHostingState == null)
        {
            return;
        }

        // Find the matching provider
        var provider = HostingProviders.FirstOrDefault(p => p.ProviderId == _currentHostingState.ProviderId);
        if (provider == null)
        {
            return;
        }

        SelectedHostingProvider = provider;

        string? token = null;
        if (credentialStore != null)
        {
            token = await credentialStore.GetCredentialAsync(provider.ProviderId).ConfigureAwait(false);
        }

        // Migrate legacy token if found in hosting state
        if (string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(_currentHostingState.AuthToken))
        {
            token = _currentHostingState.AuthToken;
            if (credentialStore != null)
            {
                await credentialStore.SaveCredentialAsync(provider.ProviderId, token).ConfigureAwait(false);
            }

            _currentHostingState.AuthToken = null;
            if (hostingStateManager != null && !string.IsNullOrEmpty(project?.ProjectPath))
            {
                await hostingStateManager.SaveStateAsync(project.ProjectPath, _currentHostingState, CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        try
        {
            if (provider.ProviderId == HostingConstants.GitHub && provider is GitHubHostingProvider githubProvider)
            {
                GitHubPersonalAccessToken = token;
                var result = await githubProvider.AuthenticateWithTokenAsync(token, CancellationToken.None).ConfigureAwait(false);
                if (result.Success)
                {
                    AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.RestoredConnection", "Restored connection");
                    logger.LogInformation("Restored GitHub authentication from secure credential store");
                }
            }
            else if (provider.ProviderId == HostingConstants.Dropbox && provider is DropboxHostingProvider dropboxProvider)
            {
                DropboxAccessToken = token;
                var result = await dropboxProvider.AuthenticateWithTokenAsync(token, CancellationToken.None).ConfigureAwait(false);
                if (result.Success)
                {
                    AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.RestoredConnection", "Restored connection");
                    logger.LogInformation("Restored Dropbox authentication from secure credential store");
                }
            }

            // Notify computed properties
            OnPropertyChanged(nameof(IsProviderAuthenticated));
            OnPropertyChanged(nameof(NeedsAuthentication));
            OnPropertyChanged(nameof(ShowGitHubPatInput));
            OnPropertyChanged(nameof(ShowGoogleOAuthButton));
            OnPropertyChanged(nameof(ShowDropboxTokenInput));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(PublishButtonText));
            OnPropertyChanged(nameof(TargetDestinationDescription));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore authentication");
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

            var result = await publisherStudioService.ExportProviderDefinitionAsync(
                project,
                catalogHostingInfo,
                ProviderDefinitionUrl,
                cancellationToken: CancellationToken.None);

            if (result.Success && result.Data != null)
            {
                ProviderDefinitionJson = result.Data;
                logger.LogInformation("Generated provider definition JSON with {CatalogCount} catalogs", catalogHostingInfo.Count);
            }
            else
            {
                logger.LogError("Failed to generate provider definition: {Error}", result.FirstError);
                UploadStatusMessage = $"Failed to generate definition: {result.FirstError}";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating provider definition");
            UploadStatusMessage = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Uploads the provider definition to the selected hosting provider.
    /// </summary>
    [RelayCommand]
    private async Task<OperationResult<HostingUploadResult>> UploadProviderDefinitionAsync()
    {
        if (SelectedHostingProvider == null)
        {
            UploadStatusMessage = PleaseSelectHostingProviderMessage;
            return OperationResult<HostingUploadResult>.CreateFailure(PleaseSelectHostingProviderMessage);
        }

        // Regenerate to ensure latest values
        await GenerateProviderDefinitionAsync();

        if (string.IsNullOrWhiteSpace(ProviderDefinitionJson))
        {
            return OperationResult<HostingUploadResult>.CreateFailure("Provider definition JSON is empty");
        }

        try
        {
            IsUploading = true;
            UploadStatusMessage = "Uploading provider definition...";

            if (_uploadCts != null)
            {
                await _uploadCts.CancelAsync();
                _uploadCts.Dispose();
            }

            _uploadCts = new CancellationTokenSource();
            var ct = _uploadCts.Token;

            var fileName = project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName;
            var existingDefFileId = _currentHostingState?.Definition?.FileId;

            // Upload or update as a file
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ProviderDefinitionJson));
            var result = (!string.IsNullOrEmpty(existingDefFileId) && SelectedHostingProvider.SupportsUpdate)
                ? await SelectedHostingProvider.UpdateFileAsync(existingDefFileId, stream, fileName, cancellationToken: ct)
                : await SelectedHostingProvider.UploadFileAsync(stream, fileName, cancellationToken: ct);

            if (result.Success && result.Data != null)
            {
                ProviderDefinitionUrl = result.Data.DirectDownloadUrl;
                if (_currentHostingState != null && !string.IsNullOrEmpty(project.ProjectPath))
                {
                    _currentHostingState.Definition = new HostedFileInfo
                    {
                        FileId = result.Data.FileId,
                        Url = result.Data.DirectDownloadUrl,
                        FileSize = result.Data.FileSize,
                        LastUpdated = DateTime.UtcNow,
                    };
                    if (hostingStateManager != null) await hostingStateManager.SaveStateAsync(project.ProjectPath, _currentHostingState, ct);
                }

                GenerateSubscriptionUrl(); // Regenerate based on new definition URL
                RefreshUploadHierarchy();
                RefreshHostedAssets();
                UploadStatusMessage = "Provider definition uploaded successfully.";
                logger.LogInformation("Uploaded provider definition to {Url}", ProviderDefinitionUrl);
                return result;
            }
            else
            {
                UploadStatusMessage = $"Upload failed: {result.FirstError}";
                return result;
            }
        }
        catch (Exception ex)
        {
            UploadStatusMessage = $"Error uploading definition: {ex.Message}";
            logger.LogError(ex, "Error uploading provider definition");
            return OperationResult<HostingUploadResult>.CreateFailure($"Error uploading definition: {ex.Message}");
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
            SubscriptionUrl = publisherStudioService.GenerateSubscriptionUrl(ProviderDefinitionUrl);
            logger.LogInformation("Generated subscription URL using definition URL");
        }
        else
        {
            // No definition URL available - this should not happen in normal flow
            SubscriptionUrl = "Please publish to generate subscription URL";
            logger.LogWarning("Cannot generate subscription URL: definition URL not available");
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
                logger.LogInformation("Copied subscription URL to clipboard");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy to clipboard");
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
                logger.LogInformation("Copied catalog JSON to clipboard");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy catalog to clipboard");
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
                logger.LogInformation("Copied provider definition JSON to clipboard");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy provider definition to clipboard");
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
                logger.LogInformation("Copied provider definition URL to clipboard");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy provider definition URL to clipboard");
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
                logger.LogInformation("Copied catalog URL to clipboard");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy catalog URL to clipboard");
        }
    }

    /// <summary>
    /// Initializes catalog statuses from project and hosting state.
    /// </summary>
    private void InitializeCatalogStatuses()
    {
        var existingStatuses = CatalogStatuses.ToDictionary(s => s.Catalog.Id);

        foreach (var catalog in project.Catalogs)
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

        var projectCatalogIds = new HashSet<string>(project.Catalogs.Select(c => c.Id));
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
        var catalog = project.Catalogs.FirstOrDefault(c => c.Id == catalogId);
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
        if (IsUploading)
        {
            return;
        }

        // Set as active catalog temporarily
        var previousActive = ActiveCatalog;
        ActiveCatalog = catalog;

        try
        {
            var uploadResult = await UploadCatalogAsync();
            if (uploadResult.Success)
            {
                // Update status
                var status = CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == catalog.Id);
                if (status != null)
                {
                    status.IsPublished = true;
                    status.LastPublished = DateTime.UtcNow;
                    status.HasChanges = false;
                }
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
        if (!CanPublishAllCatalogs())
        {
            return;
        }

        _uploadCts?.Dispose();
        _uploadCts = new CancellationTokenSource();
        var cancellationToken = _uploadCts.Token;

        IsUploading = true;
        PublishCompleted = false;
        var publishedAny = false;
        var succeededCount = 0;

        try
        {
            var totalCatalogs = project.Catalogs.Count;
            var currentCatalog = 0;

            foreach (var catalog in project.Catalogs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentCatalog++;
                if (await PublishCatalogItemAsync(catalog, currentCatalog, totalCatalogs, cancellationToken))
                {
                    publishedAny = true;
                    succeededCount++;
                }
            }

            if (publishedAny)
            {
                await FinalizePublishAllSuccessAsync(succeededCount, totalCatalogs);
            }
            else
            {
                UploadStatusMessage = "Publishing all catalogs failed.";
                notificationService?.ShowError(GetLocalizedString("Tools.PublisherStudio.Publish.PublishFailed", "Publish Failed"), UploadStatusMessage);
            }
        }
        catch (OperationCanceledException)
        {
            UploadStatusMessage = "Publishing all catalogs was canceled.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish all catalogs");
            UploadStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
            _uploadCts?.Dispose();
            _uploadCts = null;
        }
    }

    private bool CanPublishAllCatalogs()
    {
        if (IsUploading || SelectedHostingProvider == null)
        {
            return false;
        }

        if (HasIncompatibleArtifactsForActiveCatalog)
        {
            var warningMsg = $"{SelectedHostingProvider?.DisplayName ?? "This provider"} only hosts catalog metadata (JSON). The active catalog '{ActiveCatalog?.Name}' has {ActiveCatalogPendingArtifactsCount} local file(s) pending upload. Either provide direct CDN URLs for those files, or switch to Google Drive or Dropbox to host binary archives.";
            UploadStatusMessage = warningMsg;
            notificationService?.ShowError(GetLocalizedString("Tools.PublisherStudio.Publish.IncompatibleProvider", "Incompatible Provider"), warningMsg);
            return false;
        }

        return IsValid;
    }

    private async Task<bool> PublishCatalogItemAsync(NamedCatalog catalog, int currentCatalog, int totalCatalogs, CancellationToken cancellationToken)
    {
        UploadStatusMessage = $"Publishing catalog {currentCatalog}/{totalCatalogs}: {catalog.Name}";

        var previousActive = ActiveCatalog;
        ActiveCatalog = catalog;
        try
        {
            var res = await UploadCatalogCoreAsync(cancellationToken, manageUploadingState: false);
            if (res.Success)
            {
                var status = CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == catalog.Id);
                if (status != null)
                {
                    status.IsPublished = true;
                    status.LastPublished = DateTime.UtcNow;
                    status.HasChanges = false;
                }

                return true;
            }

            return false;
        }
        finally
        {
            ActiveCatalog = previousActive;
        }
    }

    private async Task FinalizePublishAllSuccessAsync(int succeededCount, int totalCatalogs)
    {
        // Generate provider definition with all catalogs
        await GenerateProviderDefinitionAsync();

        // Upload definition
        if (!string.IsNullOrWhiteSpace(ProviderDefinitionJson))
        {
            var defResult = await UploadProviderDefinitionAsync();
            if (defResult != null && !defResult.Success)
            {
                UploadStatusMessage = succeededCount == totalCatalogs
                    ? $"Successfully published all {totalCatalogs} catalog(s), but provider definition upload failed: {defResult.FirstError}"
                    : $"Published {succeededCount} of {totalCatalogs} catalog(s), but provider definition upload failed: {defResult.FirstError}";
                notificationService?.ShowWarning(GetLocalizedString("Tools.PublisherStudio.Publish.PublishWarning", "Publish Warning"), UploadStatusMessage);
            }
        }

        GenerateSubscriptionUrl();
        RefreshUploadHierarchy();
        RefreshHostedAssets();
        PublishCompleted = true;
        if (!UploadStatusMessage.StartsWith("Successfully", StringComparison.OrdinalIgnoreCase))
        {
            UploadStatusMessage = succeededCount == totalCatalogs
                ? $"Successfully published all {totalCatalogs} catalogs!"
                : $"Published {succeededCount} of {totalCatalogs} catalogs.";
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
                notificationService?.ShowSuccess(GetLocalizedString("Tools.PublisherStudio.Publish.Copied", "Copied"), GetLocalizedString("Tools.PublisherStudio.Publish.LinkCopied", "Link copied to clipboard."));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy URL to clipboard");
        }
    }

    /// <summary>
    /// Opens the Google Auth Platform / Project Configuration page in the default web browser.
    /// </summary>
    [RelayCommand]
    private void OpenGoogleAuthPlatformConsole()
    {
        OpenExternalBrowserUrl(HostingConstants.GoogleAuthPlatformUrl);
    }

    /// <summary>
    /// Opens the Google Auth Platform Audience / Test Users page in the default web browser.
    /// </summary>
    [RelayCommand]
    private void OpenGoogleAudienceConsole()
    {
        OpenExternalBrowserUrl(HostingConstants.GoogleAuthAudienceUrl);
    }

    /// <summary>
    /// Opens the Google Cloud Console credentials page in the default web browser.
    /// </summary>
    [RelayCommand]
    private void OpenGoogleCredentialsConsole()
    {
        OpenExternalBrowserUrl(HostingConstants.GoogleCloudConsoleCredentialsUrl);
    }

    /// <summary>
    /// Opens the GitHub Personal Access Token creation page in the default web browser.
    /// </summary>
    [RelayCommand]
    private void OpenGitHubTokenConsole()
    {
        OpenExternalBrowserUrl(GitHubConstants.PatCreationUrl);
    }

    /// <summary>
    /// Opens the Dropbox Developer App Console in the default web browser.
    /// </summary>
    [RelayCommand]
    private void OpenDropboxAppConsole()
    {
        OpenExternalBrowserUrl(HostingConstants.DropboxAppConsoleUrl);
    }

    private void OpenExternalBrowserUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("Refusing to open non-HTTP/HTTPS URL in browser: {Url}", url);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            logger.LogInformation("Opened URL in browser: {Url}", uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open URL in browser: {Url}", url);
            notificationService?.ShowWarning(GetLocalizedString("Tools.PublisherStudio.Publish.BrowserError", "Browser Error"), FormatLocalizedString("Tools.PublisherStudio.Publish.CouldNotOpenUrl", "Could not open URL: {0}", url));
        }
    }

    private bool IsCloudProviderUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return HostingConstants.IsCloudProviderHost(uri.Host);
    }

    /// <summary>
    /// Opens the Dropbox Developer App Creation page in the default web browser.
    /// </summary>
    [RelayCommand]
    private void OpenDropboxCreateApp()
    {
        OpenExternalBrowserUrl(HostingConstants.DropboxCreateAppUrl);
    }

    /// <summary>
    /// Opens the GitHub Personal Access Token creation page in the default web browser.
    /// </summary>
    [RelayCommand]
    private void OpenGitHubTokensPage()
    {
        OpenExternalBrowserUrl(HostingConstants.GitHubPersonalAccessTokensUrl);
    }

    /// <summary>
    /// Opens the connected provider storage folder in the web browser.
    /// </summary>
    [RelayCommand]
    private void OpenCloudFolder()
    {
        if (SelectedHostingProvider?.ProviderId == HostingConstants.Dropbox)
        {
            OpenExternalBrowserUrl(HostingConstants.DropboxWebFolderUrl);
        }
        else if (SelectedHostingProvider?.ProviderId == HostingConstants.GoogleDrive && !string.IsNullOrEmpty(_currentHostingState?.FolderUrl))
        {
            OpenExternalBrowserUrl(_currentHostingState.FolderUrl);
        }
        else if (SelectedHostingProvider?.ProviderId == HostingConstants.GoogleDrive)
        {
            OpenExternalBrowserUrl(HostingConstants.GoogleDriveWebHomeUrl);
        }
        else if (SelectedHostingProvider?.ProviderId == HostingConstants.GitHub)
        {
            OpenExternalBrowserUrl(HostingConstants.GitHubGistWebHomeUrl);
        }
    }

    /// <summary>
    /// Scans the connected hosting provider for uploaded files and syncs hosting state.
    /// </summary>
    [RelayCommand]
    private async Task ScanCloudStorageAsync()
    {
        if (SelectedHostingProvider == null)
        {
            return;
        }

        if (!IsProviderAuthenticated)
        {
            StorageScanStatusMessage = "Please connect to your hosting provider first.";
            notificationService?.ShowWarning(GetLocalizedString("Tools.PublisherStudio.Publish.ProviderNotConnected", "Provider Not Connected"), GetLocalizedString("Tools.PublisherStudio.Publish.ConnectBeforeScan", "Connect to your hosting provider before scanning storage."));
            return;
        }

        if (_scanCts != null)
        {
            await _scanCts.CancelAsync();
            _scanCts.Dispose();
        }

        _scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = _scanCts.Token;

        IsScanningStorage = true;
        StorageScanStatusMessage = $"Scanning {SelectedHostingProvider.DisplayName} folder for hosted files...";

        try
        {
            var result = await SelectedHostingProvider.RecoverHostingStateAsync(ct);
            if (result.Success && result.Data != null)
            {
                MergeCloudHostingState(result.Data);

                if (!string.IsNullOrEmpty(project.ProjectPath) && _currentHostingState != null)
                {
                    if (hostingStateManager != null) await hostingStateManager.SaveStateAsync(project.ProjectPath, _currentHostingState, ct);
                }

                InitializeCatalogStatuses();
                RefreshUploadHierarchy();
                RefreshHostedAssets();
                GenerateSubscriptionUrl();

                var foundCount = (_currentHostingState?.Catalogs.Count ?? 0) + (_currentHostingState?.Artifacts.Count ?? 0) + (_currentHostingState?.Definition != null ? 1 : 0);
                StorageScanStatusMessage = $"Sync complete! Discovered {foundCount} file(s) in {SelectedHostingProvider.DisplayName}.";
                notificationService?.ShowSuccess(GetLocalizedString("Tools.PublisherStudio.Publish.StorageSynced", "Storage Synced"), StorageScanStatusMessage, autoDismissMs: 4000);
            }
            else
            {
                StorageScanStatusMessage = result.FirstError ?? "No hosted files discovered in cloud storage folder.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scan cloud storage");
            StorageScanStatusMessage = $"Scan error: {ex.Message}";
            notificationService?.ShowError(GetLocalizedString("Tools.PublisherStudio.Publish.ScanError", "Scan Error"), ex.Message);
        }
        finally
        {
            IsScanningStorage = false;
        }
    }

    private async Task ScanCloudStorageSilentlyAsync()
    {
        try
        {
            if (IsScanningStorage || IsUploading || SelectedHostingProvider == null || !IsProviderAuthenticated)
            {
                return;
            }

            var result = await SelectedHostingProvider.RecoverHostingStateAsync(CancellationToken.None);
            if (result.Success && result.Data != null)
            {
                MergeCloudHostingState(result.Data);
                if (!string.IsNullOrEmpty(project.ProjectPath) && _currentHostingState != null)
                {
                    if (hostingStateManager != null) await hostingStateManager.SaveStateAsync(project.ProjectPath, _currentHostingState, CancellationToken.None);
                }

                InitializeCatalogStatuses();
                RefreshUploadHierarchy();
                RefreshHostedAssets();
                GenerateSubscriptionUrl();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Silent cloud state recovery encountered an issue");
        }
    }

    private void MergeCloudHostingState(HostingState cloudState)
    {
        _currentHostingState ??= new HostingState { ProviderId = SelectedHostingProvider?.ProviderId ?? string.Empty };

        if (cloudState.Definition != null)
        {
            _currentHostingState.Definition = cloudState.Definition;
            ProviderDefinitionUrl = cloudState.Definition.Url;
        }

        if (!string.IsNullOrEmpty(cloudState.FolderUrl))
        {
            _currentHostingState.FolderUrl = cloudState.FolderUrl;
        }

        foreach (var cloudCat in cloudState.Catalogs)
        {
            var existing = _currentHostingState.Catalogs.FirstOrDefault(c => c.CatalogId == cloudCat.CatalogId || (!string.IsNullOrEmpty(cloudCat.FileName) && c.FileName == cloudCat.FileName));
            if (existing != null)
            {
                existing.Url = cloudCat.Url;
                existing.FileSize = cloudCat.FileSize;
                existing.LastUpdated = cloudCat.LastUpdated;
                if (!string.IsNullOrEmpty(cloudCat.FileName))
                {
                    existing.FileName = cloudCat.FileName;
                }
            }
            else
            {
                _currentHostingState.Catalogs.Add(cloudCat);
            }
        }

        foreach (var cloudArt in cloudState.Artifacts)
        {
            var existing = _currentHostingState.Artifacts.FirstOrDefault(a => a.FileName == cloudArt.FileName);
            if (existing != null)
            {
                existing.Url = cloudArt.Url;
                existing.FileSize = cloudArt.FileSize;
                existing.LastUpdated = cloudArt.LastUpdated;
            }
            else
            {
                _currentHostingState.Artifacts.Add(cloudArt);
            }
        }
    }

    /// <summary>
    /// Copies an asset download URL to the clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyAssetUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(url);
                notificationService?.ShowSuccess(GetLocalizedString("Tools.PublisherStudio.Publish.CopiedToClipboard", "Copied to Clipboard"), GetLocalizedString("Tools.PublisherStudio.Publish.DirectDownloadUrlCopied", "Direct download URL copied."), autoDismissMs: 2500);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to copy URL to clipboard");
        }
    }

    /// <summary>
    /// Opens an asset URL in the user's default browser.
    /// </summary>
    [RelayCommand]
    private void OpenUrlInBrowser(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            OpenExternalBrowserUrl(url);
        }
    }
}
