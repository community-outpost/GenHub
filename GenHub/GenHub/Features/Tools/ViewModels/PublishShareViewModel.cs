using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Messages;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required dependencies for Publisher Studio operations.")]
[method: System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required dependencies for Publisher Studio operations.")]
public partial class PublishShareViewModel(
    PublisherStudioProject project,
    IPublisherStudioService publisherStudioService,
    ILogger logger,
    IHostingProviderFactory? hostingProviderFactory = null,
    IHostingStateManager? hostingStateManager = null,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null,
    IHostingCredentialStore? credentialStore = null,
    Action<string>? browserLauncher = null,
    IPublisherSubscriptionStore? subscriptionStore = null) : ObservableObject, IDisposable
{
    /// <summary>
    /// Artwork slots that can reference local image files in content metadata.
    /// </summary>
    private enum ArtworkSlot
    {
        Icon,
        Banner,
        Backdrop,
    }

    /// <summary>
    /// Google Drive OAuth client credentials persisted in the encrypted credential store.
    /// </summary>
    /// <param name="ClientId">The Google OAuth client ID.</param>
    /// <param name="ClientSecret">The Google OAuth client secret.</param>
    private sealed record GoogleDriveClientCredentials(string ClientId, string ClientSecret);

    /// <summary>
    /// Pinned notification toast that mirrors upload progress until disposed.
    /// Terminal success or failure toasts are shown separately by the owning operation.
    /// </summary>
    private sealed class UploadProgressToastScope : IDisposable
    {
        private readonly PublishShareViewModel _owner;
        private readonly INotificationService? _notificationService;
        private readonly Guid _notificationId;
        private bool _disposed;

        public UploadProgressToastScope(PublishShareViewModel owner, INotificationService? notificationService, string title, string message)
        {
            _owner = owner;
            _notificationService = notificationService;
            if (notificationService == null)
            {
                return;
            }

            var notification = new NotificationMessage(NotificationType.Info, title, message, autoDismissMilliseconds: null);
            _notificationId = notification.Id;
            notificationService.Show(notification);
            owner.PropertyChanged += OnOwnerPropertyChanged;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.PropertyChanged -= OnOwnerPropertyChanged;
            if (_notificationService != null && _notificationId != Guid.Empty)
            {
                _notificationService.Dismiss(_notificationId);
            }
        }

        private static string BuildProgressMessage(string statusMessage, int progress)
        {
            if (string.IsNullOrWhiteSpace(statusMessage))
            {
                return $"{progress}%";
            }

            return progress > 0 && progress < 100 ? $"{statusMessage} ({progress}%)" : statusMessage;
        }

        private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_disposed || _notificationService == null)
            {
                return;
            }

            if (e.PropertyName is nameof(PublishShareViewModel.UploadProgress) or nameof(PublishShareViewModel.UploadStatusMessage))
            {
                _notificationService.Update(_notificationId, BuildProgressMessage(_owner.UploadStatusMessage, _owner.UploadProgress));
            }
        }
    }

    private const string SuccessLiteral = "Success";
    private const string WarningLiteral = "Warning";
    private const string CommonNotificationSuccessKey = "Common.Notification.Success";
    private const string CommonNotificationWarningKey = "Common.Notification.Warning";
    private const string CopiedToClipboardKey = "Tools.PublisherStudio.Publish.CopiedToClipboard";
    private const string PublishWarningKey = "Tools.PublisherStudio.Publish.PublishWarning";
    private const string PublishWarningDefaultMessage = "Publish Warning";
    private const string PublishSuccessTitleKey = "Tools.PublisherStudio.Publish.SuccessTitle";
    private const string PublishFailedTitleKey = "Tools.PublisherStudio.Publish.FailedTitle";
    private const string UploadFailedDefaultMessage = "Upload Failed";
    private const string IncompatibleProviderTitleKey = "Tools.PublisherStudio.Publish.IncompatibleProvider";
    private const string IncompatibleProviderDefaultMessage = "Incompatible Provider";
    private const string RestoredConnectionTitleKey = "Tools.PublisherStudio.Publish.RestoredConnection";
    private const string RestoredConnectionDefaultMessage = "Restored connection";

    private const string LoadFailedTitleKey = "Tools.PublisherStudio.Hosting.LoadFailedTitle";
    private const string LoadFailedDefaultTitle = "Load Failed";
    private const string PublishErrorFormatKey = "Tools.PublisherStudio.Publish.ErrorFormat";
    private const string PublishErrorFormatDefault = "Error: {0}";

    /// <summary>
    /// Progress band (0-80%) shared by pending artifact and artwork uploads.
    /// The remaining band covers the catalog JSON upload.
    /// </summary>
    private const int PendingUploadProgressBand = 80;

    private static readonly HttpClient SharedHttpClient = new(
        ImageCacheService.CreateSsrfSafeSocketsHttpHandler(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(5)))
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>
    /// Gets or sets an optional HttpClient override for unit testing.
    /// </summary>
    internal static HttpClient? HttpClientOverrideForTesting { get; set; }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Generic.List<HostedAssetItemViewModel>> _probingUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly Dictionary<string, HostingState> _hostingStates = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _probeCts = new();
    private HostingState? _currentHostingState;

    /// <summary>
    /// Gets the current hosting state for testing or inspection.
    /// </summary>
    internal HostingState? CurrentHostingState => _currentHostingState;

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
    private bool _isLoadingHostingState;

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
    private string _dropboxAppKey = string.Empty;

    [ObservableProperty]
    private string _googleClientId = string.Empty;

    [ObservableProperty]
    private string _googleClientSecret = string.Empty;

    [ObservableProperty]
    private bool _hasDefinitionChanges = true;

    private System.Threading.CancellationTokenSource? _authCts;
    private CancellationTokenSource? _uploadCts;
    private CancellationTokenSource? _activeUploadCts;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _silentScanCts;
    private bool _isRestoringAuthentication;

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

    partial void OnIsScanningStorageChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoDefinitionBanner));
    }

    [ObservableProperty]
    private string _storageScanStatusMessage = string.Empty;

    [ObservableProperty]
    private string _hostingFolderPath = HostingConstants.DropboxDefaultPublisherFolder;

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

    [ObservableProperty]
    private NamedCatalog? _activeCatalog = project?.Catalogs.FirstOrDefault();

    /// <summary>
    /// Gets the collection of hosted assets across definition, catalogs, and releases.
    /// </summary>
    public ObservableCollection<HostedAssetItemViewModel> HostedAssets { get; } = new();

    /// <summary>
    /// Gets the filtered collection of hosted assets matching current filter and search criteria.
    /// </summary>
    public ObservableCollection<HostedAssetItemViewModel> FilteredHostedAssets { get; } = new();

    [ObservableProperty]
    private string _inventoryCategoryFilter = "All";

    [ObservableProperty]
    private string _inventorySearchText = string.Empty;

    /// <summary>
    /// Gets the three-tier upload hierarchy (1. Definition / 2. Catalogs / 3. Content items and releases).
    /// </summary>
    public UploadHierarchyItemViewModel UploadHierarchy { get; } = new();

    /// <summary>
    /// Gets the collection of catalog publish statuses.
    /// </summary>
    public ObservableCollection<CatalogPublishStatus> CatalogStatuses { get; } = project?.Catalogs != null ? new ObservableCollection<CatalogPublishStatus>(project.Catalogs.Select(c => new CatalogPublishStatus(c, localizationService))) : [];

    /// <summary>
    /// Gets a value indicating whether any catalog needs publishing.
    /// </summary>
    public bool AnyCatalogNeedsPublish => CatalogStatuses.Any(s => s.NeedsPublish);

    /// <summary>
    /// Gets the available catalogs in the project.
    /// </summary>
    public ObservableCollection<NamedCatalog> AvailableCatalogs { get; } = project?.Catalogs != null ? new ObservableCollection<NamedCatalog>(project.Catalogs) : [];

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
    public ObservableCollection<IHostingProvider> HostingProviders { get; } = hostingProviderFactory != null ? new ObservableCollection<IHostingProvider>(hostingProviderFactory.GetCatalogHostingProviders()) : [];

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
    /// Gets the exact redirect URI users must register in their Dropbox application console.
    /// Dropbox rejects sign-in attempts whose redirect URI does not match character for character.
    /// </summary>
    public string DropboxRedirectUri => HostingConstants.DropboxOAuthRedirectUri;

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
    /// Gets an explanatory warning message when the provider cannot host the pending local files.
    /// </summary>
    public string IncompatibleArtifactsWarningMessage =>
        FormatLocalizedString("Tools.PublisherStudio.Publish.IncompatibleArtifactsWarningFormat", "{0} only hosts catalog metadata (JSON). Your project has {1} local file(s) pending upload. Either provide direct CDN URLs for those files, or switch to Google Drive or Dropbox to host binary archives.", SelectedHostingProvider?.DisplayName ?? "This provider", PendingArtifactsCount);

    /// <summary>
    /// Gets the content item count in the active catalog.
    /// </summary>
    public int ContentItemCount => ActiveCatalog?.Catalog.Content.Count ?? 0;

    /// <summary>
    /// Gets the localized hosted-assets count display text.
    /// </summary>
    public string HostedAssetsCountText => FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetsTrackedFormat", "{0} Assets Tracked", HostedAssets.Count);

    /// <summary>
    /// Gets the localized catalogs count display text.
    /// </summary>
    public string CatalogStatusesCountText => FormatLocalizedString("Tools.PublisherStudio.Publish.CatalogsCountFormat", "{0} Catalogs", CatalogStatuses.Count);

    /// <summary>
    /// Gets the total release count across all content items in the active catalog.
    /// </summary>
    public int TotalReleaseCount => ActiveCatalog?.Catalog.Content.Sum(c => c.Releases.Count) ?? 0;

    /// <summary>
    /// Gets the localized display name of the selected hosting provider.
    /// </summary>
    public string ProviderDisplayName => SelectedHostingProvider?.DisplayName
        ?? GetLocalizedString("Tools.PublisherStudio.Hosting.NotConnected", "Not Connected");

    /// <summary>
    /// Gets or sets the callback used to switch tabs in the parent studio view model.
    /// </summary>
    public Action<int>? NavigateToTabCallback { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when hosting provider authentication state changes.
    /// Wired by the parent studio view model to update tab locks and setup state.
    /// </summary>
    public Action? AuthenticationChangedCallback { get; set; }

    /// <summary>
    /// Gets the primary discovered publisher definition from cloud storage, if any.
    /// </summary>
    public HostedAssetItemViewModel? DiscoveredCloudDefinition =>
        HostedAssets.FirstOrDefault(a => a.IsDefinition && a.CanLoadToProject);

    /// <summary>
    /// Gets a value indicating whether an existing publisher definition was discovered in cloud storage.
    /// </summary>
    public bool HasDiscoveredCloudDefinition => DiscoveredCloudDefinition != null;

    /// <summary>
    /// Gets the file name of the discovered cloud definition.
    /// </summary>
    public string DiscoveredDefinitionFileName =>
        DiscoveredCloudDefinition?.Name ?? HostingConstants.DefaultDefinitionFileName;

    /// <summary>
    /// Gets a value indicating whether the discovered cloud definition banner should be displayed.
    /// Shown when authenticated and a definition was discovered in the cloud.
    /// </summary>
    public bool ShowDiscoveredDefinitionBanner =>
        IsProviderAuthenticated &&
        HasDiscoveredCloudDefinition &&
        !IsCurrentProfileMatchingDiscoveredDefinition();

    private bool IsCurrentProfileMatchingDiscoveredDefinition()
    {
        if (DiscoveredCloudDefinition == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(project.Catalog?.Publisher?.Id))
        {
            if (!string.IsNullOrEmpty(_currentHostingState?.Definition?.Url) &&
                string.Equals(DiscoveredCloudDefinition.Url, _currentHostingState.Definition.Url, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(DiscoveredCloudDefinition.Name, project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a value indicating whether the "no cloud definition found" banner should be displayed.
    /// Shown when authenticated, not currently scanning, and no definition was discovered.
    /// </summary>
    public bool ShowNoDefinitionBanner =>
        IsProviderAuthenticated &&
        !HasDiscoveredCloudDefinition &&
        !IsScanningStorage &&
        string.IsNullOrWhiteSpace(_currentHostingState?.Definition?.Url);

    /// <summary>
    /// Gets or sets the callback used to persist the project after uploads mutate it.
    /// Wired by the parent studio view model to a silent project save.
    /// </summary>
    public Func<Task>? SaveProjectCallback { get; set; }

    /// <summary>
    /// Gets or sets the callback used to reload parent studio view models after definitions or catalogs are loaded.
    /// </summary>
    public Func<Task>? ProjectReloadCallback { get; set; }

    /// <summary>
    /// Gets or sets the callback used to refresh the Content Library display after uploads mutate artifacts.
    /// Wired by the parent studio view model so pending badges and hints update without switching tabs.
    /// </summary>
    public Action? LibraryRefreshCallback { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked after the provider definition uploads successfully.
    /// Wired by the parent studio view model to clear definition change tracking.
    /// </summary>
    public Action? DefinitionUploadedCallback { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the remote provider definition becomes stale.
    /// Wired by the parent studio view model to re-enable definition uploads.
    /// </summary>
    public Action? DefinitionStaleCallback { get; set; }

    /// <summary>
    /// Gets a value indicating whether the provider definition has been published before.
    /// </summary>
    public bool IsDefinitionPublished => !string.IsNullOrEmpty(_currentHostingState?.Definition?.Url);

    private string PleaseSelectHostingProviderMessage => GetLocalizedString("Tools.PublisherStudio.Publish.SelectHostingProvider", "Please select a hosting provider");

    private string UploadAlreadyInProgressMessage => GetLocalizedString("Tools.PublisherStudio.Publish.UploadAlreadyInProgress", "An upload is already in progress.");

    /// <summary>
    /// Uploads a single artifact chosen from the Content Library tab.
    /// Applies the same guards and notifications as hosted-asset uploads.
    /// </summary>
    /// <param name="artifact">The artifact to upload.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UploadArtifactFromLibraryAsync(ReleaseArtifact? artifact)
    {
        if (artifact == null)
        {
            return;
        }

        if (IsUploading)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.UploadInProgressTitle", "Upload In Progress"),
                GetLocalizedString("Tools.PublisherStudio.Hosting.UploadInProgress", "Another upload is already in progress."));
            return;
        }

        var provider = SelectedHostingProvider;
        if (provider == null || NeedsAuthentication)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.ProviderNotConnected", "Provider Not Connected"),
                GetLocalizedString("Tools.PublisherStudio.Hosting.ConnectBeforeUpload", "Connect to your hosting provider before uploading files."));
            return;
        }

        var located = FindArtifactOwner(artifact);
        if (located.ContentId == null || located.Version == null)
        {
            return;
        }

        await ExecuteLocatedArtifactUploadAsync(provider, artifact, located.ContentId, located.Version);
    }

    /// <summary>
    /// Uploads the provider definition to the selected hosting provider.
    /// Public so the studio shell can trigger definition uploads from its header button.
    /// </summary>
    /// <returns>The upload result.</returns>
    [RelayCommand]
    public async Task<OperationResult<HostingUploadResult>> UploadProviderDefinitionAsync()
    {
        var (acquired, cts) = await TryBeginPublishAsync().ConfigureAwait(false);
        if (!acquired || cts == null)
        {
            return OperationResult<HostingUploadResult>.CreateFailure(UploadAlreadyInProgressMessage);
        }

        if (SelectedHostingProvider == null)
        {
            EndPublish(cts);
            return OperationResult<HostingUploadResult>.CreateFailure(PleaseSelectHostingProviderMessage);
        }

        var cancellationToken = cts.Token;
        using var progressToast = BeginUploadProgressToast(
            GetLocalizedString("Tools.PublisherStudio.Publish.UploadStartedTitle", "Uploading"),
            GetLocalizedString("Tools.PublisherStudio.Publish.DefinitionUploadStarted", "Uploading provider definition..."));

        try
        {
            // Cascade down: Publish only catalogs that have pending changes (or are not published yet)
            var catalogsToPublish = project.Catalogs.Where(c =>
                CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == c.Id)?.NeedsPublish ?? true).ToList();

            var totalCatalogs = catalogsToPublish.Count;
            var (succeededCount, failedCatalogs) = await PublishCatalogsAsync(catalogsToPublish, cancellationToken);

            if (totalCatalogs > 0 && succeededCount == 0)
            {
                UploadStatusMessage = BuildAllCatalogsFailedMessage(failedCatalogs);
                NotifyDefinitionStale();
                notificationService?.ShowError(
                    GetLocalizedString(PublishFailedTitleKey, UploadFailedDefaultMessage),
                    UploadStatusMessage);
                return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
            }

            if (failedCatalogs.Count > 0)
            {
                var partialDefinitionResult = await UploadProviderDefinitionCoreAsync(cancellationToken, manageUploadingState: false, suppressNotifications: true);
                if (partialDefinitionResult.Success)
                {
                    await SyncLocalSubscriptionMetadataAsync();
                }

                UploadStatusMessage = BuildPartialPublishMessage(
                    succeededCount,
                    totalCatalogs,
                    failedCatalogs,
                    partialDefinitionResult.Success ? null : GetDefinitionError(partialDefinitionResult));
                NotifyDefinitionStale();
                notificationService?.ShowWarning(
                    GetLocalizedString(PublishWarningKey, PublishWarningDefaultMessage),
                    UploadStatusMessage,
                    NotificationDurations.Long);
                return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
            }

            if (totalCatalogs > 0)
            {
                notificationService?.ShowSuccess(
                    GetLocalizedString("Tools.PublisherStudio.Publish.CatalogsPublishedTitle", "Catalogs Published"),
                    FormatLocalizedString("Tools.PublisherStudio.Publish.CatalogsPublishedMessageFormat", "Successfully published {0} catalog(s).", succeededCount),
                    NotificationDurations.Short);
            }

            var result = await UploadProviderDefinitionCoreAsync(cancellationToken, manageUploadingState: false);
            if (result.Success)
            {
                HasDefinitionChanges = false;
                await SyncLocalSubscriptionMetadataAsync();
                notificationService?.ShowSuccess(
                    GetLocalizedString("Tools.PublisherStudio.Publish.DefinitionPublishedTitle", "Definition Published"),
                    GetLocalizedString("Tools.PublisherStudio.Publish.DefinitionPublishedMessage", "Provider definition uploaded successfully."),
                    NotificationDurations.Short);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.PublishAllCanceled", "Publishing was canceled.");
            return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload provider definition with cascade");
            UploadStatusMessage = FormatLocalizedString(PublishErrorFormatKey, PublishErrorFormatDefault, ex.Message);
            return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
        }
        finally
        {
            EndPublish(cts);
        }
    }

    /// <summary>
    /// Refreshes localized display text after a culture change.
    /// </summary>
    public void RefreshLocalizedText()
    {
        UpdateHostingFolderPath();
        RefreshUploadHierarchy();
        RefreshHostedAssets();
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(TargetDestinationDescription));
        OnPropertyChanged(nameof(IncompatibleArtifactsWarningMessage));
        OnPropertyChanged(nameof(HostedAssetsCountText));
        OnPropertyChanged(nameof(CatalogStatusesCountText));
    }

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
        await EnsureHostingStatesLoadedAsync(cancellationToken);

        var renamedAny = false;
        var staleRemotes = new List<(string ProviderId, CatalogHostingInfo Entry, string OldFileId)>();
        foreach (var (providerId, state) in _hostingStates)
        {
            var entry = state.Catalogs.FirstOrDefault(c => c.CatalogId == oldCatalogId);
            if (entry != null)
            {
                if (!string.IsNullOrWhiteSpace(entry.FileId))
                {
                    staleRemotes.Add((providerId, entry, entry.FileId));
                }

                entry.CatalogId = newCatalogId;
                entry.CatalogName = newCatalogName;
                entry.FileName = newFileName;
                entry.LastUpdated = DateTime.UtcNow;
                renamedAny = true;
            }
        }

        if (renamedAny)
        {
            await SaveAllHostingStatesAsync(cancellationToken);
            await DeleteRenamedCatalogRemotesAsync(staleRemotes, cancellationToken);
        }
    }

    /// <summary>
    /// Deletes the remotely hosted catalog files for a removed catalog across all
    /// providers and prunes their hosting-state entries. Artifact files are left
    /// untouched because content IDs can be shared by other catalogs.
    /// </summary>
    /// <param name="catalogId">The removed catalog ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when every remote catalog file was deleted or none existed; otherwise false.</returns>
    public async Task<bool> DeleteCatalogRemotesAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        await EnsureHostingStatesLoadedAsync(cancellationToken);

        var allCleaned = true;
        var stateChanged = false;
        foreach (var (providerId, state) in _hostingStates)
        {
            var entry = state.Catalogs.FirstOrDefault(c => c.CatalogId == catalogId);
            if (entry == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.FileId))
            {
                state.Catalogs.Remove(entry);
                stateChanged = true;
                continue;
            }

            var provider = HostingProviders.FirstOrDefault(p =>
                string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            if (provider == null || !provider.IsAuthenticated)
            {
                allCleaned = false;
                continue;
            }

            try
            {
                var result = await provider.DeleteFileAsync(entry.FileId, cancellationToken);
                if (result.Success)
                {
                    logger.LogInformation("Deleted remote catalog {FileId} from {Provider}", entry.FileId, providerId);
                    state.Catalogs.Remove(entry);
                    stateChanged = true;
                }
                else
                {
                    logger.LogWarning("Failed to delete remote catalog {FileId} from {Provider}: {Error}", entry.FileId, providerId, result.FirstError);
                    allCleaned = false;
                }
            }
            catch (OperationCanceledException ex)
            {
                logger.LogInformation(ex, "Remote catalog cleanup was canceled for {FileId}", entry.FileId);
                allCleaned = false;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete remote catalog {FileId} from {Provider}", entry.FileId, providerId);
                allCleaned = false;
            }
        }

        if (stateChanged)
        {
            await SaveAllHostingStatesAsync(cancellationToken);
            RefreshHostedAssets();
        }

        return allCleaned;
    }

    /// <summary>
    /// Marks a catalog as having unpublished changes.
    /// </summary>
    /// <param name="catalogId">The catalog ID.</param>
    public void MarkCatalogChanged(string catalogId)
    {
        var status = CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == catalogId);
        if (status == null)
        {
            return;
        }

        status.HasChanges = true;
        RefreshUploadHierarchy();
    }

    /// <summary>
    /// Marks every catalog as having unpublished changes.
    /// Used when a project-wide edit (such as the publisher profile) affects all exports.
    /// </summary>
    public void MarkAllCatalogsChanged()
    {
        foreach (var status in CatalogStatuses)
        {
            status.HasChanges = true;
        }

        RefreshUploadHierarchy();
    }

    /// <summary>
    /// Gets a value indicating whether a catalog needs publishing.
    /// Unknown catalogs report true so their publish actions stay enabled.
    /// </summary>
    /// <param name="catalogId">The catalog ID.</param>
    /// <returns>True when the catalog was never published or has pending changes.</returns>
    public bool CatalogNeedsPublish(string catalogId)
    {
        return CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == catalogId)?.NeedsPublish ?? true;
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
    /// Asynchronously initializes hosting state, upload hierarchy, and validates catalogs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        InitializeCatalogStatuses();
        RefreshUploadHierarchy();
        RefreshHostedAssets();
        RefreshArtifactStatuses();
        await LoadHostingStateAsync();
        await ValidateCatalogAsync();
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
        UpdateHostingFolderPath();
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
        TotalHostedFilesCount = HostedAssets.Count;
        HostedDefinitionCount = defCount;
        HostedCatalogsCount = catCount;
        HostedArtifactsCount = artCount;
        ExternalCdnCount = cdnCount;
        ApplyHostedAssetFilter();
        OnPropertyChanged(nameof(HostedAssetsCountText));
        OnPropertyChanged(nameof(DiscoveredCloudDefinition));
        OnPropertyChanged(nameof(HasDiscoveredCloudDefinition));
        OnPropertyChanged(nameof(DiscoveredDefinitionFileName));
        OnPropertyChanged(nameof(ShowDiscoveredDefinitionBanner));
        OnPropertyChanged(nameof(ShowNoDefinitionBanner));
        AuthenticationChangedCallback?.Invoke();
    }

    /// <summary>
    /// Rebuilds the artifact URL validation statuses from the active catalog.
    /// </summary>
    public void RefreshArtifactStatuses()
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
                    ArtifactStatuses.Add(new ArtifactUrlStatus(artifact, content.Name, release.Version, localizationService));
                }
            }
        }

        OnPropertyChanged(nameof(PendingArtifactsCount));
        OnPropertyChanged(nameof(ExternalCdnArtifactsCount));
        OnPropertyChanged(nameof(HasIncompatibleArtifactsForProvider));
        OnPropertyChanged(nameof(IncompatibleArtifactsWarningMessage));
        OnPropertyChanged(nameof(ShowDiscoveredDefinitionBanner));
        OnPropertyChanged(nameof(ShowNoDefinitionBanner));
    }

    /// <summary>
    /// Filters the hosted assets collection according to the active category filter and search text.
    /// </summary>
    public void ApplyHostedAssetFilter()
    {
        FilteredHostedAssets.Clear();

        var filter = InventoryCategoryFilter ?? "All";
        var search = InventorySearchText?.Trim() ?? string.Empty;

        var items = HostedAssets.AsEnumerable();

        if (string.Equals(filter, "Definition", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(a => a.IsDefinition);
        }
        else if (string.Equals(filter, "Catalog", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(a => a.IsCatalog);
        }
        else if (string.Equals(filter, "Artifact", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(a => a.IsArtifact && !a.IsExternalCdn);
        }
        else if (string.Equals(filter, "ExternalCdn", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(a => a.IsExternalCdn);
        }

        if (!string.IsNullOrEmpty(search))
        {
            items = items.Where(a =>
                (!string.IsNullOrEmpty(a.Name) && a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(a.Category) && a.Category.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(a.Location) && a.Location.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var item in items)
        {
            FilteredHostedAssets.Add(item);
        }
    }

    /// <summary>
    /// Searches known hosted assets across provider storage, hosting states, and project catalogs for a matching SHA-256 hash.
    /// </summary>
    /// <param name="sha256">The SHA-256 hash to find.</param>
    /// <returns>The file name, download URL, and file size if found; otherwise, null.</returns>
    public (string Name, string Url, long Size)? FindHostedAssetBySha256(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
        {
            return null;
        }

        return FindInHostedAssets(sha256) ??
               FindInHostingStates(sha256) ??
               FindInProjectCatalogs(sha256);
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
            _activeUploadCts?.Cancel();
            _activeUploadCts?.Dispose();
            _activeUploadCts = null;
            _probeCts?.Cancel();
            _probeCts?.Dispose();
            _probeCts = null;
            _publishGate.Dispose();
            _uploadCts?.Cancel();
            _uploadCts?.Dispose();
            _uploadCts = null;
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;
            _silentScanCts?.Cancel();
            _silentScanCts?.Dispose();
            _silentScanCts = null;
            foreach (var status in CatalogStatuses)
            {
                status.Dispose();
            }
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

    private static bool IsSameArtifact(ArtifactHostingInfo entry, string fileName, string contentId, string version)
    {
        if (entry.FileName != fileName)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(entry.ContentId) && !string.IsNullOrEmpty(contentId) && entry.ContentId != contentId)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(entry.Version) && !string.IsNullOrEmpty(version) && entry.Version != version)
        {
            return false;
        }

        return true;
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

    private static (ReleaseArtifact? Artifact, string? ContentId, string? Version) FindArtifactInCatalog(NamedCatalog catalog, HostedAssetItemViewModel asset)
    {
        foreach (var content in catalog.Catalog.Content)
        {
            if (!ContentMatchesFilter(content, asset.ContentId))
            {
                continue;
            }

            var found = FindArtifactInContent(content, asset);
            if (found.Artifact != null)
            {
                return found;
            }
        }

        return (null, null, null);
    }

    private static (ReleaseArtifact? Artifact, string? ContentId, string? Version) FindArtifactInContent(CatalogContentItem content, HostedAssetItemViewModel asset)
    {
        foreach (var release in content.Releases)
        {
            if (!ReleaseMatchesFilter(release, asset.ReleaseVersion))
            {
                continue;
            }

            var match = FindArtifactByFileName(release, asset.Name);
            if (match != null)
            {
                return (match, content.Id, release.Version);
            }
        }

        return (null, null, null);
    }

    private static bool ContentMatchesFilter(CatalogContentItem content, string? contentId)
    {
        return contentId == null || string.Equals(content.Id, contentId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReleaseMatchesFilter(ContentRelease release, string? version)
    {
        return version == null || string.Equals(release.Version, version, StringComparison.OrdinalIgnoreCase);
    }

    private static ReleaseArtifact? FindArtifactByFileName(ContentRelease release, string? fileName)
    {
        return release.Artifacts.FirstOrDefault(a =>
            string.Equals(a.Filename, fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string EnsureDirectDownloadUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (url.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase) && url.Contains("dl=0", StringComparison.OrdinalIgnoreCase))
        {
            return url.Replace("dl=0", "dl=1");
        }

        return url;
    }

    private static bool IsValidHttpUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static (string Name, string Url, long Size)? FindInArtifacts(IEnumerable<ReleaseArtifact>? artifacts, string sha256)
    {
        if (artifacts == null)
        {
            return null;
        }

        var match = artifacts.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.DownloadUrl) &&
            string.Equals(a.Sha256, sha256, StringComparison.OrdinalIgnoreCase));

        return match?.DownloadUrl != null ? (match.Filename, match.DownloadUrl, match.Size) : null;
    }

    private static (string Name, string Url, long Size)? FindInContentItem(CatalogContentItem content, string sha256)
    {
        if (content.Releases != null)
        {
            foreach (var release in content.Releases)
            {
                var match = FindInArtifacts(release.Artifacts, sha256);
                if (match != null)
                {
                    return match;
                }
            }
        }

        if (content.AddonReleases != null)
        {
            foreach (var addon in content.AddonReleases)
            {
                var match = FindInArtifacts(addon.Artifacts, sha256);
                if (match != null)
                {
                    return match;
                }
            }
        }

        return null;
    }

    private (string Name, string Url, long Size)? FindInHostedAssets(string sha256)
    {
        var hosted = HostedAssets.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Url) &&
            string.Equals(a.Sha256, sha256, StringComparison.OrdinalIgnoreCase));
        if (hosted != null)
        {
            return (hosted.Name, hosted.Url, hosted.FileSize);
        }

        return null;
    }

    private (string Name, string Url, long Size)? FindInHostingStates(string sha256)
    {
        foreach (var state in _hostingStates.Values)
        {
            var art = state.Artifacts.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.Url) &&
                string.Equals(a.Sha256, sha256, StringComparison.OrdinalIgnoreCase));
            if (art != null)
            {
                return (art.FileName, art.Url, art.FileSize);
            }
        }

        return null;
    }

    private (string Name, string Url, long Size)? FindInProjectCatalogs(string sha256)
    {
        if (project?.Catalogs == null)
        {
            return null;
        }

        foreach (var catalog in project.Catalogs.Select(namedCat => namedCat.Catalog))
        {
            if (catalog?.Content == null)
            {
                continue;
            }

            foreach (var content in catalog.Content)
            {
                var match = FindInContentItem(content, sha256);
                if (match != null)
                {
                    return match;
                }
            }
        }

        return null;
    }

    private string GetLocalizedString(string key, string defaultValue) =>
        localizationService?.GetString(key) ?? defaultValue;

    private string FormatLocalizedString(string key, string defaultValueFormat, params object?[] args)
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

    private void UpdateHostingFolderPath()
    {
        HostingFolderPath = FolderLabelForProvider(SelectedHostingProvider?.ProviderId);
    }

    private string FolderLabelForProvider(string? providerId)
    {
        return providerId switch
        {
            HostingConstants.Dropbox => HostingConstants.DropboxDefaultPublisherFolder,
            HostingConstants.GoogleDrive => HostingConstants.GoogleDriveDefaultPublisherFolder,
            HostingConstants.GitHub => GetLocalizedString("Tools.PublisherStudio.Hosting.DestinationGitHubGists", HostingConstants.GitHubGistsDestinationLabel),
            _ => GetLocalizedString("Tools.PublisherStudio.Hosting.DestinationRemoteCloud", HostingConstants.RemoteCloudDestinationLabel),
        };
    }

    private string LocationLabelForAssetUrl(string? downloadUrl, string fallbackProviderName, string fallbackFolder)
    {
        if (!string.IsNullOrWhiteSpace(downloadUrl) &&
            Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) &&
            HostingConstants.GetProviderIdForHost(uri.Host) is { } ownerId)
        {
            var ownerName = HostingProviders.FirstOrDefault(p =>
                string.Equals(p.ProviderId, ownerId, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? fallbackProviderName;
            return $"{ownerName} ({FolderLabelForProvider(ownerId)})";
        }

        return $"{fallbackProviderName} ({fallbackFolder})";
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
            AssetKind = HostedAssetKind.Definition,
            CanUpload = !isDefHosted && SelectedHostingProvider != null && SelectedHostingProvider.SupportsCatalogHosting,
            CanLoadToProject = false,
            LoadButtonTooltip = GetLocalizedString("Tools.PublisherStudio.Hosting.LoadToProjectTip", "Load this definition and its catalogs into current project"),
            Name = project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName,
            Category = GetLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryDefinition", "Publisher Definition"),
            Location = isDefHosted ? $"{providerName} ({HostingFolderPath})" : GetLocalizedString("Tools.PublisherStudio.Hosting.AssetLocationLocalOnly", "Local only"),
            FileSize = defSize,
            Url = defUrl ?? string.Empty,
            Status = isDefHosted
                ? GetLocalizedString("Tools.PublisherStudio.Hosting.StatusLiveOnline", HostingConstants.StatusLiveOnline)
                : GetLocalizedString("Tools.PublisherStudio.Hosting.StatusPendingUpload", HostingConstants.StatusPendingUpload),
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
                AssetKind = HostedAssetKind.Catalog,
                CatalogId = catalog.Id,
                CanUpload = !isCatHosted && SelectedHostingProvider != null && SelectedHostingProvider.SupportsCatalogHosting,
                Name = catalog.FileName,
                Category = FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCatalogFormat", "Catalog Manifest ({0})", catalog.Name),
                Location = isCatHosted ? $"{providerName} ({HostingFolderPath})" : GetLocalizedString("Tools.PublisherStudio.Hosting.AssetLocationLocalOnly", "Local only"),
                FileSize = catSize,
                Url = catUrl,
                Status = isCatHosted
                    ? GetLocalizedString("Tools.PublisherStudio.Hosting.StatusLiveOnline", HostingConstants.StatusLiveOnline)
                    : GetLocalizedString("Tools.PublisherStudio.Hosting.StatusPendingUpload", HostingConstants.StatusPendingUpload),
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
            .SelectMany(content => content.Releases.SelectMany(release => release.Artifacts.Select(artifact => (content.Id, content.Name, release.Version, artifact))));

        foreach (var (contentId, contentName, version, artifact) in allArtifacts)
        {
            ProcessArtifactAsset(artifact, contentId, contentName, version, providerName, ref totalBytes, ref artCount, ref cdnCount);
        }
    }

    private void ProcessArtifactAsset(
        ReleaseArtifact artifact,
        string contentId,
        string contentName,
        string releaseVersion,
        string providerName,
        ref long totalBytes,
        ref int artCount,
        ref int cdnCount)
    {
        var isExternal = !string.IsNullOrEmpty(artifact.DownloadUrl) && !IsCloudProviderUrl(artifact.DownloadUrl);
        var isCloud = !string.IsNullOrEmpty(artifact.DownloadUrl) && IsCloudProviderUrl(artifact.DownloadUrl);
        var artHosting = _currentHostingState?.Artifacts.FirstOrDefault(a => a.FileName == artifact.Filename || a.Url == artifact.DownloadUrl)
            ?? _hostingStates.Values.SelectMany(s => s.Artifacts).FirstOrDefault(a => a.FileName == artifact.Filename || a.Url == artifact.DownloadUrl);
        var artSize = artifact.Size > 0 ? artifact.Size : (artHosting?.FileSize ?? 0);
        var artUpdated = artHosting?.LastUpdated ?? DateTime.MinValue;

        string location = string.Empty;
        string status = string.Empty;
        if (isCloud)
        {
            artCount++;
            totalBytes += artSize;
            location = LocationLabelForAssetUrl(artifact.DownloadUrl, providerName, HostingFolderPath);
            status = GetLocalizedString("Tools.PublisherStudio.Hosting.StatusLiveOnline", HostingConstants.StatusLiveOnline);
        }
        else if (isExternal)
        {
            cdnCount++;
            location = GetLocalizedString("Tools.PublisherStudio.Hosting.StatusExternalCdn", HostingConstants.StatusExternalCdn);
            status = GetLocalizedString("Tools.PublisherStudio.Hosting.StatusExternalCdn", HostingConstants.StatusExternalCdn);
        }
        else
        {
            location = GetLocalizedString("Tools.PublisherStudio.Hosting.AssetLocationLocalFile", "Local file");
            status = GetLocalizedString("Tools.PublisherStudio.Hosting.StatusPendingUpload", HostingConstants.StatusPendingUpload);
        }

        var hasLocalSource = !string.IsNullOrEmpty(artifact.LocalFilePath);
        var canUploadArtifact = hasLocalSource && !isCloud && SelectedHostingProvider != null && SelectedHostingProvider.SupportsArtifactHosting;

        var item = new HostedAssetItemViewModel
        {
            AssetKind = HostedAssetKind.Artifact,
            ContentId = contentId,
            ContentName = contentName,
            ReleaseVersion = releaseVersion,
            LocalFilePath = artifact.LocalFilePath,
            CanUpload = canUploadArtifact,
            Name = artifact.Filename,
            Category = FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryReleaseFormat", "{0} v{1}", contentName, releaseVersion),
            Location = location,
            FileSize = artSize,
            Url = artifact.DownloadUrl ?? string.Empty,
            Status = status,
            IsOnline = isCloud,
            IsExternalCdn = isExternal,
            LastUpdated = artUpdated,
            Sha256 = artifact.Sha256,
        };

        HostedAssets.Add(item);

        if (artSize <= 0 && isExternal && IsValidHttpUrl(artifact.DownloadUrl))
        {
            var url = artifact.DownloadUrl!;
            var isNewProbe = false;
            var list = _probingUrls.GetOrAdd(url, _ =>
            {
                isNewProbe = true;
                return [];
            });

            lock (list)
            {
                list.Add(item);
            }

            if (isNewProbe)
            {
                var ct = _probeCts?.Token ?? CancellationToken.None;
                _ = ProbeExternalAssetSizeAsync(url, artifact, ct);
            }
        }
    }

    private async Task<long?> ProbeRangedGetSizeAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
            getRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (getResponse.IsSuccessStatusCode || getResponse.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                return getResponse.Content.Headers.ContentRange?.Length
                    ?? getResponse.Content.Headers.ContentLength;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ranged GET probe failed for {Url}", url);
        }

        return null;
    }

    private async Task<long?> DetectExternalAssetSizeAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        var detectedSize = await RemoteFileSizeProbe.TryProbeSizeAsync(client, url, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);

        if ((detectedSize is null or <= 0) && !cancellationToken.IsCancellationRequested)
        {
            detectedSize = await ProbeRangedGetSizeAsync(client, url, cancellationToken).ConfigureAwait(false);
        }

        return detectedSize;
    }

    private void DispatchArtifactSizeUpdate(string url, long size)
    {
        if (!_probingUrls.TryGetValue(url, out var list))
        {
            return;
        }

        HostedAssetItemViewModel[] targets;
        lock (list)
        {
            targets = [.. list];
        }

        void UpdateItems()
        {
            foreach (var target in targets)
            {
                target.FileSize = size;
            }
        }

        if (Avalonia.Application.Current == null || Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            UpdateItems();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateItems);
        }
    }

    private async Task ProbeExternalAssetSizeAsync(string url, ReleaseArtifact artifact, CancellationToken cancellationToken)
    {
        try
        {
            var client = HttpClientOverrideForTesting ?? SharedHttpClient;
            var detectedSize = await DetectExternalAssetSizeAsync(client, url, cancellationToken).ConfigureAwait(false);

            if (detectedSize is > 0 && !cancellationToken.IsCancellationRequested)
            {
                var size = detectedSize.Value;
                artifact.Size = size;
                DispatchArtifactSizeUpdate(url, size);
                logger.LogInformation("Probed external artifact size for {FileName}: {Size} bytes", artifact.Filename, size);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not probe size for external artifact {Url}", url);
        }
        finally
        {
            _probingUrls.TryRemove(url, out _);
        }
    }

    private void PopulateCloudScanAssets(string providerName, ref long totalBytes, ref int catCount, ref int artCount)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        PopulateDiscoveredDefinitions(providerName, ref totalBytes);
        PopulateDiscoveredCatalogs(providerName, ref totalBytes, ref catCount);
        PopulateDiscoveredArtifacts(providerName, ref totalBytes, ref artCount);
    }

    private void PopulateDiscoveredDefinitions(string providerName, ref long totalBytes)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        foreach (var cloudDef in _currentHostingState.Definitions.Where(cloudDef => !HostedAssets.Any(a =>
            (!string.IsNullOrEmpty(a.Url) && !string.IsNullOrEmpty(cloudDef.Url) && string.Equals(a.Url, cloudDef.Url, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(cloudDef.FileName) && string.Equals(a.Name, cloudDef.FileName, StringComparison.OrdinalIgnoreCase)))))
        {
            totalBytes += cloudDef.FileSize;
            HostedDefinitionCount++;
            var stem = Path.GetFileNameWithoutExtension(cloudDef.FileName);
            var categoryName = FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudDefinitionFormat", "Cloud Publisher Definition ({0})", stem);

            HostedAssets.Add(new HostedAssetItemViewModel
            {
                AssetKind = HostedAssetKind.Definition,
                CanUpload = false,
                CanLoadToProject = !string.IsNullOrEmpty(cloudDef.Url),
                LoadButtonTooltip = GetLocalizedString("Tools.PublisherStudio.Hosting.LoadToProjectTip", "Load this definition and its catalogs into current project"),
                Name = string.IsNullOrEmpty(cloudDef.FileName) ? HostingConstants.DefaultDefinitionFileName : cloudDef.FileName,
                Category = categoryName,
                Location = $"{providerName} ({HostingFolderPath})",
                FileSize = cloudDef.FileSize,
                Url = cloudDef.Url,
                Status = HostingConstants.StatusLiveOnline,
                IsOnline = true,
                IsExternalCdn = false,
                LastUpdated = cloudDef.LastUpdated,
            });
        }
    }

    private void PopulateDiscoveredCatalogs(string providerName, ref long totalBytes, ref int catCount)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        foreach (var cloudCat in _currentHostingState.Catalogs.Where(cloudCat => !HostedAssets.Any(a =>
            (!string.IsNullOrEmpty(a.Url) && !string.IsNullOrEmpty(cloudCat.Url) && string.Equals(a.Url, cloudCat.Url, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(cloudCat.FileName) && string.Equals(a.Name, cloudCat.FileName, StringComparison.OrdinalIgnoreCase)))))
        {
            catCount++;
            totalBytes += cloudCat.FileSize;
            var isProjectCat = project.Catalogs.Any(c => c.Id == cloudCat.CatalogId || c.FileName == cloudCat.FileName);

            HostedAssets.Add(new HostedAssetItemViewModel
            {
                AssetKind = HostedAssetKind.Catalog,
                CatalogId = cloudCat.CatalogId,
                CanUpload = false,
                CanLoadToProject = !isProjectCat && !string.IsNullOrEmpty(cloudCat.Url),
                LoadButtonTooltip = GetLocalizedString("Tools.PublisherStudio.Hosting.LoadCatalogTip", "Load this catalog into current project"),
                Name = string.IsNullOrEmpty(cloudCat.FileName) ? $"catalog-{cloudCat.CatalogId}.json" : cloudCat.FileName,
                Category = FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudCatalogFormat", "Cloud Catalog ({0})", cloudCat.CatalogId),
                Location = $"{providerName} ({HostingFolderPath})",
                FileSize = cloudCat.FileSize,
                Url = cloudCat.Url,
                Status = HostingConstants.StatusLiveOnline,
                IsOnline = true,
                IsExternalCdn = false,
                LastUpdated = cloudCat.LastUpdated,
            });
        }
    }

    private void PopulateDiscoveredArtifacts(string providerName, ref long totalBytes, ref int artCount)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        foreach (var cloudArt in _currentHostingState.Artifacts.Where(cloudArt => !HostedAssets.Any(a =>
            (!string.IsNullOrEmpty(a.Url) && !string.IsNullOrEmpty(cloudArt.Url) && string.Equals(a.Url, cloudArt.Url, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(cloudArt.FileName) && string.Equals(a.Name, cloudArt.FileName, StringComparison.OrdinalIgnoreCase)))))
        {
            artCount++;
            totalBytes += cloudArt.FileSize;
            HostedAssets.Add(new HostedAssetItemViewModel
            {
                AssetKind = HostedAssetKind.Artifact,
                CanUpload = false,
                CanAddToCatalog = !string.IsNullOrEmpty(cloudArt.Url),
                Name = cloudArt.FileName,
                Category = GetLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudArtifact", "Cloud Artifact"),
                Location = $"{providerName} ({HostingFolderPath})",
                FileSize = cloudArt.FileSize,
                Url = cloudArt.Url,
                Status = HostingConstants.StatusLiveOnline,
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
        OnPropertyChanged(nameof(ProviderDisplayName));
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

        UpdateHostingFolderPath();
        SyncActiveHostingState();

        RefreshHostedAssets();
        AuthenticationChangedCallback?.Invoke();

        if (value == null)
        {
            // Clear auth status when no provider selected
            AuthenticationStatusMessage = string.Empty;
            return;
        }

        if (_isLoadingHostingState)
        {
            return;
        }

        _ = RestoreAuthenticationAsync();
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

        await RunAuthenticationFlowAsync(ExecuteAuthenticationByProviderTypeAsync);
    }

    /// <summary>
    /// Connects Dropbox using the interactive OAuth 2.0 browser flow.
    /// Stores a refresh token so the session survives short-lived access-token expiry.
    /// </summary>
    [RelayCommand]
    private async Task ConnectDropboxOAuthAsync()
    {
        if (SelectedHostingProvider is not DropboxHostingProvider dropboxProvider)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DropboxAppKey))
        {
            AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.EnterDropboxAppKey", "Please enter your Dropbox app key. Create an app in the Dropbox App Console to get one.");
            return;
        }

        var appKey = DropboxAppKey.Trim();
        await RunAuthenticationFlowAsync(async cancellationToken =>
            (OperationResult<bool>?)await dropboxProvider.AuthenticateWithOAuthAsync(appKey, cancellationToken));
    }

    private async Task RunAuthenticationFlowAsync(Func<System.Threading.CancellationToken, Task<OperationResult<bool>?>> attempt)
    {
        if (_authCts != null)
        {
            await _authCts.CancelAsync();
            _authCts.Dispose();
        }

        _authCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(HostingConstants.BrowserAuthTimeoutSeconds));

        IsAuthenticating = true;
        AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.Authenticating", "Authenticating...");

        try
        {
            var result = await attempt(_authCts.Token);
            if (result == null)
            {
                return;
            }

            if (result.Success)
            {
                await HandleAuthenticationSuccessAsync();
                if (SelectedHostingProvider is { SupportsCatalogHosting: true })
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
            AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.AuthCanceledDriveHelp", "Authentication was canceled or timed out. For Google Drive, ensure you selected 'Desktop app' (not 'Web application') in Google Cloud Console.");
            logger.LogInformation(ex, "Authentication canceled or timed out for {Provider}", SelectedHostingProvider?.DisplayName);
            notificationService?.ShowWarning(GetLocalizedString("Tools.PublisherStudio.Publish.AuthCanceledTitle", "Authentication Canceled"), GetLocalizedString("Tools.PublisherStudio.Publish.AuthCanceledTimeout", "Authentication timed out or was canceled."));
        }
        catch (Exception ex)
        {
            AuthenticationStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.AuthenticationErrorFormat", "Authentication error: {0}", ex.Message);
            logger.LogError(ex, "Authentication error for {Provider}", SelectedHostingProvider?.DisplayName);
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
            AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.AuthenticationCanceled", "Authentication canceled.");
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
                AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.EnterGitHubToken", "Please enter your GitHub Personal Access Token");
                return null;
            }

            return await githubProvider.AuthenticateWithTokenAsync(GitHubPersonalAccessToken, cancellationToken);
        }

        if (SelectedHostingProvider is DropboxHostingProvider dropboxProvider)
        {
            if (string.IsNullOrWhiteSpace(DropboxAccessToken))
            {
                AuthenticationStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.EnterDropboxToken", "Please enter your Dropbox Access Token");
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
            AuthenticationStatusMessage = GetLocalizedString(
                "Tools.PublisherStudio.Publish.GoogleDriveCredentialsNeededMessage",
                "Google Drive requires client credentials. Enter your Client ID and Client Secret above.");
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.GoogleDriveCredentialsNeededTitle", "Google Drive Credentials Needed"),
                GetLocalizedString("Tools.PublisherStudio.Publish.GoogleDriveCredentialsNeededMessage", "Please enter your Google OAuth Client ID and Secret to connect to Google Drive. Follow the Project Configuration guide above."));
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

        // Google client credentials are app-level (not project-level), so persist
        // them independently of the project path for post-restart restores.
        if (SelectedHostingProvider?.ProviderId == HostingConstants.GoogleDrive)
        {
            await PersistGoogleDriveClientCredentialsAsync();
        }

        // Save token to hosting state for persistence
        await SaveAuthTokenAsync();

        OnPropertyChanged(nameof(IsProviderAuthenticated));
        OnPropertyChanged(nameof(NeedsAuthentication));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(TargetDestinationDescription));
        AuthenticationChangedCallback?.Invoke();

        notificationService?.ShowSuccess(GetLocalizedString("Tools.PublisherStudio.Publish.ConnectedTitle", "Connected"), FormatLocalizedString("Tools.PublisherStudio.Publish.ConnectedMessage", "Successfully connected to {0}. You can now publish your catalog.", SelectedHostingProvider?.DisplayName ?? "Provider"), autoDismissMs: 4000);
    }

    private void HandleAuthenticationFailure(OperationResult<bool> result)
    {
        AuthenticationStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.AuthFailed", "Authentication failed: {0}", result.FirstError ?? "Unknown error");
        logger.LogWarning("Authentication failed for {Provider}: {Error}", SelectedHostingProvider?.DisplayName ?? "Provider", result.FirstError);

        notificationService?.ShowError(GetLocalizedString("Tools.PublisherStudio.Publish.AuthError", "Authentication Error"), result.FirstError ?? GetLocalizedString("Tools.PublisherStudio.Publish.AuthFailedMessage", "Failed to authenticate with the hosting provider."));
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
        OnPropertyChanged(nameof(ShowDiscoveredDefinitionBanner));
        OnPropertyChanged(nameof(ShowNoDefinitionBanner));
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
                await credentialStore.DeleteCredentialAsync(SelectedHostingProvider.ProviderId);
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
            AuthenticationChangedCallback?.Invoke();

            logger.LogInformation("Signed out from {Provider}", SelectedHostingProvider.DisplayName);
        }
        catch (Exception ex)
        {
            AuthenticationStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.SignOutError", "Sign out error: {0}", ex.Message);
            logger.LogError(ex, "Sign out error for {Provider}", SelectedHostingProvider.DisplayName);
        }
    }

    private async Task LoadHostingStateAsync()
    {
        if (string.IsNullOrEmpty(project.ProjectPath))
            return;

        _isLoadingHostingState = true;
        try
        {
            var result = hostingStateManager != null ? await hostingStateManager.LoadStatesAsync(project.ProjectPath, CancellationToken.None) : null;
            if (result?.Success == true && result.Data != null)
            {
                _hostingStates.Clear();
                foreach (var (providerId, state) in result.Data.States)
                {
                    _hostingStates[providerId] = state;
                }

                HasPreviouslyPublished = _hostingStates.Values.Any(HasPublishedContent);
                RestoreBestHostingProvider();
                SyncActiveHostingState();
                RefreshHostedAssets();
                logger.LogInformation(
                    "Loaded hosting states for {ProviderCount} provider(s)",
                    _hostingStates.Count);
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
        finally
        {
            _isLoadingHostingState = false;
        }

        // After loading state, restore authentication
        await RestoreAuthenticationAsync();
    }

    private bool HasPublishedContent(HostingState state)
    {
        return state.Definition != null || state.Catalogs.Count > 0 || state.Artifacts.Count > 0;
    }

    private HostingState GetOrCreateHostingState(string? providerId)
    {
        if (string.IsNullOrEmpty(providerId))
        {
            return new HostingState();
        }

        if (!_hostingStates.TryGetValue(providerId, out var state))
        {
            state = new HostingState { ProviderId = providerId };
            _hostingStates[providerId] = state;
        }

        return state;
    }

    private void SyncActiveHostingState()
    {
        var providerId = SelectedHostingProvider?.ProviderId;
        _currentHostingState = !string.IsNullOrEmpty(providerId)
            ? GetOrCreateHostingState(providerId)
            : null;

        ProviderDefinitionUrl = _currentHostingState?.Definition?.Url ?? string.Empty;
        if (_currentHostingState?.Catalogs.Count > 0)
        {
            CatalogUrl = _currentHostingState.Catalogs[0].Url;
            PrimaryCatalogUrl = _currentHostingState.Catalogs[0].Url;
        }
        else
        {
            CatalogUrl = string.Empty;
            PrimaryCatalogUrl = string.Empty;
        }

        GenerateSubscriptionUrl();
        InitializeCatalogStatuses();
        RefreshUploadHierarchy();
    }

    private void RestoreBestHostingProvider()
    {
        var selectedId = SelectedHostingProvider?.ProviderId;
        if (!string.IsNullOrEmpty(selectedId) &&
            _hostingStates.TryGetValue(selectedId, out var selectedState) &&
            HasPublishedContent(selectedState))
        {
            return;
        }

        var best = _hostingStates.Values
            .Where(HasPublishedContent)
            .OrderByDescending(s => s.LastPublished ?? DateTime.MinValue)
            .FirstOrDefault();

        if (best == null && !string.IsNullOrEmpty(selectedId) && !_hostingStates.ContainsKey(selectedId))
        {
            best = _hostingStates.Values.FirstOrDefault();
        }

        if (best == null)
        {
            return;
        }

        var provider = HostingProviders.FirstOrDefault(p =>
            string.Equals(p.ProviderId, best.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (provider != null && !ReferenceEquals(provider, SelectedHostingProvider))
        {
            logger.LogInformation("Restoring hosting provider {ProviderId} from previous publish", best.ProviderId);
            SelectedHostingProvider = provider;
        }
    }

    private async Task<OperationResult<bool>> SaveAllHostingStatesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(project.ProjectPath) || hostingStateManager == null)
        {
            return OperationResult<bool>.CreateFailure("No project path or hosting state manager.");
        }

        return await hostingStateManager.SaveStatesAsync(
            project.ProjectPath,
            new PublisherHostingStates { States = _hostingStates },
            cancellationToken);
    }

    private void PopulateUploadHierarchyHeader()
    {
        UploadHierarchy.PublisherName = project.Catalog.Publisher?.Name ?? GetLocalizedString("Tools.PublisherStudio.Publish.UnknownPublisher", "Publisher");
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
            LocalizationService = localizationService,
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
            LocalizationService = localizationService,
        };

        var hostedInfo = _currentHostingState?.Catalogs?.FirstOrDefault(c => c.CatalogId == namedCat.Id);
        if (hostedInfo != null)
        {
            catNode.DirectDownloadUrl = hostedInfo.Url;
            catNode.IsPublished = true;
            catNode.LastUpdated = hostedInfo.LastUpdated;
        }

        // Sync publish state from the tracked status so hierarchy nodes grey out
        // together with the status list after uploads and edits.
        var publishStatus = CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == namedCat.Id);
        if (publishStatus != null)
        {
            catNode.IsPublished = publishStatus.IsPublished;
            catNode.HasChanges = publishStatus.HasChanges;
            catNode.LastUpdated = publishStatus.LastPublished ?? catNode.LastUpdated;
            if (publishStatus.PublishedUrl != null)
            {
                catNode.DirectDownloadUrl = publishStatus.PublishedUrl;
            }
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
                ValidationMessage = GetLocalizedString("Tools.PublisherStudio.Publish.NoCatalogSelected", "No catalog selected");
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
                ValidationMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.ValidationArtifactsInvalidFormat", "Validation failed: {0} artifacts have invalid or missing URLs", artifactErrors.Count);
                return;
            }

            var result = await publisherStudioService.ValidateCatalogAsync(ActiveCatalog.Catalog, allowPendingArtifacts: true, cancellationToken: CancellationToken.None);
            IsValid = result.Success;
            ValidationMessage = result.Success
                ? FormatLocalizedString("Tools.PublisherStudio.Publish.CatalogValidFormat", "Catalog '{0}' is valid", ActiveCatalog.Name)
                : FormatLocalizedString("Tools.PublisherStudio.Publish.ValidationFailedFormat", "Validation failed: {0}", result.FirstError);

            logger.LogInformation("Catalog '{CatalogName}' validation: {IsValid}", ActiveCatalog.Name, IsValid);
        }
        catch (Exception ex)
        {
            IsValid = false;
            ValidationMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.ValidationErrorFormat", "Validation error: {0}", ex.Message);
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
                CatalogJson = string.Empty;
                logger.LogError("Failed to export catalog '{CatalogName}': {Error}", ActiveCatalog.Name, result.FirstError);
                notificationService?.ShowError(
                    GetLocalizedString("Tools.PublisherStudio.Publish.ExportFailedTitle", "Export Failed"),
                    result.FirstError ?? GetLocalizedString("Tools.PublisherStudio.Publish.ExportCatalogJsonFailed", "Failed to export catalog JSON."));
            }
        }
        catch (Exception ex)
        {
            CatalogJson = string.Empty;
            logger.LogError(ex, "Error exporting catalog");
            notificationService?.ShowError(
                GetLocalizedString("Tools.PublisherStudio.Publish.ExportFailedTitle", "Export Failed"),
                ex.Message);
        }
    }

    /// <summary>
    /// Uploads the catalog to the selected hosting provider.
    /// </summary>
    [RelayCommand]
    private async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync()
    {
        var (acquired, cts) = await TryBeginPublishAsync().ConfigureAwait(false);
        if (!acquired || cts == null)
        {
            return OperationResult<HostingUploadResult>.CreateFailure(UploadAlreadyInProgressMessage);
        }

        try
        {
            return await UploadCatalogCoreAsync(cts.Token, manageUploadingState: false);
        }
        finally
        {
            EndPublish(cts);
        }
    }

    private async Task<(bool Acquired, CancellationTokenSource? Cts)> TryBeginPublishAsync()
    {
        if (IsUploading || !await _publishGate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return (false, null);
        }

        var cts = new CancellationTokenSource();
        _activeUploadCts = cts;
        IsUploading = true;
        return (true, cts);
    }

    private void EndPublish(CancellationTokenSource? cts)
    {
        try
        {
            if (ReferenceEquals(_activeUploadCts, cts))
            {
                _activeUploadCts = null;
            }

            cts?.Dispose();
        }
        finally
        {
            IsUploading = false;
            try
            {
                _publishGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Gate was disposed during shutdown.
            }
        }
    }

    private async Task<OperationResult<HostingUploadResult>?> ValidateUploadPreconditionsAsync(bool suppressNotification = false)
    {
        if (SelectedHostingProvider == null)
        {
            UploadStatusMessage = PleaseSelectHostingProviderMessage;
            if (!suppressNotification)
            {
                notificationService?.ShowWarning(
                    GetLocalizedString("Tools.PublisherStudio.Publish.NoProviderTitle", "Provider Required"),
                    PleaseSelectHostingProviderMessage);
            }

            return OperationResult<HostingUploadResult>.CreateFailure(PleaseSelectHostingProviderMessage);
        }

        if (HasIncompatibleArtifactsForActiveCatalog)
        {
            var warningMsg = FormatLocalizedString("Tools.PublisherStudio.Publish.IncompatibleArtifactsActiveCatalogFormat", "{0} only hosts catalog metadata (JSON). The active catalog '{1}' has {2} local file(s) pending upload. Either provide direct CDN URLs for those files, or switch to Google Drive or Dropbox to host binary archives.", SelectedHostingProvider?.DisplayName ?? "This provider", ActiveCatalog?.Name, ActiveCatalogPendingArtifactsCount);
            UploadStatusMessage = warningMsg;
            notificationService?.ShowError(GetLocalizedString(IncompatibleProviderTitleKey, IncompatibleProviderDefaultMessage), warningMsg);
            return OperationResult<HostingUploadResult>.CreateFailure(warningMsg);
        }

        await ValidateCatalogAsync();
        if (!IsValid)
        {
            var catName = !string.IsNullOrWhiteSpace(ActiveCatalog?.Name) ? ActiveCatalog.Name : GetLocalizedString("Tools.PublisherStudio.Common.Catalog", "Catalog");
            var detail = string.IsNullOrWhiteSpace(ValidationMessage)
                ? GetLocalizedString("Tools.PublisherStudio.Publish.FixValidationBeforeUpload", "Please fix catalog validation errors before uploading.")
                : ValidationMessage;
            UploadStatusMessage = $"{catName}: {detail}";
            if (!suppressNotification)
            {
                notificationService?.ShowWarning(
                    GetLocalizedString("Tools.PublisherStudio.Publish.ValidationFailedTitle", "Validation Failed"),
                    UploadStatusMessage);
            }

            return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
        }

        return null;
    }

    private async Task<OperationResult<HostingUploadResult>> UploadCatalogCoreAsync(
        CancellationToken cancellationToken,
        bool manageUploadingState,
        bool suppressNotifications = false,
        bool uploadDefinition = true)
    {
        var preconditionResult = await ValidateUploadPreconditionsAsync(suppressNotifications);
        if (preconditionResult != null || SelectedHostingProvider == null)
        {
            return preconditionResult ?? OperationResult<HostingUploadResult>.CreateFailure(PleaseSelectHostingProviderMessage);
        }

        if (IsScanningStorage)
        {
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.ScanInProgress", "Storage scan in progress. Please try again shortly.");
            return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
        }

        try
        {
            if (manageUploadingState)
            {
                IsUploading = true;
            }

            UploadProgress = 0;
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.PreparingToPublish", "Preparing to publish...");
            PublishCompleted = false;
            CurrentPublishStep = 0;
            PublishSummary = string.Empty;

            if (!await EnsureProviderAuthenticatedAsync(cancellationToken))
            {
                return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
            }

            // 1. Upload Pending Artifacts and Artwork
            CurrentPublishStep = 1;
            var pendingUploadResult = await UploadPendingContentAsync(SelectedHostingProvider, cancellationToken, suppressNotifications);
            if (pendingUploadResult != null)
            {
                MarkActiveCatalogStale();
                NotifyDefinitionStale();
                return pendingUploadResult;
            }

            // 2. Export Active Catalog (Now includes new URLs)
            CurrentPublishStep = 2;
            if (ActiveCatalog == null)
            {
                UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.NoCatalogSelected", "No catalog selected");
                return OperationResult<HostingUploadResult>.CreateFailure("No catalog selected");
            }

            UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.GeneratingCatalogFormat", "Generating catalog '{0}'...", ActiveCatalog.Name);
            var exportResult = await publisherStudioService.ExportCatalogAsync(project, ActiveCatalog, cancellationToken: cancellationToken);
            if (!exportResult.Success || string.IsNullOrEmpty(exportResult.Data))
            {
                UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.ExportCatalogFailedFormat", "Failed to export catalog: {0}", exportResult.FirstError);
                return OperationResult<HostingUploadResult>.CreateFailure(exportResult);
            }

            CatalogJson = exportResult.Data;
            UploadProgress = PendingUploadProgressBand;
            UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.UploadingCatalogFormat", "Uploading catalog '{0}' to {1}...", ActiveCatalog.Name, SelectedHostingProvider.DisplayName);

            // 3. Upload Catalog
            CurrentPublishStep = 3;
            var progress = new Progress<int>(p =>
            {
                UploadProgress = PendingUploadProgressBand + (int)(p * 0.2);
            });

            var uploadResult = await PerformCatalogUploadAsync(progress, cancellationToken);
            if (uploadResult.Success && uploadResult.Data != null)
            {
                await CompletePublishSuccessAsync(uploadResult.Data, cancellationToken, suppressNotifications, uploadDefinition);
                return uploadResult;
            }
            else
            {
                UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.CatalogUploadFailedFormat", "Catalog upload failed: {0}", uploadResult.FirstError);
                return uploadResult;
            }
        }
        catch (OperationCanceledException ex)
        {
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.UploadCanceled", "Upload canceled.");
            logger.LogInformation(ex, "Catalog upload was canceled.");
            return OperationResult<HostingUploadResult>.CreateFailure("Upload canceled");
        }
        catch (Exception ex)
        {
            UploadStatusMessage = FormatLocalizedString(PublishErrorFormatKey, PublishErrorFormatDefault, ex.Message);
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
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.Authenticating", "Authenticating...");
            var authResult = await ExecuteAuthenticationByProviderTypeAsync(cancellationToken);
            if (authResult == null || !authResult.Success)
            {
                UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.AuthFailed", "Authentication failed: {0}", authResult?.FirstError ?? AuthenticationStatusMessage);
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

        var isMatchingProvider = _currentHostingState != null &&
            string.Equals(_currentHostingState.ProviderId, SelectedHostingProvider.ProviderId, StringComparison.Ordinal);

        var existingCatalogFileId = isMatchingProvider
            ? _currentHostingState!.Catalogs.FirstOrDefault(c => c.CatalogId == ActiveCatalog.Id)?.FileId
            : null;

        var catalogFileName = string.IsNullOrEmpty(ActiveCatalog.FileName)
            ? $"catalog-{ActiveCatalog.Id}.json"
            : ActiveCatalog.FileName;

        if (!string.IsNullOrEmpty(existingCatalogFileId) && SelectedHostingProvider.SupportsUpdate)
        {
            UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.UpdatingCatalogFormat", "Updating existing catalog '{0}'...", ActiveCatalog.Name);
            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(CatalogJson));
            return await SelectedHostingProvider.UpdateFileAsync(existingCatalogFileId, stream, catalogFileName, progress, cancellationToken);
        }

        return await SelectedHostingProvider.UploadCatalogAsync(CatalogJson, project.Catalog.Publisher.Id, catalogFileName, progress, cancellationToken);
    }

    private async Task CompletePublishSuccessAsync(
        HostingUploadResult data,
        CancellationToken cancellationToken = default,
        bool suppressNotifications = false,
        bool uploadDefinition = true)
    {
        if (SelectedHostingProvider == null) return;

        CatalogUrl = data.DirectDownloadUrl;
        if (string.IsNullOrWhiteSpace(PrimaryCatalogUrl))
        {
            PrimaryCatalogUrl = CatalogUrl;
        }

        SubscriptionUrl = SelectedHostingProvider.GetSubscriptionLink(CatalogUrl);
        UploadProgress = 100;
        UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.PublishedSuccessfully", "Published successfully!");
        logger.LogInformation("Catalog and artifacts uploaded to {Provider}: {Url}", SelectedHostingProvider.ProviderId, CatalogUrl);

        await SaveHostingStateAsync(data.FileId, data.DirectDownloadUrl, data.FileSize, cancellationToken);
        MarkActiveCatalogPublished(data.DirectDownloadUrl);
        RefreshUploadHierarchy();
        await PersistProjectAfterPublishAsync();
        await PersistCurrentDropboxCredentialAsync();
        NotifyLibraryRefresh();

        if (uploadDefinition)
        {
            await HandlePostPublishDefinitionAsync(cancellationToken, suppressNotifications);
        }
        else
        {
            CurrentPublishStep = 6;
            PublishCompleted = true;
            PublishSummary = BuildPublishSummary(CatalogUrl, ProviderDefinitionUrl, SubscriptionUrl);
            if (!suppressNotifications)
            {
                notificationService?.ShowSuccess(
                    GetLocalizedString(PublishSuccessTitleKey, SuccessLiteral),
                    UploadStatusMessage,
                    autoDismissMs: 4000);
            }
        }
    }

    private async Task HandlePostPublishDefinitionAsync(
        CancellationToken cancellationToken,
        bool suppressNotifications)
    {
        // 4. Generate and upload provider definition
        CurrentPublishStep = 4;
        UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.GeneratingProviderDefinition", "Generating provider definition...");
        var definitionGenerated = await GenerateProviderDefinitionAsync();

        var defResult = definitionGenerated
            ? await UploadProviderDefinitionIfAvailableAsync(cancellationToken)
            : null;

        // 5. Generate subscription URL (uses definition URL if available)
        GenerateSubscriptionUrl();

        CurrentPublishStep = 6;
        PublishCompleted = true;
        PublishSummary = BuildPublishSummary(CatalogUrl, ProviderDefinitionUrl, SubscriptionUrl);
        if (defResult != null && !defResult.Success)
        {
            UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.DefinitionUploadFailedAfterPublishFormat", "Catalog published, but provider definition upload failed: {0}", defResult.FirstError);
            NotifyDefinitionStale();
            if (!suppressNotifications)
            {
                notificationService?.ShowWarning(GetLocalizedString(PublishWarningKey, PublishWarningDefaultMessage), UploadStatusMessage);
            }
        }
        else if (!definitionGenerated)
        {
            NotifyDefinitionStale();
            if (!suppressNotifications)
            {
                notificationService?.ShowWarning(GetLocalizedString(PublishWarningKey, PublishWarningDefaultMessage), UploadStatusMessage);
            }
        }
        else
        {
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.PublishedSuccessfully", "Published successfully!");
            if (!suppressNotifications)
            {
                notificationService?.ShowSuccess(
                    GetLocalizedString(PublishSuccessTitleKey, SuccessLiteral),
                    UploadStatusMessage,
                    autoDismissMs: 4000);
            }
        }
    }

    private void MarkActiveCatalogPublished(string catalogUrl)
    {
        if (ActiveCatalog == null)
        {
            return;
        }

        var status = CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == ActiveCatalog.Id);
        if (status != null)
        {
            status.IsPublished = true;
            status.PublishedUrl = catalogUrl;
            status.LastPublished = DateTime.UtcNow;
            status.HasChanges = false;
        }
    }

    private async Task<OperationResult<HostingUploadResult>?> UploadProviderDefinitionIfAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ProviderDefinitionJson) || SelectedHostingProvider == null)
        {
            return null;
        }

        CurrentPublishStep = 5;
        UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.UploadingProviderDefinition", "Uploading provider definition...");
        var defFileName = project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName;
        var isMatchingProvider = _currentHostingState != null &&
            string.Equals(_currentHostingState.ProviderId, SelectedHostingProvider.ProviderId, StringComparison.Ordinal);

        var existingDefFileId = isMatchingProvider
            ? _currentHostingState!.Definition?.FileId
            : null;

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
                await SaveAllHostingStatesAsync(cancellationToken);
            }

            RefreshHostedAssets();
            NotifyDefinitionUploaded();
            return defUploadResult;
        }

        logger.LogWarning("Provider definition upload failed: {Error}", defUploadResult.FirstError);
        return defUploadResult;
    }

    private async Task<OperationResult<HostingUploadResult>?> UploadPendingContentAsync(
        IHostingProvider provider,
        CancellationToken cancellationToken,
        bool suppressNotifications = false)
    {
        var pendingArtworkCount = ActiveCatalog == null ? 0 : CollectPendingArtwork(ActiveCatalog).Count;
        var (artifactsOk, completedArtifacts) = await UploadPendingArtifactsAsync(provider, cancellationToken, pendingArtworkCount);
        if (!artifactsOk)
        {
            return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
        }

        // 1b. Upload Pending Artwork (local image files referenced by content metadata)
        if (!await UploadPendingArtworkAsync(provider, cancellationToken, suppressNotifications, completedArtifacts, completedArtifacts + pendingArtworkCount))
        {
            return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
        }

        return null;
    }

    private async Task<(bool Success, int CompletedCount)> UploadPendingArtifactsAsync(
        IHostingProvider provider,
        CancellationToken cancellationToken = default,
        int additionalProgressTotal = 0)
    {
        if (ActiveCatalog == null)
        {
            return (true, 0);
        }

        var allReleases = ActiveCatalog.Catalog.Content.SelectMany(c => c.Releases).ToList();
        var pendingArtifacts = allReleases
            .SelectMany(r => r.Artifacts)
            .Where(a => !string.IsNullOrEmpty(a.LocalFilePath) && string.IsNullOrEmpty(a.DownloadUrl))
            .ToList();

        if (pendingArtifacts.Count == 0)
        {
            return (true, 0);
        }

        if (!provider.SupportsArtifactHosting)
        {
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.ArtifactHostingNotSupported", "Provider does not support artifact hosting. Please add URLs manually.");
            return (false, 0);
        }

        BuildUploadQueue(pendingArtifacts);

        int total = UploadQueue.Count;
        int current = 0;

        // Defer state persistence and UI rebuilds until the loop completes so that
        // publishing N artifacts costs one save and one refresh instead of N.
        try
        {
            foreach (var task in UploadQueue)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current++;
                if (!await ExecuteSingleArtifactUploadAsync(provider, task, current, total, cancellationToken, progressOffset: 0, progressTotal: total + additionalProgressTotal, deferPersistenceAndRefresh: true))
                {
                    return (false, current - 1);
                }
            }

            return (true, total);
        }
        finally
        {
            await FlushArtifactHostingStateAsync(CancellationToken.None);
        }
    }

    private async Task<bool> UploadPendingArtworkAsync(
        IHostingProvider provider,
        CancellationToken cancellationToken,
        bool suppressNotifications = false,
        int progressOffset = 0,
        int progressTotal = 0)
    {
        if (ActiveCatalog == null)
        {
            return true;
        }

        var pending = CollectPendingArtwork(ActiveCatalog);
        if (pending.Count == 0)
        {
            return true;
        }

        if (!provider.SupportsArtifactHosting)
        {
            UploadStatusMessage = GetLocalizedString(
                "Tools.PublisherStudio.Publish.ArtworkHostingNotSupported",
                "Provider does not support artwork hosting. Use direct image URLs or switch providers.");
            if (!suppressNotifications)
            {
                notificationService?.ShowError(
                    GetLocalizedString(IncompatibleProviderTitleKey, IncompatibleProviderDefaultMessage),
                    UploadStatusMessage);
            }

            return false;
        }

        int total = pending.Count;
        int current = 0;
        foreach (var (content, slot, localPath) in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;
            if (!await UploadSingleArtworkAsync(provider, content, slot, localPath, current, total, cancellationToken, suppressNotifications, progressOffset, progressTotal))
            {
                return false;
            }
        }

        return true;
    }

    private List<(CatalogContentItem Content, ArtworkSlot Slot, string LocalPath)> CollectPendingArtwork(
        NamedCatalog catalog)
    {
        var pending = new List<(CatalogContentItem Content, ArtworkSlot Slot, string LocalPath)>();
        foreach (var content in catalog.Catalog.Content)
        {
            var metadata = content.Metadata;
            if (metadata == null)
            {
                continue;
            }

            AddPendingArtwork(pending, content, ArtworkSlot.Icon, metadata.IconUrl);
            AddPendingArtwork(pending, content, ArtworkSlot.Banner, metadata.BannerUrl);
            AddPendingArtwork(pending, content, ArtworkSlot.Backdrop, metadata.BackdropUrl);
        }

        return pending;
    }

    private void AddPendingArtwork(
        List<(CatalogContentItem Content, ArtworkSlot Slot, string LocalPath)> pending,
        CatalogContentItem content,
        ArtworkSlot slot,
        string? value)
    {
        if (IsPendingArtworkPath(value))
        {
            pending.Add((content, slot, value!.Trim()));
        }
    }

    private bool IsPendingArtworkPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps);
    }

    private void ApplyArtworkUrl(CatalogContentItem content, ArtworkSlot slot, string url)
    {
        content.Metadata ??= new ContentRichMetadata();
        switch (slot)
        {
            case ArtworkSlot.Icon:
                content.Metadata.IconUrl = url;
                break;
            case ArtworkSlot.Banner:
                content.Metadata.BannerUrl = url;
                break;
            case ArtworkSlot.Backdrop:
                content.Metadata.BackdropUrl = url;
                break;
            default:
                break;
        }
    }

    private void ReportPendingUploadProgress(int completedItems, int totalItems, int itemPercent)
    {
        if (totalItems <= 0)
        {
            return;
        }

        UploadProgress = (int)(((completedItems + (itemPercent / 100.0)) / totalItems) * PendingUploadProgressBand);
    }

    private async Task<bool> UploadSingleArtworkAsync(
        IHostingProvider provider,
        CatalogContentItem content,
        ArtworkSlot slot,
        string localPath,
        int current,
        int total,
        CancellationToken cancellationToken,
        bool suppressNotifications = false,
        int progressOffset = 0,
        int progressTotal = 0)
    {
        var displayName = Path.GetFileName(localPath);
        UploadStatusMessage = FormatLocalizedString(
            "Tools.PublisherStudio.Publish.UploadingArtworkFormat",
            "Uploading artwork {0}/{1}: {2}",
            current,
            total,
            displayName);

        if (!File.Exists(localPath))
        {
            var msg = FormatLocalizedString(
                "Tools.PublisherStudio.Publish.ArtworkFileMissingFormat",
                "Artwork file not found: {0}",
                localPath);
            return FailArtworkUpload(msg, suppressNotifications);
        }

        OperationResult<HostingUploadResult> result;
        try
        {
            await using var stream = File.OpenRead(localPath);
            var combinedTotal = progressTotal > 0 ? progressTotal : total;
            var progress = new Progress<int>(p =>
            {
                ReportPendingUploadProgress(progressOffset + current - 1, combinedTotal, p);
            });
            var uploadFileName = $"{content.Id}-{slot.ToString().ToLowerInvariant()}-{displayName}";
            result = await provider.UploadFileAsync(stream, uploadFileName, null, progress, cancellationToken);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read artwork file {Path}", localPath);
            var msg = FormatLocalizedString(
                "Tools.PublisherStudio.Publish.ArtworkFileMissingFormat",
                "Artwork file not found: {0}",
                localPath);
            return FailArtworkUpload(msg, suppressNotifications);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied reading artwork file {Path}", localPath);
            var msg = FormatLocalizedString(
                "Tools.PublisherStudio.Publish.ArtworkFileMissingFormat",
                "Artwork file not found: {0}",
                localPath);
            return FailArtworkUpload(msg, suppressNotifications);
        }

        if (!result.Success || result.Data == null || string.IsNullOrWhiteSpace(result.Data.DirectDownloadUrl))
        {
            var msg = FormatLocalizedString(
                "Tools.PublisherStudio.Publish.ArtworkUploadFailedFormat",
                "Failed to upload artwork {0}: {1}",
                displayName,
                result.FirstError);
            return FailArtworkUpload(msg, suppressNotifications);
        }

        ApplyArtworkUrl(content, slot, result.Data.DirectDownloadUrl);
        return true;
    }

    private bool FailArtworkUpload(string message, bool suppressNotifications = false)
    {
        UploadStatusMessage = message;
        if (!suppressNotifications)
        {
            notificationService?.ShowError(
                GetLocalizedString("Tools.PublisherStudio.Publish.ArtworkUploadFailedTitle", "Artwork Upload Failed"),
                message);
        }

        return false;
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
                    LocalizationService = localizationService,
                });
            }
        }
    }

    private async Task<(Stream? Stream, string? TempZipPath, bool Success)> PrepareArtifactStreamAsync(ArtifactUploadTask task, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(task.Artifact.LocalFilePath))
        {
            UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.CompressingFolderFormat", "Compressing folder '{0}' into archive...", Path.GetFileName(task.Artifact.LocalFilePath));
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
            var completed = false;
            try
            {
                await Task.Run(() => ZipFile.CreateFromDirectory(task.Artifact.LocalFilePath, tempZipToCleanup), cancellationToken);

                using (var hashStream = File.OpenRead(tempZipToCleanup))
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = await sha256.ComputeHashAsync(hashStream, cancellationToken);
                    task.Artifact.Sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }

                var stream = File.OpenRead(tempZipToCleanup);
                task.Artifact.Size = stream.Length;
                completed = true;
                return (stream, tempZipToCleanup, true);
            }
            finally
            {
                if (!completed)
                {
                    CleanupTempZipFile(tempZipToCleanup);
                }
            }
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
        task.ErrorMessage = GetLocalizedString("Tools.PublisherStudio.Publish.FileOrDirectoryNotFound", "File or directory not found");
        UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.FileOrDirectoryNotFoundFormat", "File or directory not found: {0}", task.Artifact.LocalFilePath);
        return (null, null, false);
    }

    private async Task FlushArtifactHostingStateAsync(CancellationToken cancellationToken)
    {
        await SaveAllHostingStatesAsync(cancellationToken);

        RefreshHostedAssets();
        RefreshUploadHierarchy();
        RefreshArtifactStatuses();
    }

    private async Task RecordUploadedArtifactHostingStateAsync(
        ArtifactUploadTask task,
        HostingUploadResult uploadData,
        CancellationToken cancellationToken = default,
        bool deferPersistenceAndRefresh = false)
    {
        task.Artifact.DownloadUrl = uploadData.DirectDownloadUrl;
        task.Status = UploadStatus.Uploaded;
        task.Progress = 100;
        logger.LogInformation("Uploaded artifact {File} to {Url}", task.Artifact.Filename, task.Artifact.DownloadUrl);

        if (_currentHostingState == null)
        {
            return;
        }

        var existingArt = _currentHostingState.Artifacts.FirstOrDefault(a => IsSameArtifact(a, task.Artifact.Filename, task.ContentId, task.Version));
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

        if (deferPersistenceAndRefresh)
        {
            return;
        }

        await FlushArtifactHostingStateAsync(cancellationToken);
    }

    private async Task<bool> ExecuteSingleArtifactUploadAsync(
        IHostingProvider provider,
        ArtifactUploadTask task,
        int current,
        int total,
        CancellationToken cancellationToken = default,
        int progressOffset = 0,
        int progressTotal = 0,
        bool deferPersistenceAndRefresh = false)
    {
        task.Status = UploadStatus.Uploading;
        UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.UploadingArtifactFormat", "Uploading artifact {0}/{1}: {2}", current, total, task.Artifact.Filename);
        var combinedTotal = progressTotal > 0 ? progressTotal : total;
        ReportPendingUploadProgress(progressOffset + current - 1, combinedTotal, 0);

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
                    ReportPendingUploadProgress(progressOffset + current - 1, combinedTotal, p);
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
                    await RecordUploadedArtifactHostingStateAsync(task, result.Data, cancellationToken, deferPersistenceAndRefresh);
                    return true;
                }

                task.Status = UploadStatus.Failed;
                task.ErrorMessage = result.FirstError ?? GetLocalizedString("Tools.PublisherStudio.Publish.UploadFailedShort", "Upload failed");
                UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.ArtifactUploadFailedFormat", "Failed to upload {0}: {1}", task.Artifact.Filename, result.FirstError);
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
            task.ErrorMessage = GetLocalizedString("Tools.PublisherStudio.Publish.UploadCanceledError", "Upload canceled");
            throw;
        }
        catch (Exception ex)
        {
            task.Status = UploadStatus.Failed;
            task.ErrorMessage = ex.Message;
            UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.ErrorUploadingArtifactFormat", "Error uploading {0}: {1}", task.Artifact.Filename, ex.Message);
            logger.LogError(ex, "Error uploading artifact {Filename}", task.Artifact.Filename);
            return false;
        }
    }

    private async Task SaveHostingStateAsync(string catalogFileId, string catalogUrl, long catalogFileSize = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(project.ProjectPath))
            return;

        _currentHostingState = GetOrCreateHostingState(SelectedHostingProvider?.ProviderId ?? HostingConstants.UnknownProviderId);

        // Update or add catalog entry using the active catalog ID
        var catalogId = ActiveCatalog?.Id ?? "default";
        var catalogEntry = _currentHostingState.Catalogs.FirstOrDefault(c => c.CatalogId == catalogId);
        if (catalogEntry == null)
        {
            catalogEntry = new CatalogHostingInfo { CatalogId = catalogId };
            _currentHostingState.Catalogs.Add(catalogEntry);
        }

        var previousFileId = catalogEntry.FileId;
        catalogEntry.FileId = catalogFileId;
        catalogEntry.Url = catalogUrl;
        catalogEntry.FileSize = catalogFileSize;
        catalogEntry.FileName = ActiveCatalog?.FileName ?? $"catalog-{catalogId}.json";
        catalogEntry.CatalogName = ActiveCatalog?.Name ?? catalogId;
        catalogEntry.LastUpdated = DateTime.UtcNow;

        _currentHostingState.LastPublished = DateTime.UtcNow;

        var result = await SaveAllHostingStatesAsync(cancellationToken);
        if (result.Success)
        {
            HasPreviouslyPublished = true;
            logger.LogInformation("Saved hosting state");
        }

        if (!string.IsNullOrWhiteSpace(previousFileId))
        {
            await DeleteOrphanedRemoteFileAsync(previousFileId, catalogFileId, cancellationToken);
        }

        RefreshHostedAssets();
    }

    private async Task DeleteOrphanedRemoteFileAsync(string? previousFileId, string currentFileId, CancellationToken cancellationToken)
    {
        if (SelectedHostingProvider == null ||
            string.IsNullOrWhiteSpace(previousFileId) ||
            string.Equals(previousFileId, currentFileId, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var result = await SelectedHostingProvider.DeleteFileAsync(previousFileId, cancellationToken);
            if (result.Success)
            {
                logger.LogInformation("Deleted orphaned remote file {FileId} after re-upload under a new name", previousFileId);
            }
            else
            {
                logger.LogWarning("Failed to delete orphaned remote file {FileId}: {Error}", previousFileId, result.FirstError);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Orphaned remote file cleanup was canceled for {FileId}", previousFileId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete orphaned remote file {FileId}", previousFileId);
        }
    }

    private async Task EnsureHostingStatesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_hostingStates.Count > 0 || string.IsNullOrEmpty(project.ProjectPath))
        {
            return;
        }

        var loadResult = hostingStateManager != null
            ? await hostingStateManager.LoadStatesAsync(project.ProjectPath, cancellationToken)
            : null;
        if (loadResult?.Success == true && loadResult.Data != null)
        {
            foreach (var (providerId, state) in loadResult.Data.States)
            {
                _hostingStates[providerId] = state;
            }
        }
    }

    /// <summary>
    /// Best-effort deletion of the pre-rename remote catalog files.
    /// Without this, the next cloud scan would resurrect the old catalog file as a ghost entry.
    /// Entries whose remote file was deleted are reset to pending so the next publish uploads fresh;
    /// entries that could not be reached keep their URL and are cleaned by orphan deletion on publish.
    /// </summary>
    /// <param name="staleRemotes">The provider, entry, and pre-rename remote file ID per renamed catalog.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DeleteRenamedCatalogRemotesAsync(
        List<(string ProviderId, CatalogHostingInfo Entry, string OldFileId)> staleRemotes,
        CancellationToken cancellationToken)
    {
        var clearedAny = false;
        foreach (var (providerId, entry, oldFileId) in staleRemotes)
        {
            var provider = HostingProviders.FirstOrDefault(p =>
                string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            if (provider == null || !provider.IsAuthenticated)
            {
                continue;
            }

            try
            {
                var result = await provider.DeleteFileAsync(oldFileId, cancellationToken);
                if (result.Success)
                {
                    logger.LogInformation("Deleted pre-rename remote catalog {FileId} from {Provider}", oldFileId, providerId);
                    entry.FileId = string.Empty;
                    entry.Url = string.Empty;
                    entry.FileSize = 0;
                    clearedAny = true;
                }
                else
                {
                    logger.LogWarning("Failed to delete pre-rename remote catalog {FileId} from {Provider}: {Error}", oldFileId, providerId, result.FirstError);
                }
            }
            catch (OperationCanceledException ex)
            {
                logger.LogInformation(ex, "Pre-rename remote catalog cleanup was canceled for {FileId}", oldFileId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete pre-rename remote catalog {FileId} from {Provider}", oldFileId, providerId);
            }
        }

        if (clearedAny)
        {
            await SaveAllHostingStatesAsync(cancellationToken);
        }
    }

    private async Task SaveAuthTokenAsync()
    {
        if (string.IsNullOrEmpty(project?.ProjectPath) || SelectedHostingProvider == null)
        {
            return;
        }

        _currentHostingState = GetOrCreateHostingState(SelectedHostingProvider.ProviderId);
        _currentHostingState.AuthToken = null;

        string? tokenToStore = null;
        if (SelectedHostingProvider.ProviderId == HostingConstants.GitHub)
        {
            tokenToStore = GitHubPersonalAccessToken;
        }
        else if (SelectedHostingProvider.ProviderId == HostingConstants.Dropbox)
        {
            tokenToStore = (SelectedHostingProvider as DropboxHostingProvider)?.ExportCredentialPayload() ?? DropboxAccessToken;
        }

        if (credentialStore != null && !string.IsNullOrEmpty(tokenToStore))
        {
            await credentialStore.SaveCredentialAsync(SelectedHostingProvider.ProviderId, tokenToStore);
        }

        await SaveAllHostingStatesAsync(CancellationToken.None);
    }

    private async Task RestoreAuthenticationAsync()
    {
        if (_isRestoringAuthentication)
        {
            return;
        }

        _isRestoringAuthentication = true;
        try
        {
            await RestoreAuthenticationCoreAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore authentication");
        }
        finally
        {
            _isRestoringAuthentication = false;
        }
    }

    private async Task RestoreAuthenticationCoreAsync()
    {
        var persistedProvider = !string.IsNullOrEmpty(_currentHostingState?.ProviderId)
            ? HostingProviders.FirstOrDefault(p => p.ProviderId == _currentHostingState.ProviderId)
            : null;

        var provider = persistedProvider
            ?? SelectedHostingProvider
            ?? HostingProviders.FirstOrDefault();

        if (provider == null)
        {
            return;
        }

        SelectedHostingProvider = provider;

        // Google Drive persists OAuth tokens under the broker user key and client
        // credentials under a dedicated key, never under the provider ID, so its
        // restore path does not depend on a provider-ID token.
        if (provider.ProviderId == HostingConstants.GoogleDrive)
        {
            await TryRestoreProviderAuthenticationAsync(provider, string.Empty);
            return;
        }

        var token = await RetrieveOrMigrateTokenAsync(provider);
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        await TryRestoreProviderAuthenticationAsync(provider, token);
    }

    private async Task<string?> RetrieveOrMigrateTokenAsync(IHostingProvider provider)
    {
        string? token = null;
        if (credentialStore != null)
        {
            token = await credentialStore.GetCredentialAsync(provider.ProviderId, CancellationToken.None);
        }

        // Migrate legacy token if found in hosting state
        if (string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(_currentHostingState?.AuthToken))
        {
            token = _currentHostingState.AuthToken;
            if (credentialStore != null)
            {
                await credentialStore.SaveCredentialAsync(provider.ProviderId, token, CancellationToken.None);
            }

            _currentHostingState.AuthToken = null;
            await SaveAllHostingStatesAsync(CancellationToken.None);
        }

        return token;
    }

    private async Task TryRestoreProviderAuthenticationAsync(IHostingProvider provider, string token)
    {
        try
        {
            if (provider.ProviderId == HostingConstants.GitHub)
            {
                GitHubPersonalAccessToken = token;
                if (provider is GitHubHostingProvider githubProvider)
                {
                    var result = await githubProvider.AuthenticateWithTokenAsync(token, CancellationToken.None);
                    if (result.Success)
                    {
                        AuthenticationStatusMessage = GetLocalizedString(RestoredConnectionTitleKey, RestoredConnectionDefaultMessage);
                        logger.LogInformation("Restored GitHub authentication from secure credential store");
                    }
                }
            }
            else if (provider.ProviderId == HostingConstants.Dropbox)
            {
                if (provider is DropboxHostingProvider dropboxProvider)
                {
                    await RestoreDropboxAuthenticationAsync(dropboxProvider, token);
                }
                else if (DropboxOAuthService.TryParseCredential(token, out var parsedCredential) && parsedCredential != null)
                {
                    // Custom provider implementations: surface the stored OAuth access token without verification.
                    DropboxAppKey = parsedCredential.AppKey;
                    DropboxAccessToken = parsedCredential.AccessToken;
                }
                else
                {
                    DropboxAccessToken = token;
                }
            }
            else if (provider.ProviderId == HostingConstants.GoogleDrive)
            {
                await RestoreGoogleDriveAuthenticationAsync(provider);
            }

            NotifyAuthenticationPropertiesChanged();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore authentication");
        }
    }

    private async Task RestoreDropboxAuthenticationAsync(DropboxHostingProvider dropboxProvider, string token)
    {
        if (DropboxOAuthService.TryParseCredential(token, out var credential) && credential != null)
        {
            DropboxAppKey = credential.AppKey;
            DropboxAccessToken = credential.AccessToken;
            dropboxProvider.SetOAuthCredentials(credential);
            var result = await dropboxProvider.AuthenticateAsync(CancellationToken.None);
            if (result.Success)
            {
                await PersistCurrentDropboxCredentialAsync();
                AuthenticationStatusMessage = GetLocalizedString(RestoredConnectionTitleKey, RestoredConnectionDefaultMessage);
                logger.LogInformation("Restored Dropbox OAuth session from secure credential store");
            }

            return;
        }

        DropboxAccessToken = token;
        var legacyResult = await dropboxProvider.AuthenticateWithTokenAsync(token, CancellationToken.None);
        if (legacyResult.Success)
        {
            AuthenticationStatusMessage = GetLocalizedString(RestoredConnectionTitleKey, RestoredConnectionDefaultMessage);
            logger.LogInformation("Restored Dropbox authentication from secure credential store");
        }
    }

    private async Task PersistGoogleDriveClientCredentialsAsync()
    {
        if (credentialStore == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(GoogleClientId) || string.IsNullOrWhiteSpace(GoogleClientSecret))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new GoogleDriveClientCredentials(GoogleClientId.Trim(), GoogleClientSecret.Trim()));
        await credentialStore.SaveCredentialAsync(HostingConstants.GoogleDriveClientCredentialKey, payload, CancellationToken.None);
    }

    private async Task RestoreGoogleDriveAuthenticationAsync(IHostingProvider provider)
    {
        if (credentialStore == null || provider is not GoogleDriveHostingProvider gdrive)
        {
            return;
        }

        var clientJson = await credentialStore.GetCredentialAsync(HostingConstants.GoogleDriveClientCredentialKey, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(clientJson))
        {
            return;
        }

        GoogleDriveClientCredentials? clientCredentials;
        try
        {
            clientCredentials = JsonSerializer.Deserialize<GoogleDriveClientCredentials>(clientJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Stored Google Drive client credentials are corrupted and will be ignored.");
            return;
        }

        if (clientCredentials == null ||
            string.IsNullOrWhiteSpace(clientCredentials.ClientId) ||
            string.IsNullOrWhiteSpace(clientCredentials.ClientSecret))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(GoogleClientId))
        {
            GoogleClientId = clientCredentials.ClientId;
        }

        if (string.IsNullOrWhiteSpace(GoogleClientSecret))
        {
            GoogleClientSecret = clientCredentials.ClientSecret;
        }

        gdrive.CustomClientId = clientCredentials.ClientId;
        gdrive.CustomClientSecret = clientCredentials.ClientSecret;

        // Only attempt a silent restore when OAuth tokens were persisted.
        // Without tokens, AuthenticateAsync would pop the browser during startup.
        var storedToken = await credentialStore.GetCredentialAsync(
            CredentialStoreDataStore.DefaultKeyPrefix + CredentialStoreDataStore.BrokerUserKey,
            CancellationToken.None);
        if (string.IsNullOrWhiteSpace(storedToken))
        {
            return;
        }

        var result = await gdrive.AuthenticateAsync(CancellationToken.None);
        if (result.Success)
        {
            AuthenticationStatusMessage = GetLocalizedString(RestoredConnectionTitleKey, RestoredConnectionDefaultMessage);
            logger.LogInformation("Restored Google Drive authentication from secure credential store");
        }
    }

    private async Task PersistCurrentDropboxCredentialAsync()
    {
        if (credentialStore == null || SelectedHostingProvider is not DropboxHostingProvider dropboxProvider)
        {
            return;
        }

        if (!dropboxProvider.HasOAuthCredentials)
        {
            return;
        }

        var payload = dropboxProvider.ExportCredentialPayload();
        if (string.IsNullOrEmpty(payload))
        {
            return;
        }

        try
        {
            await credentialStore.SaveCredentialAsync(dropboxProvider.ProviderId, payload, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Failed to persist the Dropbox OAuth credential; the session will not survive restarts.");
        }
    }

    private void NotifyAuthenticationPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsProviderAuthenticated));
        OnPropertyChanged(nameof(NeedsAuthentication));
        OnPropertyChanged(nameof(ShowGitHubPatInput));
        OnPropertyChanged(nameof(ShowGoogleOAuthButton));
        OnPropertyChanged(nameof(ShowDropboxTokenInput));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(TargetDestinationDescription));
        AuthenticationChangedCallback?.Invoke();
    }

    /// <summary>
    /// Generates the provider definition JSON.
    /// </summary>
    /// <returns><c>true</c> when fresh definition JSON was generated; otherwise <c>false</c>.</returns>
    [RelayCommand]
    private async Task<bool> GenerateProviderDefinitionAsync()
    {
        if (_currentHostingState == null || _currentHostingState.Catalogs.Count == 0)
        {
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.NoCatalogsPublishedYet", "No catalogs have been published yet");
            return false;
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
                UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.NoCatalogUrlsForDefinition", "No catalog URLs available for definition");
                return false;
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
                return true;
            }
            else
            {
                logger.LogError("Failed to generate provider definition: {Error}", result.FirstError);
                UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.GenerateDefinitionFailedFormat", "Failed to generate definition: {0}", result.FirstError);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating provider definition");
            UploadStatusMessage = FormatLocalizedString(PublishErrorFormatKey, PublishErrorFormatDefault, ex.Message);
            return false;
        }
    }

    private async Task<OperationResult<HostingUploadResult>?> ValidateProviderDefinitionPreconditionsAsync(bool suppressNotifications = false)
    {
        if (SelectedHostingProvider == null)
        {
            UploadStatusMessage = PleaseSelectHostingProviderMessage;
            if (!suppressNotifications)
            {
                notificationService?.ShowWarning(
                    GetLocalizedString("Tools.PublisherStudio.Publish.NoProviderTitle", "Provider Required"),
                    PleaseSelectHostingProviderMessage);
            }

            return OperationResult<HostingUploadResult>.CreateFailure(PleaseSelectHostingProviderMessage);
        }

        var definitionGenerated = await GenerateProviderDefinitionAsync();
        if (!definitionGenerated || string.IsNullOrWhiteSpace(ProviderDefinitionJson))
        {
            if (!definitionGenerated)
            {
                NotifyDefinitionStale();
            }

            var msg = !string.IsNullOrWhiteSpace(UploadStatusMessage)
                ? UploadStatusMessage
                : GetLocalizedString("Tools.PublisherStudio.Publish.NoCatalogsPublishedPublishFirst", "No catalogs have been published yet. Please publish a catalog first.");
            if (!suppressNotifications)
            {
                notificationService?.ShowWarning(
                    GetLocalizedString("Tools.PublisherStudio.Publish.DefinitionFailedTitle", "Provider Definition Required"),
                    msg);
            }

            return OperationResult<HostingUploadResult>.CreateFailure(msg);
        }

        return null;
    }

    private async Task HandleProviderDefinitionUploadSuccessAsync(HostingUploadResult uploadResult, bool suppressNotifications, CancellationToken ct)
    {
        ProviderDefinitionUrl = uploadResult.DirectDownloadUrl;
        if (!string.IsNullOrEmpty(project.ProjectPath) && SelectedHostingProvider != null)
        {
            _currentHostingState = GetOrCreateHostingState(SelectedHostingProvider.ProviderId);
            var previousDefinitionFileId = _currentHostingState.Definition?.FileId;
            _currentHostingState.Definition = new HostedFileInfo
            {
                FileId = uploadResult.FileId,
                Url = uploadResult.DirectDownloadUrl,
                FileSize = uploadResult.FileSize,
                LastUpdated = DateTime.UtcNow,
            };
            await SaveAllHostingStatesAsync(ct);

            if (!string.IsNullOrWhiteSpace(previousDefinitionFileId))
            {
                await DeleteOrphanedRemoteFileAsync(previousDefinitionFileId, uploadResult.FileId, ct);
            }
        }

        GenerateSubscriptionUrl(); // Regenerate based on new definition URL
        RefreshUploadHierarchy();
        RefreshHostedAssets();
        NotifyDefinitionUploaded();
        UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.ProviderDefinitionUploaded", "Provider definition uploaded successfully.");
        logger.LogInformation("Uploaded provider definition to {Url}", ProviderDefinitionUrl);
        if (!suppressNotifications)
        {
            notificationService?.ShowSuccess(
                GetLocalizedString(PublishSuccessTitleKey, SuccessLiteral),
                GetLocalizedString("Tools.PublisherStudio.Publish.ProviderDefinitionUploadedMessage", "Provider definition uploaded successfully."),
                autoDismissMs: 4000);
        }
    }

    private async Task<OperationResult<HostingUploadResult>> UploadProviderDefinitionCoreAsync(
        CancellationToken cancellationToken,
        bool manageUploadingState,
        bool suppressNotifications = false)
    {
        var preconditionResult = await ValidateProviderDefinitionPreconditionsAsync(suppressNotifications);
        if (preconditionResult != null || SelectedHostingProvider == null)
        {
            return preconditionResult ?? OperationResult<HostingUploadResult>.CreateFailure(PleaseSelectHostingProviderMessage);
        }

        if (IsScanningStorage)
        {
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.ScanInProgress", "Storage scan in progress. Please try again shortly.");
            return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
        }

        try
        {
            if (manageUploadingState)
            {
                IsUploading = true;
            }

            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.UploadingProviderDefinition", "Uploading provider definition...");

            var fileName = project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName;
            var existingDefFileId = _currentHostingState?.Definition?.FileId;

            // Upload or update as a file
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ProviderDefinitionJson));
            var result = (!string.IsNullOrEmpty(existingDefFileId) && SelectedHostingProvider.SupportsUpdate)
                ? await SelectedHostingProvider.UpdateFileAsync(existingDefFileId, stream, fileName, cancellationToken: cancellationToken)
                : await SelectedHostingProvider.UploadFileAsync(stream, fileName, cancellationToken: cancellationToken);

            if (result.Success && result.Data != null)
            {
                await HandleProviderDefinitionUploadSuccessAsync(result.Data, suppressNotifications, cancellationToken);
                return result;
            }

            UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.UploadFailedFormat", "Upload failed: {0}", result.FirstError);
            NotifyDefinitionStale();
            if (!suppressNotifications)
            {
                notificationService?.ShowError(
                    GetLocalizedString(PublishFailedTitleKey, UploadFailedDefaultMessage),
                    result.FirstError ?? GetLocalizedString("Tools.PublisherStudio.Publish.UploadDefinitionFailedMessage", "Failed to upload provider definition."));
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.ErrorUploadingDefinitionFormat", "Error uploading definition: {0}", ex.Message);
            logger.LogError(ex, "Error uploading provider definition");
            NotifyDefinitionStale();
            if (!suppressNotifications)
            {
                notificationService?.ShowError(
                    GetLocalizedString(PublishFailedTitleKey, UploadFailedDefaultMessage),
                    ex.Message);
            }

            return OperationResult<HostingUploadResult>.CreateFailure($"Error uploading definition: {ex.Message}");
        }
        finally
        {
            if (manageUploadingState)
            {
                IsUploading = false;
            }
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
            SubscriptionUrl = GetLocalizedString("Tools.PublisherStudio.Publish.NoSubscriptionUrlPlaceholder", "Please publish to generate subscription URL");
            logger.LogWarning("Cannot generate subscription URL: definition URL not available");
        }
    }

    private async Task CopyToClipboardAsync(string text, string successTitle, string successMessage)
    {
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var clipboard = (mainWindow != null ? Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.Clipboard : null) ?? mainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
                notificationService?.ShowSuccess(successTitle, successMessage, autoDismissMs: 3000);
                logger.LogInformation("Copied text to clipboard");
            }
            else
            {
                notificationService?.ShowWarning(
                    GetLocalizedString(CommonNotificationWarningKey, WarningLiteral),
                    GetLocalizedString("Tools.PublisherStudio.Publish.ClipboardUnavailable", "Clipboard is not available."));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy to clipboard");
            notificationService?.ShowError(
                GetLocalizedString("Common.Notification.Error", "Error"),
                FormatLocalizedString("Tools.PublisherStudio.Publish.CopyToClipboardFailedFormat", "Failed to copy to clipboard: {0}", ex.Message));
        }
    }

    /// <summary>
    /// Copies the subscription URL to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopySubscriptionUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(SubscriptionUrl) || string.IsNullOrWhiteSpace(ProviderDefinitionUrl))
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.NoSubscriptionUrlTitle", "Subscription Link Unavailable"),
                GetLocalizedString("Tools.PublisherStudio.Publish.NoSubscriptionUrlMessage", "Please publish your catalogs and provider definition first before copying the subscription link."));
            return;
        }

        await CopyToClipboardAsync(
            SubscriptionUrl,
            GetLocalizedString(CommonNotificationSuccessKey, SuccessLiteral),
            GetLocalizedString(CopiedToClipboardKey, "Subscription link copied to clipboard!"));
    }

    /// <summary>
    /// Copies the Dropbox OAuth redirect URI so users can register it exactly in their Dropbox app console.
    /// </summary>
    [RelayCommand]
    private async Task CopyDropboxRedirectUriAsync()
    {
        await CopyToClipboardAsync(
            DropboxRedirectUri,
            GetLocalizedString(CommonNotificationSuccessKey, SuccessLiteral),
            GetLocalizedString("Tools.PublisherStudio.Hosting.RedirectUriCopied", "Redirect URI copied. Paste it into your Dropbox app's Redirect URIs list."));
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
            notificationService?.ShowWarning(
                GetLocalizedString(CommonNotificationWarningKey, WarningLiteral),
                GetLocalizedString("Tools.PublisherStudio.Publish.CatalogJsonGenerationFailed", "Catalog JSON could not be generated."));
            return;
        }

        await CopyToClipboardAsync(
            CatalogJson,
            GetLocalizedString(CommonNotificationSuccessKey, SuccessLiteral),
            GetLocalizedString("Tools.PublisherStudio.Publish.CatalogJsonCopiedMessage", "Catalog JSON copied to clipboard!"));
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
            notificationService?.ShowWarning(
                GetLocalizedString(CommonNotificationWarningKey, WarningLiteral),
                GetLocalizedString("Tools.PublisherStudio.Publish.DefinitionJsonGenerationFailed", "Provider definition JSON could not be generated."));
            return;
        }

        await CopyToClipboardAsync(
            ProviderDefinitionJson,
            GetLocalizedString(CommonNotificationSuccessKey, SuccessLiteral),
            GetLocalizedString("Tools.PublisherStudio.Publish.ProviderDefinitionJsonCopiedMessage", "Provider definition JSON copied to clipboard!"));
    }

    /// <summary>
    /// Copies the provider definition URL to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyProviderDefinitionUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(ProviderDefinitionUrl))
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.DefinitionFailedTitle", "Definition Link Unavailable"),
                GetLocalizedString("Tools.PublisherStudio.Publish.NoDefinitionUrlMessage", "Please upload the provider definition first before copying its URL."));
            return;
        }

        await CopyToClipboardAsync(
            ProviderDefinitionUrl,
            GetLocalizedString(CommonNotificationSuccessKey, SuccessLiteral),
            GetLocalizedString(CopiedToClipboardKey, "Provider definition URL copied to clipboard!"));
    }

    /// <summary>
    /// Copies the catalog URL to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyCatalogUrlAsync()
    {
        if (string.IsNullOrEmpty(CatalogUrl))
        {
            notificationService?.ShowWarning(
                GetLocalizedString(CommonNotificationWarningKey, WarningLiteral),
                GetLocalizedString("Tools.PublisherStudio.Publish.NoCatalogUrlMessage", "Please publish this catalog first before copying its URL."));
            return;
        }

        await CopyToClipboardAsync(
            CatalogUrl,
            GetLocalizedString(CommonNotificationSuccessKey, SuccessLiteral),
            GetLocalizedString(CopiedToClipboardKey, "Catalog URL copied to clipboard!"));
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
                status = new CatalogPublishStatus(catalog, localizationService);
                CatalogStatuses.Add(status);
            }

            // Check if published; reset stale state when the catalog is no longer hosted
            var hostingInfo = _currentHostingState?.Catalogs
                .FirstOrDefault(c => c.CatalogId == catalog.Id);
            var isHosted = hostingInfo != null && !string.IsNullOrWhiteSpace(hostingInfo.Url);

            status.IsPublished = isHosted;
            status.PublishedUrl = isHosted ? hostingInfo!.Url : null;
            status.LastPublished = isHosted ? hostingInfo!.LastUpdated : null;
        }

        var projectCatalogIds = new HashSet<string>(project.Catalogs.Select(c => c.Id));
        for (var i = CatalogStatuses.Count - 1; i >= 0; i--)
        {
            if (!projectCatalogIds.Contains(CatalogStatuses[i].Catalog.Id))
            {
                CatalogStatuses[i].Dispose();
                CatalogStatuses.RemoveAt(i);
            }
        }

        SubscribeCatalogStatusChanges();
        OnPropertyChanged(nameof(CatalogStatusesCountText));
        OnPropertyChanged(nameof(AnyCatalogNeedsPublish));
    }

    private void SubscribeCatalogStatusChanges()
    {
        foreach (var status in CatalogStatuses)
        {
            status.PropertyChanged -= OnCatalogStatusPropertyChanged;
            status.PropertyChanged += OnCatalogStatusPropertyChanged;
        }
    }

    private void OnCatalogStatusPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CatalogPublishStatus.NeedsPublish) or nameof(CatalogPublishStatus.IsPublished) or nameof(CatalogPublishStatus.HasChanges))
        {
            OnPropertyChanged(nameof(AnyCatalogNeedsPublish));
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
        var (acquired, cts) = await TryBeginPublishAsync().ConfigureAwait(false);
        if (!acquired || cts == null)
        {
            notificationService?.ShowWarning(
                GetLocalizedString(PublishWarningKey, PublishWarningDefaultMessage),
                UploadAlreadyInProgressMessage);
            return;
        }

        // Set as active catalog temporarily
        var previousActive = ActiveCatalog;
        ActiveCatalog = catalog;
        using var progressToast = BeginUploadProgressToast(
            GetLocalizedString("Tools.PublisherStudio.Publish.PublishStartedTitle", "Publishing"),
            FormatLocalizedString(
                "Tools.PublisherStudio.Publish.PublishCatalogStartedFormat",
                "Publishing catalog '{0}'...",
                catalog.Name));

        var cancellationToken = cts.Token;

        try
        {
            var uploadResult = await UploadCatalogCoreAsync(cancellationToken, manageUploadingState: false, suppressNotifications: true);
            if (!uploadResult.Success && cancellationToken.IsCancellationRequested)
            {
                UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.UploadCanceled", "Upload canceled.");
            }
            else if (uploadResult.Success)
            {
                // Publish status is updated centrally in CompletePublishSuccessAsync.
                notificationService?.ShowSuccess(
                    GetLocalizedString(PublishSuccessTitleKey, "Published"),
                    FormatLocalizedString(
                        "Tools.PublisherStudio.Publish.CatalogPublishedFormat",
                        "Catalog '{0}' published successfully.",
                        catalog.Name),
                    autoDismissMs: 4000);
            }
            else
            {
                notificationService?.ShowError(
                    GetLocalizedString(PublishFailedTitleKey, "Publish Failed"),
                    uploadResult.FirstError ?? GetLocalizedString("Tools.PublisherStudio.Publish.CatalogPublishFailed", "Failed to publish catalog."));
            }
        }
        finally
        {
            // Restore previous active catalog
            ActiveCatalog = previousActive;
            EndPublish(cts);
        }
    }

    private async Task<bool> ValidatePublishAllPreconditionsAsync()
    {
        if (SelectedHostingProvider == null)
        {
            UploadStatusMessage = PleaseSelectHostingProviderMessage;
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.NoProviderTitle", "Provider Required"),
                PleaseSelectHostingProviderMessage);
            return false;
        }

        if (SelectedHostingProvider.RequiresAuthentication && !SelectedHostingProvider.IsAuthenticated)
        {
            var authOk = await EnsureProviderAuthenticatedAsync(CancellationToken.None);
            if (!authOk)
            {
                notificationService?.ShowWarning(
                    GetLocalizedString("Tools.PublisherStudio.Publish.AuthError", "Authentication Required"),
                    UploadStatusMessage);
                return false;
            }
        }

        if (HasIncompatibleArtifactsForActiveCatalog)
        {
            var warningMsg = FormatLocalizedString("Tools.PublisherStudio.Publish.IncompatibleArtifactsActiveCatalogFormat", "{0} only hosts catalog metadata (JSON). The active catalog '{1}' has {2} local file(s) pending upload. Either provide direct CDN URLs for those files, or switch to Google Drive or Dropbox to host binary archives.", SelectedHostingProvider.DisplayName, ActiveCatalog?.Name, ActiveCatalogPendingArtifactsCount);
            UploadStatusMessage = warningMsg;
            notificationService?.ShowError(GetLocalizedString(IncompatibleProviderTitleKey, IncompatibleProviderDefaultMessage), warningMsg);
            return false;
        }

        await ValidateCatalogAsync();
        if (!IsValid)
        {
            UploadStatusMessage = string.IsNullOrWhiteSpace(ValidationMessage)
                ? GetLocalizedString("Tools.PublisherStudio.Publish.FixValidationBeforePublish", "Please fix catalog validation errors before publishing.")
                : ValidationMessage;
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.ValidationFailedTitle", "Validation Failed"),
                UploadStatusMessage);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Publishes all catalogs in sequence.
    /// </summary>
    [RelayCommand]
    private async Task PublishAllCatalogsAsync()
    {
        var (acquired, cts) = await TryBeginPublishAsync().ConfigureAwait(false);
        if (!acquired || cts == null)
        {
            return;
        }

        if (!await ValidatePublishAllPreconditionsAsync())
        {
            EndPublish(cts);
            return;
        }

        var cancellationToken = cts.Token;
        PublishCompleted = false;
        using var progressToast = BeginUploadProgressToast(
            GetLocalizedString("Tools.PublisherStudio.Publish.PublishStartedTitle", "Publishing"),
            FormatLocalizedString(
                "Tools.PublisherStudio.Publish.PublishAllStartedFormat",
                "Publishing {0} catalog(s) to {1}...",
                project.Catalogs.Count,
                SelectedHostingProvider?.DisplayName ?? string.Empty));

        try
        {
            var catalogs = project.Catalogs.ToList();
            var totalCatalogs = catalogs.Count;
            var (succeededCount, failedCatalogs) = await PublishCatalogsAsync(catalogs, cancellationToken);

            if (succeededCount > 0)
            {
                await FinalizePublishAllSuccessAsync(succeededCount, totalCatalogs, failedCatalogs, cancellationToken);
            }
            else
            {
                UploadStatusMessage = BuildAllCatalogsFailedMessage(failedCatalogs);
                notificationService?.ShowError(
                    GetLocalizedString(PublishFailedTitleKey, UploadFailedDefaultMessage),
                    UploadStatusMessage);
            }
        }
        catch (OperationCanceledException)
        {
            UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.PublishAllCanceled", "Publishing all catalogs was canceled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish all catalogs");
            UploadStatusMessage = FormatLocalizedString(PublishErrorFormatKey, PublishErrorFormatDefault, ex.Message);
        }
        finally
        {
            EndPublish(cts);
        }
    }

    private bool CanPublishAllCatalogs()
    {
        return !IsUploading;
    }

    private async Task<(int SucceededCount, List<(string Name, string Error)> FailedCatalogs)> PublishCatalogsAsync(
        IReadOnlyList<NamedCatalog> catalogs,
        CancellationToken cancellationToken)
    {
        var succeededCount = 0;
        var failedCatalogs = new List<(string Name, string Error)>();
        for (var i = 0; i < catalogs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var catalog = catalogs[i];
            var (success, error) = await PublishCatalogItemAsync(catalog, i + 1, catalogs.Count, cancellationToken, uploadDefinition: false);
            if (success)
            {
                succeededCount++;
            }
            else
            {
                logger.LogWarning("Catalog publish failed: {CatalogName} - {Error}", catalog.Name, error);
                failedCatalogs.Add((catalog.Name, error ?? GetLocalizedString("Tools.PublisherStudio.Publish.UploadFailedShort", "Upload failed")));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (succeededCount, failedCatalogs);
    }

    private string BuildAllCatalogsFailedMessage(List<(string Name, string Error)> failedCatalogs)
    {
        var distinctErrors = failedCatalogs.Select(f => f.Error).Distinct().ToList();
        var combinedError = distinctErrors.Count == 1
            ? distinctErrors[0]
            : string.Join(Environment.NewLine, failedCatalogs.Select(f => $"{f.Name}: {f.Error}"));
        return string.IsNullOrWhiteSpace(combinedError)
            ? GetLocalizedString("Tools.PublisherStudio.Publish.PublishAllFailed", "Publishing all catalogs failed.")
            : combinedError;
    }

    private string BuildPartialPublishMessage(
        int succeededCount,
        int totalCatalogs,
        List<(string Name, string Error)> failedCatalogs,
        string? definitionError)
    {
        var failedDetails = string.Join("; ", failedCatalogs.Select(f => $"{f.Name}: {f.Error}"));
        return string.IsNullOrWhiteSpace(definitionError)
            ? FormatLocalizedString(
                "Tools.PublisherStudio.Publish.PublishAllPartialFormat",
                "Published {0} of {1} catalog(s). Failed: {2}",
                succeededCount,
                totalCatalogs,
                failedDetails)
            : FormatLocalizedString(
                "Tools.PublisherStudio.Publish.PublishAllPartialWithDefinitionErrorFormat",
                "Published {0} of {1} catalog(s). Failed: {2}. Provider definition failed: {3}",
                succeededCount,
                totalCatalogs,
                failedDetails,
                definitionError);
    }

    private string GetDefinitionError(OperationResult<HostingUploadResult> definitionResult)
    {
        return !string.IsNullOrWhiteSpace(definitionResult.FirstError)
            ? definitionResult.FirstError
            : GetLocalizedString("Tools.PublisherStudio.Publish.UploadDefinitionFailedMessage", "Failed to upload provider definition.");
    }

    private async Task<(bool Success, string? Error)> PublishCatalogItemAsync(
        NamedCatalog catalog,
        int currentCatalog,
        int totalCatalogs,
        CancellationToken cancellationToken,
        bool uploadDefinition = true)
    {
        UploadStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.PublishingCatalogFormat", "Publishing catalog {0}/{1}: {2}", currentCatalog, totalCatalogs, catalog.Name);

        var previousActive = ActiveCatalog;
        ActiveCatalog = catalog;
        try
        {
            var res = await UploadCatalogCoreAsync(
                cancellationToken,
                manageUploadingState: false,
                suppressNotifications: true,
                uploadDefinition: uploadDefinition);
            if (res.Success)
            {
                // Publish status is updated centrally in CompletePublishSuccessAsync.
                return (true, null);
            }

            MarkActiveCatalogStale();
            return (false, res.FirstError);
        }
        finally
        {
            ActiveCatalog = previousActive;
        }
    }

    private async Task FinalizePublishAllSuccessAsync(
        int succeededCount,
        int totalCatalogs,
        List<(string Name, string Error)> failedCatalogs,
        CancellationToken cancellationToken)
    {
        var defResult = await UploadProviderDefinitionCoreAsync(cancellationToken, manageUploadingState: false, suppressNotifications: true);
        var defError = defResult.Success ? null : GetDefinitionError(defResult);

        GenerateSubscriptionUrl();
        RefreshUploadHierarchy();
        RefreshHostedAssets();
        PublishCompleted = true;

        if (failedCatalogs.Count > 0)
        {
            UploadStatusMessage = BuildPartialPublishMessage(succeededCount, totalCatalogs, failedCatalogs, defError);
            NotifyDefinitionStale();
            notificationService?.ShowWarning(
                GetLocalizedString(PublishWarningKey, PublishWarningDefaultMessage),
                UploadStatusMessage,
                NotificationDurations.Long);
        }
        else if (defError != null)
        {
            UploadStatusMessage = FormatLocalizedString(
                "Tools.PublisherStudio.Publish.PublishAllSuccessDefinitionFailedFormat",
                "Successfully published all {0} catalog(s), but provider definition upload failed: {1}",
                totalCatalogs,
                defError);
            NotifyDefinitionStale();
            notificationService?.ShowWarning(
                GetLocalizedString(PublishWarningKey, PublishWarningDefaultMessage),
                UploadStatusMessage);
        }
        else
        {
            UploadStatusMessage = FormatLocalizedString(
                "Tools.PublisherStudio.Publish.PublishAllSuccessFormat",
                "Successfully published all {0} catalogs!",
                totalCatalogs);
            notificationService?.ShowSuccess(
                GetLocalizedString(PublishSuccessTitleKey, SuccessLiteral),
                UploadStatusMessage,
                autoDismissMs: 4000);
        }
    }

    [RelayCommand]
    private async Task CopyCustomUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            notificationService?.ShowWarning(
                GetLocalizedString(CommonNotificationWarningKey, WarningLiteral),
                GetLocalizedString("Tools.PublisherStudio.Publish.NoUrlToCopy", "No URL available to copy."));
            return;
        }

        await CopyToClipboardAsync(
            url,
            GetLocalizedString(CopiedToClipboardKey, "Copied to Clipboard"),
            GetLocalizedString("Tools.PublisherStudio.Publish.DirectDownloadUrlCopied", "Direct download URL copied."));
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
    /// Opens the Google Drive API enablement page in the default web browser.
    /// Uploads fail with 403 accessNotConfigured until the API is enabled for the project.
    /// </summary>
    [RelayCommand]
    private void OpenGoogleDriveApiConsole()
    {
        OpenExternalBrowserUrl(HostingConstants.GoogleDriveApiEnablementUrl);
    }

    /// <summary>
    /// Opens the GitHub Personal Access Token creation page in the default web browser.
    /// </summary>
    [RelayCommand]
    private void OpenGitHubTokenConsole()
    {
        OpenExternalBrowserUrl(HostingConstants.GitHubPersonalAccessTokensUrl);
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
            if (browserLauncher != null)
            {
                browserLauncher(uri.AbsoluteUri);
            }
            else
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }

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

    [MemberNotNullWhen(true, nameof(SelectedHostingProvider))]
    private bool ValidateCanScanCloudStorage()
    {
        if (SelectedHostingProvider == null)
        {
            return false;
        }

        if (!IsProviderAuthenticated)
        {
            StorageScanStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.ConnectProviderFirst", "Please connect to your hosting provider first.");
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.ProviderNotConnected", "Provider Not Connected"),
                GetLocalizedString("Tools.PublisherStudio.Publish.ConnectBeforeScan", "Connect to your hosting provider before scanning storage."));
            return false;
        }

        if (IsUploading || _silentScanCts != null)
        {
            StorageScanStatusMessage = GetLocalizedString(
                "Tools.PublisherStudio.Publish.ScanBlockedByOperation",
                "Please wait for the current upload or sync to finish before scanning.");
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.ScanError", "Scan Error"),
                StorageScanStatusMessage);
            return false;
        }

        return true;
    }

    private async Task ApplyRecoveredHostingStateAsync(HostingState recoveredState, CancellationToken ct)
    {
        MergeCloudHostingState(recoveredState);
        await SaveAllHostingStatesAsync(ct);

        InitializeCatalogStatuses();
        RefreshUploadHierarchy();
        RefreshHostedAssets();
        GenerateSubscriptionUrl();

        var foundCount = (_currentHostingState?.Catalogs.Count ?? 0) +
                         (_currentHostingState?.Artifacts.Count ?? 0) +
                         (_currentHostingState?.Definition != null ? 1 : 0);
        StorageScanStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.SyncCompleteFormat", "Sync complete! Discovered {0} file(s) in {1}.", foundCount, SelectedHostingProvider?.DisplayName);
        notificationService?.ShowSuccess(
            GetLocalizedString("Tools.PublisherStudio.Publish.StorageSynced", "Storage Synced"),
            StorageScanStatusMessage,
            autoDismissMs: 4000);
    }

    /// <summary>
    /// Scans the connected hosting provider for uploaded files and syncs hosting state.
    /// </summary>
    [RelayCommand]
    private async Task ScanCloudStorageAsync()
    {
        if (!ValidateCanScanCloudStorage())
        {
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
        StorageScanStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.ScanningFolderFormat", "Scanning {0} folder for hosted files...", SelectedHostingProvider.DisplayName);

        try
        {
            var result = await SelectedHostingProvider.RecoverHostingStateAsync(ct);
            if (result.Success && result.Data != null)
            {
                await ApplyRecoveredHostingStateAsync(result.Data, ct);
            }
            else
            {
                StorageScanStatusMessage = result.FirstError ?? GetLocalizedString("Tools.PublisherStudio.Publish.NoHostedFilesFound", "No hosted files discovered in cloud storage folder.");
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Cloud storage scan timed out or was canceled");
            StorageScanStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.ScanTimedOut", "Scan timed out or was canceled. Please try again.");
            notificationService?.ShowWarning(GetLocalizedString("Tools.PublisherStudio.Publish.ScanError", "Scan Error"), StorageScanStatusMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scan cloud storage");
            StorageScanStatusMessage = FormatLocalizedString("Tools.PublisherStudio.Publish.ScanErrorFormat", "Scan error: {0}", ex.Message);
            notificationService?.ShowError(GetLocalizedString("Tools.PublisherStudio.Publish.ScanError", "Scan Error"), ex.Message);
        }
        finally
        {
            if (_scanCts?.Token == ct)
            {
                if (_silentScanCts == null)
                {
                    IsScanningStorage = false;
                }

                _scanCts.Dispose();
                _scanCts = null;
            }
        }
    }

    private async Task ScanCloudStorageSilentlyAsync()
    {
        if (IsScanningStorage || IsUploading || SelectedHostingProvider == null || !IsProviderAuthenticated || _silentScanCts != null)
        {
            return;
        }

        _silentScanCts = new CancellationTokenSource();
        var silentCt = _silentScanCts.Token;
        IsScanningStorage = true;

        try
        {
            var result = await SelectedHostingProvider.RecoverHostingStateAsync(silentCt);
            if (result.Success && result.Data != null)
            {
                MergeCloudHostingState(result.Data);
                await SaveAllHostingStatesAsync(silentCt);

                InitializeCatalogStatuses();
                RefreshUploadHierarchy();
                RefreshHostedAssets();
                GenerateSubscriptionUrl();
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Silent cloud state recovery was canceled");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Silent cloud state recovery encountered an issue");
        }
        finally
        {
            if (_scanCts == null)
            {
                IsScanningStorage = false;
            }

            _silentScanCts?.Dispose();
            _silentScanCts = null;
        }
    }

    private void MergeCloudHostingState(HostingState cloudState)
    {
        _currentHostingState ??= GetOrCreateHostingState(SelectedHostingProvider?.ProviderId ?? HostingConstants.UnknownProviderId);

        if (cloudState.Definition != null && !string.IsNullOrEmpty(cloudState.Definition.Url))
        {
            _currentHostingState.Definition = cloudState.Definition;
            ProviderDefinitionUrl = cloudState.Definition.Url;
        }

        if (!string.IsNullOrEmpty(cloudState.FolderUrl))
        {
            _currentHostingState.FolderUrl = cloudState.FolderUrl;
        }

        foreach (var cloudDef in cloudState.Definitions)
        {
            MergeCloudDefinition(cloudDef);
        }

        foreach (var cloudCat in cloudState.Catalogs)
        {
            MergeCloudCatalog(cloudCat);
        }

        foreach (var cloudArt in cloudState.Artifacts)
        {
            MergeCloudArtifact(cloudArt);
        }
    }

    private void MergeCloudDefinition(HostedFileInfo cloudDef)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        var existing = _currentHostingState.Definitions.FirstOrDefault(d =>
            (!string.IsNullOrEmpty(d.FileName) && !string.IsNullOrEmpty(cloudDef.FileName) && string.Equals(d.FileName, cloudDef.FileName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(d.FileId) && !string.IsNullOrEmpty(cloudDef.FileId) && string.Equals(d.FileId, cloudDef.FileId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(d.Url) && !string.IsNullOrEmpty(cloudDef.Url) && string.Equals(d.Url, cloudDef.Url, StringComparison.OrdinalIgnoreCase)));

        if (existing != null)
        {
            if (cloudDef.LastUpdated >= existing.LastUpdated)
            {
                existing.Url = cloudDef.Url;
                existing.FileSize = cloudDef.FileSize;
                existing.LastUpdated = cloudDef.LastUpdated;
                if (!string.IsNullOrEmpty(cloudDef.FileName))
                {
                    existing.FileName = cloudDef.FileName;
                }
            }
        }
        else
        {
            _currentHostingState.Definitions.Add(cloudDef);
        }

        if (_currentHostingState.Definition == null && !string.IsNullOrEmpty(cloudDef.Url))
        {
            _currentHostingState.Definition = cloudDef;
            ProviderDefinitionUrl = cloudDef.Url;
        }
    }

    private void MergeCloudCatalog(CatalogHostingInfo cloudCat)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        var existing = _currentHostingState.Catalogs.FirstOrDefault(c =>
            c.CatalogId == cloudCat.CatalogId ||
            (!string.IsNullOrEmpty(cloudCat.FileName) && c.FileName == cloudCat.FileName) ||
            (!string.IsNullOrEmpty(c.FileId) && !string.IsNullOrEmpty(cloudCat.FileId) && c.FileId == cloudCat.FileId));
        if (existing != null)
        {
            if (cloudCat.LastUpdated < existing.LastUpdated)
            {
                return;
            }

            if (!string.IsNullOrEmpty(cloudCat.Url))
            {
                existing.Url = cloudCat.Url;
            }

            existing.FileSize = cloudCat.FileSize;
            existing.LastUpdated = cloudCat.LastUpdated;
            if (!string.IsNullOrEmpty(cloudCat.FileName) && string.Equals(existing.CatalogId, cloudCat.CatalogId, StringComparison.Ordinal))
            {
                existing.FileName = cloudCat.FileName;
            }
        }
        else if (!string.IsNullOrEmpty(cloudCat.Url))
        {
            _currentHostingState.Catalogs.Add(cloudCat);
        }
    }

    private void MergeCloudArtifact(ArtifactHostingInfo cloudArt)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        var existing = _currentHostingState.Artifacts.FirstOrDefault(a => IsSameArtifact(a, cloudArt.FileName, cloudArt.ContentId, cloudArt.Version));
        if (existing != null)
        {
            if (cloudArt.LastUpdated < existing.LastUpdated)
            {
                return;
            }

            if (!string.IsNullOrEmpty(cloudArt.Url))
            {
                existing.Url = cloudArt.Url;
            }

            existing.FileSize = cloudArt.FileSize;
            existing.LastUpdated = cloudArt.LastUpdated;
        }
        else if (!string.IsNullOrEmpty(cloudArt.Url))
        {
            _currentHostingState.Artifacts.Add(cloudArt);
        }
    }

    /// <summary>
    /// Uploads a single hosted asset row (definition, catalog, or artifact) to the connected provider.
    /// </summary>
    [RelayCommand]
    private async Task UploadHostedAssetAsync(HostedAssetItemViewModel? asset)
    {
        if (asset == null || !asset.CanUpload || asset.IsUploading)
        {
            return;
        }

        if (IsUploading)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.UploadInProgressTitle", "Upload In Progress"),
                GetLocalizedString("Tools.PublisherStudio.Hosting.UploadInProgress", "Another upload is already in progress."));
            return;
        }

        if (SelectedHostingProvider == null || NeedsAuthentication)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.ProviderNotConnected", "Provider Not Connected"),
                GetLocalizedString("Tools.PublisherStudio.Hosting.ConnectBeforeUpload", "Connect to your hosting provider before uploading files."));
            return;
        }

        asset.IsUploading = true;
        try
        {
            switch (asset.AssetKind)
            {
                case HostedAssetKind.Definition:
                    await UploadProviderDefinitionAsync();
                    break;
                case HostedAssetKind.Catalog:
                    await UploadCatalogAssetAsync(asset);
                    break;
                case HostedAssetKind.Artifact:
                    await UploadSingleArtifactAssetAsync(asset);
                    break;
                default:
                    break;
            }
        }
        finally
        {
            asset.IsUploading = false;
        }
    }

    private async Task UploadCatalogAssetAsync(HostedAssetItemViewModel asset)
    {
        var catalog = project.Catalogs.FirstOrDefault(c => c.Id == asset.CatalogId);
        if (catalog == null)
        {
            return;
        }

        await PublishCatalogAsync(catalog);
    }

    private async Task UploadSingleArtifactAssetAsync(HostedAssetItemViewModel asset)
    {
        var provider = SelectedHostingProvider;
        if (provider == null)
        {
            return;
        }

        var located = FindProjectArtifact(asset);
        if (located.Artifact == null || located.ContentId == null || located.Version == null)
        {
            return;
        }

        await ExecuteLocatedArtifactUploadAsync(provider, located.Artifact, located.ContentId, located.Version);
    }

    private (string? ContentId, string? Version) FindArtifactOwner(ReleaseArtifact artifact)
    {
        foreach (var catalog in project.Catalogs)
        {
            var content = catalog.Catalog.Content.FirstOrDefault(c =>
                c.Releases.Any(r => r.Artifacts.Contains(artifact)));
            var release = content?.Releases.FirstOrDefault(r => r.Artifacts.Contains(artifact));
            if (content != null && release != null)
            {
                return (content.Id, release.Version);
            }
        }

        return (null, null);
    }

    private async Task ExecuteLocatedArtifactUploadAsync(
        IHostingProvider provider,
        ReleaseArtifact artifact,
        string contentId,
        string version)
    {
        if (!provider.SupportsArtifactHosting)
        {
            UploadStatusMessage = GetLocalizedString(
                "Tools.PublisherStudio.Publish.ArtifactHostingNotSupported",
                "Provider does not support artifact hosting. Please add URLs manually.");
            notificationService?.ShowError(
                GetLocalizedString(IncompatibleProviderTitleKey, IncompatibleProviderDefaultMessage),
                UploadStatusMessage);
            logger.LogWarning(
                "Blocked single artifact upload of {File} to {Provider}: provider does not support artifact hosting.",
                artifact.Filename,
                provider.DisplayName);
            return;
        }

        if (string.IsNullOrEmpty(artifact.LocalFilePath) ||
            (!File.Exists(artifact.LocalFilePath) && !Directory.Exists(artifact.LocalFilePath)))
        {
            UploadStatusMessage = FormatLocalizedString(
                "Tools.PublisherStudio.Hosting.LocalFileMissingFormat",
                "Local file not found: {0}",
                artifact.LocalFilePath);
            notificationService?.ShowError(
                GetLocalizedString(PublishFailedTitleKey, UploadFailedDefaultMessage),
                UploadStatusMessage);
            return;
        }

        var (acquired, cts) = await TryBeginPublishAsync().ConfigureAwait(false);
        if (!acquired || cts == null)
        {
            notificationService?.ShowWarning(
                GetLocalizedString(PublishWarningKey, PublishWarningDefaultMessage),
                UploadAlreadyInProgressMessage);
            return;
        }

        var cancellationToken = cts.Token;
        using var progressToast = BeginUploadProgressToast(
            GetLocalizedString("Tools.PublisherStudio.Publish.UploadStartedTitle", "Uploading"),
            FormatLocalizedString(
                "Tools.PublisherStudio.Hosting.SingleUploadStartedFormat",
                "Uploading {0}...",
                artifact.Filename));
        try
        {
            var task = new ArtifactUploadTask
            {
                ContentId = contentId,
                Version = version,
                Artifact = artifact,
                Status = UploadStatus.Pending,
                LocalizationService = localizationService,
            };

            if (await ExecuteSingleArtifactUploadAsync(provider, task, 1, 1, cancellationToken))
            {
                MarkCatalogWithContentStale(contentId);
                NotifyDefinitionStale();
                RefreshUploadHierarchy();
                RefreshArtifactStatuses();
                await PersistProjectAfterPublishAsync();
                await PersistCurrentDropboxCredentialAsync();
                NotifyLibraryRefresh();
                notificationService?.ShowSuccess(
                    GetLocalizedString(PublishSuccessTitleKey, "Published"),
                    FormatLocalizedString(
                        "Tools.PublisherStudio.Hosting.SingleUploadSuccessFormat",
                        "{0} uploaded successfully.",
                        artifact.Filename),
                    autoDismissMs: 4000);
            }
            else
            {
                notificationService?.ShowError(
                    GetLocalizedString(PublishFailedTitleKey, UploadFailedDefaultMessage),
                    FormatLocalizedString(
                        "Tools.PublisherStudio.Hosting.SingleUploadFailedFormat",
                        "Failed to upload {0}: {1}",
                        artifact.Filename,
                        task.ErrorMessage));
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Single artifact upload was canceled.");
        }
        finally
        {
            EndPublish(cts);
        }
    }

    private void NotifyLibraryRefresh()
    {
        try
        {
            LibraryRefreshCallback?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Library refresh callback failed after artifact upload");
        }
    }

    private void NotifyDefinitionUploaded()
    {
        try
        {
            DefinitionUploadedCallback?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Definition uploaded callback failed");
        }
    }

    private UploadProgressToastScope BeginUploadProgressToast(string title, string message)
    {
        return new UploadProgressToastScope(this, notificationService, title, message);
    }

    private void NotifyDefinitionStale()
    {
        HasDefinitionChanges = true;
        try
        {
            DefinitionStaleCallback?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Definition stale callback failed");
        }
    }

    private void MarkActiveCatalogStale()
    {
        if (ActiveCatalog == null)
        {
            return;
        }

        var status = CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == ActiveCatalog.Id);
        if (status != null)
        {
            status.HasChanges = true;
        }
    }

    private void MarkCatalogWithContentStale(string contentId)
    {
        var catalog = project.Catalogs.FirstOrDefault(c => c.Catalog.Content.Any(content => content.Id == contentId));
        if (catalog != null)
        {
            var status = CatalogStatuses.FirstOrDefault(s => s.Catalog.Id == catalog.Id);
            if (status != null)
            {
                status.HasChanges = true;
            }
        }
    }

    private (ReleaseArtifact? Artifact, string? ContentId, string? Version) FindProjectArtifact(HostedAssetItemViewModel asset)
    {
        foreach (var catalog in project.Catalogs)
        {
            var found = FindArtifactInCatalog(catalog, asset);
            if (found.Artifact != null)
            {
                return found;
            }
        }

        return (null, null, null);
    }

    private async Task PersistProjectAfterPublishAsync()
    {
        if (SaveProjectCallback == null)
        {
            return;
        }

        try
        {
            await SaveProjectCallback();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist project after publish");
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
                notificationService?.ShowSuccess(GetLocalizedString(CopiedToClipboardKey, "Copied to Clipboard"), GetLocalizedString("Tools.PublisherStudio.Publish.DirectDownloadUrlCopied", "Direct download URL copied."), autoDismissMs: 2500);
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

    /// <summary>
    /// Cancels the currently active upload operation.
    /// </summary>
    [RelayCommand]
    private void CancelUpload()
    {
        var cts = _activeUploadCts ?? _uploadCts;
        try
        {
            if (cts != null && !cts.IsCancellationRequested)
            {
                cts.Cancel();
                UploadStatusMessage = GetLocalizedString("Tools.PublisherStudio.Publish.PublishAllCanceled", "Publishing was canceled.");
            }
        }
        catch (ObjectDisposedException)
        {
            // Upload already completed or was disposed concurrently
        }
    }

    partial void OnInventoryCategoryFilterChanged(string value) => ApplyHostedAssetFilter();

    partial void OnInventorySearchTextChanged(string value) => ApplyHostedAssetFilter();

    /// <summary>
    /// Sets the current category filter for the hosted asset inventory.
    /// </summary>
    /// <param name="filter">The category filter name.</param>
    [RelayCommand]
    private void SetInventoryFilter(string filter)
    {
        InventoryCategoryFilter = filter;
    }

    /// <summary>
    /// Pulls the primary discovered cloud publisher definition into the project.
    /// </summary>
    [RelayCommand]
    private async Task PullPrimaryCloudDefinitionAsync()
    {
        var target = DiscoveredCloudDefinition;
        if (target == null)
        {
            logger.LogWarning("No discovered cloud definition available to pull");
            return;
        }

        await LoadAssetToProjectAsync(target);
    }

    /// <summary>
    /// Navigates to the Publisher Profile tab to create a new profile from scratch.
    /// </summary>
    [RelayCommand]
    private void CreateNewProfile()
    {
        NavigateToTabCallback?.Invoke(0);
    }

    /// <summary>
    /// Loads a cloud definition or catalog asset directly into the current project.
    /// </summary>
    /// <param name="asset">The hosted asset item to load.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task LoadAssetToProjectAsync(HostedAssetItemViewModel? asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.Url))
        {
            return;
        }

        try
        {
            if (asset.IsDefinition)
            {
                await LoadDefinitionToProjectAsync(asset);
            }
            else if (asset.IsCatalog)
            {
                await LoadCatalogToProjectAsync(asset);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load asset {AssetName} into project", asset.Name);
            notificationService?.ShowError(
                GetLocalizedString(LoadFailedTitleKey, LoadFailedDefaultTitle),
                FormatLocalizedString("Tools.PublisherStudio.Hosting.LoadFailedFormat", "Failed to load {0}: {1}", asset.Name, ex.Message));
        }
    }

    private async Task LoadDefinitionToProjectAsync(HostedAssetItemViewModel asset)
    {
        notificationService?.ShowInfo(
            GetLocalizedString("Tools.PublisherStudio.Hosting.LoadingDefinitionTitle", "Loading Definition"),
            FormatLocalizedString("Tools.PublisherStudio.Hosting.LoadingDefinitionFormat", "Loading publisher definition from {0}...", asset.Name));

        var json = await DownloadStringFromUrlAsync(asset.Url);
        if (string.IsNullOrWhiteSpace(json))
        {
            notificationService?.ShowError(
                GetLocalizedString(LoadFailedTitleKey, LoadFailedDefaultTitle),
                GetLocalizedString("Tools.PublisherStudio.Hosting.DownloadEmptyError", "Downloaded definition was empty."));
            return;
        }

        PublisherDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<PublisherDefinition>(json, PublisherJsonOptions.Definition);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize publisher definition JSON from {Url}", asset.Url);
            notificationService?.ShowError(
                GetLocalizedString(LoadFailedTitleKey, LoadFailedDefaultTitle),
                FormatLocalizedString("Tools.PublisherStudio.Hosting.DeserializeDefinitionErrorFormat", "Invalid publisher definition JSON: {0}", ex.Message));
            return;
        }

        if (definition?.Publisher == null)
        {
            notificationService?.ShowError(
                GetLocalizedString(LoadFailedTitleKey, LoadFailedDefaultTitle),
                GetLocalizedString("Tools.PublisherStudio.Hosting.InvalidDefinitionError", "Definition does not contain valid publisher information."));
            return;
        }

        ApplyDefinitionProfile(definition, asset.Name);

        var loadedCatalogsCount = await DownloadAndAttachCatalogsAsync(definition);

        UpdateHostingDefinitionState(asset);

        // Persist project changes
        if (SaveProjectCallback != null)
        {
            await SaveProjectCallback();
        }

        // Refresh studio child view models
        if (ProjectReloadCallback != null)
        {
            await ProjectReloadCallback();
        }

        RefreshHostedAssets();

        notificationService?.ShowSuccess(
            GetLocalizedString("Tools.PublisherStudio.Hosting.LoadedDefinitionSuccessTitle", "Definition Loaded"),
            FormatLocalizedString("Tools.PublisherStudio.Hosting.LoadedDefinitionSuccessFormat", "Successfully loaded publisher definition '{0}' and {1} catalog(s).", definition.Publisher.Name ?? asset.Name, loadedCatalogsCount),
            autoDismissMs: 4000);
    }

    private void ApplyDefinitionProfile(PublisherDefinition definition, string assetName)
    {
        if (project.Catalog != null)
        {
            project.Catalog.Publisher = definition.Publisher;
            if (definition.Referrals != null)
            {
                project.Catalog.Referrals = definition.Referrals;
            }
        }

        project.ProviderDefinitionFileName = assetName;

        if (definition.Tags != null)
        {
            project.Tags = definition.Tags;
        }
    }

    private void UpdateHostingDefinitionState(HostedAssetItemViewModel asset)
    {
        _currentHostingState ??= GetOrCreateHostingState(SelectedHostingProvider?.ProviderId ?? HostingConstants.UnknownProviderId);
        _currentHostingState.Definition = new HostedFileInfo
        {
            Url = asset.Url,
            FileName = asset.Name,
            FileSize = asset.FileSize,
            LastUpdated = asset.LastUpdated != DateTime.MinValue ? asset.LastUpdated : DateTime.UtcNow,
        };
        ProviderDefinitionUrl = asset.Url;
    }

    private async Task<int> DownloadAndAttachCatalogsAsync(PublisherDefinition definition)
    {
        if (definition.Catalogs == null || definition.Catalogs.Count == 0)
        {
            return 0;
        }

        var loadedCatalogsCount = 0;
        foreach (var catRef in definition.Catalogs)
        {
            if (string.IsNullOrWhiteSpace(catRef.Url))
            {
                continue;
            }

            if (await TryLoadReferencedCatalogAsync(catRef))
            {
                loadedCatalogsCount++;
            }
        }

        return loadedCatalogsCount;
    }

    private async Task<bool> TryLoadReferencedCatalogAsync(CatalogEntry catRef)
    {
        try
        {
            var catJson = await DownloadStringFromUrlAsync(catRef.Url);
            if (string.IsNullOrWhiteSpace(catJson))
            {
                return false;
            }

            var pubCat = JsonSerializer.Deserialize<PublisherCatalog>(catJson, PublisherJsonOptions.Definition);
            if (pubCat == null)
            {
                return false;
            }

            var catFileName = Path.GetFileName(new Uri(catRef.Url).LocalPath);
            if (string.IsNullOrEmpty(catFileName) || !catFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                catFileName = $"catalog-{catRef.Id}.json";
            }

            var existingNamedCat = project.Catalogs.FirstOrDefault(c => c.Id == catRef.Id);
            if (existingNamedCat != null)
            {
                existingNamedCat.Catalog = pubCat;
                existingNamedCat.FileName = catFileName;
                existingNamedCat.Name = catRef.Name ?? catRef.Id;
            }
            else
            {
                project.Catalogs.Add(new NamedCatalog
                {
                    Id = catRef.Id,
                    Name = catRef.Name ?? catRef.Id,
                    FileName = catFileName,
                    Catalog = pubCat,
                });
            }

            return true;
        }
        catch (Exception catEx)
        {
            logger.LogWarning(catEx, "Failed to download/load catalog from {Url}", catRef.Url);
            return false;
        }
    }

    private async Task LoadCatalogToProjectAsync(HostedAssetItemViewModel asset)
    {
        notificationService?.ShowInfo(
            GetLocalizedString("Tools.PublisherStudio.Hosting.LoadingCatalogTitle", "Loading Catalog"),
            FormatLocalizedString("Tools.PublisherStudio.Hosting.LoadingCatalogFormat", "Loading catalog from {0}...", asset.Name));

        var json = await DownloadStringFromUrlAsync(asset.Url);
        if (string.IsNullOrWhiteSpace(json))
        {
            notificationService?.ShowError(
                GetLocalizedString(LoadFailedTitleKey, LoadFailedDefaultTitle),
                GetLocalizedString("Tools.PublisherStudio.Hosting.DownloadEmptyError", "Downloaded catalog was empty."));
            return;
        }

        PublisherCatalog? pubCat;
        try
        {
            pubCat = JsonSerializer.Deserialize<PublisherCatalog>(json, PublisherJsonOptions.Definition);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize catalog JSON from {Url}", asset.Url);
            notificationService?.ShowError(
                GetLocalizedString(LoadFailedTitleKey, LoadFailedDefaultTitle),
                FormatLocalizedString("Tools.PublisherStudio.Hosting.DeserializeCatalogErrorFormat", "Invalid catalog JSON: {0}", ex.Message));
            return;
        }

        if (pubCat == null)
        {
            notificationService?.ShowError(
                GetLocalizedString(LoadFailedTitleKey, LoadFailedDefaultTitle),
                GetLocalizedString("Tools.PublisherStudio.Hosting.InvalidCatalogError", "Catalog JSON is missing a valid identifier."));
            return;
        }

        var catFileName = asset.Name;
        if (string.IsNullOrEmpty(catFileName))
        {
            catFileName = HostingConstants.DefaultCatalogFileName;
        }

        var catId = "main";
        if (catFileName.StartsWith("catalog-", StringComparison.OrdinalIgnoreCase) && catFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && catFileName.Length > 13)
        {
            catId = catFileName[8..^5];
        }
        else if (catFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !string.Equals(catFileName, "catalog.json", StringComparison.OrdinalIgnoreCase))
        {
            catId = Path.GetFileNameWithoutExtension(catFileName);
        }

        var catName = char.ToUpperInvariant(catId[0]) + catId[1..];

        var existingNamedCat = project.Catalogs.FirstOrDefault(c => string.Equals(c.Id, catId, StringComparison.OrdinalIgnoreCase) || string.Equals(c.FileName, catFileName, StringComparison.OrdinalIgnoreCase));
        if (existingNamedCat != null)
        {
            existingNamedCat.Catalog = pubCat;
            existingNamedCat.FileName = catFileName;
            catName = existingNamedCat.Name;
        }
        else
        {
            project.Catalogs.Add(new NamedCatalog
            {
                Id = catId,
                Name = catName,
                FileName = catFileName,
                Catalog = pubCat,
            });
        }

        // Update hosting state for this catalog
        _currentHostingState ??= GetOrCreateHostingState(SelectedHostingProvider?.ProviderId ?? HostingConstants.UnknownProviderId);
        MergeCloudCatalog(new CatalogHostingInfo
        {
            CatalogId = catId,
            FileName = catFileName,
            CatalogName = catName,
            Url = asset.Url,
            FileSize = asset.FileSize,
            LastUpdated = asset.LastUpdated != DateTime.MinValue ? asset.LastUpdated : DateTime.UtcNow,
        });

        // Persist project changes
        if (SaveProjectCallback != null)
        {
            await SaveProjectCallback();
        }

        // Refresh studio child view models
        if (ProjectReloadCallback != null)
        {
            await ProjectReloadCallback();
        }

        RefreshHostedAssets();

        notificationService?.ShowSuccess(
            GetLocalizedString("Tools.PublisherStudio.Hosting.LoadedCatalogSuccessTitle", "Catalog Loaded"),
            FormatLocalizedString("Tools.PublisherStudio.Hosting.LoadedCatalogSuccessFormat", "Successfully loaded catalog '{0}' into project.", catName),
            autoDismissMs: 4000);
    }

    /// <summary>
    /// Adds a discovered cloud artifact binary directly into the active catalog release.
    /// </summary>
    /// <param name="asset">The hosted asset item to add.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task AddArtifactToActiveCatalogAsync(HostedAssetItemViewModel? asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.Url))
        {
            return;
        }

        if (ActiveCatalog == null)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Hosting.NoActiveCatalogTitle", "No Active Catalog"),
                GetLocalizedString("Tools.PublisherStudio.Hosting.NoActiveCatalogError", "Please select or create an active catalog first."));
            return;
        }

        var fileName = asset.Name;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var directUrl = asset.Url;

        // Find matching content item, or fallback to first, or create new
        var content = ActiveCatalog.Catalog.Content.FirstOrDefault(c =>
            string.Equals(c.Name, stem, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Id, stem, StringComparison.OrdinalIgnoreCase)) ??
            ActiveCatalog.Catalog.Content.FirstOrDefault();

        if (content == null)
        {
            content = new CatalogContentItem
            {
                Id = stem.ToLowerInvariant().Replace(' ', '-'),
                Name = stem,
                ContentType = ContentType.Mod,
                Description = stem,
                Releases = [],
            };
            ActiveCatalog.Catalog.Content.Add(content);
        }

        var release = content.Releases.FirstOrDefault();
        if (release == null)
        {
            release = new ContentRelease
            {
                Version = "1.0.0",
                ReleaseDate = DateTime.UtcNow,
                Artifacts = [],
            };
            content.Releases.Add(release);
        }

        // Check if artifact already exists in this release
        var existingArt = release.Artifacts.FirstOrDefault(a =>
            string.Equals(a.Filename, fileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.DownloadUrl, directUrl, StringComparison.OrdinalIgnoreCase));

        if (existingArt != null)
        {
            existingArt.DownloadUrl = directUrl;
            if (!string.IsNullOrEmpty(asset.Sha256))
            {
                existingArt.Sha256 = asset.Sha256;
            }

            if (asset.FileSize > 0)
            {
                existingArt.Size = asset.FileSize;
            }
        }
        else
        {
            release.Artifacts.Add(new ReleaseArtifact
            {
                Filename = fileName,
                DownloadUrl = directUrl,
                Size = asset.FileSize,
                Sha256 = asset.Sha256 ?? string.Empty,
            });
        }

        // Mark catalog dirty
        ActiveCatalog.Catalog.LastUpdated = DateTime.UtcNow;
        MarkActiveCatalogStale();

        // Save project
        if (SaveProjectCallback != null)
        {
            await SaveProjectCallback();
        }

        // Refresh studio child view models
        if (ProjectReloadCallback != null)
        {
            await ProjectReloadCallback();
        }

        RefreshHostedAssets();

        notificationService?.ShowSuccess(
            GetLocalizedString("Tools.PublisherStudio.Hosting.ArtifactAddedTitle", "Artifact Added"),
            FormatLocalizedString("Tools.PublisherStudio.Hosting.ArtifactAddedToCatalogFormat", "Added '{0}' to catalog '{1}'.", fileName, ActiveCatalog.Name),
            autoDismissMs: 4000);
    }

    private async Task<string?> DownloadStringFromUrlAsync(string url)
    {
        var directUrl = EnsureDirectDownloadUrl(url);
        if (!NetworkSecurityHelper.IsSafeUrl(directUrl, out var failureReason))
        {
            logger.LogWarning("Refusing to download string from unsafe or disallowed URL {Url}: {Reason}", directUrl, failureReason);
            return null;
        }

        try
        {
            var client = HttpClientOverrideForTesting ?? SharedHttpClient;
            return await CatalogDocumentReader.ReadAsync(
                client,
                directUrl,
                CatalogConstants.MaxCatalogSizeBytes,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download string from {Url}", directUrl);
            return null;
        }
    }

    private async Task SyncLocalSubscriptionMetadataAsync()
    {
        if (subscriptionStore == null || string.IsNullOrWhiteSpace(project.Catalog?.Publisher?.Id))
        {
            return;
        }

        try
        {
            var publisherId = project.Catalog.Publisher.Id;
            var subResult = await subscriptionStore.GetSubscriptionAsync(publisherId, CancellationToken.None);
            if (subResult.Success && subResult.Data != null)
            {
                var sub = subResult.Data;
                var updated = false;
                if (!string.IsNullOrWhiteSpace(project.Catalog.Publisher.AvatarUrl) &&
                    !string.Equals(sub.AvatarUrl, project.Catalog.Publisher.AvatarUrl, StringComparison.OrdinalIgnoreCase))
                {
                    sub.AvatarUrl = project.Catalog.Publisher.AvatarUrl;
                    updated = true;
                }

                if (!string.IsNullOrWhiteSpace(project.Catalog.Publisher.Name) &&
                    !string.Equals(sub.PublisherName, project.Catalog.Publisher.Name, StringComparison.OrdinalIgnoreCase))
                {
                    sub.PublisherName = project.Catalog.Publisher.Name;
                    updated = true;
                }

                if (updated)
                {
                    await subscriptionStore.UpdateSubscriptionAsync(sub, CancellationToken.None);
                    WeakReferenceMessenger.Default.Send(new PublisherSubscriptionsChangedMessage(publisherId));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync local subscription metadata for publisher {PublisherId}", project.Catalog?.Publisher?.Id);
        }
    }
}
