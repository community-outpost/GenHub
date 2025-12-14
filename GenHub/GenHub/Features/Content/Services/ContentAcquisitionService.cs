using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Service for acquiring content from providers.
/// Centralizes content discovery, resolution, download, and installation logic.
/// </summary>
public class ContentAcquisitionService(
    Lazy<IContentOrchestrator> orchestrator,
    IContentManifestPool manifestPool,
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IPublisherManifestFactory> publisherFactories,
    INotificationService? notificationService,
    ILogger<ContentAcquisitionService> logger)
    : IContentAcquisitionService
{
    private readonly Lazy<IContentOrchestrator> _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    private readonly IContentManifestPool _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
    private readonly IEnumerable<IPublisherManifestFactory> _publisherFactories = publisherFactories ?? throw new ArgumentNullException(nameof(publisherFactories));
    private readonly INotificationService? _notificationService = notificationService;
    private readonly ILogger<ContentAcquisitionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly GitHubReleasesDiscoverer _gitHubDiscoverer = discoverers.OfType<GitHubReleasesDiscoverer>().FirstOrDefault()
            ?? throw new InvalidOperationException("GitHubReleasesDiscoverer not registered");

    private readonly IContentResolver _gitHubResolver = resolvers.FirstOrDefault(r =>
            r.ResolverId?.Equals(GitHubConstants.GitHubReleaseResolverId, StringComparison.OrdinalIgnoreCase) == true)
            ?? throw new InvalidOperationException("GitHubResolver not registered");

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> AcquireGeneralsOnlineContentAsync(
        string variant,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await AcquireGeneralsOnlineContentAsync(variant, null, progress, cancellationToken);
    }

    /// <summary>
    /// Acquires GeneralsOnline content, optionally checking a local installation first.
    /// </summary>
    public async Task<OperationResult<ContentManifest>> AcquireGeneralsOnlineContentAsync(
        string variant,
        string? existingInstallationPath,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(variant))
        {
            return OperationResult<ContentManifest>.CreateFailure("Variant cannot be null or empty");
        }

        try
        {
            _logger.LogInformation("Acquiring GeneralsOnline content for variant: {Variant}", variant);

            // First, check if the correct variant manifest is already in the pool
            var existingManifestResult = await FindManifestInPoolAsync(
                GeneralsOnlineConstants.PublisherType,
                variant,
                cancellationToken);

            if (existingManifestResult.Success && existingManifestResult.Data != null)
            {
                _logger.LogInformation(
                    "Found existing GeneralsOnline {Variant} manifest in pool: {ManifestId}",
                    variant,
                    existingManifestResult.Data.Id);
                return OperationResult<ContentManifest>.CreateSuccess(existingManifestResult.Data);
            }

            // Check for local installation if path provided
            if (!string.IsNullOrEmpty(existingInstallationPath) && Directory.Exists(existingInstallationPath))
            {
                var factory = _publisherFactories.OfType<GeneralsOnlineManifestFactory>().FirstOrDefault();
                if (factory != null)
                {
                    _logger.LogInformation("Attempting to create GeneralsOnline manifests from local installation at {Path}", existingInstallationPath);
                    var localManifests = await factory.CreateManifestsFromLocalInstallAsync(existingInstallationPath, cancellationToken);

                    if (localManifests.Count > 0)
                    {
                        _logger.LogInformation("Successfully created {Count} local manifests for GeneralsOnline", localManifests.Count);

                        // Add all created manifests to the pool
                        foreach (var m in localManifests)
                        {
                            await _manifestPool.AddManifestAsync(m, existingInstallationPath, cancellationToken);
                        }

                        // Find the one we asked for
                        var targetManifest = localManifests.FirstOrDefault(m =>
                            m.Name.Contains(variant, StringComparison.OrdinalIgnoreCase) ||
                            m.Id.Value.Contains(variant, StringComparison.OrdinalIgnoreCase));

                        if (targetManifest != null)
                        {
                            return OperationResult<ContentManifest>.CreateSuccess(targetManifest);
                        }
                    }
                }
            }

            // Not in pool, need to acquire content
            _notificationService?.ShowInfo(
                "Searching for Content",
                $"Looking for GeneralsOnline {variant} releases...",
                autoDismissMs: NotificationDurations.Medium);

            var searchQuery = new ContentSearchQuery
            {
                SearchTerm = "generalsonline",
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                Take = 10,
                ProviderName = GeneralsOnlineConstants.PublisherType
            };

            var searchResult = await _orchestrator.Value.SearchAsync(searchQuery, cancellationToken);
            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    "No GeneralsOnline content found. Ensure network connectivity.");
            }

            // Find any GeneralsOnline result to trigger download (acquisition creates all variants)
            var matchingResult = searchResult.Data.FirstOrDefault(r =>
                r.Name.Contains("generalsonline", StringComparison.OrdinalIgnoreCase));

            if (matchingResult == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    "No GeneralsOnline content found in search results.");
            }

            _logger.LogInformation("Downloading GeneralsOnline content: {Name}", matchingResult.Name);

            // Notify user about download
            _notificationService?.ShowInfo(
                "Downloading Content",
                $"Downloading GeneralsOnline {variant}... This may take a moment.",
                autoDismissMs: NotificationDurations.Long);

            // Create progress handler to notify on extraction
            bool extractionNotified = false;
            var internalProgress = new Progress<ContentAcquisitionProgress>(p =>
            {
                progress?.Report(p); // Forward upstream

                if (p.Phase == ContentAcquisitionPhase.Extracting && !extractionNotified)
                {
                    extractionNotified = true;
                    _notificationService?.ShowInfo(
                       "Extracting Content",
                       $"Extracting GeneralsOnline {variant}... almost done!",
                       autoDismissMs: NotificationDurations.Medium);
                }
            });

            var acquisitionResult = await _orchestrator.Value.AcquireContentAsync(matchingResult, internalProgress, cancellationToken);
            if (!acquisitionResult.Success || acquisitionResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to download GeneralsOnline content: {string.Join(", ", acquisitionResult.Errors)}");
            }

            // After acquisition, the pool now contains all GeneralsOnline manifests.
            // Look up the correct variant manifest from the pool.
            var variantManifestResult = await FindManifestInPoolAsync(
                GeneralsOnlineConstants.PublisherType,
                variant,
                cancellationToken);

            if (!variantManifestResult.Success || variantManifestResult.Data == null)
            {
                // Fall back to the acquired manifest if pool lookup fails
                _logger.LogWarning(
                    "Could not find {Variant} manifest in pool, using acquired manifest: {ManifestId}",
                    variant,
                    acquisitionResult.Data.Id);
                return OperationResult<ContentManifest>.CreateSuccess(acquisitionResult.Data);
            }

            _logger.LogInformation(
                "Successfully acquired GeneralsOnline content: {ManifestId} ({ManifestName})",
                variantManifestResult.Data.Id,
                variantManifestResult.Data.Name);

            return OperationResult<ContentManifest>.CreateSuccess(variantManifestResult.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring GeneralsOnline content for variant {Variant}", variant);
            return OperationResult<ContentManifest>.CreateFailure($"Error acquiring content: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> AcquireSuperHackersContentAsync(
        GameType gameType,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await AcquireSuperHackersContentAsync(gameType, null, progress, cancellationToken);
    }

    /// <summary>
    /// Acquires SuperHackers content, optionally checking a local installation first.
    /// </summary>
    public async Task<OperationResult<ContentManifest>> AcquireSuperHackersContentAsync(
        GameType gameType,
        string? existingInstallationPath,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Acquiring SuperHackers content for game type: {GameType}", gameType);

            // Check for local installation if path provided
            if (!string.IsNullOrEmpty(existingInstallationPath) && Directory.Exists(existingInstallationPath))
            {
                var factory = _publisherFactories.OfType<SuperHackersManifestFactory>().FirstOrDefault();
                if (factory != null)
                {
                    _logger.LogInformation("Attempting to create SuperHackers manifests from local installation at {Path}", existingInstallationPath);
                    var localManifests = await factory.CreateManifestsFromLocalInstallAsync(existingInstallationPath, cancellationToken);

                    if (localManifests.Count > 0)
                    {
                        _logger.LogInformation("Successfully created {Count} local manifests for SuperHackers", localManifests.Count);

                        // Add all created manifests to the pool
                        foreach (var m in localManifests)
                        {
                            await _manifestPool.AddManifestAsync(m, existingInstallationPath, cancellationToken);
                        }

                        // Find the one we asked for
                        var targetManifest = localManifests.FirstOrDefault(m => m.TargetGame == gameType);

                        if (targetManifest != null)
                        {
                            return OperationResult<ContentManifest>.CreateSuccess(targetManifest);
                        }
                    }
                }
            }

            _notificationService?.ShowInfo(
                "Searching for Content",
                $"Looking for SuperHackers {gameType} releases...",
                autoDismissMs: NotificationDurations.Medium);

            var searchQuery = new ContentSearchQuery
            {
                ProviderName = SuperHackersConstants.PublisherId
            };
            var result = await _gitHubDiscoverer.DiscoverAsync(searchQuery, cancellationToken);

            _logger.LogDebug(
                "GitHubReleasesDiscoverer returned {Count} releases",
                result.Data?.Count() ?? 0);

            if (!result.Success)
            {
                var errors = string.Join("; ", result.Errors);
                _logger.LogWarning("GitHub discovery failed: {Errors}", errors);
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to discover SuperHackers releases: {errors}");
            }

            if (result.Data == null || !result.Data.Any())
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    "No SuperHackers releases found from GitHub. Ensure network connectivity and that the repository contains releases.");
            }

            var allReleases = result.Data.Select(r =>
            {
                r.ProviderName = PublisherTypeConstants.TheSuperHackers;
                return r;
            }).ToList();

            var gameNamePattern = gameType == GameType.ZeroHour ? "zerohour" : "generals";
            var excludePattern = gameType == GameType.Generals ? "zerohour" : null;

            var matchingReleases = allReleases
                .Where(r =>
                {
                    var nameLower = r.Name?.ToLowerInvariant() ?? string.Empty;
                    var matchesTarget = nameLower.Contains(gameNamePattern);
                    var excludeMatch = excludePattern != null && nameLower.Contains(excludePattern);
                    return matchesTarget && !excludeMatch;
                })
                .OrderByDescending(r => r.LastUpdated)
                .ToList();

            if (!matchingReleases.Any())
            {
                _logger.LogWarning("No releases matching '{Pattern}', using latest release", gameNamePattern);
                matchingReleases = allReleases
                    .OrderByDescending(r => r.LastUpdated)
                    .Take(1)
                    .ToList();
            }

            if (!matchingReleases.Any())
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"No SuperHackers releases found for {gameType}");
            }

            var latestRelease = matchingReleases.First();

            _logger.LogInformation("Found SuperHackers release: {Name} v{Version}", latestRelease.Name, latestRelease.Version);

            var resolveResult = await _gitHubResolver.ResolveAsync(latestRelease, cancellationToken);
            if (!resolveResult.Success || resolveResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to resolve SuperHackers manifest: {resolveResult.FirstError}");
            }

            var manifest = resolveResult.Data;

            _logger.LogInformation("Downloading SuperHackers content: {Name}", latestRelease.Name);

            // Notify user about download
            _notificationService?.ShowInfo(
                "Downloading Content",
                $"Downloading SuperHackers {gameType}... This may take a moment.",
                autoDismissMs: NotificationDurations.Long);

            latestRelease.SetData(manifest);

            // Create progress handler to notify on extraction
            bool extractionNotified = false;
            var internalProgress = new Progress<ContentAcquisitionProgress>(p =>
            {
                progress?.Report(p); // Forward upstream

                if (p.Phase == ContentAcquisitionPhase.Extracting && !extractionNotified)
                {
                    extractionNotified = true;
                    _notificationService?.ShowInfo(
                       "Extracting Content",
                       $"Extracting SuperHackers {gameType}... almost done!",
                       autoDismissMs: NotificationDurations.Medium);
                }
            });

            var acquisitionResult = await _orchestrator.Value.AcquireContentAsync(latestRelease, internalProgress, cancellationToken);
            if (!acquisitionResult.Success || acquisitionResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to download SuperHackers content: {string.Join(", ", acquisitionResult.Errors)}");
            }

            // After acquisition, the pool now contains manifests for both Generals and ZeroHour.
            // Look up the correct game type manifest from the pool.
            var gameTypeSuffix = gameType == GameType.Generals
                ? SuperHackersConstants.GeneralsSuffix
                : SuperHackersConstants.ZeroHourSuffix;

            var gameTypeManifestResult = await FindManifestInPoolAsync(
                SuperHackersConstants.PublisherId,
                gameTypeSuffix,
                cancellationToken);

            if (!gameTypeManifestResult.Success || gameTypeManifestResult.Data == null)
            {
                // Fall back to the acquired manifest if pool lookup fails
                _logger.LogWarning(
                    "Could not find {GameType} manifest in pool, using acquired manifest: {ManifestId}",
                    gameType,
                    acquisitionResult.Data.Id);
                return OperationResult<ContentManifest>.CreateSuccess(acquisitionResult.Data);
            }

            _logger.LogInformation(
                "Successfully acquired SuperHackers content: {ManifestId} ({ManifestName})",
                gameTypeManifestResult.Data.Id,
                gameTypeManifestResult.Data.Name);

            return OperationResult<ContentManifest>.CreateSuccess(gameTypeManifestResult.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring SuperHackers content for game type {GameType}", gameType);
            return OperationResult<ContentManifest>.CreateFailure($"Error acquiring content: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> AcquireContentFromSearchResultAsync(
        ContentSearchResult searchResult,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (searchResult == null)
        {
            return OperationResult<ContentManifest>.CreateFailure("Search result cannot be null");
        }

        try
        {
            _logger.LogInformation("Acquiring content from search result: {Name}", searchResult.Name);

            var manifest = searchResult.GetData<ContentManifest>();
            if (manifest == null)
            {
                _logger.LogWarning("Manifest not attached to search result, resolving for {ContentId}", searchResult.Id);

                var resolveResult = await _orchestrator.Value.GetContentManifestAsync(
                    searchResult.ProviderName,
                    searchResult.Id,
                    cancellationToken);

                if (!resolveResult.Success || resolveResult.Data == null)
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Failed to resolve manifest: {string.Join(", ", resolveResult.Errors)}");
                }

                manifest = resolveResult.Data;
                searchResult.SetData(manifest);
            }

            var acquisitionResult = await _orchestrator.Value.AcquireContentAsync(searchResult, progress, cancellationToken);
            if (!acquisitionResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to download content: {string.Join(", ", acquisitionResult.Errors)}");
            }

            _logger.LogInformation("Successfully acquired content: {ManifestId}", manifest.Id);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring content from search result: {Name}", searchResult.Name);
            return OperationResult<ContentManifest>.CreateFailure($"Error acquiring content: {ex.Message}");
        }
    }

    /// <summary>
    /// Searches the manifest pool for a manifest matching the given publisher and variant/suffix.
    /// </summary>
    /// <param name="publisherType">The publisher type identifier (e.g., "generalsonline", "thesuperhackers").</param>
    /// <param name="variantOrSuffix">The variant or suffix to match (e.g., "30hz", "60hz", "generals", "zerohour").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the matching manifest, or failure if not found.</returns>
    private async Task<OperationResult<ContentManifest>> FindManifestInPoolAsync(
        string publisherType,
        string variantOrSuffix,
        CancellationToken cancellationToken)
    {
        var allManifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!allManifestsResult.Success || allManifestsResult.Data == null)
        {
            return OperationResult<ContentManifest>.CreateFailure("Failed to get manifests from pool");
        }

        // Look for a manifest whose ID contains both the publisher type and variant/suffix
        // Example IDs: "1.20241215.generalsonline.gameclient.30hz", "1.20241215.thesuperhackers.gameclient.generals"
        var matchingManifest = allManifestsResult.Data.FirstOrDefault(m =>
            m.Id.Value.Contains(publisherType, StringComparison.OrdinalIgnoreCase) &&
            m.Id.Value.Contains(variantOrSuffix, StringComparison.OrdinalIgnoreCase) &&
            m.ContentType == ContentType.GameClient);

        if (matchingManifest == null)
        {
            _logger.LogDebug(
                "No manifest found in pool matching publisher '{Publisher}' and variant '{Variant}'",
                publisherType,
                variantOrSuffix);
            return OperationResult<ContentManifest>.CreateFailure(
                $"No manifest found for {publisherType} {variantOrSuffix}");
        }

        _logger.LogDebug(
            "Found matching manifest in pool: {ManifestId}",
            matchingManifest.Id);

        return OperationResult<ContentManifest>.CreateSuccess(matchingManifest);
    }
}
