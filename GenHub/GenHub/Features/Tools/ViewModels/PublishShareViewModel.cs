using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Interfaces.Telemetry;
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
    IPublisherSubscriptionStore? subscriptionStore = null,
    ITelemetryService? telemetryService = null,
    IPublisherStudioDialogService? dialogService = null) : ObservableObject, IDisposable
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
    /// A gallery media entry (screenshot or video) that references a local file and must be hosted on publish.
    /// </summary>
    /// <param name="ContentId">The owning content item, used to name the remote file.</param>
    /// <param name="DisplayName">The local file name, used to name the remote file.</param>
    /// <param name="LocalPath">The resolved local file path.</param>
    /// <param name="RemoteFileName">The unique remote file name for this entry.</param>
    /// <param name="ApplyUrl">Replaces the local reference with the hosted URL.</param>
    private sealed record PendingMediaUpload(string ContentId, string DisplayName, string LocalPath, string RemoteFileName, Action<string> ApplyUrl);

    /// <summary>
    /// A gallery media reference that could not be resolved to a local file and will not be uploaded.
    /// </summary>
    /// <param name="ContentId">The owning content item.</param>
    /// <param name="Reference">The unresolvable raw reference.</param>
    private sealed record SkippedMediaReference(string ContentId, string Reference);

    /// <summary>
    /// Google Drive OAuth client credentials persisted in the encrypted credential store.
    /// </summary>
    /// <param name="ClientId">The Google OAuth client ID.</param>
    /// <param name="ClientSecret">The Google OAuth client secret.</param>
    private sealed record GoogleDriveClientCredentials(string ClientId, string ClientSecret);

    /// <summary>
    /// A single catalog reference to a hosted file URL, used to link inventory
    /// rows back to the definition, catalog, content, and release that use them.
    /// </summary>
    /// <param name="CatalogId">The referencing catalog ID.</param>
    /// <param name="CatalogName">The referencing catalog display name.</param>
    /// <param name="ContentId">The referencing content ID, when known.</param>
    /// <param name="ContentName">The referencing content display name, when known.</param>
    /// <param name="ReleaseVersion">The referencing release version, when known.</param>
    /// <param name="Slot">Which field references the URL (artifact, screenshot, video, icon, and so on).</param>
    private sealed record AssetReference(
        string CatalogId,
        string CatalogName,
        string? ContentId,
        string? ContentName,
        string? ReleaseVersion,
        string Slot);

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

    private const string InventorySortByName = "Name";
    private const string InventorySortBySize = "Size";
    private const string SortGlyphAscending = "▲";
    private const string SortGlyphDescending = "▼";

    private const string MediaSlotArtifact = "Artifact";
    private const string MediaSlotScreenshot = "Screenshot";
    private const string MediaSlotVideo = "Video";
    private const string MediaSlotIcon = "Icon";
    private const string MediaSlotBanner = "Banner";
    private const string MediaSlotBackdrop = "Backdrop";
    private const string MediaSlotReleaseImage = "ReleaseImage";
    private const string MediaSlotReleaseVideo = "ReleaseVideo";

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
    private readonly Dictionary<string, List<HostedAssetChildViewModel>> _remotePreviewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _probedMediaSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _probeLock = new();
    private CancellationTokenSource? _probeCts = new();
    private HostingState? _currentHostingState;
    private Dictionary<string, List<AssetReference>>? _linkageIndex;

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
    private int _hostedScreenshotsCount;

    [ObservableProperty]
    private int _hostedVideosCount;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph))]
    [NotifyPropertyChangedFor(nameof(SizeSortGlyph))]
    private string _inventorySortColumn = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph))]
    [NotifyPropertyChangedFor(nameof(SizeSortGlyph))]
    private bool _inventorySortDescending;

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
    /// Gets the sort direction glyph for the file name column header.
    /// </summary>
    public string NameSortGlyph => GetSortGlyph(InventorySortByName);

    /// <summary>
    /// Gets the sort direction glyph for the file size column header.
    /// </summary>
    public string SizeSortGlyph => GetSortGlyph(InventorySortBySize);

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
    /// Gets or sets an optional confirmation override for destructive inventory actions.
    /// Used by unit tests to auto-confirm or reject delete dialogs; when null, the
    /// injected dialog service is used, and deletes are refused when neither is available.
    /// </summary>
    public Func<string, string, Task<bool>>? ConfirmationCallback { get; set; }

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
            var entry = state.Catalogs.FirstOrDefault(c => string.Equals(c.CatalogId, catalogId, StringComparison.OrdinalIgnoreCase));
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

            if (IsCatalogFileIdShared(state, entry.FileId, catalogId))
            {
                logger.LogWarning("Keeping remote catalog file {FileId}: it is shared with another catalog after a filename collision", entry.FileId);
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
        _linkageIndex = BuildUrlReferenceIndex();
        long totalBytes = 0;
        var defCount = 0;
        var catCount = 0;
        var artCount = 0;
        var screenshotCount = 0;
        var videoCount = 0;
        var cdnCount = 0;

        var providerName = SelectedHostingProvider?.DisplayName ?? "Cloud Storage";

        PopulatePublisherDefinitionAsset(providerName, ref totalBytes, ref defCount);
        PopulateCatalogAssets(providerName, ref totalBytes, ref catCount);
        PopulateArtifactAssets(providerName, ref totalBytes, ref artCount, ref cdnCount);
        PopulateMediaAssets(providerName, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
        PopulateCloudScanAssets(providerName, ref totalBytes, ref catCount, ref artCount, ref screenshotCount, ref videoCount);

        foreach (var item in HostedAssets)
        {
            item.PropertyChanged += OnHostedAssetPropertyChanged;
        }

        TotalStorageUsedFormatted = GenHub.Core.Helpers.FileSizeFormatter.Format(totalBytes);
        TotalHostedFilesCount = HostedAssets.Count;
        HostedDefinitionCount = defCount;
        HostedCatalogsCount = catCount;
        HostedArtifactsCount = artCount;
        HostedScreenshotsCount = screenshotCount;
        HostedVideosCount = videoCount;
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
        else if (string.Equals(filter, "Screenshot", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(a => a.IsScreenshot && !a.IsExternalCdn);
        }
        else if (string.Equals(filter, "Video", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(a => a.IsVideo && !a.IsExternalCdn);
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
                (!string.IsNullOrEmpty(a.Location) && a.Location.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(a.LinkedToText) && a.LinkedToText.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        items = ApplyInventorySort(items);

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

    private static string DetermineCatalogIdFromFileName(string catFileName)
    {
        if (catFileName.StartsWith("catalog-", StringComparison.OrdinalIgnoreCase) && catFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && catFileName.Length > 13)
        {
            var id = catFileName[8..^5];
            return string.IsNullOrWhiteSpace(id) ? "main" : id;
        }

        if (catFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !string.Equals(catFileName, "catalog.json", StringComparison.OrdinalIgnoreCase))
        {
            var stem = Path.GetFileNameWithoutExtension(catFileName);
            return string.IsNullOrWhiteSpace(stem) ? "main" : stem;
        }

        return "main";
    }

    /// <summary>
    /// Resolves a collision-free remote filename for a catalog. Catalogs imported from
    /// identically named files would otherwise overwrite each other on the host and end
    /// up sharing one download URL, which makes subscribers show the wrong items.
    /// Both the original filename and the generated fallback are checked against the
    /// names owned by other catalogs, incrementing until the name is unused.
    /// </summary>
    private static string ResolveUniqueCatalogFileName(
        PublisherStudioProject project,
        NamedCatalog activeCatalog,
        IEnumerable<string>? additionalUsedNames = null)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in project.Catalogs)
        {
            if (string.Equals(other.Id, activeCatalog.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            usedNames.Add(!string.IsNullOrWhiteSpace(other.FileName) ? other.FileName : $"catalog-{other.Id}.json");
        }

        if (additionalUsedNames != null)
        {
            usedNames.UnionWith(additionalUsedNames.Where(name => !string.IsNullOrWhiteSpace(name)));
        }

        var fileName = activeCatalog.FileName;
        if (!string.IsNullOrWhiteSpace(fileName) && !usedNames.Contains(fileName))
        {
            return fileName;
        }

        var candidate = $"catalog-{activeCatalog.Id}.json";
        var suffix = 2;
        while (usedNames.Contains(candidate))
        {
            candidate = $"catalog-{activeCatalog.Id}-{suffix}.json";
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// Gets remote filenames already claimed by other catalogs in persisted hosting state.
    /// These are the actual upload names, which can differ from the project filenames
    /// after collision resolution.
    /// </summary>
    private static IReadOnlyList<string> GetPersistedCatalogFileNames(HostingState? hostingState, string activeCatalogId)
    {
        return hostingState?.Catalogs
            .Where(c => !string.Equals(c.CatalogId, activeCatalogId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.FileName))
            .Select(c => c.FileName)
            .ToList() ?? [];
    }

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

    private static bool IsMatchingDefinition(HostedFileInfo d, HostedFileInfo cloudDef)
    {
        return (!string.IsNullOrEmpty(d.FileName) && !string.IsNullOrEmpty(cloudDef.FileName) && string.Equals(d.FileName, cloudDef.FileName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(d.FileId) && !string.IsNullOrEmpty(cloudDef.FileId) && string.Equals(d.FileId, cloudDef.FileId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(d.Url) && !string.IsNullOrEmpty(cloudDef.Url) && string.Equals(d.Url, cloudDef.Url, StringComparison.OrdinalIgnoreCase));
    }

    private static void UpdateExistingDefinition(HostedFileInfo existing, HostedFileInfo cloudDef)
    {
        if (cloudDef.LastUpdated < existing.LastUpdated)
        {
            return;
        }

        // Never clobber a resolved shareable URL with an empty scan result: providers
        // such as Dropbox report unshared files without a URL on every scan.
        if (!string.IsNullOrEmpty(cloudDef.Url))
        {
            existing.Url = cloudDef.Url;
        }

        existing.FileSize = cloudDef.FileSize;
        existing.LastUpdated = cloudDef.LastUpdated;
        if (!string.IsNullOrEmpty(cloudDef.FileName))
        {
            existing.FileName = cloudDef.FileName;
        }
    }

    private IEnumerable<HostedAssetItemViewModel> ApplyInventorySort(IEnumerable<HostedAssetItemViewModel> items)
    {
        if (string.Equals(InventorySortColumn, InventorySortByName, StringComparison.OrdinalIgnoreCase))
        {
            return InventorySortDescending
                ? items.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(a => a.FileSize)
                : items.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.FileSize);
        }

        if (string.Equals(InventorySortColumn, InventorySortBySize, StringComparison.OrdinalIgnoreCase))
        {
            return InventorySortDescending
                ? items.OrderByDescending(a => a.FileSize).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(a => a.FileSize).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase);
        }

        return items;
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

        // A hosted definition is loadable when the local publisher profile is empty (e.g. after
        // local data was wiped): this is the only path that surfaces the restore banner and pull command.
        var canLoadDefinitionToProject = isDefHosted && string.IsNullOrWhiteSpace(project.Catalog?.Publisher?.Id);

        var definitionItem = new HostedAssetItemViewModel
        {
            AssetKind = HostedAssetKind.Definition,
            CanUpload = !isDefHosted && SelectedHostingProvider != null && SelectedHostingProvider.SupportsCatalogHosting,
            CanLoadToProject = canLoadDefinitionToProject,
            CanDelete = isDefHosted,
            LoadButtonTooltip = GetLocalizedString("Tools.PublisherStudio.Hosting.LoadToProjectTip", "Load this definition and its catalogs into current project"),
            Name = project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName,
            FileId = _currentHostingState?.Definition?.FileId ?? string.Empty,
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
            DefinitionName = ResolveDefinitionDisplayName(),
        };
        BuildDefinitionChildren(definitionItem);
        HostedAssets.Add(definitionItem);
    }

    private void PopulateCatalogAssets(string providerName, ref long totalBytes, ref int catCount)
    {
        foreach (var catalog in project.Catalogs)
        {
            var catHosting = _currentHostingState?.Catalogs.FirstOrDefault(c => c.CatalogId == catalog.Id)
                ?? _currentHostingState?.Catalogs.FirstOrDefault(c => c.FileName == catalog.FileName);
            var isCatHosted = catHosting != null && !string.IsNullOrWhiteSpace(catHosting.Url);
            var catSize = catHosting?.FileSize ?? 0;
            var catUrl = catHosting?.Url ?? string.Empty;
            var catUpdated = catHosting?.LastUpdated ?? DateTime.MinValue;

            if (isCatHosted)
            {
                catCount++;
                totalBytes += catSize;
            }

            var catalogItem = new HostedAssetItemViewModel
            {
                AssetKind = HostedAssetKind.Catalog,
                CatalogId = catalog.Id,
                CatalogName = catalog.Name,
                DefinitionName = ResolveDefinitionDisplayName(),
                CanUpload = !isCatHosted && SelectedHostingProvider != null && SelectedHostingProvider.SupportsCatalogHosting,
                CanDelete = isCatHosted,
                Name = catalog.FileName,
                FileId = catHosting?.FileId ?? string.Empty,
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
            };
            BuildCatalogChildren(catalogItem, catalog);
            HostedAssets.Add(catalogItem);
        }
    }

    private void PopulateArtifactAssets(string providerName, ref long totalBytes, ref int artCount, ref int cdnCount)
    {
        foreach (var catalog in project.Catalogs)
        {
            foreach (var content in catalog.Catalog.Content)
            {
                foreach (var release in content.Releases)
                {
                    foreach (var artifact in release.Artifacts)
                    {
                        ProcessArtifactAsset(artifact, catalog, content.Id, content.Name, release.Version, providerName, ref totalBytes, ref artCount, ref cdnCount);
                    }
                }

                foreach (var addon in content.AddonReleases)
                {
                    foreach (var artifact in addon.Artifacts)
                    {
                        ProcessArtifactAsset(artifact, catalog, content.Id, content.Name, addon.Version, providerName, ref totalBytes, ref artCount, ref cdnCount);
                    }
                }
            }
        }
    }

    private void ProcessArtifactAsset(
        ReleaseArtifact artifact,
        NamedCatalog catalog,
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
        var artHosting = !string.IsNullOrEmpty(artifact.DownloadUrl)
            ? FindHostingArtifactByUrl(artifact.DownloadUrl)
            : _currentHostingState?.Artifacts.FirstOrDefault(a => a.FileName == artifact.Filename);
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
            CatalogId = catalog.Id,
            CatalogName = catalog.Name,
            DefinitionName = ResolveDefinitionDisplayName(),
            ContentId = contentId,
            ContentName = contentName,
            ReleaseVersion = releaseVersion,
            LocalFilePath = artifact.LocalFilePath,
            CanUpload = canUploadArtifact,
            CanDelete = isCloud || isExternal,
            Name = artifact.Filename,
            FileId = artHosting?.FileId ?? string.Empty,
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
        ApplyArtifactLinkage(item, catalog, contentName, releaseVersion);
        BuildReferenceChildren(item);

        HostedAssets.Add(item);

        if (artSize <= 0 && isExternal && IsValidHttpUrl(artifact.DownloadUrl))
        {
            var url = artifact.DownloadUrl!;
            var ct = _probeCts?.Token ?? CancellationToken.None;
            TrackExternalProbe(url, item, probeUrl => ProbeExternalAssetSizeAsync(probeUrl, artifact, ct));
        }
    }

    private void TrackExternalProbe(string url, HostedAssetItemViewModel item, Func<string, Task> startProbe)
    {
        var isNewProbe = false;
        var list = _probingUrls.GetOrAdd(url, _ =>
        {
            isNewProbe = true;
            return [];
        });

        lock (_probeLock)
        {
            list.Add(item);
        }

        if (isNewProbe)
        {
            _ = startProbe(url);
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

    private void PopulateMediaAssets(string providerName, ref long totalBytes, ref int screenshotCount, ref int videoCount, ref int cdnCount)
    {
        var emittedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in project.Catalogs)
        {
            foreach (var content in catalog.Catalog.Content)
            {
                PopulateContentMetadataMedia(content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
                foreach (var release in content.Releases)
                {
                    PopulateReleaseMedia(release, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
                }

                foreach (var addon in content.AddonReleases)
                {
                    PopulateReleaseMedia(addon, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
                }
            }
        }
    }

    private void PopulateContentMetadataMedia(
        CatalogContentItem content,
        NamedCatalog catalog,
        string providerName,
        HashSet<string> emittedUrls,
        ref long totalBytes,
        ref int screenshotCount,
        ref int videoCount,
        ref int cdnCount)
    {
        var metadata = content.Metadata;
        if (metadata == null)
        {
            return;
        }

        foreach (var url in metadata.ScreenshotUrls)
        {
            ProcessMediaAsset(url, MediaSlotScreenshot, null, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
        }

        if (!string.IsNullOrWhiteSpace(metadata.VideoUrl))
        {
            ProcessMediaAsset(metadata.VideoUrl, MediaSlotVideo, null, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
        }

        foreach (var url in metadata.VideoUrls)
        {
            ProcessMediaAsset(url, MediaSlotVideo, null, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
        }

        ProcessMediaAsset(metadata.IconUrl, MediaSlotIcon, null, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
        ProcessMediaAsset(metadata.BannerUrl, MediaSlotBanner, null, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
        ProcessMediaAsset(metadata.BackdropUrl, MediaSlotBackdrop, null, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
    }

    private void PopulateReleaseMedia(
        ContentRelease release,
        CatalogContentItem content,
        NamedCatalog catalog,
        string providerName,
        HashSet<string> emittedUrls,
        ref long totalBytes,
        ref int screenshotCount,
        ref int videoCount,
        ref int cdnCount)
    {
        foreach (var url in release.ImageUrls)
        {
            ProcessMediaAsset(url, MediaSlotReleaseImage, release.Version, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
        }

        foreach (var url in release.VideoUrls)
        {
            ProcessMediaAsset(url, MediaSlotReleaseVideo, release.Version, content, catalog, providerName, emittedUrls, ref totalBytes, ref screenshotCount, ref videoCount, ref cdnCount);
        }
    }

    private void ProcessMediaAsset(
        string? url,
        string slot,
        string? releaseVersion,
        CatalogContentItem content,
        NamedCatalog catalog,
        string providerName,
        HashSet<string> emittedUrls,
        ref long totalBytes,
        ref int screenshotCount,
        ref int videoCount,
        ref int cdnCount)
    {
        if (!IsValidHttpUrl(url) || !emittedUrls.Add(url!))
        {
            return;
        }

        var isVideo = string.Equals(slot, MediaSlotVideo, StringComparison.Ordinal) ||
            string.Equals(slot, MediaSlotReleaseVideo, StringComparison.Ordinal);
        var kind = isVideo ? HostedAssetKind.Video : HostedAssetKind.Screenshot;
        var isExternal = !IsCloudProviderUrl(url);
        var isCloud = !isExternal;
        var hosting = FindHostingArtifactByUrl(url!);
        var mediaSize = hosting?.FileSize
            ?? (_probedMediaSizes.TryGetValue(url!, out var cachedSize) ? cachedSize : 0);

        var location = string.Empty;
        var status = string.Empty;
        if (isCloud)
        {
            if (isVideo)
            {
                videoCount++;
            }
            else
            {
                screenshotCount++;
            }

            totalBytes += mediaSize;
            location = LocationLabelForAssetUrl(url, providerName, HostingFolderPath);
            status = GetLocalizedString("Tools.PublisherStudio.Hosting.StatusLiveOnline", HostingConstants.StatusLiveOnline);
        }
        else
        {
            cdnCount++;
            location = GetLocalizedString("Tools.PublisherStudio.Hosting.StatusExternalCdn", HostingConstants.StatusExternalCdn);
            status = GetLocalizedString("Tools.PublisherStudio.Hosting.StatusExternalCdn", HostingConstants.StatusExternalCdn);
        }

        var slotLabel = GetMediaSlotLabel(slot);
        var item = new HostedAssetItemViewModel
        {
            AssetKind = kind,
            CatalogId = catalog.Id,
            CatalogName = catalog.Name,
            DefinitionName = ResolveDefinitionDisplayName(),
            ContentId = content.Id,
            ContentName = content.Name,
            ReleaseVersion = releaseVersion,
            CanUpload = false,
            CanDelete = true,
            Name = FileNameFromUrl(url!, $"{content.Name} {slotLabel}"),
            FileId = hosting?.FileId ?? string.Empty,
            Category = isVideo
                ? FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryVideoFormat", "Video ({0})", content.Name)
                : FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryScreenshotFormat", "Screenshot ({0})", content.Name),
            Location = location,
            FileSize = mediaSize,
            Url = url!,
            Status = status,
            IsOnline = isCloud,
            IsExternalCdn = isExternal,
            LastUpdated = hosting?.LastUpdated ?? DateTime.MinValue,
        };
        ApplyMediaLinkage(item, catalog, content, releaseVersion, slotLabel);
        BuildReferenceChildren(item);
        HostedAssets.Add(item);

        if (mediaSize <= 0 && isExternal)
        {
            var ct = _probeCts?.Token ?? CancellationToken.None;
            TrackExternalProbe(url!, item, probeUrl => ProbeExternalMediaSizeAsync(probeUrl, item, ct));
        }
    }

    private async Task ProbeExternalMediaSizeAsync(string url, HostedAssetItemViewModel item, CancellationToken cancellationToken)
    {
        try
        {
            var client = HttpClientOverrideForTesting ?? SharedHttpClient;
            var detectedSize = await DetectExternalAssetSizeAsync(client, url, cancellationToken).ConfigureAwait(false);
            if (detectedSize is > 0 && !cancellationToken.IsCancellationRequested)
            {
                _probedMediaSizes[url] = detectedSize.Value;
                DispatchArtifactSizeUpdate(url, detectedSize.Value);
                logger.LogInformation("Probed external media size for {FileName}: {Size} bytes", item.Name, detectedSize.Value);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not probe size for external media {Url}", url);
        }
        finally
        {
            _probingUrls.TryRemove(url, out _);
        }
    }

    private void PopulateCloudScanAssets(string providerName, ref long totalBytes, ref int catCount, ref int artCount, ref int screenshotCount, ref int videoCount)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        PopulateDiscoveredDefinitions(providerName, ref totalBytes);
        PopulateDiscoveredCatalogs(providerName, ref totalBytes, ref catCount);
        PopulateDiscoveredArtifacts(providerName, ref totalBytes, ref artCount, ref screenshotCount, ref videoCount);
    }

    private void PopulateDiscoveredDefinitions(string providerName, ref long totalBytes)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        foreach (var cloudDef in _currentHostingState.Definitions.Where(cloudDef => !HostedAssets.Any(a =>
            (!string.IsNullOrEmpty(a.Url) && !string.IsNullOrEmpty(cloudDef.Url) && string.Equals(a.Url, cloudDef.Url, StringComparison.OrdinalIgnoreCase)) ||
            (a.IsOnline && !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(cloudDef.FileName) && string.Equals(a.Name, cloudDef.FileName, StringComparison.OrdinalIgnoreCase)))))
        {
            totalBytes += cloudDef.FileSize;
            HostedDefinitionCount++;
            var stem = Path.GetFileNameWithoutExtension(cloudDef.FileName);
            var categoryName = FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudDefinitionFormat", "Cloud Publisher Definition ({0})", stem);

            var discoveredDefinition = new HostedAssetItemViewModel
            {
                AssetKind = HostedAssetKind.Definition,
                CanUpload = false,
                CanLoadToProject = !string.IsNullOrEmpty(cloudDef.Url) || !string.IsNullOrEmpty(cloudDef.FileId),
                CanDelete = true,
                LoadButtonTooltip = GetLocalizedString("Tools.PublisherStudio.Hosting.LoadToProjectTip", "Load this definition and its catalogs into current project"),
                Name = string.IsNullOrEmpty(cloudDef.FileName) ? HostingConstants.DefaultDefinitionFileName : cloudDef.FileName,
                FileId = cloudDef.FileId,
                Category = categoryName,
                Location = $"{providerName} ({HostingFolderPath})",
                FileSize = cloudDef.FileSize,
                Url = cloudDef.Url,
                Status = HostingConstants.StatusLiveOnline,
                IsOnline = true,
                IsExternalCdn = false,
                LastUpdated = cloudDef.LastUpdated,
                LinkedToText = GetLocalizedString("Tools.PublisherStudio.Hosting.LinkedToNotLinked", "Not linked"),
                LinkedToTooltip = GetLocalizedString("Tools.PublisherStudio.Hosting.LinkedToNotInProjectTip", "This file is not part of the current project. Expand the row to preview its contents."),
                NeedsRemotePreview = true,
            };
            HostedAssets.Add(discoveredDefinition);
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
            var projectCat = project.Catalogs.FirstOrDefault(c => c.Id == cloudCat.CatalogId || c.FileName == cloudCat.FileName);

            var discoveredCatalog = new HostedAssetItemViewModel
            {
                AssetKind = HostedAssetKind.Catalog,
                CatalogId = cloudCat.CatalogId,
                CatalogName = projectCat?.Name ?? cloudCat.CatalogName,
                DefinitionName = projectCat != null ? ResolveDefinitionDisplayName() : null,
                CanUpload = false,
                CanLoadToProject = projectCat == null && (!string.IsNullOrEmpty(cloudCat.Url) || !string.IsNullOrEmpty(cloudCat.FileId)),
                CanDelete = true,
                FileId = cloudCat.FileId,
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
            };
            if (projectCat != null)
            {
                BuildCatalogChildren(discoveredCatalog, projectCat);
            }
            else
            {
                discoveredCatalog.LinkedToText = GetLocalizedString("Tools.PublisherStudio.Hosting.LinkedToNotLinked", "Not linked");
                discoveredCatalog.LinkedToTooltip = GetLocalizedString("Tools.PublisherStudio.Hosting.LinkedToNotInProjectTip", "This file is not part of the current project. Expand the row to preview its contents.");
                discoveredCatalog.NeedsRemotePreview = true;
            }

            HostedAssets.Add(discoveredCatalog);
        }
    }

    private void PopulateDiscoveredArtifacts(string providerName, ref long totalBytes, ref int artCount, ref int screenshotCount, ref int videoCount)
    {
        if (_currentHostingState == null)
        {
            return;
        }

        foreach (var cloudArt in _currentHostingState.Artifacts.Where(cloudArt => !HostedAssets.Any(a =>
            (!string.IsNullOrEmpty(a.Url) && !string.IsNullOrEmpty(cloudArt.Url) && string.Equals(a.Url, cloudArt.Url, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(cloudArt.FileName) && string.Equals(a.Name, cloudArt.FileName, StringComparison.OrdinalIgnoreCase)))))
        {
            totalBytes += cloudArt.FileSize;
            var refs = FindReferencesByUrl(cloudArt.Url);
            var kind = ClassifyDiscoveredKind(cloudArt.FileName, refs);
            if (kind == HostedAssetKind.Video)
            {
                videoCount++;
            }
            else if (kind == HostedAssetKind.Screenshot)
            {
                screenshotCount++;
            }
            else
            {
                artCount++;
            }

            var discoveredFile = new HostedAssetItemViewModel
            {
                AssetKind = kind,
                CanUpload = false,
                CanAddToCatalog = kind == HostedAssetKind.Artifact && !string.IsNullOrEmpty(cloudArt.Url),
                CanDelete = true,
                Name = cloudArt.FileName,
                FileId = cloudArt.FileId,
                Category = BuildDiscoveredFileCategory(kind, refs, cloudArt.FileName),
                Location = $"{providerName} ({HostingFolderPath})",
                FileSize = cloudArt.FileSize,
                Url = cloudArt.Url,
                Status = HostingConstants.StatusLiveOnline,
                IsOnline = true,
                IsExternalCdn = false,
                LastUpdated = cloudArt.LastUpdated,
                Sha256 = cloudArt.Sha256,
            };
            ApplyCloudFileLinkage(discoveredFile, refs);
            BuildReferenceChildren(discoveredFile);
            HostedAssets.Add(discoveredFile);
        }
    }

    private Dictionary<string, List<AssetReference>> GetLinkageIndex() =>
        _linkageIndex ??= BuildUrlReferenceIndex();

    private Dictionary<string, List<AssetReference>> BuildUrlReferenceIndex()
    {
        var index = new Dictionary<string, List<AssetReference>>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in project.Catalogs)
        {
            foreach (var content in catalog.Catalog.Content)
            {
                IndexContentReferences(index, catalog, content);
            }
        }

        return index;
    }

    private void IndexContentReferences(Dictionary<string, List<AssetReference>> index, NamedCatalog catalog, CatalogContentItem content)
    {
        IndexMetadataReferences(index, catalog, content);
        foreach (var release in content.Releases)
        {
            IndexReleaseReferences(index, catalog, content, release);
        }

        foreach (var addon in content.AddonReleases)
        {
            IndexReleaseReferences(index, catalog, content, addon);
        }
    }

    private void IndexMetadataReferences(Dictionary<string, List<AssetReference>> index, NamedCatalog catalog, CatalogContentItem content)
    {
        var metadata = content.Metadata;
        if (metadata == null)
        {
            return;
        }

        foreach (var url in metadata.ScreenshotUrls)
        {
            AddUrlReference(index, url, new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, null, MediaSlotScreenshot));
        }

        AddUrlReference(index, metadata.VideoUrl, new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, null, MediaSlotVideo));
        foreach (var url in metadata.VideoUrls)
        {
            AddUrlReference(index, url, new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, null, MediaSlotVideo));
        }

        AddUrlReference(index, metadata.IconUrl, new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, null, MediaSlotIcon));
        AddUrlReference(index, metadata.BannerUrl, new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, null, MediaSlotBanner));
        AddUrlReference(index, metadata.BackdropUrl, new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, null, MediaSlotBackdrop));
    }

    private void IndexReleaseReferences(Dictionary<string, List<AssetReference>> index, NamedCatalog catalog, CatalogContentItem content, ContentRelease release)
    {
        foreach (var artifact in release.Artifacts)
        {
            AddUrlReference(index, artifact.DownloadUrl, new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, release.Version, MediaSlotArtifact));
        }

        foreach (var url in release.ImageUrls)
        {
            AddUrlReference(index, url, new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, release.Version, MediaSlotReleaseImage));
        }

        foreach (var url in release.VideoUrls)
        {
            AddUrlReference(index, url, new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, release.Version, MediaSlotReleaseVideo));
        }
    }

    private void AddUrlReference(Dictionary<string, List<AssetReference>> index, string? url, AssetReference reference)
    {
        if (!IsValidHttpUrl(url))
        {
            return;
        }

        if (!index.TryGetValue(url!, out var list))
        {
            list = [];
            index[url!] = list;
        }

        if (!list.Contains(reference))
        {
            list.Add(reference);
        }
    }

    private IReadOnlyList<AssetReference>? FindReferencesByUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return GetLinkageIndex().TryGetValue(url, out var list) ? list : null;
    }

    private string ResolveDefinitionDisplayName()
    {
        var publisher = project.Catalog?.Publisher;
        if (!string.IsNullOrWhiteSpace(publisher?.Name))
        {
            return publisher.Name;
        }

        if (!string.IsNullOrWhiteSpace(publisher?.Id))
        {
            return publisher.Id;
        }

        return project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName;
    }

    private string GetMediaSlotLabel(string slot)
    {
        return slot switch
        {
            MediaSlotArtifact => GetLocalizedString("Tools.PublisherStudio.Hosting.MediaSlotArtifact", "Release file"),
            MediaSlotScreenshot => GetLocalizedString("Tools.PublisherStudio.Hosting.MediaSlotScreenshot", "Screenshot"),
            MediaSlotVideo => GetLocalizedString("Tools.PublisherStudio.Hosting.MediaSlotVideo", "Video"),
            MediaSlotIcon => GetLocalizedString("Tools.PublisherStudio.Hosting.MediaSlotIcon", "Icon"),
            MediaSlotBanner => GetLocalizedString("Tools.PublisherStudio.Hosting.MediaSlotBanner", "Banner"),
            MediaSlotBackdrop => GetLocalizedString("Tools.PublisherStudio.Hosting.MediaSlotBackdrop", "Backdrop"),
            MediaSlotReleaseImage => GetLocalizedString("Tools.PublisherStudio.Hosting.MediaSlotReleaseImage", "Release image"),
            MediaSlotReleaseVideo => GetLocalizedString("Tools.PublisherStudio.Hosting.MediaSlotReleaseVideo", "Release video"),
            _ => slot,
        };
    }

    private string FormatReferenceText(AssetReference reference)
    {
        var contentName = reference.ContentName ?? reference.ContentId ?? "?";
        if (!string.IsNullOrWhiteSpace(reference.ReleaseVersion))
        {
            return FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToRefWithVersionFormat", "{0} › {1} v{2}", reference.CatalogName, contentName, reference.ReleaseVersion);
        }

        return FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToRefFormat", "{0} › {1}", reference.CatalogName, contentName);
    }

    private string FormatLinkedToText(IReadOnlyList<AssetReference> refs)
    {
        var primary = FormatReferenceText(refs[0]);
        return refs.Count > 1
            ? FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToMoreFormat", "{0} (+{1} more)", primary, refs.Count - 1)
            : primary;
    }

    private string BuildReferencesTooltip(IReadOnlyList<AssetReference> refs)
    {
        var lines = new System.Text.StringBuilder();
        lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToDefinitionLineFormat", "Definition: {0}", ResolveDefinitionDisplayName()));
        foreach (var reference in refs.Take(6))
        {
            lines.AppendLine();
            lines.Append(FormatReferenceText(reference));
            lines.Append(" (");
            lines.Append(GetMediaSlotLabel(reference.Slot));
            lines.Append(')');
        }

        if (refs.Count > 6)
        {
            lines.AppendLine();
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToMoreRefsFormat", "+{0} more", refs.Count - 6));
        }

        return lines.ToString();
    }

    private void ApplyArtifactLinkage(HostedAssetItemViewModel item, NamedCatalog catalog, string contentName, string releaseVersion)
    {
        var refs = FindReferencesByUrl(item.Url);
        if (refs is { Count: > 0 })
        {
            item.LinkCount = refs.Count;
            item.LinkedToText = FormatLinkedToText(refs);
            item.LinkedToTooltip = BuildReferencesTooltip(refs);
            return;
        }

        item.LinkCount = string.IsNullOrWhiteSpace(item.Url) ? 0 : 1;
        item.LinkedToText = FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToRefWithVersionFormat", "{0} › {1} v{2}", catalog.Name, contentName, releaseVersion);
        item.LinkedToTooltip = FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToDefinitionLineFormat", "Definition: {0}", ResolveDefinitionDisplayName());
    }

    private void ApplyMediaLinkage(HostedAssetItemViewModel item, NamedCatalog catalog, CatalogContentItem content, string? releaseVersion, string slotLabel)
    {
        var refs = FindReferencesByUrl(item.Url);
        if (refs is { Count: > 0 })
        {
            item.LinkCount = refs.Count;
            item.LinkedToText = FormatLinkedToText(refs);
            item.LinkedToTooltip = BuildReferencesTooltip(refs);
            return;
        }

        item.LinkCount = 1;
        var reference = new AssetReference(catalog.Id, catalog.Name, content.Id, content.Name, releaseVersion, slotLabel);
        item.LinkedToText = FormatReferenceText(reference);
        item.LinkedToTooltip = FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToDefinitionLineFormat", "Definition: {0}", ResolveDefinitionDisplayName());
    }

    private void ApplyCloudFileLinkage(HostedAssetItemViewModel item, IReadOnlyList<AssetReference>? refs)
    {
        if (refs is { Count: > 0 })
        {
            item.CatalogId = refs[0].CatalogId;
            item.CatalogName = refs[0].CatalogName;
            item.ContentId = refs[0].ContentId;
            item.ContentName = refs[0].ContentName;
            item.ReleaseVersion = refs[0].ReleaseVersion;
            item.DefinitionName = ResolveDefinitionDisplayName();
            item.LinkCount = refs.Count;
            item.LinkedToText = FormatLinkedToText(refs);
            item.LinkedToTooltip = BuildReferencesTooltip(refs);
            return;
        }

        item.LinkCount = 0;
        item.LinkedToText = GetLocalizedString("Tools.PublisherStudio.Hosting.LinkedToNotLinked", "Not linked");
        item.LinkedToTooltip = GetLocalizedString("Tools.PublisherStudio.Hosting.LinkedToNotLinkedTip", "This file is not referenced by any catalog in the current project.");
    }

    private void BuildDefinitionChildren(HostedAssetItemViewModel item)
    {
        item.Children.Clear();
        foreach (var catalog in project.Catalogs)
        {
            var hostingUrl = _currentHostingState?.Catalogs.FirstOrDefault(c =>
                string.Equals(c.CatalogId, catalog.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Url))?.Url;
            var state = !string.IsNullOrWhiteSpace(hostingUrl)
                ? GetLocalizedString("Tools.PublisherStudio.Hosting.StatusLiveOnline", HostingConstants.StatusLiveOnline)
                : GetLocalizedString("Tools.PublisherStudio.Hosting.StatusPendingUpload", HostingConstants.StatusPendingUpload);
            item.Children.Add(new HostedAssetChildViewModel
            {
                Name = catalog.Name,
                Detail = FormatLocalizedString("Tools.PublisherStudio.Hosting.ChildCatalogDetailFormat", "{0} · {1}", catalog.FileName, state),
                CopyUrl = hostingUrl ?? item.Url,
            });
        }

        item.HasChildren = item.Children.Count > 0;
        item.IsEmpty = item.Children.Count == 0;
        item.EmptyStateText = GetLocalizedString("Tools.PublisherStudio.Hosting.EmptyDefinitionText", "No catalogs linked to this definition");
        item.ChildrenSummary = FormatLocalizedString("Tools.PublisherStudio.Hosting.ChildrenCatalogsFormat", "{0} catalogs", item.Children.Count);
        item.LinkedToText = FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToDefinitionFormat", "{0} catalog(s)", project.Catalogs.Count);
        item.LinkedToTooltip = FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToDefinitionTipFormat", "Publisher definition listing {0} catalog(s)", project.Catalogs.Count);
    }

    private void BuildCatalogChildren(HostedAssetItemViewModel item, NamedCatalog catalog)
    {
        item.Children.Clear();
        foreach (var content in catalog.Catalog.Content)
        {
            item.Children.Add(BuildContentChild(content, item.Url));
        }

        item.HasChildren = item.Children.Count > 0;
        item.IsEmpty = item.Children.Count == 0;
        item.EmptyStateText = GetLocalizedString("Tools.PublisherStudio.Hosting.EmptyCatalogText", "Empty catalog — no content linked");
        item.ChildrenSummary = FormatLocalizedString("Tools.PublisherStudio.Hosting.ChildrenContentsFormat", "{0} contents", item.Children.Count);
        item.LinkedToText = FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToCatalogFormat", "Definition: {0}", ResolveDefinitionDisplayName());
        item.LinkedToTooltip = FormatLocalizedString(
            "Tools.PublisherStudio.Hosting.LinkedToCatalogTipFormat",
            "Listed in {0} · {1} content item(s)",
            project.ProviderDefinitionFileName ?? HostingConstants.DefaultDefinitionFileName,
            catalog.Catalog.Content.Count);
    }

    private void BuildReferenceChildren(HostedAssetItemViewModel item)
    {
        item.Children.Clear();
        var refs = FindReferencesByUrl(item.Url);
        if (refs is not { Count: > 0 })
        {
            return;
        }

        foreach (var reference in refs)
        {
            item.Children.Add(new HostedAssetChildViewModel
            {
                Name = FormatReferenceText(reference),
                Detail = GetMediaSlotLabel(reference.Slot),
                CopyUrl = item.Url,
            });
        }

        item.HasChildren = true;
        item.LinkCount = refs.Count;
        item.ChildrenSummary = FormatLocalizedString("Tools.PublisherStudio.Hosting.ChildrenReferencesFormat", "{0} references", refs.Count);
    }

    private HostedAssetChildViewModel BuildContentChild(CatalogContentItem content, string copyUrl)
    {
        var releaseCount = content.Releases.Count + content.AddonReleases.Count;
        var fileCount = content.Releases.SelectMany(r => r.Artifacts).Count() +
            content.AddonReleases.SelectMany(r => r.Artifacts).Count();
        return new HostedAssetChildViewModel
        {
            Name = content.Name,
            Detail = FormatLocalizedString(
                "Tools.PublisherStudio.Hosting.ChildContentDetailFormat",
                "{0} · {1} releases · {2} files · {3} media",
                content.ContentType.ToString(),
                releaseCount,
                fileCount,
                CountMediaUrls(content)),
            CopyUrl = copyUrl,
        };
    }

    private void OnHostedAssetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is HostedAssetItemViewModel item &&
            string.Equals(e.PropertyName, nameof(HostedAssetItemViewModel.IsExpanded), StringComparison.Ordinal) &&
            item.IsExpanded)
        {
            _ = EnsureChildrenLoadedAsync(item);
        }
    }

    private async Task EnsureChildrenLoadedAsync(HostedAssetItemViewModel item)
    {
        if (!item.NeedsRemotePreview || item.RemotePreviewLoaded || item.IsLoadingChildren)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(item.Url) && _remotePreviewCache.TryGetValue(item.Url, out var cached))
        {
            ApplyPreviewChildren(item, cached);
            return;
        }

        item.IsLoadingChildren = true;
        item.ChildrenLoadError = null;
        try
        {
            var (children, error) = await LoadRemotePreviewAsync(item, CancellationToken.None).ConfigureAwait(false);
            if (children == null)
            {
                item.ChildrenLoadError = FormatLocalizedString(
                    "Tools.PublisherStudio.Hosting.PreviewLoadFailedFormat",
                    "Preview unavailable: {0}",
                    string.IsNullOrWhiteSpace(error) ? item.Name : error);
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.Url))
            {
                _remotePreviewCache[item.Url] = children;
            }

            ApplyPreviewChildren(item, children);
        }
        finally
        {
            item.IsLoadingChildren = false;
        }
    }

    private void ApplyPreviewChildren(HostedAssetItemViewModel item, List<HostedAssetChildViewModel> children)
    {
        item.Children.Clear();
        foreach (var child in children)
        {
            item.Children.Add(child);
        }

        item.HasChildren = item.Children.Count > 0;
        item.IsEmpty = item.Children.Count == 0;
        if (item.IsEmpty)
        {
            item.EmptyStateText = item.IsDefinition
                ? GetLocalizedString("Tools.PublisherStudio.Hosting.EmptyDefinitionText", "No catalogs linked to this definition")
                : GetLocalizedString("Tools.PublisherStudio.Hosting.EmptyCatalogText", "Empty catalog — no content linked");
        }

        item.ChildrenSummary = item.IsDefinition
            ? FormatLocalizedString("Tools.PublisherStudio.Hosting.ChildrenCatalogsFormat", "{0} catalogs", item.Children.Count)
            : FormatLocalizedString("Tools.PublisherStudio.Hosting.ChildrenContentsFormat", "{0} contents", item.Children.Count);
        item.RemotePreviewLoaded = true;
    }

    private async Task<(List<HostedAssetChildViewModel>? Children, string? Error)> LoadRemotePreviewAsync(HostedAssetItemViewModel item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.Url))
        {
            return (null, GetLocalizedString("Tools.PublisherStudio.Hosting.NoShareableUrlError", "No shareable download URL is available."));
        }

        var json = await DownloadStringFromUrlAsync(item.Url).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, GetLocalizedString("Tools.PublisherStudio.Hosting.PreviewDownloadFailedText", "The file could not be downloaded."));
        }

        try
        {
            return item.IsDefinition
                ? BuildDefinitionPreview(json, item)
                : BuildCatalogPreview(json, item);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse remote preview JSON for {AssetName}", item.Name);
            return (null, ex.Message);
        }
    }

    private (List<HostedAssetChildViewModel>? Children, string? Error) BuildCatalogPreview(string json, HostedAssetItemViewModel item)
    {
        var catalog = JsonSerializer.Deserialize<PublisherCatalog>(json, PublisherJsonOptions.Definition);
        if (catalog == null)
        {
            return (null, item.Name);
        }

        var children = catalog.Content.Select(content => BuildContentChild(content, item.Url)).ToList();
        return (children, null);
    }

    private (List<HostedAssetChildViewModel>? Children, string? Error) BuildDefinitionPreview(string json, HostedAssetItemViewModel item)
    {
        var definition = JsonSerializer.Deserialize<PublisherDefinition>(json, PublisherJsonOptions.Definition);
        if (definition == null)
        {
            return (null, item.Name);
        }

        var children = definition.Catalogs.Select(entry => new HostedAssetChildViewModel
        {
            Name = entry.Name,
            Detail = entry.Url,
            CopyUrl = entry.Url,
        }).ToList();
        return (children, null);
    }

    private int CountMediaUrls(CatalogContentItem content)
    {
        var count = 0;
        var metadata = content.Metadata;
        if (metadata != null)
        {
            count += metadata.ScreenshotUrls.Count(url => !string.IsNullOrWhiteSpace(url));
            count += metadata.VideoUrls.Count(url => !string.IsNullOrWhiteSpace(url));
            count += string.IsNullOrWhiteSpace(metadata.VideoUrl) ? 0 : 1;
            count += string.IsNullOrWhiteSpace(metadata.IconUrl) ? 0 : 1;
            count += string.IsNullOrWhiteSpace(metadata.BannerUrl) ? 0 : 1;
            count += string.IsNullOrWhiteSpace(metadata.BackdropUrl) ? 0 : 1;
        }

        count += content.Releases.Sum(r => r.ImageUrls.Count(url => !string.IsNullOrWhiteSpace(url)) + r.VideoUrls.Count(url => !string.IsNullOrWhiteSpace(url)));
        count += content.AddonReleases.Sum(r => r.ImageUrls.Count(url => !string.IsNullOrWhiteSpace(url)) + r.VideoUrls.Count(url => !string.IsNullOrWhiteSpace(url)));
        return count;
    }

    private HostedAssetKind ClassifyDiscoveredKind(string fileName, IReadOnlyList<AssetReference>? refs)
    {
        if (refs is { Count: > 0 })
        {
            foreach (var reference in refs)
            {
                if (string.Equals(reference.Slot, MediaSlotArtifact, StringComparison.Ordinal))
                {
                    return HostedAssetKind.Artifact;
                }
            }

            foreach (var reference in refs)
            {
                if (string.Equals(reference.Slot, MediaSlotVideo, StringComparison.Ordinal) ||
                    string.Equals(reference.Slot, MediaSlotReleaseVideo, StringComparison.Ordinal))
                {
                    return HostedAssetKind.Video;
                }
            }

            return HostedAssetKind.Screenshot;
        }

        if (HostingConstants.IsVideoFileName(fileName))
        {
            return HostedAssetKind.Video;
        }

        if (HostingConstants.IsScreenshotFileName(fileName))
        {
            return HostedAssetKind.Screenshot;
        }

        return HostedAssetKind.Artifact;
    }

    private string BuildDiscoveredFileCategory(HostedAssetKind kind, IReadOnlyList<AssetReference>? refs, string fileName)
    {
        if (refs is { Count: > 0 } && !string.IsNullOrWhiteSpace(refs[0].ContentName))
        {
            return kind switch
            {
                HostedAssetKind.Video => FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudVideoFormat", "Cloud Video ({0})", refs[0].ContentName),
                HostedAssetKind.Screenshot => FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudScreenshotFormat", "Cloud Screenshot ({0})", refs[0].ContentName),
                _ => FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudArtifactFormat", "Cloud Artifact ({0})", refs[0].ContentName),
            };
        }

        return kind switch
        {
            HostedAssetKind.Video => GetLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudVideo", "Cloud Video"),
            HostedAssetKind.Screenshot => GetLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudScreenshot", "Cloud Screenshot"),
            _ => string.IsNullOrWhiteSpace(fileName)
                ? GetLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudArtifact", "Cloud Artifact")
                : FormatLocalizedString("Tools.PublisherStudio.Hosting.AssetCategoryCloudArtifactFileFormat", "Cloud Artifact ({0})", Path.GetFileNameWithoutExtension(fileName)),
        };
    }

    private ArtifactHostingInfo? FindHostingArtifactByUrl(string url)
    {
        return _currentHostingState?.Artifacts.FirstOrDefault(a => string.Equals(a.Url, url, StringComparison.OrdinalIgnoreCase))
            ?? _hostingStates.Values.SelectMany(s => s.Artifacts).FirstOrDefault(a => string.Equals(a.Url, url, StringComparison.OrdinalIgnoreCase));
    }

    private string FileNameFromUrl(string url, string fallback)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/'));
            var name = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
            if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, "uc", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return fallback;
    }

    private string GetSortGlyph(string column)
    {
        if (!string.Equals(InventorySortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return InventorySortDescending ? SortGlyphDescending : SortGlyphAscending;
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
    private async Task ExportCatalogAsync() => await ExportCatalogCoreAsync(trackTelemetry: true);

    private async Task ExportCatalogCoreAsync(bool trackTelemetry)
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
                if (trackTelemetry)
                {
                    telemetryService?.TrackEvent(TelemetryConstants.Events.PublisherStudioDefinitionExported, new Dictionary<string, object?>
                    {
                        [TelemetryConstants.Properties.PublisherName] = project.Catalog.Publisher?.Name ?? project.ProjectName,
                        [TelemetryConstants.Properties.ContentName] = ActiveCatalog.Name,
                        [TelemetryConstants.Properties.ContentType] = TelemetryConstants.ContentTypes.Catalog,
                    });
                }
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
            var catalogName = ActiveCatalog?.Name;
            var uploadResult = await UploadCatalogCoreAsync(cts.Token, manageUploadingState: false);
            if (catalogName != null)
            {
                TrackCatalogPublished(catalogName, uploadResult);
            }

            return uploadResult;
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

            // An attempted upload can change remote state even if its response is lost.
            NotifyDefinitionStale();

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

    private bool IsFileIdSharedWithOtherCatalog(string catalogId, string fileId)
    {
        return _currentHostingState?.Catalogs.Any(c =>
            !string.Equals(c.CatalogId, catalogId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.FileId, fileId, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private bool IsCatalogFileIdShared(HostingState state, string fileId, string catalogId)
    {
        return state.Catalogs
            .Any(c => !string.Equals(c.CatalogId, catalogId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.FileId, fileId, StringComparison.OrdinalIgnoreCase));
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

        var catalogFileName = ResolveUniqueCatalogFileName(project, ActiveCatalog, GetPersistedCatalogFileNames(_currentHostingState, ActiveCatalog.Id));

        // A file ID shared with another catalog means a previous filename collision merged
        // both catalogs into one remote file. Upload fresh so this catalog gets its own file.
        var fileIdShared = !string.IsNullOrEmpty(existingCatalogFileId) &&
            IsFileIdSharedWithOtherCatalog(ActiveCatalog.Id, existingCatalogFileId);

        if (!string.IsNullOrEmpty(existingCatalogFileId) && SelectedHostingProvider.SupportsUpdate && !fileIdShared)
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
            TrackProviderDefinitionExported();
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

    private void TrackProviderDefinitionExported()
    {
        var catalogCount = _currentHostingState?.Catalogs
            .Where(c => !string.IsNullOrEmpty(c.Url))
            .Select(c => c.CatalogId)
            .Distinct()
            .Count() ?? 0;
        telemetryService?.TrackEvent(TelemetryConstants.Events.PublisherStudioDefinitionExported, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.PublisherName] = project.Catalog.Publisher?.Name ?? project.ProjectName,
            [TelemetryConstants.Properties.ContentType] = TelemetryConstants.ContentTypes.Definition,
            [TelemetryConstants.Properties.CatalogCount] = catalogCount,
            [TelemetryConstants.Properties.DefinitionUrl] = TelemetryUrlHelper.StripSensitiveUrlParts(ProviderDefinitionUrl),
        });
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
        var pendingMediaCount = ActiveCatalog == null ? 0 : CollectPendingMedia(ActiveCatalog).Pending.Count;
        var (artifactsOk, completedArtifacts) = await UploadPendingArtifactsAsync(provider, cancellationToken, pendingArtworkCount + pendingMediaCount);
        if (!artifactsOk)
        {
            return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
        }

        // 1b. Upload Pending Artwork (local image files referenced by content metadata)
        if (!await UploadPendingArtworkAsync(provider, cancellationToken, suppressNotifications, completedArtifacts, completedArtifacts + pendingArtworkCount + pendingMediaCount))
        {
            return OperationResult<HostingUploadResult>.CreateFailure(UploadStatusMessage);
        }

        // 1c. Upload Pending Gallery Media (local screenshots and videos referenced by content metadata and releases)
        if (!await UploadPendingMediaAsync(provider, cancellationToken, suppressNotifications, completedArtifacts + pendingArtworkCount, completedArtifacts + pendingArtworkCount + pendingMediaCount))
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
            var trimmed = value!.Trim();
            var localPath = ResolveArtworkLocalPath(trimmed);
            if (localPath == null)
            {
                logger.LogDebug("Skipping artwork path that cannot be resolved under the project directory for '{ContentId}'", content.Id);
                return;
            }

            pending.Add((content, slot, localPath));
        }
    }

    private bool IsPendingArtworkPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A bare Assets/ reference is a built-in resource unless it exists as a
        // publisher-local file, in which case it must be uploaded for subscribers.
        if (trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveArtworkLocalPath(trimmed) != null;
        }

        return !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Resolves an artwork path to an existing local file. Relative paths resolve
    /// against the studio project directory and must stay inside it, so crafted
    /// catalog metadata cannot exfiltrate publisher-local files outside the project.
    /// Values that escape the project or cannot be parsed as paths resolve to null.
    /// </summary>
    private string? ResolveArtworkLocalPath(string trimmed)
    {
        var projectDirectory = Path.GetDirectoryName(project.ProjectPath);
        if (!string.IsNullOrEmpty(projectDirectory))
        {
            try
            {
                var root = Path.GetFullPath(projectDirectory) + Path.DirectorySeparatorChar;
                var combined = Path.GetFullPath(Path.Combine(projectDirectory, trimmed));
                if (combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(combined))
                {
                    return combined;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                logger.LogDebug(ex, "Ignoring artwork path that cannot be resolved under the project directory");
                return null;
            }
        }

        return null;
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

        var combinedTotal = progressTotal > 0 ? progressTotal : total;
        var progress = new Progress<int>(p =>
        {
            ReportPendingUploadProgress(progressOffset + current - 1, combinedTotal, p);
        });
        var uploadFileName = $"{content.Id}-{slot.ToString().ToLowerInvariant()}-{displayName}";
        var (result, fileReadFailed) = await UploadLocalFileAsync(provider, localPath, uploadFileName, progress, cancellationToken);
        if (fileReadFailed)
        {
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

    /// <summary>
    /// Uploads a local file to the hosting provider, mapping file read problems to a flagged failure.
    /// </summary>
    /// <param name="provider">The hosting provider.</param>
    /// <param name="localPath">The local file path.</param>
    /// <param name="uploadFileName">The remote file name.</param>
    /// <param name="progress">The progress callback (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upload result and whether a local file read failed.</returns>
    private async Task<(OperationResult<HostingUploadResult> Result, bool FileReadFailed)> UploadLocalFileAsync(
        IHostingProvider provider,
        string localPath,
        string uploadFileName,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(localPath);
            var result = await provider.UploadFileAsync(stream, uploadFileName, null, progress, cancellationToken);
            return (result, false);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read local file {Path}", localPath);
            return (OperationResult<HostingUploadResult>.CreateFailure($"Local file unavailable: {localPath}"), true);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied reading local file {Path}", localPath);
            return (OperationResult<HostingUploadResult>.CreateFailure($"Local file unavailable: {localPath}"), true);
        }
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

    private async Task<bool> UploadPendingMediaAsync(
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

        var (pending, skipped) = CollectPendingMedia(ActiveCatalog);
        if (pending.Count == 0)
        {
            WarnAboutSkippedMedia(skipped, suppressNotifications);
            return true;
        }

        if (!provider.SupportsArtifactHosting)
        {
            UploadStatusMessage = GetLocalizedString(
                "Tools.PublisherStudio.Publish.MediaHostingNotSupported",
                "Provider does not support media hosting. Use direct image/video URLs or switch providers.");
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
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;
            if (!await UploadSingleMediaAsync(provider, item, current, total, cancellationToken, suppressNotifications, progressOffset, progressTotal))
            {
                return false;
            }
        }

        WarnAboutSkippedMedia(skipped, suppressNotifications);
        return true;
    }

    private void WarnAboutSkippedMedia(List<SkippedMediaReference> skipped, bool suppressNotifications)
    {
        foreach (var skippedItem in skipped)
        {
            logger.LogWarning(
                "Skipping unresolvable gallery media reference {Reference} for content {ContentId}",
                skippedItem.Reference,
                skippedItem.ContentId);
        }

        if (skipped.Count > 0 && !suppressNotifications)
        {
            notificationService?.ShowWarning(
                GetLocalizedString(
                    "Tools.PublisherStudio.Publish.MediaSkippedTitle",
                    "Unresolved Media Skipped"),
                FormatLocalizedString(
                    "Tools.PublisherStudio.Publish.MediaSkippedFormat",
                    "{0} media reference(s) could not be resolved to local files and were left unchanged.",
                    skipped.Count));
        }
    }

    private (List<PendingMediaUpload> Pending, List<SkippedMediaReference> Skipped) CollectPendingMedia(NamedCatalog catalog)
    {
        var pending = new List<PendingMediaUpload>();
        var skipped = new List<SkippedMediaReference>();
        var projectDirectory = Path.GetDirectoryName(project.ProjectPath);
        foreach (var content in catalog.Catalog.Content ?? [])
        {
            CollectContentMedia(pending, skipped, content, projectDirectory);
            var releases = content.Releases ?? [];
            for (var releaseIndex = 0; releaseIndex < releases.Count; releaseIndex++)
            {
                var release = releases[releaseIndex];
                CollectReleaseMedia(pending, skipped, content.Id, $"release-{releaseIndex}", release.ImageUrls, release.VideoUrls, projectDirectory);
            }

            var addonReleases = content.AddonReleases ?? [];
            for (var addonIndex = 0; addonIndex < addonReleases.Count; addonIndex++)
            {
                var addon = addonReleases[addonIndex];
                CollectReleaseMedia(pending, skipped, content.Id, $"addon-{addonIndex}", addon.ImageUrls, addon.VideoUrls, projectDirectory);
            }
        }

        return (pending, skipped);
    }

    private void CollectContentMedia(List<PendingMediaUpload> pending, List<SkippedMediaReference> skipped, CatalogContentItem content, string? projectDirectory)
    {
        var metadata = content.Metadata;
        if (metadata == null)
        {
            return;
        }

        AddPendingMediaList(pending, skipped, content.Id, "metadata", "screenshots", metadata.ScreenshotUrls, projectDirectory, (index, url) => metadata.ScreenshotUrls[index] = url);
        AddPendingMediaList(pending, skipped, content.Id, "metadata", "videos", metadata.VideoUrls, projectDirectory, (index, url) => metadata.VideoUrls[index] = url);
        AddPendingMediaUrl(pending, skipped, content.Id, "metadata", "trailer", 0, metadata.VideoUrl, projectDirectory, url => metadata.VideoUrl = url);
    }

    private void CollectReleaseMedia(
        List<PendingMediaUpload> pending,
        List<SkippedMediaReference> skipped,
        string contentId,
        string scope,
        List<string>? imageUrls,
        List<string>? videoUrls,
        string? projectDirectory)
    {
        if (imageUrls != null)
        {
            AddPendingMediaList(pending, skipped, contentId, scope, "images", imageUrls, projectDirectory, (index, url) => imageUrls[index] = url);
        }

        if (videoUrls != null)
        {
            AddPendingMediaList(pending, skipped, contentId, scope, "videos", videoUrls, projectDirectory, (index, url) => videoUrls[index] = url);
        }
    }

    private void AddPendingMediaList(
        List<PendingMediaUpload> pending,
        List<SkippedMediaReference> skipped,
        string contentId,
        string scope,
        string kind,
        List<string>? urls,
        string? projectDirectory,
        Action<int, string> applyUrl)
    {
        if (urls == null)
        {
            return;
        }

        for (var index = 0; index < urls.Count; index++)
        {
            var capturedIndex = index;
            AddPendingMediaUrl(pending, skipped, contentId, scope, kind, capturedIndex, urls[index], projectDirectory, url => applyUrl(capturedIndex, url));
        }
    }

    private void AddPendingMediaUrl(
        List<PendingMediaUpload> pending,
        List<SkippedMediaReference> skipped,
        string contentId,
        string scope,
        string kind,
        int index,
        string? value,
        string? projectDirectory,
        Action<string> applyUrl)
    {
        if (string.IsNullOrWhiteSpace(value) || MediaFileHelper.IsRemoteHttpUrl(value))
        {
            return;
        }

        var localPath = MediaFileHelper.TryResolveLocalMediaPath(projectDirectory, value);
        if (localPath == null)
        {
            skipped.Add(new SkippedMediaReference(contentId, value));
            return;
        }

        var fileName = Path.GetFileName(localPath);
        pending.Add(new PendingMediaUpload(contentId, fileName, localPath, $"{contentId}-media-{scope}-{kind}-{index}-{fileName}", applyUrl));
    }

    private async Task<bool> UploadSingleMediaAsync(
        IHostingProvider provider,
        PendingMediaUpload item,
        int current,
        int total,
        CancellationToken cancellationToken,
        bool suppressNotifications = false,
        int progressOffset = 0,
        int progressTotal = 0)
    {
        UploadStatusMessage = FormatLocalizedString(
            "Tools.PublisherStudio.Publish.UploadingMediaFormat",
            "Uploading media {0}/{1}: {2}",
            current,
            total,
            item.DisplayName);

        if (!File.Exists(item.LocalPath))
        {
            var msg = FormatLocalizedString(
                "Tools.PublisherStudio.Publish.MediaFileMissingFormat",
                "Media file not found: {0}",
                item.LocalPath);
            return FailMediaUpload(msg, suppressNotifications);
        }

        var combinedTotal = progressTotal > 0 ? progressTotal : total;
        var progress = new Progress<int>(p =>
        {
            ReportPendingUploadProgress(progressOffset + current - 1, combinedTotal, p);
        });
        var (result, fileReadFailed) = await UploadLocalFileAsync(provider, item.LocalPath, item.RemoteFileName, progress, cancellationToken);
        if (fileReadFailed)
        {
            var msg = FormatLocalizedString(
                "Tools.PublisherStudio.Publish.MediaFileMissingFormat",
                "Media file not found: {0}",
                item.LocalPath);
            return FailMediaUpload(msg, suppressNotifications);
        }

        if (!result.Success || result.Data == null || string.IsNullOrWhiteSpace(result.Data.DirectDownloadUrl))
        {
            var msg = FormatLocalizedString(
                "Tools.PublisherStudio.Publish.MediaUploadFailedFormat",
                "Failed to upload media {0}: {1}",
                item.DisplayName,
                result.FirstError);
            return FailMediaUpload(msg, suppressNotifications);
        }

        item.ApplyUrl(result.Data.DirectDownloadUrl);
        return true;
    }

    private bool FailMediaUpload(string message, bool suppressNotifications = false)
    {
        UploadStatusMessage = message;
        if (!suppressNotifications)
        {
            notificationService?.ShowError(
                GetLocalizedString("Tools.PublisherStudio.Publish.MediaUploadFailedTitle", "Media Upload Failed"),
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
        catalogEntry.FileName = ActiveCatalog != null
            ? ResolveUniqueCatalogFileName(project, ActiveCatalog, GetPersistedCatalogFileNames(_currentHostingState, catalogId))
            : $"catalog-{catalogId}.json";
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
            // A file ID still referenced by another catalog must be kept: deleting it would
            // break that catalog's subscribers. This happens when a previous filename
            // collision merged two catalogs into one remote file.
            if (!IsFileIdSharedWithOtherCatalog(catalogId, previousFileId))
            {
                await DeleteOrphanedRemoteFileAsync(previousFileId, catalogFileId, cancellationToken);
            }
            else
            {
                logger.LogInformation("Skipping deletion of shared remote file {FileId} still referenced by another catalog", previousFileId);
            }
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
        catch (OperationCanceledException)
        {
            NotifyDefinitionStale();
            throw;
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
                notificationService?.ShowSuccess(successTitle, successMessage, autoDismissMs: NotificationDurations.Short);
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
            // Generate first if not already done; a clipboard copy is not an export.
            await ExportCatalogCoreAsync(trackTelemetry: false);
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
            TrackCatalogPublished(catalog.Name, uploadResult);
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
        var combinedError = string.Join(Environment.NewLine, failedCatalogs.Select(f => $"{f.Name}: {f.Error}"));
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

    private void TrackCatalogPublished(string catalogName, OperationResult<HostingUploadResult> result)
    {
        var properties = new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.PublisherName] = project.Catalog.Publisher?.Name ?? project.ProjectName,
            [TelemetryConstants.Properties.ContentName] = catalogName,
            [TelemetryConstants.Properties.Success] = result.Success,
            [TelemetryConstants.Properties.ProviderType] = SelectedHostingProvider?.ProviderId,
        };

        if (!result.Success && !string.IsNullOrWhiteSpace(result.FirstError))
        {
            properties[TelemetryConstants.Properties.ErrorMessage] = result.FirstError;
        }

        telemetryService?.TrackEvent(TelemetryConstants.Events.PublisherStudioPublished, properties);
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
            TrackCatalogPublished(catalog.Name, res);
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
        if (defResult.Success)
        {
            await SyncLocalSubscriptionMetadataAsync();
        }

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

        // When the local profile is empty, a sync doubles as a restore: pull the discovered
        // definition and its catalogs so the project reflects what is actually hosted.
        if (await TryAutoRestoreCloudDefinitionAsync(ct, resolveShareableUrl: true))
        {
            await SaveAllHostingStatesAsync(ct);
            return;
        }

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
    /// Restores the discovered cloud publisher definition and its catalogs into the project when
    /// the local publisher profile is empty (for example after local data was wiped) and cloud
    /// storage holds a published definition.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="resolveShareableUrl">True to resolve a shareable URL on demand when the
    /// discovered definition has none yet; false keeps background scans side-effect free.</param>
    /// <returns>True when the cloud publisher definition was successfully restored and populated the local profile; false otherwise.</returns>
    private async Task<bool> TryAutoRestoreCloudDefinitionAsync(CancellationToken cancellationToken, bool resolveShareableUrl)
    {
        if (!string.IsNullOrWhiteSpace(project.Catalog?.Publisher?.Id))
        {
            return false;
        }

        var target = DiscoveredCloudDefinition;
        if (target == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(target.Url) &&
            (!resolveShareableUrl || !await EnsureAssetDownloadUrlAsync(target, cancellationToken)))
        {
            return false;
        }

        try
        {
            await LoadDefinitionToProjectAsync(target);

            // Download and validation failures inside the loader return normally, so only
            // report a restore when the publisher profile was actually populated.
            return !string.IsNullOrWhiteSpace(project.Catalog?.Publisher?.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException
            or System.Text.Json.JsonException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            logger.LogWarning(ex, "Automatic restore of cloud publisher definition failed; manual restore remains available");
            return false;
        }
    }

    /// <summary>
    /// Resolves a shareable download URL on demand for a discovered asset that has none yet
    /// (for example a Dropbox file with no shared link) and persists it into hosting state.
    /// </summary>
    /// <param name="asset">The hosted asset item to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the asset has a usable download URL afterwards.</returns>
    private async Task<bool> EnsureAssetDownloadUrlAsync(HostedAssetItemViewModel asset, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(asset.Url))
        {
            return true;
        }

        if (SelectedHostingProvider == null || string.IsNullOrWhiteSpace(asset.FileId) || string.IsNullOrWhiteSpace(asset.Name))
        {
            return false;
        }

        try
        {
            var result = await SelectedHostingProvider.EnsureShareableDownloadUrlAsync(asset.FileId, asset.Name, cancellationToken);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Data))
            {
                logger.LogWarning(
                    "Provider {Provider} could not resolve a shareable URL for {File}: {Error}",
                    SelectedHostingProvider.DisplayName,
                    asset.Name,
                    result.FirstError);
                return false;
            }

            asset.Url = result.Data;
            PersistResolvedAssetUrl(asset);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve a shareable URL for {File}", asset.Name);
            return false;
        }
    }

    /// <summary>
    /// Writes a resolved asset URL back into the persisted hosting state entries so later
    /// scans and inventory rebuilds keep the shareable link.
    /// </summary>
    /// <param name="asset">The hosted asset item carrying the resolved URL.</param>
    private void PersistResolvedAssetUrl(HostedAssetItemViewModel asset)
    {
        if (_currentHostingState == null || string.IsNullOrWhiteSpace(asset.Url))
        {
            return;
        }

        foreach (var definition in _currentHostingState.Definitions.Where(d =>
            (!string.IsNullOrEmpty(d.FileId) && !string.IsNullOrEmpty(asset.FileId) && string.Equals(d.FileId, asset.FileId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(d.FileName) && string.Equals(d.FileName, asset.Name, StringComparison.OrdinalIgnoreCase))))
        {
            definition.Url = asset.Url;
        }

        foreach (var catalog in _currentHostingState.Catalogs.Where(c =>
            (!string.IsNullOrEmpty(c.FileId) && !string.IsNullOrEmpty(asset.FileId) && string.Equals(c.FileId, asset.FileId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(c.FileName) && string.Equals(c.FileName, asset.Name, StringComparison.OrdinalIgnoreCase))))
        {
            catalog.Url = asset.Url;
        }
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
                await TryAutoRestoreCloudDefinitionAsync(silentCt, resolveShareableUrl: false);
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

        var existing = _currentHostingState.Definitions.FirstOrDefault(d => IsMatchingDefinition(d, cloudDef));

        if (existing != null)
        {
            UpdateExistingDefinition(existing, cloudDef);
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
        HasDefinitionChanges = false;
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
    /// Sorts the hosted asset inventory by the given column, toggling direction on repeat clicks.
    /// </summary>
    /// <param name="column">The sort column ("Name" or "Size").</param>
    [RelayCommand]
    private void SetInventorySort(string? column)
    {
        column ??= string.Empty;
        if (string.Equals(InventorySortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            InventorySortDescending = !InventorySortDescending;
        }
        else
        {
            InventorySortColumn = column;
            InventorySortDescending = false;
        }

        ApplyHostedAssetFilter();
    }

    /// <summary>
    /// Deletes a hosted asset from cloud storage after confirmation, then repairs
    /// every local reference (catalog entries, definition listings, hosting state)
    /// and re-uploads the affected manifests so subscribers stay consistent.
    /// </summary>
    /// <param name="asset">The hosted asset to delete.</param>
    /// <param name="cancellationToken">Token to cancel the delete operation.</param>
    [RelayCommand]
    private async Task DeleteHostedAssetAsync(HostedAssetItemViewModel? asset, CancellationToken cancellationToken)
    {
        if (asset == null || asset.IsDeleting)
        {
            return;
        }

        if (!asset.CanDelete || (!asset.IsOnline && !asset.IsExternalCdn))
        {
            notificationService?.ShowInfo(
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteNotHostedTitle", "Nothing To Delete"),
                FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteNotHostedMessageFormat", "'{0}' is not hosted yet, so there is nothing to delete from cloud storage.", asset.Name));
            return;
        }

        if (IsUploading || IsScanningStorage)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteBusyTitle", "Operation In Progress"),
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteBusyMessage", "Please wait for the current upload or scan to finish before deleting."));
            return;
        }

        await EnsureHostingStatesLoadedAsync(cancellationToken);

        if (asset.IsDefinition)
        {
            await DeleteDefinitionAssetAsync(asset, cancellationToken);
        }
        else if (asset.IsCatalog)
        {
            await DeleteCatalogAssetAsync(asset, cancellationToken);
        }
        else if (asset.IsMedia)
        {
            await DeleteMediaAssetAsync(asset, cancellationToken);
        }
        else
        {
            await DeleteArtifactAssetAsync(asset, cancellationToken);
        }
    }

    private async Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        if (ConfirmationCallback != null)
        {
            return await ConfirmationCallback(title, message);
        }

        if (dialogService != null)
        {
            return await dialogService.ShowConfirmationAsync(
                title,
                message,
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteAction", "Delete"),
                GetLocalizedString("Tools.PublisherStudio.Hosting.Cancel", "Cancel"));
        }

        logger.LogWarning("Delete refused: no confirmation mechanism is available");
        return false;
    }

    private bool RequireAnyProviderForDelete()
    {
        if (HostingProviders.Any(p => p.IsAuthenticated))
        {
            return true;
        }

        notificationService?.ShowWarning(
            GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteProviderRequiredTitle", "Provider Not Connected"),
            GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteProviderRequiredMessage", "Connect to your hosting provider before deleting cloud files."));
        return false;
    }

    private async Task DeleteDefinitionAssetAsync(HostedAssetItemViewModel asset, CancellationToken cancellationToken)
    {
        var isActive = IsActiveDefinitionAsset(asset);
        var title = GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteDefinitionTitle", "Delete Hosted Definition?");
        var message = isActive
            ? FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteActiveDefinitionMessageFormat", "'{0}' is your published provider definition. Subscribers use it to discover catalog updates, so deleting it breaks update checks until you republish. Delete '{0}' from cloud storage?", asset.Name)
            : FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteDefinitionMessageFormat", "Delete '{0}' from cloud storage? It is not referenced by the current project.", asset.Name);

        if (!await ConfirmDeleteAsync(title, message))
        {
            return;
        }

        if (!RequireAnyProviderForDelete())
        {
            return;
        }

        asset.IsDeleting = true;
        try
        {
            var remoteDeleted = await DeleteRemoteFileAsync(asset.FileId, asset.Url, cancellationToken);
            if (!remoteDeleted)
            {
                NotifyRemoteDeleteFailed(asset);
                return;
            }

            PruneDefinitionStateEntries(asset);
            await SaveAllHostingStatesAsync(cancellationToken);

            if (isActive)
            {
                ProviderDefinitionUrl = string.Empty;
                GenerateSubscriptionUrl();
                HasDefinitionChanges = true;
                NotifyDefinitionStale();
            }

            if (SaveProjectCallback != null)
            {
                await SaveProjectCallback();
            }

            _linkageIndex = null;
            RefreshHostedAssets();
            RefreshUploadHierarchy();

            var deletedMessage = FormatLocalizedString("Tools.PublisherStudio.Hosting.DeletedDefinitionMessageFormat", "'{0}' was deleted from cloud storage.", asset.Name);
            if (isActive)
            {
                deletedMessage += " " + GetLocalizedString("Tools.PublisherStudio.Hosting.DeletedDefinitionRepublishNote", "Republish the provider definition to restore subscriber updates.");
            }

            notificationService?.ShowSuccess(
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteSuccessTitle", "Deleted"),
                deletedMessage,
                NotificationDurations.Long);
        }
        finally
        {
            asset.IsDeleting = false;
        }
    }

    private bool IsActiveDefinitionAsset(HostedAssetItemViewModel asset)
    {
        var active = _currentHostingState?.Definition;
        if (active != null)
        {
            if (!string.IsNullOrWhiteSpace(asset.FileId) && string.Equals(asset.FileId, active.FileId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(asset.Url) && string.Equals(asset.Url, active.Url, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(asset.Url) && string.Equals(asset.Url, ProviderDefinitionUrl, StringComparison.OrdinalIgnoreCase);
    }

    private void PruneDefinitionStateEntries(HostedAssetItemViewModel asset)
    {
        foreach (var state in _hostingStates.Values)
        {
            if (state.Definition != null && MatchesHostedFile(state.Definition, asset.Url, asset.FileId, asset.Name))
            {
                state.Definition = null;
            }

            state.Definitions.RemoveAll(d => MatchesHostedFile(d, asset.Url, asset.FileId, asset.Name));
        }
    }

    private bool MatchesHostedFile(HostedFileInfo entry, string? url, string? fileId, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileId) && string.Equals(entry.FileId, fileId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(url) && string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(fileId) &&
            !string.IsNullOrWhiteSpace(fileName) && string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyRemoteDeleteFailed(HostedAssetItemViewModel asset)
    {
        notificationService?.ShowError(
            GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteRemoteFailedTitle", "Delete Failed"),
            FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteRemoteFailedMessageFormat", "Could not delete '{0}' from cloud storage. The remote file could not be located or the provider rejected the request. No local changes were made.", asset.Name));
    }

    private async Task DeleteCatalogAssetAsync(HostedAssetItemViewModel asset, CancellationToken cancellationToken)
    {
        var projectCatalog = FindProjectCatalog(asset);
        if (projectCatalog != null && project.Catalogs.Count <= 1)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteLastCatalogTitle", "Cannot Delete Last Catalog"),
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteLastCatalogMessage", "A project needs at least one catalog. Remove its contents instead, or create a replacement catalog first."));
            return;
        }

        var (title, message, orphans, definitionWillUpdate) = BuildCatalogDeleteImpact(asset, projectCatalog);
        if (!await ConfirmDeleteAsync(title, message))
        {
            return;
        }

        if (!RequireAnyProviderForDelete())
        {
            return;
        }

        asset.IsDeleting = true;
        try
        {
            await ExecuteCatalogDeleteAsync(asset, projectCatalog, orphans, definitionWillUpdate, cancellationToken);
        }
        finally
        {
            asset.IsDeleting = false;
        }
    }

    private NamedCatalog? FindProjectCatalog(HostedAssetItemViewModel asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.CatalogId))
        {
            var byId = project.Catalogs.FirstOrDefault(c => string.Equals(c.Id, asset.CatalogId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                return byId;
            }
        }

        return project.Catalogs.FirstOrDefault(c => string.Equals(c.FileName, asset.Name, StringComparison.OrdinalIgnoreCase));
    }

    private (string Title, string Message, IReadOnlyList<string> Orphans, bool DefinitionWillUpdate) BuildCatalogDeleteImpact(
        HostedAssetItemViewModel asset,
        NamedCatalog? projectCatalog)
    {
        var title = FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteCatalogTitleFormat", "Delete Catalog '{0}'?", projectCatalog?.Name ?? asset.Name);
        if (projectCatalog == null)
        {
            var cloudOnly = FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteCatalogCloudOnlyMessageFormat", "Delete '{0}' from cloud storage? It is not part of the current project.", asset.Name);
            return (title, cloudOnly, [], false);
        }

        var orphans = FindCatalogOrphanedFiles(projectCatalog);
        var hadPublishedUrl = _hostingStates.Values
            .SelectMany(s => s.Catalogs)
            .Any(c => string.Equals(c.CatalogId, projectCatalog.Id, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Url));
        var definitionWillUpdate = IsDefinitionPublished && hadPublishedUrl && project.Catalogs.Count > 1;

        var lines = new System.Text.StringBuilder();
        lines.Append(FormatLocalizedString(
            "Tools.PublisherStudio.Hosting.DeleteCatalogContentsLineFormat",
            "{0} content item(s), {1} release(s), {2} file(s) and {3} media link(s) will be removed from the project.",
            projectCatalog.Catalog.Content.Count,
            projectCatalog.Catalog.Content.Sum(c => c.Releases.Count + c.AddonReleases.Count),
            projectCatalog.Catalog.Content.Sum(c => c.Releases.SelectMany(r => r.Artifacts).Count() + c.AddonReleases.SelectMany(r => r.Artifacts).Count()),
            projectCatalog.Catalog.Content.Sum(CountMediaUrls)));
        if (definitionWillUpdate)
        {
            lines.Append(' ');
            lines.Append(GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteCatalogDefinitionLine", "The provider definition will be updated and re-uploaded without this catalog."));
        }

        if (orphans.Count > 0)
        {
            lines.Append(' ');
            lines.Append(FormatLocalizedString(
                "Tools.PublisherStudio.Hosting.DeleteCatalogOrphansLineFormat",
                "{0} file(s) on Drive are only used by this catalog and will become unlinked (kept on Drive): {1}.",
                orphans.Count,
                string.Join(", ", orphans.Take(5)) + (orphans.Count > 5 ? " " + FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToMoreRefsFormat", "+{0} more", orphans.Count - 5) : string.Empty)));
        }

        return (title, lines.ToString(), orphans, definitionWillUpdate);
    }

    private IReadOnlyList<string> FindCatalogOrphanedFiles(NamedCatalog removed)
    {
        var removedUrls = CollectCatalogFileUrls(removed);
        if (removedUrls.Count == 0)
        {
            return [];
        }

        var otherUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in project.Catalogs)
        {
            if (!ReferenceEquals(catalog, removed))
            {
                otherUrls.UnionWith(CollectCatalogFileUrls(catalog));
            }
        }

        var orphans = new List<string>();
        foreach (var url in removedUrls.Where(url => !otherUrls.Contains(url)))
        {
            var entry = _currentHostingState?.Artifacts.FirstOrDefault(a => string.Equals(a.Url, url, StringComparison.OrdinalIgnoreCase));
            orphans.Add(entry?.FileName ?? DescribeOrphanUrl(url));
        }

        return orphans;
    }

    private string DescribeOrphanUrl(string url)
    {
        var refs = FindReferencesByUrl(url);
        if (refs is { Count: > 0 } && !string.IsNullOrWhiteSpace(refs[0].ContentName))
        {
            return $"{refs[0].ContentName} ({GetMediaSlotLabel(refs[0].Slot)})";
        }

        return FileNameFromUrl(url, url);
    }

    private HashSet<string> CollectCatalogFileUrls(NamedCatalog catalog)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in catalog.Catalog.Content)
        {
            foreach (var release in content.Releases.Concat(content.AddonReleases))
            {
                foreach (var artifact in release.Artifacts)
                {
                    AddCloudUrl(urls, artifact.DownloadUrl);
                }

                foreach (var url in release.ImageUrls.Concat(release.VideoUrls))
                {
                    AddCloudUrl(urls, url);
                }
            }

            var metadata = content.Metadata;
            if (metadata != null)
            {
                foreach (var url in metadata.ScreenshotUrls.Concat(metadata.VideoUrls))
                {
                    AddCloudUrl(urls, url);
                }

                AddCloudUrl(urls, metadata.VideoUrl);
                AddCloudUrl(urls, metadata.IconUrl);
                AddCloudUrl(urls, metadata.BannerUrl);
                AddCloudUrl(urls, metadata.BackdropUrl);
            }
        }

        return urls;
    }

    private void AddCloudUrl(HashSet<string> urls, string? url)
    {
        if (IsValidHttpUrl(url) && IsCloudProviderUrl(url))
        {
            urls.Add(url!);
        }
    }

    private async Task ExecuteCatalogDeleteAsync(
        HostedAssetItemViewModel asset,
        NamedCatalog? projectCatalog,
        IReadOnlyList<string> orphans,
        bool definitionWillUpdate,
        CancellationToken cancellationToken)
    {
        var catalogId = projectCatalog?.Id ?? asset.CatalogId;
        if (string.IsNullOrWhiteSpace(catalogId))
        {
            catalogId = DetermineCatalogIdFromFileName(asset.Name);
        }

        var remotesCleaned = await DeleteCatalogRemotesAsync(catalogId, cancellationToken);

        if (projectCatalog != null)
        {
            project.Catalogs.Remove(projectCatalog);
            project.IsDirty = true;
            if (SaveProjectCallback != null)
            {
                await SaveProjectCallback();
            }

            if (ProjectReloadCallback != null)
            {
                await ProjectReloadCallback();
            }
        }

        _linkageIndex = null;
        var definitionFixed = await RepublishDefinitionAfterDeleteAsync(definitionWillUpdate, cancellationToken);
        RefreshHostedAssets();
        RefreshUploadHierarchy();
        RefreshArtifactStatuses();
        await ValidateCatalogAsync();

        NotifyCatalogDeleteResult(asset, projectCatalog, orphans, remotesCleaned, definitionFixed);
    }

    private void NotifyCatalogDeleteResult(
        HostedAssetItemViewModel asset,
        NamedCatalog? projectCatalog,
        IReadOnlyList<string> orphans,
        bool remotesCleaned,
        bool definitionFixed)
    {
        var displayName = projectCatalog?.Name ?? asset.Name;
        if (!remotesCleaned)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteSuccessTitle", "Deleted"),
                FormatLocalizedString("Tools.PublisherStudio.Hosting.DeletedCatalogPartialFormat", "Catalog '{0}' was removed locally, but some remote files could not be deleted. Rescan and retry from the inventory.", displayName),
                NotificationDurations.Long);
            return;
        }

        var message = new System.Text.StringBuilder(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeletedCatalogMessageFormat", "Catalog '{0}' was deleted.", displayName));
        if (orphans.Count > 0)
        {
            message.Append(' ');
            message.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteOrphansKeptNoteFormat", "{0} orphaned file(s) were kept on Drive and can be deleted individually.", orphans.Count));
        }

        if (!definitionFixed)
        {
            message.Append(' ');
            message.Append(GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteDefinitionStaleNote", "The provider definition could not be re-uploaded and is marked for publishing."));
        }

        notificationService?.ShowSuccess(
            GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteSuccessTitle", "Deleted"),
            message.ToString(),
            NotificationDurations.Long);
    }

    private async Task<bool> RepublishDefinitionAfterDeleteAsync(bool shouldFix, CancellationToken cancellationToken)
    {
        if (!shouldFix)
        {
            return true;
        }

        var (acquired, cts) = await TryBeginPublishAsync().ConfigureAwait(false);
        if (!acquired || cts == null)
        {
            NotifyDefinitionStale();
            return false;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            var result = await UploadProviderDefinitionCoreAsync(linked.Token, manageUploadingState: false, suppressNotifications: true);
            if (result.Success)
            {
                await SyncLocalSubscriptionMetadataAsync();
                return true;
            }

            NotifyDefinitionStale();
            return false;
        }
        catch (OperationCanceledException)
        {
            NotifyDefinitionStale();
            return false;
        }
        finally
        {
            EndPublish(cts);
        }
    }

    private async Task DeleteArtifactAssetAsync(HostedAssetItemViewModel asset, CancellationToken cancellationToken)
    {
        var refs = FindReferencesByUrl(asset.Url);
        if (refs is not { Count: > 0 })
        {
            await DeleteUnlinkedCloudFileAsync(asset, cancellationToken);
            return;
        }

        var emptied = FindEmptiedReleases(asset.Url!);
        var title = asset.IsExternalCdn
            ? FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteExternalTitleFormat", "Remove Link '{0}'?", asset.Name)
            : FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteArtifactTitleFormat", "Delete '{0}'?", asset.Name);
        var message = BuildArtifactDeleteMessage(asset, refs, emptied);

        if (!await ConfirmDeleteAsync(title, message))
        {
            return;
        }

        if (asset.IsOnline && !RequireAnyProviderForDelete())
        {
            return;
        }

        asset.IsDeleting = true;
        try
        {
            await ExecuteArtifactDeleteAsync(asset, refs, emptied, cancellationToken);
        }
        finally
        {
            asset.IsDeleting = false;
        }
    }

    private string BuildArtifactDeleteMessage(HostedAssetItemViewModel asset, IReadOnlyList<AssetReference> refs, IReadOnlyList<(string ContentName, string Version)> emptied)
    {
        var lines = new System.Text.StringBuilder();
        if (asset.IsExternalCdn)
        {
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteExternalMessageFormat", "'{0}' is hosted on an external CDN, so there is no remote file to delete. Remove its link from {1} release(s)?", asset.Name, refs.Count));
        }
        else
        {
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteArtifactMessageFormat", "Delete '{0}' ({1}) from cloud storage?", asset.Name, asset.FileSizeFormatted));
            lines.Append(' ');
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteArtifactConsequenceLineFormat", "The file will be removed from {0} release(s); affected catalogs will be updated and re-uploaded.", refs.Count));
        }

        lines.Append(' ');
        lines.Append(GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteArtifactUsedByLine", "Used by:"));
        lines.Append(' ');
        lines.Append(string.Join("; ", refs.Take(4).Select(FormatReferenceText)));
        if (refs.Count > 4)
        {
            lines.Append(' ');
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToMoreRefsFormat", "+{0} more", refs.Count - 4));
        }

        if (asset.IsOnline && HasNonArtifactReferences(refs))
        {
            lines.Append(' ');
            lines.Append(GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteSharedUrlWarningLine", "This file is also referenced in another way; the remote file will be kept and only the selected links will be removed."));
        }

        foreach (var (contentName, version) in emptied.Take(3))
        {
            lines.Append(' ');
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteArtifactEmptiedLineFormat", "Warning: release v{0} of '{1}' will be left without files.", version, contentName));
        }

        return lines.ToString();
    }

    private IReadOnlyList<(string ContentName, string Version)> FindEmptiedReleases(string url)
    {
        var emptied = new List<(string ContentName, string Version)>();
        foreach (var catalog in project.Catalogs)
        {
            foreach (var content in catalog.Catalog.Content)
            {
                foreach (var release in content.Releases.Concat(content.AddonReleases))
                {
                    if (release.Artifacts.Count > 0 && release.Artifacts.All(a => string.Equals(a.DownloadUrl, url, StringComparison.OrdinalIgnoreCase)))
                    {
                        emptied.Add((content.Name, release.Version));
                    }
                }
            }
        }

        return emptied;
    }

    private async Task ExecuteArtifactDeleteAsync(
        HostedAssetItemViewModel asset,
        IReadOnlyList<AssetReference> refs,
        IReadOnlyList<(string ContentName, string Version)> emptied,
        CancellationToken cancellationToken)
    {
        var remoteKeptShared = asset.IsOnline && HasNonArtifactReferences(refs);
        if (asset.IsOnline && !remoteKeptShared)
        {
            var remoteDeleted = await DeleteRemoteFileAsync(asset.FileId, asset.Url, cancellationToken);
            if (!remoteDeleted)
            {
                NotifyRemoteDeleteFailed(asset);
                return;
            }
        }

        var affected = RemoveArtifactReferences(asset.Url!);
        var (republished, republishFailed) = await FinalizeReferenceDeleteAsync(affected, asset, cancellationToken);
        await RefreshAfterDeleteAsync();

        var message = asset.IsExternalCdn
            ? new System.Text.StringBuilder(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeletedExternalMessageFormat", "'{0}' was unlinked from {1} release(s).", asset.Name, refs.Count))
            : new System.Text.StringBuilder(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeletedArtifactMessageFormat", "'{0}' was deleted and removed from {1} release(s).", asset.Name, refs.Count));
        AppendDeleteFollowUpNotes(message, emptied, republishFailed, republished, remoteKeptShared);
        NotifyReferenceDeleteResult(message, republishFailed);
    }

    private async Task<(int Republished, int Failed)> FinalizeReferenceDeleteAsync(
        IReadOnlyList<NamedCatalog> affected,
        HostedAssetItemViewModel asset,
        CancellationToken cancellationToken)
    {
        PruneArtifactStateEntries(asset.Url, asset.FileId, asset.Name);
        await SaveAllHostingStatesAsync(cancellationToken);

        foreach (var catalog in affected)
        {
            MarkCatalogChanged(catalog.Id);
        }

        project.IsDirty = true;
        if (SaveProjectCallback != null)
        {
            await SaveProjectCallback();
        }

        LibraryRefreshCallback?.Invoke();
        _linkageIndex = null;
        return await RepublishCatalogsAfterDeleteAsync(affected, cancellationToken);
    }

    private async Task RefreshAfterDeleteAsync()
    {
        RefreshHostedAssets();
        RefreshUploadHierarchy();
        RefreshArtifactStatuses();
        await ValidateCatalogAsync();
    }

    private void NotifyReferenceDeleteResult(System.Text.StringBuilder message, int republishFailed)
    {
        if (republishFailed > 0)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteSuccessTitle", "Deleted"),
                message.ToString(),
                NotificationDurations.Long);
        }
        else
        {
            notificationService?.ShowSuccess(
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteSuccessTitle", "Deleted"),
                message.ToString(),
                NotificationDurations.Long);
        }
    }

    private bool HasNonArtifactReferences(IReadOnlyList<AssetReference> refs) =>
        refs.Any(r => !string.Equals(r.Slot, MediaSlotArtifact, StringComparison.Ordinal));

    private bool HasArtifactReferences(IReadOnlyList<AssetReference> refs) =>
        refs.Any(r => string.Equals(r.Slot, MediaSlotArtifact, StringComparison.Ordinal));

    private void AppendDeleteFollowUpNotes(
        System.Text.StringBuilder message,
        IReadOnlyList<(string ContentName, string Version)> emptied,
        int republishFailed,
        int republished,
        bool remoteKeptShared = false)
    {
        if (remoteKeptShared)
        {
            message.Append(' ');
            message.Append(GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteRemoteKeptSharedNote", "The remote file was kept because other references still use it."));
        }

        foreach (var (contentName, version) in emptied.Take(3))
        {
            message.Append(' ');
            message.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteEmptiedReleaseNoteFormat", "Release v{0} of '{1}' now has no files. Add a replacement or delete the release.", version, contentName));
        }

        if (republishFailed > 0)
        {
            message.Append(' ');
            message.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteRepublishFailedNoteFormat", "{0} catalog(s) could not be re-uploaded and are marked for publishing.", republishFailed));
        }
        else if (republished > 0)
        {
            message.Append(' ');
            message.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteRepublishedNoteFormat", "{0} catalog(s) re-uploaded with the fix.", republished));
        }
    }

    private IReadOnlyList<NamedCatalog> RemoveArtifactReferences(string url)
    {
        var affected = new List<NamedCatalog>();
        foreach (var catalog in project.Catalogs)
        {
            var removedAny = false;
            foreach (var content in catalog.Catalog.Content)
            {
                removedAny |= RemoveUrlArtifacts(content.Releases, url);
                removedAny |= RemoveUrlArtifacts(content.AddonReleases, url);
            }

            if (removedAny)
            {
                affected.Add(catalog);
            }
        }

        return affected;
    }

    private bool RemoveUrlArtifacts(List<ContentRelease> releases, string url)
    {
        var removedAny = false;
        foreach (var release in releases)
        {
            var removed = release.Artifacts.RemoveAll(a => string.Equals(a.DownloadUrl, url, StringComparison.OrdinalIgnoreCase));
            removedAny |= removed > 0;
        }

        return removedAny;
    }

    private void PruneArtifactStateEntries(string? url, string? fileId, string? fileName)
    {
        foreach (var state in _hostingStates.Values)
        {
            state.Artifacts.RemoveAll(a => MatchesHostedFile(a, url, fileId, fileName));
        }
    }

    private async Task DeleteMediaAssetAsync(HostedAssetItemViewModel asset, CancellationToken cancellationToken)
    {
        var refs = FindReferencesByUrl(asset.Url);
        if (refs is not { Count: > 0 })
        {
            await DeleteUnlinkedCloudFileAsync(asset, cancellationToken);
            return;
        }

        var title = asset.IsExternalCdn
            ? FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteExternalTitleFormat", "Remove Link '{0}'?", asset.Name)
            : FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteMediaTitleFormat", "Delete Media '{0}'?", asset.Name);
        var lines = new System.Text.StringBuilder();
        if (asset.IsExternalCdn)
        {
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteExternalMediaMessageFormat", "'{0}' is hosted on an external CDN, so there is no remote file to delete. Remove its link from {1} place(s)?", asset.Name, refs.Count));
        }
        else
        {
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteMediaMessageFormat", "Delete '{0}' from cloud storage?", asset.Name));
            lines.Append(' ');
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteMediaConsequenceLineFormat", "The link will be removed from {0} place(s); affected catalogs will be updated and re-uploaded.", refs.Count));
        }

        lines.Append(' ');
        lines.Append(GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteArtifactUsedByLine", "Used by:"));
        lines.Append(' ');
        lines.Append(string.Join("; ", refs.Take(4).Select(FormatReferenceText)));
        if (refs.Count > 4)
        {
            lines.Append(' ');
            lines.Append(FormatLocalizedString("Tools.PublisherStudio.Hosting.LinkedToMoreRefsFormat", "+{0} more", refs.Count - 4));
        }

        if (asset.IsOnline && HasArtifactReferences(refs))
        {
            lines.Append(' ');
            lines.Append(GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteSharedUrlWarningLine", "This file is also referenced in another way; the remote file will be kept and only the selected links will be removed."));
        }

        if (!await ConfirmDeleteAsync(title, lines.ToString()))
        {
            return;
        }

        if (asset.IsOnline && !RequireAnyProviderForDelete())
        {
            return;
        }

        asset.IsDeleting = true;
        try
        {
            await ExecuteMediaDeleteAsync(asset, refs, cancellationToken);
        }
        finally
        {
            asset.IsDeleting = false;
        }
    }

    private async Task ExecuteMediaDeleteAsync(HostedAssetItemViewModel asset, IReadOnlyList<AssetReference> refs, CancellationToken cancellationToken)
    {
        var remoteKeptShared = asset.IsOnline && HasArtifactReferences(refs);
        if (asset.IsOnline && !remoteKeptShared)
        {
            var remoteDeleted = await DeleteRemoteFileAsync(asset.FileId, asset.Url, cancellationToken);
            if (!remoteDeleted)
            {
                NotifyRemoteDeleteFailed(asset);
                return;
            }
        }

        var affected = RemoveMediaReferences(asset.Url!);
        var (republished, republishFailed) = await FinalizeReferenceDeleteAsync(affected, asset, cancellationToken);
        await RefreshAfterDeleteAsync();

        var message = asset.IsExternalCdn
            ? new System.Text.StringBuilder(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeletedExternalMediaMessageFormat", "'{0}' was unlinked from {1} place(s).", asset.Name, refs.Count))
            : new System.Text.StringBuilder(FormatLocalizedString("Tools.PublisherStudio.Hosting.DeletedMediaMessageFormat", "'{0}' was deleted and unlinked from {1} place(s).", asset.Name, refs.Count));
        AppendDeleteFollowUpNotes(message, [], republishFailed, republished, remoteKeptShared);
        NotifyReferenceDeleteResult(message, republishFailed);
    }

    private IReadOnlyList<NamedCatalog> RemoveMediaReferences(string url)
    {
        var affected = new List<NamedCatalog>();
        foreach (var catalog in project.Catalogs)
        {
            var removedAny = false;
            foreach (var content in catalog.Catalog.Content)
            {
                removedAny |= RemoveContentMediaReferences(content, url);
                foreach (var release in content.Releases.Concat(content.AddonReleases))
                {
                    removedAny |= RemoveReleaseMediaReferences(release, url);
                }
            }

            if (removedAny)
            {
                affected.Add(catalog);
            }
        }

        return affected;
    }

    private bool RemoveContentMediaReferences(CatalogContentItem content, string url)
    {
        var metadata = content.Metadata;
        if (metadata == null)
        {
            return false;
        }

        var removedAny = metadata.ScreenshotUrls.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)) > 0;
        removedAny |= metadata.VideoUrls.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)) > 0;
        removedAny |= ClearMediaField(metadata, url);
        return removedAny;
    }

    private bool ClearMediaField(ContentRichMetadata metadata, string url)
    {
        var clearedAny = false;
        if (string.Equals(metadata.VideoUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            metadata.VideoUrl = null;
            clearedAny = true;
        }

        if (string.Equals(metadata.IconUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            metadata.IconUrl = null;
            clearedAny = true;
        }

        if (string.Equals(metadata.BannerUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            metadata.BannerUrl = null;
            clearedAny = true;
        }

        if (string.Equals(metadata.BackdropUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            metadata.BackdropUrl = null;
            clearedAny = true;
        }

        return clearedAny;
    }

    private bool RemoveReleaseMediaReferences(ContentRelease release, string url)
    {
        var removedAny = release.ImageUrls.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)) > 0;
        removedAny |= release.VideoUrls.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)) > 0;
        return removedAny;
    }

    private async Task DeleteUnlinkedCloudFileAsync(HostedAssetItemViewModel asset, CancellationToken cancellationToken)
    {
        if (!asset.IsOnline)
        {
            notificationService?.ShowInfo(
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteNotHostedTitle", "Nothing To Delete"),
                FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteNotHostedMessageFormat", "'{0}' is not hosted yet, so there is nothing to delete from cloud storage.", asset.Name));
            return;
        }

        var title = FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteUnlinkedTitleFormat", "Delete Unlinked File '{0}'?", asset.Name);
        var message = FormatLocalizedString("Tools.PublisherStudio.Hosting.DeleteUnlinkedMessageFormat", "'{0}' ({1}) is not referenced by any catalog. Delete it from cloud storage?", asset.Name, asset.FileSizeFormatted);
        if (!await ConfirmDeleteAsync(title, message))
        {
            return;
        }

        if (!RequireAnyProviderForDelete())
        {
            return;
        }

        asset.IsDeleting = true;
        try
        {
            var remoteDeleted = await DeleteRemoteFileAsync(asset.FileId, asset.Url, cancellationToken);
            if (!remoteDeleted)
            {
                NotifyRemoteDeleteFailed(asset);
                return;
            }

            PruneArtifactStateEntries(asset.Url, asset.FileId, asset.Name);
            await SaveAllHostingStatesAsync(cancellationToken);

            _linkageIndex = null;
            RefreshHostedAssets();
            RefreshUploadHierarchy();

            notificationService?.ShowSuccess(
                GetLocalizedString("Tools.PublisherStudio.Hosting.DeleteSuccessTitle", "Deleted"),
                FormatLocalizedString("Tools.PublisherStudio.Hosting.DeletedUnlinkedMessageFormat", "'{0}' was deleted from cloud storage.", asset.Name),
                NotificationDurations.Long);
        }
        finally
        {
            asset.IsDeleting = false;
        }
    }

    private async Task<bool> DeleteRemoteFileAsync(string? fileId, string? url, CancellationToken cancellationToken)
    {
        var (provider, resolvedId) = ResolveDeleteTarget(fileId, url);
        if (provider == null || string.IsNullOrWhiteSpace(resolvedId))
        {
            logger.LogWarning("Delete skipped: no remote file id could be resolved for {Url}", url);
            return false;
        }

        try
        {
            var result = await provider.DeleteFileAsync(resolvedId, cancellationToken);
            if (!result.Success)
            {
                logger.LogWarning("Remote delete failed for {FileId}: {Error}", resolvedId, result.FirstError);
            }

            return result.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Remote delete failed for {FileId}", resolvedId);
            return false;
        }
    }

    private (IHostingProvider? Provider, string? FileId) ResolveDeleteTarget(string? fileId, string? url)
    {
        var byFileId = FindOwningProviderByFileId(fileId);
        if (byFileId.Provider != null)
        {
            return byFileId;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return (null, null);
        }

        foreach (var (providerId, state) in _hostingStates)
        {
            var entryId = FindStateEntryIdByUrl(state, url);
            if (string.IsNullOrWhiteSpace(entryId))
            {
                continue;
            }

            var provider = FindAuthenticatedProvider(providerId);
            if (provider != null)
            {
                return (provider, entryId);
            }
        }

        return (null, null);
    }

    private (IHostingProvider? Provider, string? FileId) FindOwningProviderByFileId(string? fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return (null, null);
        }

        foreach (var (providerId, state) in _hostingStates)
        {
            if (!StateContainsFileId(state, fileId))
            {
                continue;
            }

            var provider = FindAuthenticatedProvider(providerId);
            if (provider != null)
            {
                return (provider, fileId);
            }
        }

        return (null, null);
    }

    private bool StateContainsFileId(HostingState state, string fileId)
    {
        return state.Artifacts.Any(a => string.Equals(a.FileId, fileId, StringComparison.OrdinalIgnoreCase))
            || state.Catalogs.Any(c => string.Equals(c.FileId, fileId, StringComparison.OrdinalIgnoreCase))
            || (state.Definition != null && string.Equals(state.Definition.FileId, fileId, StringComparison.OrdinalIgnoreCase))
            || state.Definitions.Any(d => string.Equals(d.FileId, fileId, StringComparison.OrdinalIgnoreCase));
    }

    private IHostingProvider? FindAuthenticatedProvider(string providerId)
    {
        var provider = HostingProviders.FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        return provider?.IsAuthenticated == true ? provider : null;
    }

    private string? FindStateEntryIdByUrl(HostingState state, string url)
    {
        var artifact = state.Artifacts.FirstOrDefault(a => string.Equals(a.Url, url, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(artifact?.FileId))
        {
            return artifact.FileId;
        }

        var catalog = state.Catalogs.FirstOrDefault(c => string.Equals(c.Url, url, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(catalog?.FileId))
        {
            return catalog.FileId;
        }

        if (!string.IsNullOrWhiteSpace(state.Definition?.FileId) && string.Equals(state.Definition.Url, url, StringComparison.OrdinalIgnoreCase))
        {
            return state.Definition.FileId;
        }

        return state.Definitions
            .Where(d => string.Equals(d.Url, url, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(d.FileId))
            .Select(d => d.FileId)
            .FirstOrDefault();
    }

    private async Task<(int Republished, int Failed)> RepublishCatalogsAfterDeleteAsync(IReadOnlyList<NamedCatalog> affected, CancellationToken cancellationToken)
    {
        var published = affected
            .Where(c => _currentHostingState?.Catalogs.Any(e =>
                string.Equals(e.CatalogId, c.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(e.Url)) == true)
            .ToList();
        if (published.Count == 0)
        {
            return (0, 0);
        }

        var (acquired, cts) = await TryBeginPublishAsync().ConfigureAwait(false);
        if (!acquired || cts == null)
        {
            return (0, published.Count);
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            var (succeeded, failed) = await PublishCatalogsAsync(published, linked.Token);
            return (succeeded, failed.Count);
        }
        catch (OperationCanceledException)
        {
            return (0, published.Count);
        }
        finally
        {
            EndPublish(cts);
        }
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
        if (asset == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(asset.Url))
        {
            using var resolveCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (!await EnsureAssetDownloadUrlAsync(asset, resolveCts.Token))
            {
                notificationService?.ShowError(
                    GetLocalizedString(LoadFailedTitleKey, LoadFailedDefaultTitle),
                    FormatLocalizedString("Tools.PublisherStudio.Hosting.LoadFailedFormat", "Failed to load {0}: {1}", asset.Name, GetLocalizedString("Tools.PublisherStudio.Hosting.NoShareableUrlError", "No shareable download URL is available.")));
                return;
            }
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

        if (loadedCatalogsCount > 0)
        {
            RemoveEmptyDefaultCatalogPlaceholder();
        }

        return loadedCatalogsCount;
    }

    /// <summary>
    /// Drops the migrated empty placeholder catalog once real cloud catalogs are attached, so a
    /// restored project does not keep a confusing empty "Content" entry next to its catalogs.
    /// </summary>
    private void RemoveEmptyDefaultCatalogPlaceholder()
    {
        if (project.Catalog == null || project.Catalogs.Count <= 1)
        {
            return;
        }

        var removed = false;
        for (var i = project.Catalogs.Count - 1; i >= 0; i--)
        {
            var candidate = project.Catalogs[i];
            if (candidate != null &&
                ReferenceEquals(candidate.Catalog, project.Catalog) &&
                string.Equals(candidate.Id, CatalogConstants.DefaultCatalogId, StringComparison.OrdinalIgnoreCase) &&
                (candidate.Catalog.Content == null || candidate.Catalog.Content.Count == 0))
            {
                project.Catalogs.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            SyncAvailableCatalogs();
        }
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

            var existingNamedCat = project.Catalogs.FirstOrDefault(c => string.Equals(c.Id, catRef.Id, StringComparison.OrdinalIgnoreCase));
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

        var catFileName = string.IsNullOrEmpty(asset.Name)
            ? HostingConstants.DefaultCatalogFileName
            : asset.Name;

        var catId = !string.IsNullOrWhiteSpace(asset.CatalogId)
            ? asset.CatalogId
            : DetermineCatalogIdFromFileName(catFileName);
        var catName = AttachCatalogToProject(pubCat, catFileName, catId);

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

    private string AttachCatalogToProject(PublisherCatalog pubCat, string catFileName, string catId)
    {
        var safeCatId = string.IsNullOrWhiteSpace(catId) ? "main" : catId;
        var catName = char.ToUpperInvariant(safeCatId[0]) + safeCatId[1..];

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

        RemoveEmptyDefaultCatalogPlaceholder();
        if (project.Catalog == null || project.Catalog.Content == null || project.Catalog.Content.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(pubCat.Publisher?.Id) &&
                project.Catalog?.Publisher != null &&
                !string.IsNullOrWhiteSpace(project.Catalog.Publisher.Id))
            {
                pubCat.Publisher = project.Catalog.Publisher;
            }

            project.Catalog = pubCat;
        }

        SyncAvailableCatalogs();

        return catName;
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
        catch (Exception ex) when (ex is HttpRequestException or IOException)
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
