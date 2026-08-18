using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.Content.ViewModels;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for a publisher card displaying content from a single publisher.
/// </summary>
public partial class PublisherCardViewModel : ObservableObject, IRecipient<ProfileCreatedMessage>, IRecipient<ProfileUpdatedMessage>, IRecipient<ProfileDeletedMessage>, IDisposable
{
    private readonly ILogger<PublisherCardViewModel> _logger;
    private readonly IContentOrchestrator _contentOrchestrator;
    private readonly IContentManifestPool _manifestPool;
    private readonly IGameClientProfileService _profileService;
    private readonly IProfileContentService _profileContentService;
    private readonly IGameProfileManager _profileManager;
    private readonly INotificationService _notificationService;
    private readonly IContentReconciliationService _reconciliationService;
    private readonly IContentVersionComparer _versionComparer;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _profileLock = new(1, 1);

    private bool _disposed;
    [ObservableProperty]
    private string _publisherId = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _iconPathData = string.Empty;

    [ObservableProperty]
    private string? _logoSource;

    [ObservableProperty]
    private ObservableCollection<GameProfile> _availableProfiles = [];

    private string _latestVersion = string.Empty;

    /// <summary>
    /// Gets or sets the latest version string.
    /// </summary>
    public string LatestVersion
    {
        get => _latestVersion;
        set
        {
            var displayVersion = GameVersionHelper.IsDefaultVersion(value) ? string.Empty : value;
            SetProperty(ref _latestVersion, displayVersion);
        }
    }

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private DateTime? _releaseDate;

    [ObservableProperty]
    private long? _downloadSize;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ContentTypeGroup> _contentTypes = [];

    private void ContentTypes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(ContentSummary));
    }

    [ObservableProperty]
    private bool _showContentSummary = true;

    [ObservableProperty]
    private string _primaryActionText = string.Empty;

    [ObservableProperty]
    private ICommand? _primaryActionCommand;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherCardViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="contentOrchestrator">The content orchestrator.</param>
    /// <param name="manifestPool">The manifest pool.</param>
    /// <param name="profileService">The game client profile service.</param>
    /// <param name="profileContentService">The profile content service.</param>
    /// <param name="profileManager">The profile manager.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="reconciliationService">The reconciliation service.</param>
    /// <param name="versionComparer">The publisher-aware version comparer.</param>
    public PublisherCardViewModel(
        ILogger<PublisherCardViewModel> logger,
        IContentOrchestrator contentOrchestrator,
        IContentManifestPool manifestPool,
        IGameClientProfileService profileService,
        IProfileContentService profileContentService,
        IGameProfileManager profileManager,
        INotificationService notificationService,
        IContentReconciliationService reconciliationService,
        IContentVersionComparer versionComparer)
    {
        _logger = logger;
        _versionComparer = versionComparer;
        _contentOrchestrator = contentOrchestrator;
        _manifestPool = manifestPool;
        _profileService = profileService;
        _profileContentService = profileContentService;
        _profileManager = profileManager;
        _notificationService = notificationService;
        _reconciliationService = reconciliationService;

        ContentTypes.CollectionChanged += ContentTypes_CollectionChanged;

        // Register for profile creation and deletion messages
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cts.Cancel();
            _cts.Dispose();
            _profileLock.Dispose();
            WeakReferenceMessenger.Default.UnregisterAll(this);
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Refreshes the list of available profiles.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshAvailableProfilesAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var token = linkedCts.Token;

        await _profileLock.WaitAsync(token);
        try
        {
            var profilesResult = await _profileManager.GetAllProfilesAsync(token);
            if (profilesResult.Success && profilesResult.Data != null)
            {
                AvailableProfiles.Clear();

                // Force complete refresh by getting fresh profile instances
                // This ensures icon changes and other updates are reflected in the UI
                foreach (var profileId in profilesResult.Data.Select(p => p.Id))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    var freshProfileResult = await _profileManager.GetProfileAsync(profileId, token);
                    if (freshProfileResult.Success && freshProfileResult.Data != null)
                    {
                        AvailableProfiles.Add(freshProfileResult.Data);
                    }
                }

                _logger.LogDebug("Refreshed available profiles: {Count} profiles", AvailableProfiles.Count);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh available profiles");
        }
        finally
        {
            _profileLock.Release();
        }
    }

    /// <summary>
    /// Receives profile creation messages and refreshes the available profiles dropdown.
    /// </summary>
    /// <param name="message">The profile created message.</param>
    public void Receive(ProfileCreatedMessage message)
    {
        _logger.LogDebug("Profile created: {Name}", message.Profile.Name);
        RefreshProfilesOnUiThread();
    }

    /// <summary>
    /// Receives profile update messages and refreshes the available profiles dropdown.
    /// </summary>
    /// <param name="message">The profile updated message.</param>
    public void Receive(ProfileUpdatedMessage message)
    {
        _logger.LogDebug("Profile updated: {Name}", message.Profile.Name);
        RefreshProfilesOnUiThread();
    }

    /// <summary>
    /// Receives profile deletion messages and refreshes the available profiles dropdown.
    /// </summary>
    /// <param name="message">The profile deleted message.</param>
    public void Receive(ProfileDeletedMessage message)
    {
        _logger.LogDebug("Profile deleted: {Name}", message.ProfileName);
        RefreshProfilesOnUiThread();
    }

    /// <summary>
    /// Called when IsExpanded changes - refresh profiles for the dropdown.
    /// </summary>
    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _ = RefreshAvailableProfilesAsync();
        }
    }

    /// <summary>
    /// Gets a value indicating whether there is any content available.
    /// </summary>
    public bool HasContent => ContentTypes?.Count > 0;

    /// <summary>
    /// Gets a summary of available content types (e.g., "2 Content", "3 Content").
    /// </summary>
    public string ContentSummary
    {
        get
        {
            if (ContentTypes == null || ContentTypes.Count == 0)
            {
                return string.Empty;
            }

            var totalCount = ContentTypes.Sum(g => g.Items.Count);

            if (totalCount == 0)
            {
                return string.Empty;
            }

            return totalCount == 1 ? "1 Content" : $"{totalCount} Content";
        }
    }

    /// <summary>
    /// Refreshes the installation status of all content items by checking the manifest pool.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshInstallationStatusAsync()
    {
        try
        {
            var result = await _manifestPool.GetAllManifestsAsync();
            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("Failed to retrieve manifests for installation status check: {Errors}", ManifestHelper.FormatErrors(result.Errors));
                return;
            }

            // Get all installed manifests
            var allManifests = result.Data.ToList();

            _logger.LogInformation(
                "Checking installation status for {PublisherId} - {ManifestCount} total manifests in pool",
                PublisherId,
                allManifests.Count);

            // Log all manifests for debugging
            foreach (var m in allManifests)
            {
                _logger.LogDebug(
                    "Installed manifest: Id={Id}, Version={Version}, Publisher={Publisher}, ContentType={ContentType}",
                    m.Id.Value,
                    m.Version,
                    m.Publisher?.PublisherType ?? "null",
                    m.ContentType);
            }

            // Update each content item's IsInstalled status
            foreach (var group in ContentTypes)
            {
                foreach (var item in group.Items)
                {
                    UpdateItemInstallationStatus(item, allManifests);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh installation status for {PublisherId}", PublisherId);
        }
    }

    private void UpdateItemInstallationStatus(ContentItemViewModel item, List<Core.Models.Manifest.ContentManifest> allManifests)
    {
        var variants = FindContentVariants(item, allManifests, PublisherId);
        UpdateItemVariants(item, variants);

        var isDownloaded = variants.Count > 0;
        item.IsDownloaded = isDownloaded;
        item.IsInstalled = isDownloaded;

        UpdateItemUpdateAvailability(item, variants, isDownloaded);
        UpdateItemResolutionVariants(item, variants);
        UpdateItemDependencies(item, variants);

        _logger.LogDebug(
            "Content item: {Name} v{Version} ({ContentType}) - Downloaded: {IsDownloaded}, Variants: {VariantCount}, Dependencies: {DependencyCount}",
            item.Name,
            item.Version,
            item.Model.ContentType,
            item.IsDownloaded,
            item.AvailableVariants.Count,
            item.RequiredDependencyNames.Count);
    }

    private static void UpdateItemVariants(ContentItemViewModel item, List<Core.Models.Manifest.ContentManifest> variants)
    {
        if (variants.Count > 0)
        {
            var currentIds = item.AvailableVariants.Select(v => v.Id.Value).ToHashSet();
            var newIds = variants.Select(v => v.Id.Value).ToHashSet();

            if (!currentIds.SetEquals(newIds))
            {
                item.AvailableVariants.Clear();
                foreach (var variant in variants)
                {
                    item.AvailableVariants.Add(variant);
                }
            }
        }
        else
        {
            item.AvailableVariants.Clear();
        }
    }

    private void UpdateItemUpdateAvailability(ContentItemViewModel item, List<Core.Models.Manifest.ContentManifest> variants, bool isDownloaded)
    {
        if (isDownloaded && !string.IsNullOrEmpty(item.Version))
        {
            var highestInstalledVersion = variants
                .Select(v => v.Version ?? string.Empty)
                .OrderByDescending(v => v, _versionComparer.GetScheme(PublisherId))
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(highestInstalledVersion))
            {
                var isNewer = _versionComparer.IsNewer(item.Version, highestInstalledVersion, PublisherId);
                item.IsUpdateAvailable = isNewer;

                if (isNewer)
                {
                    item.UpdateAvailableVersion = item.Version;
                    _logger.LogDebug(
                        "Update available for {Name}: installed={InstalledVersion}, available={AvailableVersion}",
                        item.Name,
                        highestInstalledVersion,
                        item.Version);
                }
                else
                {
                    item.UpdateAvailableVersion = null;
                }
            }
        }
        else
        {
            item.IsUpdateAvailable = false;
            item.UpdateAvailableVersion = null;
        }
    }

    private static void UpdateItemResolutionVariants(ContentItemViewModel item, List<Core.Models.Manifest.ContentManifest> variants)
    {
        if (variants.Count == 1)
        {
            var variant = variants[0];
            item.Model.Id = variant.Id.Value;

            if (variant.Metadata?.Variants != null && variant.Metadata.Variants.Count > 0)
            {
                if (!item.ResolutionVariants.SequenceEqual(variant.Metadata.Variants))
                {
                    item.ResolutionVariants.Clear();
                    foreach (var resVariant in variant.Metadata.Variants)
                    {
                        item.ResolutionVariants.Add(resVariant);
                    }
                }

                if (string.IsNullOrEmpty(item.SelectedVariantId))
                {
                    var defaultVariant = variant.Metadata.Variants.FirstOrDefault(v => v.IsDefault);
                    item.SelectedVariantId = defaultVariant?.Id ?? variant.Metadata.Variants.FirstOrDefault()?.Id;
                }
            }
            else
            {
                item.ResolutionVariants.Clear();
                item.SelectedVariantId = null;
            }
        }
    }

    private static void UpdateItemDependencies(ContentItemViewModel item, List<Core.Models.Manifest.ContentManifest> variants)
    {
        List<string> requiredDependencies;
        if (variants.Count > 0)
        {
            var manifest = variants[0];
            requiredDependencies = manifest.Dependencies?
                .Where(d => !d.IsOptional)
                .Where(d => d.DependencyType != Core.Models.Enums.ContentType.GameInstallation &&
                           d.DependencyType != Core.Models.Enums.ContentType.GameClient)
                .Where(d => d.InstallBehavior != Core.Models.Enums.DependencyInstallBehavior.AutoInstall)
                .Select(d => d.Name ?? string.Empty)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList() ?? [];
        }
        else if (item.Model.Data is Core.Models.Manifest.ContentManifest dataManifest)
        {
            requiredDependencies = dataManifest.Dependencies?
                .Where(d => !d.IsOptional)
                .Where(d => d.DependencyType != Core.Models.Enums.ContentType.GameInstallation &&
                           d.DependencyType != Core.Models.Enums.ContentType.GameClient)
                .Where(d => d.InstallBehavior != Core.Models.Enums.DependencyInstallBehavior.AutoInstall)
                .Select(d => d.Name ?? string.Empty)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList() ?? [];
        }
        else
        {
            return;
        }

        if (!item.RequiredDependencyNames.SequenceEqual(requiredDependencies))
        {
            item.RequiredDependencyNames.Clear();
            foreach (var dep in requiredDependencies)
            {
                item.RequiredDependencyNames.Add(dep);
            }
        }
    }

    /// <summary>
    /// Extracts numeric date from version strings like "weekly-2025-11-21" or "20251121".
    /// </summary>
    private static string ExtractDateFromVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return string.Empty;
        }

        var numericVersion = GameVersionHelper.ExtractVersionFromVersionString(version);
        return numericVersion > 0 ? numericVersion.ToString() : string.Empty;
    }

    /// <summary>
    /// Formats a user-friendly progress status message.
    /// </summary>
    private static string FormatProgressStatus(GenHub.Core.Models.Content.ContentAcquisitionProgress progress)
    {
        var phaseName = progress.Phase switch
        {
            Core.Models.Content.ContentAcquisitionPhase.Downloading => "Downloading",
            Core.Models.Content.ContentAcquisitionPhase.Extracting => "Extracting",
            Core.Models.Content.ContentAcquisitionPhase.Copying => "Copying",
            Core.Models.Content.ContentAcquisitionPhase.ValidatingManifest => "Validating manifest",
            Core.Models.Content.ContentAcquisitionPhase.ValidatingFiles => "Validating files",
            Core.Models.Content.ContentAcquisitionPhase.Delivering => "Installing",
            Core.Models.Content.ContentAcquisitionPhase.Completed => "Complete",
            _ => "Processing",
        };

        if (!string.IsNullOrEmpty(progress.CurrentOperation))
        {
            return $"{phaseName}: {progress.CurrentOperation}";
        }

        // Format with percentage and phase
        var percentText = progress.ProgressPercentage > 0 ? $"{progress.ProgressPercentage:F0}%" : string.Empty;

        // Add file/bytes info if available
        if (progress.TotalBytes > 0 && progress.Phase == Core.Models.Content.ContentAcquisitionPhase.Downloading)
        {
            var downloaded = ByteFormatHelper.FormatBytes(progress.BytesProcessed);
            var total = ByteFormatHelper.FormatBytes(progress.TotalBytes);
            return $"{phaseName}: {downloaded} / {total} ({percentText})";
        }

        if (progress.TotalFiles > 0)
        {
            var phasePercent = (int)((double)progress.FilesProcessed / progress.TotalFiles * 100);
            return $"{phaseName}: {progress.FilesProcessed}/{progress.TotalFiles} files ({phasePercent}%)";
        }

        return !string.IsNullOrEmpty(percentText) ? $"{phaseName}... {percentText}" : $"{phaseName}...";
    }

    /// <summary>
    /// Finds all installed content manifest variants that match the given content item.
    /// Used to populate <see cref="ContentItemViewModel.AvailableVariants"/>.
    /// </summary>
    private static List<Core.Models.Manifest.ContentManifest> FindContentVariants(
        ContentItemViewModel item,
        List<Core.Models.Manifest.ContentManifest> allManifests,
        string publisherId)
    {
        var variants = new List<Core.Models.Manifest.ContentManifest>();
        var itemId = item.Model.Id ?? string.Empty;
        var itemVersion = item.Version ?? string.Empty;
        var itemDatePart = ExtractDateFromVersion(itemVersion);

        foreach (var manifest in allManifests)
        {
            if (IsManifestVariantMatch(item, manifest, publisherId, itemId, itemVersion, itemDatePart))
            {
                variants.Add(manifest);
            }
        }

        return [.. variants.OrderBy(v => v.Name)];
    }

    private static bool IsManifestVariantMatch(
        ContentItemViewModel item,
        Core.Models.Manifest.ContentManifest manifest,
        string publisherId,
        string itemId,
        string itemVersion,
        string itemDatePart)
    {
        if (item.Model.ContentType != manifest.ContentType)
        {
            return false;
        }

        var manifestIdParts = manifest.Id.Value.Split('.');
        if (manifestIdParts.Length >= 2 && manifestIdParts[1] == "0" && manifest.ContentType == ContentType.GameClient)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(itemId) && manifest.Id.Value.Equals(itemId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hasPublisherInId = manifestIdParts.Length > 2 &&
            manifestIdParts[2].Equals(publisherId, StringComparison.OrdinalIgnoreCase);
        var publisherMatch = hasPublisherInId ||
            (manifest.Publisher?.PublisherType?.Equals(publisherId, StringComparison.OrdinalIgnoreCase) == true);

        if (!publisherMatch)
        {
            return false;
        }

        var nameMatch = IsNameMatch(item.Name, manifest.Name);
        var manifestVersion = manifest.Version ?? string.Empty;
        var manifestDatePart = ExtractDateFromVersion(manifestVersion);
        var versionMatch = IsVersionMatch(itemVersion, manifestVersion, itemDatePart, manifestDatePart);

        var isGameClient = item.Model.ContentType == ContentType.GameClient;
        return nameMatch || (versionMatch && isGameClient);
    }

    private static bool IsNameMatch(string? itemName, string? manifestName)
    {
        var itemStr = itemName?.ToLowerInvariant() ?? string.Empty;
        var manifestStr = manifestName?.ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrEmpty(itemStr) || string.IsNullOrEmpty(manifestStr))
        {
            return false;
        }

        var normalizedItemName = itemStr.Replace(" ", string.Empty).Replace("-", string.Empty);
        var normalizedManifestName = manifestStr.Replace(" ", string.Empty).Replace("-", string.Empty);

        return normalizedManifestName.Contains(normalizedItemName, StringComparison.OrdinalIgnoreCase) ||
               normalizedItemName.Contains(normalizedManifestName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionMatch(string itemVersion, string manifestVersion, string itemDatePart, string manifestDatePart)
    {
        if (manifestVersion.Equals(itemVersion, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrEmpty(itemDatePart) &&
               !string.IsNullOrEmpty(manifestDatePart) &&
               itemDatePart.Equals(manifestDatePart, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task ToggleExpandAsync()
    {
        IsExpanded = !IsExpanded;
        _logger.LogDebug("Publisher card {PublisherId} expanded: {IsExpanded}", PublisherId, IsExpanded);

        if (IsExpanded)
        {
            await RefreshInstallationStatusAsync();
        }
    }

    [RelayCommand]
    private async Task DownloadContentAsync(ContentItemViewModel item)
    {
        if (item.IsDownloaded)
        {
            _logger.LogDebug("Content {ItemName} is already downloaded", item.Name);
            return;
        }

        if (item.Model == null)
        {
            _logger.LogError("Cannot install item {ItemName}: Model is null", item.Name);
            item.DownloadStatus = "Error: No source available";
            return;
        }

        try
        {
            _logger.LogInformation("Starting download of {ItemName} v{Version}", item.Name, item.Version);
            item.IsDownloading = true;
            item.DownloadStatus = "Downloading...";
            item.DownloadProgress = 0;

            var progress = new Progress<GenHub.Core.Models.Content.ContentAcquisitionProgress>(p =>
            {
                item.DownloadProgress = (int)p.ProgressPercentage;
                item.DownloadStatus = FormatProgressStatus(p);
            });

            var result = await _contentOrchestrator.AcquireContentAsync(item.Model, progress);

            if (result.Success && result.Data is Core.Models.Manifest.ContentManifest manifest)
            {
                item.DownloadStatus = "✓ Downloaded";
                item.DownloadProgress = 100;
                item.IsDownloaded = true;

                // Update the Model.Id with the resolved manifest ID
                item.Model.Id = manifest.Id.Value;
                _logger.LogDebug("Updated Model.Id to resolved manifest ID: {ManifestId}", item.Model.Id);

                // Refresh installation status to populate variants
                await RefreshInstallationStatusAsync();

                _logger.LogInformation("Successfully downloaded {ItemName}", item.Name);

                // Notify other components that content was acquired
                try
                {
                    var message = new Core.Models.Content.ContentAcquiredMessage(manifest);
                    WeakReferenceMessenger.Default.Send(message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send ContentAcquiredMessage");
                }

                if (manifest.ContentType == ContentType.GameClient)
                {
                    // For multi-variant content (GeneralsOnline, SuperHackers), we need to create profiles
                    // for all variants that were just installed, not just the primary one returned.
                    // Query the manifest pool for all GameClient manifests matching the version
                    // and publisher type that was just acquired.
                    var installedVersion = result.Data.Version;
                    var publisherType = result.Data.Publisher?.PublisherType;

                    var allManifests = await _manifestPool.GetAllManifestsAsync();
                    if (allManifests.Success && allManifests.Data != null)
                    {
                        // Find all GameClient manifests with matching version and publisher
                        var justInstalledGameClients = allManifests.Data.Where(m =>
                            m.Version == installedVersion &&
                            m.Publisher?.PublisherType == publisherType &&
                            m.ContentType == ContentType.GameClient).ToList();

                        _logger.LogInformation(
                            "Found {Count} GameClient variants for {Publisher} v{Version}",
                            justInstalledGameClients.Count,
                            publisherType,
                            installedVersion);

                        foreach (var m in justInstalledGameClients)
                        {
                            var profileResult = await _profileService.CreateProfileFromManifestAsync(m);
                            if (profileResult.Success)
                            {
                                _logger.LogInformation(
                                    "Created profile for {ManifestId}: {ProfileName}",
                                    manifest.Id,
                                    profileResult.Data?.Name);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Failed to create profile for {ManifestId}: {Errors}",
                                    manifest.Id,
                                    string.Join(", ", profileResult.Errors));
                            }
                        }
                    }
                }

                _notificationService.ShowSuccess("Download Complete", $"Downloaded {item.Name}");
            }
            else
            {
                var errorMsg = result.Errors != null ? string.Join(", ", result.Errors) : "Unknown error";
                item.DownloadStatus = $"✗ Failed: {errorMsg}";
                _logger.LogError("Failed to download {ItemName}: {Error}", item.Name, errorMsg);
            }
        }
        catch (Exception ex)
        {
            item.DownloadStatus = $"✗ Error: {ex.Message}";
            _logger.LogError(ex, "Exception during download of {ItemName}", item.Name);
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    [RelayCommand]
    private void ViewDetails()
    {
        _logger.LogDebug("Viewing details for {PublisherName}", DisplayName);

        // TODO: Open publisher details view or website
    }

    /// <summary>
    /// Installs the latest content item from this publisher.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task InstallLatestAsync()
    {
        var latestItem = ContentTypes
            .SelectMany(g => g.Items)
            .Where(i => !i.IsInstalled)
            .OrderByDescending(i => i.Model.LastUpdated)
            .FirstOrDefault();

        if (latestItem == null)
        {
            _logger.LogInformation("No uninstalled content available for {PublisherId}", PublisherId);
            return;
        }

        await DownloadContentAsync(latestItem);
    }

    [RelayCommand]
    private async Task AddToProfileAsync(object? args)
    {
        if (args is not object[] parameters || parameters.Length != 2)
        {
            return;
        }

        if (parameters[1] is not GameProfile profile)
        {
            return;
        }

        string contentId = string.Empty;
        string contentName = string.Empty;
        bool isDownloading = false;

        if (parameters[0] is ContentItemViewModel item)
        {
            // If item has variants, user MUST select one through the variant dropdown
            // The UI should call this method with the ContentManifest, not the item
            if (item.HasVariants && item.AvailableVariants.Count > 0)
            {
                // Use first variant as default if called directly with item
                contentId = item.AvailableVariants[0].Id.Value;
                contentName = item.AvailableVariants[0].Name ?? item.Name;
                _logger.LogDebug("Using first variant manifest ID: {ManifestId}", contentId);
            }
            else if (item.IsDownloaded && !string.IsNullOrEmpty(item.Model.Id) && item.Model.Id.Count(c => c == '.') >= 4)
            {
                // Downloaded single variant - Model.Id should be updated to proper format
                contentId = item.Model.Id;
                contentName = item.Name;
            }
            else if (!item.IsDownloaded)
            {
                item.DownloadStatus = "✗ Download content first";
                _logger.LogWarning("Cannot add {ItemName} to profile: content not downloaded yet", item.Name);
                return;
            }
            else
            {
                // Fallback - try to find the manifest in the pool that matches this item
                item.DownloadStatus = "✗ Invalid content ID";
                _logger.LogWarning("Cannot add {ItemName} to profile: invalid manifest ID format {Id}", item.Name, item.Model.Id);
                return;
            }

            isDownloading = item.IsDownloading;
        }
        else if (parameters[0] is Core.Models.Manifest.ContentManifest manifest)
        {
            // Manifest passed directly (from variant selection) - use its ID
            contentId = manifest.Id.Value;
            contentName = manifest.Name ?? GameClientConstants.UnknownVersion;
            isDownloading = false;
        }
        else
        {
            return;
        }

        if (isDownloading)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Adding {ContentName} ({ContentId}) to profile {ProfileName}", contentName, contentId, profile.Name);

            // If we have an item access, update status
            if (parameters[0] is ContentItemViewModel vmItem)
            {
                vmItem.DownloadStatus = "Adding to profile...";
            }

            var result = await _profileContentService.AddContentToProfileAsync(
                profile.Id,
                contentId,
                CancellationToken.None);

            if (result.Success)
            {
                if (parameters[0] is ContentItemViewModel successItem)
                {
                    successItem.DownloadStatus = $"✓ Added to {profile.Name}";
                }

                // Notify other components that a profile was updated
                try
                {
                    var message = new ProfileUpdatedMessage(profile);
                    WeakReferenceMessenger.Default.Send(message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send ProfileUpdatedMessage");
                }

                if (result.WasContentSwapped)
                {
                    _logger.LogInformation(
                        "Content swap: replaced {OldContent} with {NewContent} in profile {ProfileName}",
                        result.SwappedContentName,
                        contentName,
                        profile.Name);

                    _notificationService.ShowWarning(
                        "Content Replaced",
                        $"Replaced '{result.SwappedContentName ?? "conflicting content"}' with '{contentName}' in '{profile.Name}'. Only one of this type can be enabled at a time.");
                }
                else
                {
                    _notificationService.ShowSuccess(
                        "Added to Profile",
                        $"'{contentName}' added to profile '{profile.Name}'");
                }

                await RefreshAvailableProfilesAsync();
            }
            else
            {
                if (parameters[0] is ContentItemViewModel failItem)
                {
                    failItem.DownloadStatus = $"✗ Failed: {result.FirstError}";
                }

                _notificationService.ShowError(
                    "Failed to Add to Profile",
                    result.FirstError ?? "Unknown error occurred");
                _logger.LogError("Failed to add content to profile: {Error}", result.FirstError);
            }
        }
        catch (Exception ex)
        {
            if (parameters[0] is ContentItemViewModel errItem)
            {
                errItem.DownloadStatus = $"✗ Error: {ex.Message}";
            }

            _notificationService.ShowError(
                "Error Adding to Profile",
                $"An unexpected error occurred: {ex.Message}");
            _logger.LogError(ex, "Exception adding content to profile");
        }
    }

    [RelayCommand]
    private async Task CreateProfileWithContentAsync(object? parameter)
    {
        if (parameter == null)
        {
            return;
        }

        string contentId = string.Empty;
        string contentName = string.Empty;
        ContentItemViewModel? itemForStatus = null;

        if (parameter is ContentItemViewModel item)
        {
            // Handle ContentItemViewModel
            if (item.IsDownloading)
            {
                return;
            }

            itemForStatus = item;

            if (item.HasVariants && item.AvailableVariants.Count > 0)
            {
                contentId = item.AvailableVariants[0].Id.Value;
                contentName = item.AvailableVariants[0].Name ?? item.Name;
            }
            else
            {
                contentId = item.Model.Id;
                contentName = item.Name;
            }
        }
        else if (parameter is Core.Models.Manifest.ContentManifest manifest)
        {
            // Handle ContentManifest directly from variant selection
            contentId = manifest.Id.Value;
            contentName = manifest.Name ?? GameClientConstants.UnknownVersion;
        }
        else
        {
            return;
        }

        try
        {
            var baseName = $"{contentName} Profile";
            var profileName = baseName;
            var counter = 1;

            // Ensure unique name
            while (AvailableProfiles.Any(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase)))
            {
                profileName = $"{baseName} ({counter++})";
            }

            _logger.LogInformation("Creating new profile '{ProfileName}' with {ContentName}", profileName, contentName);

            if (itemForStatus != null)
            {
                itemForStatus.DownloadStatus = "Creating profile...";
            }

            var result = await _profileContentService.CreateProfileWithContentAsync(
                profileName,
                contentId,
                CancellationToken.None);

            if (result.Success && result.Data != null)
            {
                if (itemForStatus != null)
                {
                    itemForStatus.DownloadStatus = $"✓ Profile created: {result.Data.Name}";
                }

                // Notify other components that a profile was created
                try
                {
                    var message = new ProfileCreatedMessage(result.Data);
                    WeakReferenceMessenger.Default.Send(message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send ProfileCreatedMessage");
                }

                await RefreshAvailableProfilesAsync();
            }
            else
            {
                if (itemForStatus != null)
                {
                    itemForStatus.DownloadStatus = $"✗ Failed: {result.FirstError}";
                }

                _notificationService.ShowError(
                    "Failed to Create Profile",
                    result.FirstError ?? "Unknown error occurred");
                _logger.LogError("Failed to create profile: {Error}", result.FirstError);
            }
        }
        catch (Exception ex)
        {
            if (itemForStatus != null)
            {
                itemForStatus.DownloadStatus = $"✗ Error: {ex.Message}";
            }

            _notificationService.ShowError(
                "Error Creating Profile",
                $"An unexpected error occurred: {ex.Message}");
            _logger.LogError(ex, "Exception creating profile with content");
        }
    }

    private void RefreshProfilesOnUiThread()
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await RefreshAvailableProfilesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh profiles dropdown");
            }
        });
    }
}
