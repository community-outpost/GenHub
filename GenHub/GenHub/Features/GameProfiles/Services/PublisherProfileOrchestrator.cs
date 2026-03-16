using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Implementation of IPublisherProfileOrchestrator for handling publisher-based game clients.
/// </summary>
public class PublisherProfileOrchestrator(
    IContentOrchestrator contentOrchestrator,
    IContentManifestPool manifestPool,
    IGameClientProfileService gameClientProfileService,
    INotificationService notificationService,
    ILogger<PublisherProfileOrchestrator> logger) : IPublisherProfileOrchestrator
{
    /// <inheritdoc/>
    public async Task<OperationResult<int>> CreateProfilesForPublisherClientAsync(
        GameInstallation installation,
        GameClient gameClient,
        bool forceReacquireContent = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (gameClient == null) return OperationResult<int>.CreateFailure("Game client cannot be null");

            var publisherType = gameClient.PublisherType;
            if (string.IsNullOrEmpty(publisherType))
            {
                logger.LogWarning("Could not determine publisher type for client {ClientId}", gameClient.Id);
                return OperationResult<int>.CreateFailure("Publisher type unknown");
            }

            logger.LogInformation(
                "Handling publisher client {ClientName} ({PublisherType}) for installation {InstallationType}",
                gameClient.Name,
                publisherType,
                installation.InstallationType);

            // Check if manifests already exist in the pool for this publisher
            var existingManifests = await GetPublisherManifestsFromPoolAsync(publisherType, cancellationToken);

            bool shouldAcquire = false;
            if (existingManifests.Count == 0)
            {
                // No manifests in pool - need to acquire
                shouldAcquire = true;
                logger.LogInformation(
                    "No existing {PublisherType} manifests found, will acquire content",
                    publisherType);
            }
            else if (forceReacquireContent)
            {
                // Force reacquire requested - always acquire
                shouldAcquire = true;
                logger.LogInformation(
                    "Force reacquire requested for {PublisherType}",
                    publisherType);
            }
            else
            {
                // Check if a newer version is available
                var hasNewerVersion = await CheckForNewerVersionAsync(publisherType, existingManifests, cancellationToken);
                if (hasNewerVersion)
                {
                    shouldAcquire = true;
                    logger.LogInformation(
                        "Newer version available for {PublisherType}, will acquire content",
                        publisherType);
                }
                else
                {
                    logger.LogInformation(
                        "Found {Count} existing {PublisherType} manifests in pool with latest version, skipping acquisition",
                        existingManifests.Count,
                        publisherType);
                }
            }

            if (shouldAcquire)
            {
                logger.LogInformation(
                    "Acquisition triggered for {PublisherType}",
                    publisherType);
                await AcquirePublisherClientContentAsync(gameClient, cancellationToken);

                // Re-check after acquisition
                existingManifests = await GetPublisherManifestsFromPoolAsync(publisherType, cancellationToken);
            }

            // Create profiles for ALL GameClient manifests from this publisher
            var profilesCreated = 0;
            foreach (var manifest in existingManifests)
            {
                var profileResult = await gameClientProfileService.CreateProfileFromManifestAsync(manifest, cancellationToken);
                if (profileResult.Success && profileResult.Data != null)
                {
                    profilesCreated++;
                    logger.LogInformation(
                        "Created profile for {PublisherType} variant: {ManifestId} -> {ProfileName}",
                        publisherType,
                        manifest.Id,
                        profileResult.Data.Name);
                }
                else
                {
                    // Not an error - might already exist
                    logger.LogDebug(
                        "Skipped profile creation for {ManifestId}: {Reason}",
                        manifest.Id,
                        ManifestHelper.FormatErrors(profileResult.Errors));
                }
            }

            // Show single notification for all profiles created
            if (profilesCreated > 0)
            {
                notificationService.ShowSuccess(
                    $"{publisherType} Profiles Created",
                    $"Created {profilesCreated} profile(s) for {publisherType} variants.");
            }

            logger.LogInformation(
                "Created {Count} profiles for {PublisherType} from {TotalManifests} GameClient manifests",
                profilesCreated,
                publisherType,
                existingManifests.Count);

            return OperationResult<int>.CreateSuccess(profilesCreated);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating profiles for publisher client {ClientName}", gameClient?.Name);
            return OperationResult<int>.CreateFailure($"Internal error: {ex.Message}");
        }
    }

    private async Task<List<ContentManifest>> GetPublisherManifestsFromPoolAsync(string publisherType, CancellationToken cancellationToken)
    {
        try
        {
            var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
            if (!allManifestsResult.Success || allManifestsResult.Data == null)
            {
                return [];
            }

            return [.. allManifestsResult.Data
                .Where(m =>
                {
                    // Must be a GameClient from the specified publisher that has been downloaded
                    if (m.ContentType != ContentType.GameClient ||
                        !string.Equals(m.Publisher?.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase) ||
                        !ManifestHelper.IsDownloadedManifest(m))
                    {
                        return false;
                    }

                    // For Community Outpost, exclude base game content (10gn, 10zh)
                    // Base games should only be created as fallback when user explicitly declines Community Patch
                    if (string.Equals(publisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
                    {
                        // Check for 'basegame' tag in metadata (added by CommunityOutpostResolver)
                        var hasBaseGameTag = m.Metadata?.Tags?.Any(t =>
                            t.Equals("basegame", StringComparison.OrdinalIgnoreCase)) ?? false;

                        if (hasBaseGameTag)
                        {
                            logger.LogDebug(
                                "Skipping base game manifest {ManifestId} - base games should only be created when user declines Community Patch",
                                m.Id);
                            return false;
                        }
                    }

                    return true;
                })];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error getting manifests from pool for {PublisherType}", publisherType);
            return [];
        }
    }

    private async Task AcquirePublisherClientContentAsync(GameClient gameClient, CancellationToken cancellationToken)
    {
        try
        {
            var publisherType = gameClient.PublisherType!;

            // TODO: Localization - Move these strings to localization system when implemented
            logger.LogInformation(
                "Acquiring content from provider for publisher client: {ClientName} (PublisherType: '{PublisherType}')",
                gameClient.Name,
                publisherType);

            string publisherDisplayName;
            if (publisherType.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
            {
                publisherDisplayName = SuperHackersConstants.PublisherName;
            }
            else if (publisherType.Equals(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
            {
                publisherDisplayName = "Generals Online";
            }
            else if (publisherType.Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
            {
                publisherDisplayName = CommunityOutpostConstants.PublisherName;
            }
            else
            {
                publisherDisplayName = publisherType;
            }

            var searchQuery = new ContentSearchQuery
            {
                ProviderName = publisherType,
                ContentType = ContentType.GameClient,
            };

            var searchResult = await contentOrchestrator.SearchAsync(searchQuery, cancellationToken);
            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                logger.LogWarning("No content discovered from {PublisherType} provider for acquisition", publisherType);
                notificationService.ShowWarning(
                    $"{publisherDisplayName} Not Found",
                    $"Could not find {publisherDisplayName} content to download.");
                return;
            }

            var contentToAcquire = searchResult.Data.First();

            logger.LogInformation(
                "Found {PublisherType} content to acquire: {Name} v{Version}",
                publisherType,
                contentToAcquire.Name,
                contentToAcquire.Version);

            var acquireResult = await contentOrchestrator.AcquireContentAsync(contentToAcquire, cancellationToken: cancellationToken);
            if (acquireResult.Success && acquireResult.Data != null)
            {
                logger.LogInformation(
                    "Successfully acquired content for publisher client {ClientName}, manifest: {ManifestId}",
                    gameClient.Name,
                    acquireResult.Data.Id);

                notificationService.ShowSuccess(
                    $"{publisherDisplayName} Downloaded",
                    $"Successfully downloaded {contentToAcquire.Name} v{contentToAcquire.Version}.");
            }
            else
            {
                var errorMsg = ManifestHelper.FormatErrors(acquireResult.Errors);
                logger.LogWarning(
                    "Failed to acquire content for publisher client {ClientName}: {Errors}",
                    gameClient.Name,
                    errorMsg);

                notificationService.ShowError(
                    $"{publisherDisplayName} Download Failed",
                    errorMsg);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error acquiring content for publisher client {ClientName}", gameClient.Name);
        }
    }

    /// <summary>
    /// Checks if a newer version is available for the publisher content.
    /// </summary>
    /// <param name="publisherType">The publisher type to check.</param>
    /// <param name="existingManifests">The currently installed manifests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a newer version is available, false otherwise.</returns>
    private async Task<bool> CheckForNewerVersionAsync(
        string publisherType,
        List<ContentManifest> existingManifests,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the highest version from existing manifests
            var highestInstalledVersion = existingManifests
                .Select(m => m.Version)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .OrderByDescending(v => v, new VersionStringComparer(publisherType))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(highestInstalledVersion))
            {
                logger.LogDebug("No valid version found in existing manifests for {PublisherType}", publisherType);
                return true; // If we can't determine version, assume we should update
            }

            logger.LogDebug(
                "Highest installed version for {PublisherType}: {Version}",
                publisherType,
                highestInstalledVersion);

            // Discover the latest available version from the provider
            var searchQuery = new ContentSearchQuery
            {
                ProviderName = publisherType,
                ContentType = ContentType.GameClient,
            };

            var searchResult = await contentOrchestrator.SearchAsync(searchQuery, cancellationToken);
            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                logger.LogDebug("No content discovered from {PublisherType} provider for version check", publisherType);
                return false; // If we can't discover new content, don't trigger acquisition
            }

            var latestAvailable = searchResult.Data.First();
            var latestAvailableVersion = latestAvailable.Version;

            if (string.IsNullOrWhiteSpace(latestAvailableVersion))
            {
                logger.LogDebug("No version information in discovered content for {PublisherType}", publisherType);
                return false; // If no version info, assume current is fine
            }

            logger.LogDebug(
                "Latest available version for {PublisherType}: {Version}",
                publisherType,
                latestAvailableVersion);

            // Compare versions
            var comparison = Core.Helpers.VersionComparer.CompareVersions(
                latestAvailableVersion,
                highestInstalledVersion,
                publisherType);

            if (comparison > 0)
            {
                logger.LogInformation(
                    "Newer version available for {PublisherType}: {LatestVersion} > {InstalledVersion}",
                    publisherType,
                    latestAvailableVersion,
                    highestInstalledVersion);
                return true;
            }

            logger.LogDebug(
                "Installed version is up to date for {PublisherType}: {InstalledVersion} >= {LatestVersion}",
                publisherType,
                highestInstalledVersion,
                latestAvailableVersion);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking for newer version for {PublisherType}, assuming current is fine to avoid loops", publisherType);
            return false; // On error, don't trigger acquisition to avoid potential infinite loops
        }
    }

    /// <summary>
    /// Comparer for version strings that uses the VersionComparer utility.
    /// </summary>
    private class VersionStringComparer(string publisherType) : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            return Core.Helpers.VersionComparer.CompareVersions(x, y, publisherType);
        }
    }
}
