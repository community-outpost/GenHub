using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Implementation of IPublisherProfileOrchestrator for handling publisher-based game clients.
/// </summary>
public class PublisherProfileOrchestrator(
    IContentOrchestrator contentOrchestrator,
    IContentManifestPool manifestPool,
    IGameClientProfileService gameClientProfileService,
    INotificationService notificationService,
    IContentVersionComparer versionComparer,
    ILogger<PublisherProfileOrchestrator> logger,
    ILocalizationService? localizationService = null) : IPublisherProfileOrchestrator
{
    /// <inheritdoc/>
    public async Task<OperationResult<int>> CreateProfilesForPublisherClientAsync(
        GameInstallation installation,
        GameClient gameClient,
        bool forceReacquireContent = false,
        bool skipAcquisition = false,
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

            var isCommunityOutpost = string.Equals(publisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase);
            var isNonRet = isCommunityOutpost && IsNonRetail(gameClient);

            logger.LogInformation(
                "Handling publisher client {ClientName} ({PublisherType}, IsNonRet: {IsNonRet}) for installation {InstallationType}",
                gameClient.Name,
                publisherType,
                isNonRet,
                installation.InstallationType);

            // Publisher packages are Windows builds. On other hosts an install that already
            // carries a native client must get its profile from that client, not from a download.
            if (IsHostNativeClient(gameClient))
            {
                var nativeClients = (installation.AvailableGameClients ?? [])
                    .Where(c => !ReferenceEquals(c, gameClient) &&
                                string.Equals(c.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase) &&
                                (!isCommunityOutpost || IsNonRetail(c) == isNonRet) &&
                                IsHostNativeClient(c))
                    .Prepend(gameClient)
                    .DistinctBy(c => c.ExecutablePath, StringComparer.Ordinal)
                    .ToList();

                if (forceReacquireContent)
                {
                    logger.LogInformation(
                        "Ignoring reacquire request for native {PublisherType} client: the publisher package is a Windows build, so a native install has nothing to acquire",
                        publisherType);
                }

                logger.LogInformation(
                    "Using {Count} native {PublisherType} client(s) without acquiring publisher content",
                    nativeClients.Count,
                    publisherType);
                return await CreateProfilesFromDetectedClientsAsync(installation, nativeClients, publisherType, isNonRet, cancellationToken);
            }

            // Check if manifests already exist in the pool for this publisher
            var existingManifests = await GetPublisherManifestsFromPoolAsync(publisherType, cancellationToken);
            if (isCommunityOutpost)
            {
                existingManifests = existingManifests
                    .Where(m => isNonRet ? IsNonRetail(m) : !IsNonRetail(m))
                    .ToList();
            }

            bool shouldAcquire = false;
            if (skipAcquisition && existingManifests.Count > 0)
            {
                logger.LogInformation(
                    "Skip acquisition requested for {PublisherType} (IsNonRet: {IsNonRet}), creating profiles from {Count} existing manifests",
                    publisherType,
                    isNonRet,
                    existingManifests.Count);
            }
            else if (skipAcquisition && HasExecutableOnDisk(gameClient))
            {
                logger.LogInformation(
                    "Skip acquisition requested for {PublisherType} (IsNonRet: {IsNonRet}) with no pooled manifests, creating the profile from the detected client",
                    publisherType,
                    isNonRet);
                return await CreateProfilesFromDetectedClientsAsync(installation, [gameClient], publisherType, isNonRet, cancellationToken);
            }
            else if (existingManifests.Count == 0)
            {
                // No manifests in pool - need to acquire
                shouldAcquire = true;
                logger.LogInformation(
                    "No existing {PublisherType} (IsNonRet: {IsNonRet}) manifests found, will acquire content",
                    publisherType,
                    isNonRet);
            }
            else if (forceReacquireContent)
            {
                // Force reacquire requested - always acquire
                shouldAcquire = true;
                logger.LogInformation(
                    "Force reacquire requested for {PublisherType} (IsNonRet: {IsNonRet})",
                    publisherType,
                    isNonRet);
            }
            else
            {
                // Check if a newer version is available
                var hasNewerVersion = await CheckForNewerVersionAsync(publisherType, existingManifests, isCommunityOutpost, isNonRet, cancellationToken);
                if (hasNewerVersion)
                {
                    shouldAcquire = true;
                    logger.LogInformation(
                        "Newer version available for {PublisherType} (IsNonRet: {IsNonRet}), will acquire content",
                        publisherType,
                        isNonRet);
                }
                else
                {
                    logger.LogInformation(
                        "Found {Count} existing {PublisherType} (IsNonRet: {IsNonRet}) manifests in pool with latest version, skipping acquisition",
                        existingManifests.Count,
                        publisherType,
                        isNonRet);
                }
            }

            if (shouldAcquire)
            {
                logger.LogInformation(
                    "Acquisition triggered for {PublisherType} (IsNonRet: {IsNonRet})",
                    publisherType,
                    isNonRet);
                await AcquirePublisherClientContentAsync(gameClient, isCommunityOutpost, isNonRet, cancellationToken);

                // Re-check after acquisition
                existingManifests = await GetPublisherManifestsFromPoolAsync(publisherType, cancellationToken);
                if (isCommunityOutpost)
                {
                    existingManifests = existingManifests
                        .Where(m => isNonRet ? IsNonRetail(m) : !IsNonRetail(m))
                        .ToList();
                }
            }

            // Create profiles for GameClient manifests from this publisher (matching variant)
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
                    logger.LogInformation(
                        "Skipped profile creation for {ManifestId} ({Name}): {Reason}",
                        manifest.Id,
                        manifest.Name,
                        ManifestHelper.FormatErrors(profileResult.Errors));
                }
            }

            // Show single notification for all profiles created
            if (profilesCreated > 0)
            {
                var displayName = GetPublisherDisplayName(publisherType, isNonRet);

                notificationService.ShowSuccess(
                    localizationService.GetLocalizedString("GameProfiles.Notification.PublisherProfilesCreated.Title", $"{displayName} Profiles Created", displayName),
                    localizationService.GetLocalizedString("GameProfiles.Notification.PublisherProfilesCreated.Message", $"Created {profilesCreated} profile(s) for {displayName}.", profilesCreated, displayName));
            }

            logger.LogInformation(
                "Created {ProfilesCreated} profiles for {PublisherType} (IsNonRet: {IsNonRet}) from {TotalManifests} GameClient manifests",
                profilesCreated,
                publisherType,
                isNonRet,
                existingManifests.Count);

            return OperationResult<int>.CreateSuccess(profilesCreated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating profiles for publisher client {ClientName}", gameClient?.Name);
            return OperationResult<int>.CreateFailure($"Internal error: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines whether a detected client is a native macOS or Linux build on a non-Windows host.
    /// </summary>
    /// <param name="gameClient">The detected client.</param>
    /// <returns><c>true</c> when the client runs natively on this host.</returns>
    internal static bool IsHostNativeClient(GameClient gameClient)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(gameClient.ExecutablePath))
        {
            return false;
        }

        var executablePath = gameClient.ExecutablePath;
        return (File.Exists(executablePath) || Directory.Exists(executablePath))
            && ExecutableFileClassifier.IsUnixHostNative(executablePath);
    }

    /// <summary>
    /// Creates a profile for each detected client. An already existing profile is not an error;
    /// real failures are reported and fail the result when nothing was created.
    /// </summary>
    /// <param name="installation">The parent game installation.</param>
    /// <param name="gameClients">The detected clients to profile.</param>
    /// <param name="publisherType">The publisher of the clients.</param>
    /// <param name="isNonRet">Whether the clients are the non-retail Community Patch variant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of profiles created, or a failure carrying the reasons.</returns>
    internal async Task<OperationResult<int>> CreateProfilesFromDetectedClientsAsync(
        GameInstallation installation,
        IReadOnlyList<GameClient> gameClients,
        string publisherType,
        bool isNonRet,
        CancellationToken cancellationToken)
    {
        var profilesCreated = 0;
        List<string> failures = [];
        foreach (var gameClient in gameClients)
        {
            var profileResult = await gameClientProfileService.CreateProfileForGameClientAsync(
                installation,
                gameClient,
                cancellationToken: cancellationToken);

            if (profileResult.Success && profileResult.Data != null)
            {
                profilesCreated++;
                logger.LogInformation(
                    "Created profile for detected {PublisherType} client {ExecutablePath} -> {ProfileName}",
                    publisherType,
                    gameClient.ExecutablePath,
                    profileResult.Data.Name);
            }
            else if (profileResult.ErrorCode == ProfileConstants.ProfileAlreadyExistsErrorCode)
            {
                logger.LogInformation(
                    "Profile already exists for detected {PublisherType} client {ClientName}",
                    publisherType,
                    gameClient.Name);
            }
            else
            {
                var reason = ManifestHelper.FormatErrors(profileResult.Errors);
                failures.Add($"{gameClient.Name}: {reason}");
                logger.LogWarning(
                    "Failed to create profile for detected {PublisherType} client {ClientName}: {Reason}",
                    publisherType,
                    gameClient.Name,
                    reason);
            }
        }

        var displayName = GetPublisherDisplayName(publisherType, isNonRet);
        if (profilesCreated > 0)
        {
            notificationService.ShowSuccess(
                localizationService.GetLocalizedString("GameProfiles.Notification.PublisherProfilesCreated.Title", $"{displayName} Profiles Created", displayName),
                localizationService.GetLocalizedString("GameProfiles.Notification.PublisherProfilesCreated.Message", $"Created {profilesCreated} profile(s) for {displayName}.", profilesCreated, displayName));
        }

        if (failures.Count == 0)
        {
            return OperationResult<int>.CreateSuccess(profilesCreated);
        }

        notificationService.ShowWarning(
            localizationService.GetLocalizedString("GameProfiles.Notification.PublisherProfileCreationFailed.Title", $"{displayName} Profile Creation Failed", displayName),
            string.Join(Environment.NewLine, failures));

        return profilesCreated > 0
            ? OperationResult<int>.CreateSuccess(profilesCreated)
            : OperationResult<int>.CreateFailure(failures);
    }

    private static string GetPublisherDisplayName(string publisherType, bool isNonRet)
    {
        if (publisherType.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            return SuperHackersConstants.PublisherName;
        }

        if (publisherType.Equals(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
        {
            return "Generals Online";
        }

        if (publisherType.Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            return isNonRet
                ? "Community Patch (Non-Retail)"
                : CommunityOutpostConstants.PublisherName;
        }

        return publisherType;
    }

    private static bool HasExecutableOnDisk(GameClient gameClient) =>
        !string.IsNullOrEmpty(gameClient.ExecutablePath) &&
        (File.Exists(gameClient.ExecutablePath) || Directory.Exists(gameClient.ExecutablePath));

    private static bool IsNonRetail(GameClient client) =>
        CommunityOutpostConstants.IsNonRetailIdentifier(client.Id) ||
        CommunityOutpostConstants.IsNonRetailIdentifier(client.Name);

    private static bool IsNonRetail(ContentManifest manifest) =>
        CommunityOutpostConstants.IsNonRetailIdentifier(manifest.Id.Value) ||
        CommunityOutpostConstants.IsNonRetailIdentifier(manifest.Name) ||
        (manifest.Metadata?.Tags != null && manifest.Metadata.Tags.Any(CommunityOutpostConstants.IsNonRetailIdentifier));

    private static bool IsNonRetail(ContentSearchResult result) =>
        CommunityOutpostConstants.IsNonRetailIdentifier(result.Id) ||
        CommunityOutpostConstants.IsNonRetailIdentifier(result.Name) ||
        (result.Tags != null && result.Tags.Any(CommunityOutpostConstants.IsNonRetailIdentifier));

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

                    // For Community Outpost, ensure it is a Community Patch and exclude base game content (10gn, 10zh)
                    // Base games should only be created as fallback when user explicitly declines Community Patch
                    if (string.Equals(publisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase) &&
                        !CommunityOutpostConstants.IsCommunityPatch(m))
                    {
                        logger.LogDebug(
                            "Skipping non-Community Patch manifest {ManifestId} for Community Outpost publisher client",
                            m.Id);
                        return false;
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

    private ContentSearchResult? FindCandidate(
        IEnumerable<ContentSearchResult> results,
        bool isCommunityOutpost,
        bool isNonRet,
        string publisherType)
    {
        var candidates = isCommunityOutpost
            ? results.Where(r => CommunityOutpostConstants.IsCommunityPatch(r) && (isNonRet ? IsNonRetail(r) : !IsNonRetail(r)))
            : results;

        var selected = candidates.FirstOrDefault();
        if (selected == null)
        {
            var variant = isNonRet ? "non-retail" : "retail";
            logger.LogDebug(
                "No matching {Variant} content discovered from {PublisherType}",
                variant,
                publisherType);
        }

        return selected;
    }

    private async Task AcquirePublisherClientContentAsync(
        GameClient gameClient,
        bool isCommunityOutpost,
        bool isNonRet,
        CancellationToken cancellationToken)
    {
        try
        {
            var publisherType = gameClient.PublisherType!;

            // TODO: Localization - Move these strings to localization system when implemented
            logger.LogInformation(
                "Acquiring content from provider for publisher client: {ClientName} (PublisherType: '{PublisherType}', IsNonRet: {IsNonRet})",
                gameClient.Name,
                publisherType,
                isNonRet);

            var publisherDisplayName = GetPublisherDisplayName(publisherType, isNonRet);

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
                    localizationService.GetLocalizedString("GameProfiles.Notification.PublisherContentNotFound.Title", $"{publisherDisplayName} Not Found", publisherDisplayName),
                    localizationService.GetLocalizedString("GameProfiles.Notification.PublisherContentNotFound.Message", $"Could not find {publisherDisplayName} content to download.", publisherDisplayName));
                return;
            }

            var contentToAcquire = FindCandidate(searchResult.Data, isCommunityOutpost, isNonRet, publisherType);
            if (contentToAcquire == null)
            {
                var variant = isNonRet ? "non-retail" : "retail";
                logger.LogWarning(
                    "No matching {Variant} content discovered from {PublisherType} for acquisition",
                    variant,
                    publisherType);
                notificationService.ShowWarning(
                    localizationService.GetLocalizedString("GameProfiles.Notification.PublisherContentNotFound.Title", $"{publisherDisplayName} Not Found", publisherDisplayName),
                    localizationService.GetLocalizedString("GameProfiles.Notification.PublisherContentNotFound.Message", $"Could not find {publisherDisplayName} content to download.", publisherDisplayName));
                return;
            }

            logger.LogInformation(
                "Found {PublisherType} content to acquire: {Name} v{Version}",
                publisherType,
                contentToAcquire.Name,
                contentToAcquire.Version);

            using var scope = new DownloadNotificationScope(notificationService, contentToAcquire.Name);
            try
            {
                var acquireResult = await contentOrchestrator.AcquireContentAsync(contentToAcquire, scope, cancellationToken);
                if (acquireResult.Success && acquireResult.Data != null)
                {
                    logger.LogInformation(
                        "Successfully acquired content for publisher client {ClientName}, manifest: {ManifestId}",
                        gameClient.Name,
                        acquireResult.Data.Id);

                    scope.CompleteSuccess(
                        $"Successfully downloaded {contentToAcquire.Name} v{contentToAcquire.Version}.",
                        $"{publisherDisplayName} Downloaded");
                }
                else
                {
                    var errorMsg = acquireResult.FirstError ?? "Acquisition failed";
                    logger.LogWarning(
                        "Failed to acquire content for publisher client {ClientName}: {Error}",
                        gameClient.Name,
                        errorMsg);

                    scope.CompleteFailure(errorMsg);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is cooperative: signal it via the scope without logging,
                // since logging and rethrowing the same exception violates S2139.
                scope.CompleteCanceled();
                throw;
            }
            catch (Exception ex)
            {
                scope.CompleteFailure(ex.Message);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in publisher client content acquisition for {ClientName}", gameClient.Name);
        }
    }

    private async Task<bool> CheckForNewerVersionAsync(
        string publisherType,
        List<ContentManifest> existingManifests,
        bool isCommunityOutpost,
        bool isNonRet,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the highest version from existing manifests
            var highestInstalledVersion = existingManifests
                .Select(m => m.Version)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .OrderByDescending(v => v, versionComparer.GetScheme(publisherType))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(highestInstalledVersion))
            {
                logger.LogDebug("No valid version found in existing manifests for {PublisherType}", publisherType);
                return true; // If we can't determine version, assume we should update
            }

            logger.LogDebug(
                "Highest installed version for {PublisherType} (IsNonRet: {IsNonRet}): {Version}",
                publisherType,
                isNonRet,
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

            var latestAvailable = FindCandidate(searchResult.Data, isCommunityOutpost, isNonRet, publisherType);
            if (latestAvailable == null)
            {
                return false;
            }

            var latestAvailableVersion = latestAvailable.Version;

            if (string.IsNullOrWhiteSpace(latestAvailableVersion))
            {
                logger.LogDebug("No version information in discovered content for {PublisherType}", publisherType);
                return false; // If no version info, assume current is fine
            }

            logger.LogDebug(
                "Latest available version for {PublisherType} (IsNonRet: {IsNonRet}): {Version}",
                publisherType,
                isNonRet,
                latestAvailableVersion);

            // Compare versions
            var comparison = versionComparer.Compare(
                latestAvailableVersion,
                highestInstalledVersion,
                publisherType);

            if (comparison > 0)
            {
                logger.LogInformation(
                    "Newer version available for {PublisherType} (IsNonRet: {IsNonRet}): {LatestVersion} > {InstalledVersion}",
                    publisherType,
                    isNonRet,
                    latestAvailableVersion,
                    highestInstalledVersion);
                return true;
            }

            logger.LogDebug(
                "Installed version is up to date for {PublisherType} (IsNonRet: {IsNonRet}): {InstalledVersion} >= {LatestVersion}",
                publisherType,
                isNonRet,
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
}
