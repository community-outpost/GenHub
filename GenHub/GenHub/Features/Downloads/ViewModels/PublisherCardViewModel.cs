using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Features.Content.ViewModels;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for a publisher card displaying content from a single publisher.
/// </summary>
public partial class PublisherCardViewModel : ObservableObject
{
    private readonly ILogger<PublisherCardViewModel> _logger;
    private readonly IContentOrchestrator _contentOrchestrator;
    private readonly IContentManifestPool _manifestPool;

    [ObservableProperty]
    private string _publisherId = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _iconPathData = string.Empty;

    [ObservableProperty]
    private string? _logoSource;

    [ObservableProperty]
    private string _latestVersion = string.Empty;

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
    private ObservableCollection<ContentTypeGroup> _contentTypes = new();

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
    /// <param name="manifestPool">The manifest pool for checking installed content.</param>
    public PublisherCardViewModel(
        ILogger<PublisherCardViewModel> logger,
        IContentOrchestrator contentOrchestrator,
        IContentManifestPool manifestPool)
    {
        _logger = logger;
        _contentOrchestrator = contentOrchestrator;
        _manifestPool = manifestPool;

        // Subscribe to collection changes to update HasContent and ContentSummary
        ContentTypes.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(ContentSummary));
        };
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
                _logger.LogWarning("Failed to retrieve manifests for installation status check");
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
                    var wasInstalled = item.IsInstalled;
                    item.IsInstalled = IsContentInstalledSimple(item, allManifests, PublisherId);

                    _logger.LogDebug(
                        "Content item: {Name} v{Version} ({ContentType}) - Installed: {IsInstalled}",
                        item.Name,
                        item.Version,
                        item.Model.ContentType,
                        item.IsInstalled);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh installation status for {PublisherId}", PublisherId);
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

        var numericVersion = VersionHelper.ExtractVersionFromVersionString(version);
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
            var phasePercent = progress.TotalFiles > 0
                ? (int)((double)progress.FilesProcessed / progress.TotalFiles * 100)
                : 0;
            return $"{phaseName}: {progress.FilesProcessed}/{progress.TotalFiles} files ({phasePercent}%)";
        }

        if (!string.IsNullOrEmpty(progress.CurrentOperation))
        {
            return $"{phaseName}: {progress.CurrentOperation}";
        }

        return !string.IsNullOrEmpty(percentText) ? $"{phaseName}... {percentText}" : $"{phaseName}...";
    }

    /// <summary>
    /// Simple check if content is installed - matches by manifest ID or by ID components with version.
    /// </summary>
    private static bool IsContentInstalledSimple(
        ContentItemViewModel item,
        List<Core.Models.Manifest.ContentManifest> allManifests,
        string publisherId)
    {
        var itemId = item.Model.Id ?? string.Empty;
        var itemVersion = item.Version ?? string.Empty;
        var itemDatePart = ExtractDateFromVersion(itemVersion);

        foreach (var manifest in allManifests)
        {
            // Direct ID match - most reliable
            if (!string.IsNullOrEmpty(itemId) &&
                manifest.Id.Value.Equals(itemId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // If no direct ID match, try matching by publisher + content type + name
            // Check publisher match by exact ID prefix or type
            var manifestIdParts = manifest.Id.Value.Split('.');
            var hasPublisherInId = manifestIdParts.Length > 2 &&
                manifestIdParts[2].Equals(publisherId, StringComparison.OrdinalIgnoreCase);
            var publisherMatch = hasPublisherInId ||
                (manifest.Publisher?.PublisherType?.Equals(publisherId, StringComparison.OrdinalIgnoreCase) == true);

            if (!publisherMatch)
            {
                continue;
            }

            // Check content type match
            if (manifest.ContentType != item.Model.ContentType)
            {
                continue;
            }

            // For manifests from the same publisher with same content type, check name match
            // This prevents matching all map packs just because they share publisher + type
            var itemName = item.Name?.ToLowerInvariant() ?? string.Empty;
            var manifestName = manifest.Name?.ToLowerInvariant() ?? string.Empty;

            // Skip if names don't match and we have name info
            if (!string.IsNullOrEmpty(itemName) && !string.IsNullOrEmpty(manifestName))
            {
                // Allow for slight variations (e.g., "Far Cry" vs "FarCry")
                var normalizedItemName = itemName.Replace(" ", string.Empty).Replace("-", string.Empty);
                var normalizedManifestName = manifestName.Replace(" ", string.Empty).Replace("-", string.Empty);

                if (!normalizedManifestName.Contains(normalizedItemName, StringComparison.OrdinalIgnoreCase) &&
                    !normalizedItemName.Contains(normalizedManifestName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // Publisher + content type + name all match - this is enough to consider it installed
            // Version matching is optional and used for additional confirmation
            var manifestVersion = manifest.Version ?? string.Empty;
            var manifestDatePart = ExtractDateFromVersion(manifestVersion);

            // Direct version match - strongest confirmation
            if (manifestVersion.Equals(itemVersion, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Date part match (e.g., "20251121" from "weekly-2025-11-21")
            if (!string.IsNullOrEmpty(itemDatePart) &&
                !string.IsNullOrEmpty(manifestDatePart) &&
                itemDatePart.Equals(manifestDatePart, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // If publisher + content type + name match but versions differ,
            // do not consider it installed - this could be a different version
            // and would hide available updates from the user
        }

        return false;
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
    private async Task InstallContentAsync(ContentItemViewModel item)
    {
        if (item.IsInstalled)
        {
            _logger.LogDebug("Content {ItemName} is already installed", item.Name);
            return;
        }

        if (item.Model == null)
        {
            _logger.LogError("Cannot install item {ItemName}: Model is null", item.Name);
            item.InstallStatus = "Error: No source available";
            return;
        }

        try
        {
            _logger.LogInformation("Starting installation of {ItemName} v{Version}", item.Name, item.Version);
            item.IsInstalling = true;
            item.InstallStatus = "Starting download...";
            item.InstallProgress = 0;

            var progress = new Progress<GenHub.Core.Models.Content.ContentAcquisitionProgress>(p =>
            {
                item.InstallProgress = (int)p.ProgressPercentage;

                // Format a user-friendly status message based on phase
                item.InstallStatus = FormatProgressStatus(p);
            });

            var result = await _contentOrchestrator.AcquireContentAsync(item.Model, progress);

            if (result.Success)
            {
                item.InstallStatus = "✓ Installation complete";
                item.InstallProgress = 100;
                item.IsInstalled = true;

                _logger.LogInformation("Successfully installed {ItemName}", item.Name);
            }
            else
            {
                var errorMsg = result.Errors != null ? string.Join(", ", result.Errors) : "Unknown error";
                item.InstallStatus = $"✗ Failed: {errorMsg}";
                _logger.LogError("Failed to install {ItemName}: {Error}", item.Name, errorMsg);
            }
        }
        catch (Exception ex)
        {
            item.InstallStatus = $"✗ Error: {ex.Message}";
            _logger.LogError(ex, "Exception during installation of {ItemName}", item.Name);
        }
        finally
        {
            item.IsInstalling = false;
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

        await InstallContentAsync(latestItem);
    }
}
