using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Primary orchestrator for the GenHub content system. Coordinates multiple content providers
/// and manages the complete content lifecycle focused on discovery, resolution, and delivery.
/// </summary>
/// <param name="logger">The logger instance.</param>
/// <param name="providers">The content providers that orchestrate discovery-resolution-delivery pipelines.</param>
/// <param name="discoverers">The content discoverers.</param>
/// <param name="resolvers">The content resolvers.</param>
/// <param name="cache">The dynamic content cache service for performance optimization.</param>
/// <param name="contentValidator">The content validator service for manifest and content integrity.</param>
/// <param name="manifestPool">The manifest pool for acquired content.</param>
/// <param name="installationService">The game installation service for detecting installations.</param>
/// <param name="installationCasPoolService">The installation CAS pool selector.</param>
/// <param name="factoryResolver">The publisher manifest factory resolver for post-processing.</param>
/// <param name="deliverers">The content deliverers.</param>
public class ContentOrchestrator(
    ILogger<ContentOrchestrator> logger,
    IEnumerable<IContentProvider> providers,
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IDynamicContentCache cache,
    IContentValidator contentValidator,
    IContentManifestPool manifestPool,
    IGameInstallationService installationService,
    IInstallationCasPoolService installationCasPoolService,
    PublisherManifestFactoryResolver? factoryResolver = null,
    IEnumerable<IContentDeliverer>? deliverers = null) : IContentOrchestrator
{
    private readonly ConcurrentBag<IContentProvider> _providers = [.. providers];
    private readonly ConcurrentBag<IContentDiscoverer> _discoverers = [.. discoverers];
    private readonly ConcurrentBag<IContentDeliverer> _deliverers = [.. deliverers ?? []];
    private readonly ConcurrentDictionary<string, IContentResolver> _resolvers = InitializeResolvers(resolvers, logger);
    private readonly object _providerLock = new();
    private readonly object _progressLock = new();

    /// <summary>
    /// Searches for content across all enabled providers, leveraging their internal pipelines.
    /// Each provider orchestrates its own discovery-resolution-delivery pipeline internally.
    /// </summary>
    /// <param name="query">The search criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing an aggregated list of search results from all providers.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the query is null.</exception>
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> SearchAsync(
        ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Take <= 0 || query.Take > 1000)
        {
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure("Take must be between 1 and 1000");
        }

        // Checked before the cache lookup so a cache hit cannot mask an already-cancelled caller.
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogDebug("Starting orchestrated content search with query: {SearchTerm}, ContentType: {ContentType}", query.SearchTerm, query.ContentType);

        // Check cache first
        var cacheKey = query.ToCacheKey();
        var cachedResults = await cache.GetAsync<List<ContentSearchResult>>(cacheKey, cancellationToken);
        if (cachedResults != null)
        {
            logger.LogDebug("Returning cached search results for query: {SearchTerm}", query.SearchTerm);
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(cachedResults);
        }

        ConcurrentBag<string> errors = [];

        // Orchestrate search across all enabled providers concurrently
        var providersToSearch = _providers.Where(p => p.IsEnabled);

        // Optimization: If provider is specified in query, only search that provider
        if (!string.IsNullOrEmpty(query.ProviderName))
        {
            providersToSearch = providersToSearch.Where(p => p.SourceName.Equals(query.ProviderName, StringComparison.OrdinalIgnoreCase));
        }

        var searchTasks = providersToSearch.ToList();
        if (searchTasks.Count == 0)
        {
            logger.LogWarning("No enabled providers available for search");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure("No enabled providers available");
        }

        var searchTasksAsync = searchTasks
            .Select(async provider =>
            {
                var providerResults = new List<ContentSearchResult>();
                try
                {
                    logger.LogDebug("Executing search via provider: {ProviderName}", provider.SourceName);
                    var result = await provider.SearchAsync(query, cancellationToken);

                    if (result.Success && result.Data != null)
                    {
                        foreach (var item in result.Data)
                        {
                            // Ensure provider name is set correctly
                            if (string.IsNullOrEmpty(item.ProviderName))
                            {
                                item.ProviderName = provider.SourceName;
                            }

                            providerResults.Add(item);
                        }

                        logger.LogDebug("Provider {ProviderName} returned {ResultCount} results", provider.SourceName, result.Data.Count());
                    }
                    else
                    {
                        errors.Add($"{provider.SourceName}: {result.FirstError}");
                        logger.LogWarning("Provider {ProviderName} failed: {Error}", provider.SourceName, result.FirstError);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Filtered on the caller's token: a provider timing out on its own token raises
                    // TaskCanceledException too, and must not abort the other providers' results.
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Search failed for provider: {ProviderName}", provider.SourceName);
                    errors.Add($"{provider.SourceName}: {ex.Message}");
                }

                return providerResults;
            });

        var resultsPerProvider = await Task.WhenAll(searchTasksAsync);
        var allResults = resultsPerProvider.SelectMany(r => r).ToList();

        // A provider that handled cancellation internally reports it as a failed result rather
        // than an exception, which would otherwise surface here as an empty successful search.
        cancellationToken.ThrowIfCancellationRequested();

        // Deduplicate results by manifest ID across providers before sorting and pagination,
        // preferring specialized publisher providers over generic GitHub providers.
        var deduplicatedResults = allResults
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(r =>
                    !string.Equals(r.ProviderName, ContentSourceNames.GitHubDiscoverer, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(r.ProviderName, ContentSourceNames.GitHubReleasesDiscoverer, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .First())
            .ToList();

        // Apply orchestrator-level sorting and pagination
        var sortedResults = ApplySorting(deduplicatedResults, query.SortOrder)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();

        // Cache results for future queries
        if (sortedResults.Count > 0)
        {
            await cache.SetAsync(cacheKey, sortedResults, TimeSpan.FromMinutes(5), cancellationToken);
        }

        logger.LogInformation("Content search completed. Total results: {ResultCount}, Errors: {ErrorCount}", sortedResults.Count, errors.Count);

        return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(sortedResults);
    }

    /// <summary>
    /// Retrieves the manifest for a specific piece of content from a specific provider.
    /// Delegates to the provider's internal pipeline for manifest retrieval and resolution.
    /// </summary>
    /// <param name="providerName">The name of the provider.</param>
    /// <param name="contentId">The unique identifier of the content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing the game manifest.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="providerName"/> or <paramref name="contentId"/> is null.</exception>
    public async Task<OperationResult<ContentManifest>> GetContentManifestAsync(
        string providerName, string contentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerName);
        ArgumentNullException.ThrowIfNull(contentId);

        // Check cache first
        var cacheKey = $"manifest::{providerName}::{contentId}";
        var cachedManifest = await cache.GetAsync<ContentManifest>(cacheKey, cancellationToken);
        if (cachedManifest != null)
        {
            logger.LogDebug("Returning cached manifest for {ProviderName}::{ContentId}", providerName, contentId);
            return OperationResult<ContentManifest>.CreateSuccess(cachedManifest);
        }

        var provider = _providers.FirstOrDefault(p => p.SourceName == providerName);
        if (provider == null)
        {
            return OperationResult<ContentManifest>.CreateFailure($"Provider not found: {providerName}");
        }

        logger.LogDebug("Retrieving manifest from provider {ProviderName} for content {ContentId}", providerName, contentId);

        var result = await provider.GetValidatedContentAsync(contentId, cancellationToken);

        // Cache successful results
        if (result.Success && result.Data != null)
        {
            result.Data.OriginalProviderName = providerName;
            result.Data.OriginalContentId = contentId;
            await cache.SetAsync(cacheKey, result.Data, TimeSpan.FromHours(1), cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Gets featured content, optionally filtered by content type.
    /// This method leverages provider orchestration for discovering featured content.
    /// </summary>
    /// <param name="contentType">Optional content type filter.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that returns a result containing featured content search results.</returns>
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> GetFeaturedContentAsync(
        ContentType? contentType = null, CancellationToken cancellationToken = default)
    {
        var query = new ContentSearchQuery
        {
            SortOrder = ContentSortField.Relevance,
            Take = 20,
            ContentType = contentType,
            SearchTerm = string.Empty,
        };

        var result = await SearchAsync(query, cancellationToken);
        return result.Success
            ? OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(result.Data ?? [])
            : OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(result.Errors);
    }

    /// <summary>
    /// Gets all available content providers currently registered with the orchestrator.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that returns a result containing available content providers.</returns>
    public Task<OperationResult<IEnumerable<IContentProvider>>> GetAvailableProvidersAsync(CancellationToken cancellationToken = default)
    {
        var currentProviders = _providers.ToList();
        return Task.FromResult(OperationResult<IEnumerable<IContentProvider>>.CreateSuccess(currentProviders));
    }

    /// <summary>
    /// Registers a new content provider with the orchestrator.
    /// </summary>
    /// <param name="provider">The content provider to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="provider"/> is null.</exception>
    public void RegisterProvider(IContentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_providerLock)
        {
            if (_providers.All(p => !string.Equals(p.SourceName, provider.SourceName, StringComparison.OrdinalIgnoreCase)))
            {
                _providers.Add(provider);
                logger.LogInformation("Registered content provider: {ProviderName}", provider.SourceName);
            }
            else
            {
                logger.LogWarning("Attempted to register duplicate provider: {ProviderName}", provider.SourceName);
            }
        }
    }

    /// <summary>
    /// Unregisters a content provider by name.
    /// </summary>
    /// <param name="providerName">The name of the provider to unregister.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="providerName"/> is null or empty.</exception>
    public void UnregisterProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentNullException(nameof(providerName));
        }

        lock (_providerLock)
        {
            var removed = new ConcurrentBag<IContentProvider>();
            IContentProvider? providerToRemove = null;

            while (_providers.TryTake(out var p))
            {
                if (p.SourceName == providerName)
                {
                    providerToRemove = p;
                }
                else
                {
                    removed.Add(p);
                }
            }

            while (removed.TryTake(out var p))
            {
                _providers.Add(p);
            }

            if (providerToRemove != null)
            {
                logger.LogInformation("Unregistered content provider: {ProviderName}", providerName);
            }
            else
            {
                logger.LogWarning("Attempted to unregister non-existent provider: {ProviderName}", providerName);
            }
        }
    }

    /// <summary>
    /// Resolves a manifest for a discovered content item using the orchestrator's resolver registry.
    /// This is a fallback method for direct resolution when providers don't handle it internally.
    /// </summary>
    /// <param name="contentSearchResult">The discovered content item.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing the game manifest.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="contentSearchResult"/> is null.</exception>
    public async Task<OperationResult<ContentManifest>> ResolveManifestAsync(
        ContentSearchResult contentSearchResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentSearchResult);

        if (string.IsNullOrEmpty(contentSearchResult.ResolverId))
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Discovered content '{contentSearchResult.Name}' does not have a ResolverId.");
        }

        var lookupId = contentSearchResult.ResolverId;
        if (!_resolvers.TryGetValue(lookupId, out IContentResolver? resolver))
        {
            var altId = lookupId.Contains('-') ? lookupId.Replace("-", string.Empty) : lookupId;
            _resolvers.TryGetValue(altId, out resolver);
        }

        if (resolver == null)
        {
            var availableResolvers = string.Join(", ", _resolvers.Keys);
            logger.LogError("No resolver found for ResolverId: {ResolverId}. Available resolvers: [{AvailableResolvers}]. Total count: {Count}", contentSearchResult.ResolverId, availableResolvers, _resolvers.Count);

            return OperationResult<ContentManifest>.CreateFailure(
                $"No resolver found for ResolverId: {contentSearchResult.ResolverId}. Available: {availableResolvers}");
        }

        var manifestResult = await resolver.ResolveAsync(contentSearchResult, cancellationToken);
        if (manifestResult.Success && manifestResult.Data != null)
        {
            var validationResult = await contentValidator.ValidateManifestAsync(manifestResult.Data, cancellationToken);
            if (!validationResult.IsValid)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    validationResult.Issues.Select(i => $"Manifest validation failed: {i.Message}"));
            }

            return OperationResult<ContentManifest>.CreateSuccess(manifestResult.Data);
        }

        return manifestResult;
    }

    /// <summary>
    /// Acquires content and stores ContentManifest in pool for later profile usage.
    /// This is the primary method for content acquisition, separating it from workspace preparation.
    /// </summary>
    /// <param name="searchResult">The content search result to acquire.</param>
    /// <param name="progress">Optional progress reporter for acquisition status.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing the acquired game manifest.</returns>
    public async Task<OperationResult<ContentManifest>> AcquireContentAsync(
        ContentSearchResult searchResult,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchResult);

        logger.LogInformation("Acquiring content {ContentName} from {ProviderName}", searchResult.Name, searchResult.ProviderName);

        // Define stages for progress tracking
        const int totalStages = 5;
        var lastUpdateTime = DateTime.UtcNow;
        var highestReportedStage = 0;
        var highestReportedStageProgress = 0d;
        var reportingCompleted = false;

        void ReportProgress(
            int stage,
            string description,
            double stageProgress = 0,
            string? operation = null,
            bool isBottleneck = false,
            string? bottleneckReason = null,
            long bytesProcessed = 0,
            long totalBytes = 0,
            int filesProcessed = 0,
            int totalFiles = 0,
            string? currentFile = null,
            TimeSpan estimatedTimeRemaining = default)
        {
            lock (_progressLock)
            {
                // Provider callbacks can be queued after the acquisition pipeline advances. Never
                // let a delayed callback move the user backwards through the canonical stages.
                if (reportingCompleted || stage < highestReportedStage)
                {
                    return;
                }

                if (stage > highestReportedStage)
                {
                    highestReportedStage = stage;
                    highestReportedStageProgress = 0;
                }

                stageProgress = Math.Max(highestReportedStageProgress, Math.Clamp(stageProgress, 0, 100));
                highestReportedStageProgress = stageProgress;

                var now = DateTime.UtcNow;
                var timeSinceLastUpdate = now - lastUpdateTime;
                lastUpdateTime = now;

                progress?.Report(new ContentAcquisitionProgress
                {
                    CurrentStage = stage,
                    TotalStages = totalStages,
                    StageDescription = description,
                    StageProgress = stageProgress,
                    CurrentOperation = operation ?? description,
                    Phase = stage switch
                    {
                        1 => ContentAcquisitionPhase.ValidatingManifest,
                        2 => ContentAcquisitionPhase.Downloading,
                        3 => ContentAcquisitionPhase.Extracting,
                        4 => ContentAcquisitionPhase.ValidatingFiles,
                        5 => ContentAcquisitionPhase.Completed,
                        _ => ContentAcquisitionPhase.Downloading,
                    },
                    ProgressPercentage = ((stage - 1) * 100.0 / totalStages) + (stageProgress / totalStages),
                    TimeSinceLastUpdate = timeSinceLastUpdate,
                    IsBottleneck = isBottleneck,
                    BottleneckReason = bottleneckReason,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles,
                    CurrentFile = currentFile ?? string.Empty,
                    EstimatedTimeRemaining = estimatedTimeRemaining,
                });
            }
        }

        try
        {
            var provider = _providers.FirstOrDefault(p => p.SourceName.Equals(searchResult.ProviderName, StringComparison.OrdinalIgnoreCase));
            var resolveResult = await ResolveAndValidateManifestAsync(searchResult, provider, ReportProgress, cancellationToken);
            if (!resolveResult.Success || resolveResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(resolveResult);
            }

            var manifest = resolveResult.Data;
            var stagingDir = Path.Combine(Path.GetTempPath(), "GenHub", "Staging", manifest.Id);
            Directory.CreateDirectory(stagingDir);

            try
            {
                var downloadResult = await DownloadAndPrepareContentAsync(provider, manifest, stagingDir, ReportProgress, cancellationToken);
                if (!downloadResult.Success || downloadResult.Data == null)
                {
                    return OperationResult<ContentManifest>.CreateFailure(downloadResult);
                }

                var factoryResult = await ProcessFactoryVariantsAsync(downloadResult.Data, searchResult, stagingDir, ReportProgress, cancellationToken);
                if (!factoryResult.Success || factoryResult.Data == null)
                {
                    return OperationResult<ContentManifest>.CreateFailure(factoryResult);
                }

                var primaryManifest = factoryResult.Data;

                var validateResult = await ValidateStagedFilesAsync(primaryManifest, stagingDir, ReportProgress, progress != null, cancellationToken);
                if (!validateResult.Success)
                {
                    return OperationResult<ContentManifest>.CreateFailure(validateResult);
                }

                var storeResult = await StoreManifestInLibraryAsync(primaryManifest, stagingDir, ReportProgress, cancellationToken);
                if (!storeResult.Success)
                {
                    return OperationResult<ContentManifest>.CreateFailure(storeResult);
                }

                ReportProgress(5, "Complete", 100, "Content acquired successfully");
                reportingCompleted = true;

                logger.LogInformation("Content {ContentName} acquired and stored in manifest pool", searchResult.Name);
                return OperationResult<ContentManifest>.CreateSuccess(primaryManifest);
            }
            finally
            {
                try
                {
                    FileOperationsService.DeleteDirectoryIfExists(stagingDir);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to cleanup staging directory: {StagingDir}", stagingDir);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to acquire content {ContentId}", searchResult.Id);
            return OperationResult<ContentManifest>.CreateFailure($"Content acquisition failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all acquired content manifests from the pool.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result object containing all acquired game manifests.</returns>
    public async Task<OperationResult<IEnumerable<ContentManifest>>> GetAcquiredContentAsync(
        CancellationToken cancellationToken = default)
    {
        var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (manifestsResult.Success)
        {
            return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifestsResult.Data ?? []);
        }

        return OperationResult<IEnumerable<ContentManifest>>.CreateFailure(manifestsResult.Errors);
    }

    /// <summary>
    /// Removes acquired content from the pool.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result indicating success or failure of the removal operation.</returns>
    public async Task<OperationResult<bool>> RemoveAcquiredContentAsync(
        string manifestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifestId);

        try
        {
            // Retrieve the manifest first to get its original provider info for cache invalidation
            var manifestResult = await manifestPool.GetManifestAsync(manifestId, cancellationToken);

            var removalResult = await manifestPool.RemoveManifestAsync(manifestId, cancellationToken: cancellationToken);
            if (!removalResult.Success)
            {
                logger.LogWarning("Failed to remove content {ManifestId} from pool: {Error}", manifestId, removalResult.FirstError);
                return OperationResult<bool>.CreateFailure($"Failed to remove content from pool: {removalResult.FirstError}");
            }

            logger.LogInformation("Removed content {ManifestId} from pool", manifestId);

            // Invalidate related cache entries
            if (manifestResult.Success && manifestResult.Data != null)
            {
                var providerName = manifestResult.Data.OriginalProviderName;
                var contentId = manifestResult.Data.OriginalContentId;

                if (!string.IsNullOrEmpty(providerName) && !string.IsNullOrEmpty(contentId))
                {
                    await cache.InvalidateAsync($"manifest::{providerName}::{contentId}", cancellationToken);
                }
            }

            await cache.InvalidateAsync($"manifest::{manifestId}", cancellationToken);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove content {ManifestId} from pool", manifestId);
            return OperationResult<bool>.CreateFailure($"Failed to remove content: {ex.Message}");
        }
    }

    private async Task<OperationResult<ContentManifest>> ResolveAndValidateManifestAsync(
        ContentSearchResult searchResult,
        IContentProvider? provider,
        Action<int, string, double, string?, bool, string?, long, long, int, int, string?, TimeSpan> reportProgress,
        CancellationToken cancellationToken)
    {
        reportProgress(1, "Resolving content", 0, "Finding content provider...", false, null, 0, 0, 0, 0, null, default);
        reportProgress(1, "Resolving content", 30, "Validating manifest structure...", false, null, 0, 0, 0, 0, null, default);

        ContentManifest? manifest = null;
        var embeddedManifest = searchResult.GetData<ContentManifest>();
        if (embeddedManifest != null)
        {
            manifest = embeddedManifest;
            reportProgress(1, "Resolving content", 60, "Using embedded manifest", false, null, 0, 0, 0, 0, null, default);
        }
        else if (searchResult.RequiresResolution && !string.IsNullOrEmpty(searchResult.ResolverId))
        {
            logger.LogInformation("Content requires resolution. Using resolver: {ResolverId}", searchResult.ResolverId);
            reportProgress(1, "Resolving content", 40, "Resolving content details...", false, null, 0, 0, 0, 0, null, default);

            var resolveResult = await ResolveManifestAsync(searchResult, cancellationToken);
            if (!resolveResult.Success || resolveResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure($"Failed to resolve manifest: {resolveResult.FirstError}");
            }

            manifest = resolveResult.Data;
            reportProgress(1, "Resolving content", 80, "Manifest resolved", false, null, 0, 0, 0, 0, null, default);
        }
        else if (provider != null)
        {
            reportProgress(1, "Resolving content", 40, "Fetching manifest from provider...", false, null, 0, 0, 0, 0, null, default);
            var manifestResult = await provider.GetValidatedContentAsync(searchResult.Id, cancellationToken);
            if (!manifestResult.Success || manifestResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure($"Failed to get manifest: {manifestResult.FirstError}");
            }

            manifest = manifestResult.Data;
        }
        else
        {
            return OperationResult<ContentManifest>.CreateFailure($"Provider not found: {searchResult.ProviderName}");
        }

        manifest.OriginalProviderName ??= searchResult.ProviderName;
        manifest.OriginalContentId ??= searchResult.Id;

        reportProgress(1, "Resolving content", 90, "Validating manifest...", false, null, 0, 0, 0, 0, null, default);

        var validationResult = await contentValidator.ValidateManifestAsync(manifest, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    errors.Select(e => $"Manifest validation failed: {e.Message}"));
            }
        }

        reportProgress(1, "Resolving content", 100, "Manifest validated", false, null, 0, 0, 0, 0, null, default);
        return OperationResult<ContentManifest>.CreateSuccess(manifest);
    }

    private async Task<OperationResult<ContentManifest>> DownloadAndPrepareContentAsync(
        IContentProvider? provider,
        ContentManifest manifest,
        string stagingDir,
        Action<int, string, double, string?, bool, string?, long, long, int, int, string?, TimeSpan> reportProgress,
        CancellationToken cancellationToken)
    {
        reportProgress(2, "Downloading", 0, "Starting download...", false, null, 0, 0, 0, 0, null, default);

        var downloadProgress = new Progress<ContentAcquisitionProgress>(p =>
        {
            var stagePercent = p.TotalBytes > 0
                ? (double)p.BytesProcessed / p.TotalBytes * 100
                : Math.Clamp(p.ProgressPercentage, 0, 100);

            var operation = p.CurrentOperation;
            if (string.IsNullOrWhiteSpace(operation))
            {
                operation = p.TotalBytes > 0
                    ? $"Downloading: {ByteFormatHelper.FormatBytes(p.BytesProcessed)} / {ByteFormatHelper.FormatBytes(p.TotalBytes)}"
                    : "Downloading content";
            }

            reportProgress(2, "Downloading", stagePercent, operation, p.IsBottleneck, p.BottleneckReason, p.BytesProcessed, p.TotalBytes, p.FilesProcessed, p.TotalFiles, p.CurrentFile, p.EstimatedTimeRemaining);
        });

        OperationResult<ContentManifest> prepareResult;
        if (provider != null)
        {
            prepareResult = await provider.PrepareContentAsync(manifest, stagingDir, downloadProgress, cancellationToken);
        }
        else
        {
            var deliverer = _deliverers.FirstOrDefault(d =>
                !string.Equals(d.SourceName, ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase) &&
                d.CanDeliver(manifest))
                ?? _deliverers.FirstOrDefault(d => d.CanDeliver(manifest));

            if (deliverer == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"No suitable content deliverer found for manifest {manifest.Id}");
            }

            prepareResult = await deliverer.DeliverContentAsync(manifest, stagingDir, downloadProgress, cancellationToken);
        }

        if (!prepareResult.Success || prepareResult.Data == null)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content preparation failed: {prepareResult.FirstError}");
        }

        var totalContentSize = prepareResult.Data.Files.Sum(f => f.Size);
        reportProgress(2, "Downloading", 100, "Download complete", false, null, totalContentSize, totalContentSize, 0, 0, null, default);
        return prepareResult;
    }

    private async Task<OperationResult<ContentManifest>> ProcessFactoryVariantsAsync(
        ContentManifest preparedData,
        ContentSearchResult searchResult,
        string stagingDir,
        Action<int, string, double, string?, bool, string?, long, long, int, int, string?, TimeSpan> reportProgress,
        CancellationToken cancellationToken)
    {
        reportProgress(3, "Processing files", 0, "Extracting content...", false, null, 0, 0, 0, 0, null, default);

        var factory = factoryResolver?.ResolveFactory(preparedData);
        if (factory == null)
        {
            logger.LogDebug("No factory found for manifest {ManifestId}, skipping post-processing", preparedData.Id);
            reportProgress(3, "Processing files", 100, "Files processed", false, null, 0, 0, 0, 0, null, default);
            return OperationResult<ContentManifest>.CreateSuccess(preparedData);
        }

        logger.LogInformation("Post-processing manifest {ManifestId} with factory {FactoryType}", preparedData.Id, factory.GetType().Name);
        reportProgress(3, "Processing files", 20, "Processing with publisher-specific factory...", false, null, 0, 0, 0, 0, null, default);

        var processedManifests = await factory.CreateManifestsFromExtractedContentAsync(
            preparedData,
            stagingDir,
            cancellationToken);

        if (processedManifests.Count == 0)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                "Factory returned no manifests after processing");
        }

        foreach (var processed in processedManifests)
        {
            processed.OriginalProviderName ??= searchResult.ProviderName;
            processed.OriginalContentId ??= searchResult.Id;
        }

        var primaryManifest = SelectPrimaryManifest(processedManifests, searchResult);
        var otherVariants = processedManifests.Where(m => !m.Id.Equals(primaryManifest.Id)).ToList();
        List<ManifestId> storedVariantIds = [];

        if (otherVariants.Count > 0)
        {
            logger.LogInformation("Factory created {Count} manifests from {OriginalId}. Storing all variants.", processedManifests.Count, preparedData.Id);

            for (int i = 0; i < otherVariants.Count; i++)
            {
                var variantManifest = otherVariants[i];
                if (variantManifest.Files.Count == 0)
                {
                    logger.LogInformation("Skipping storage of variant manifest {ManifestId} because it contains 0 files", variantManifest.Id);
                    continue;
                }

                var variantDirectory = factory.GetManifestDirectory(variantManifest, stagingDir);
                logger.LogInformation("Storing variant manifest {ManifestId} ({Index}/{Total}) from {Directory}", variantManifest.Id, i + 1, otherVariants.Count, variantDirectory);

                var variantAddResult = await manifestPool.AddManifestAsync(
                    variantManifest,
                    variantDirectory,
                    cancellationToken: cancellationToken);

                if (!variantAddResult.Success)
                {
                    logger.LogError("Failed to store variant manifest {ManifestId}: {Error}", variantManifest.Id, variantAddResult.FirstError);
                    foreach (var rollbackId in storedVariantIds)
                    {
                        try
                        {
                            await manifestPool.RemoveManifestAsync(rollbackId, cancellationToken: cancellationToken);
                        }
                        catch (Exception rbEx)
                        {
                            logger.LogWarning(rbEx, "Failed to roll back stored variant {ManifestId}", rollbackId);
                        }
                    }

                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Failed to store variant manifest {variantManifest.Id}: {variantAddResult.FirstError}");
                }

                storedVariantIds.Add(variantManifest.Id);
            }
        }

        reportProgress(3, "Processing files", 80, "Factory processing complete", false, null, 0, 0, 0, 0, null, default);
        reportProgress(3, "Processing files", 100, "Files processed", false, null, 0, 0, 0, 0, null, default);
        return OperationResult<ContentManifest>.CreateSuccess(primaryManifest);
    }

    private async Task<OperationResult<bool>> ValidateStagedFilesAsync(
        ContentManifest manifest,
        string stagingDir,
        Action<int, string, double, string?, bool, string?, long, long, int, int, string?, TimeSpan> reportProgress,
        bool hasProgressReporter,
        CancellationToken cancellationToken)
    {
        reportProgress(4, "Validating", 0, "Starting file validation...", false, null, 0, 0, 0, 0, null, default);

        IProgress<ValidationProgress>? validationProgress = null;
        if (hasProgressReporter)
        {
            validationProgress = new Progress<ValidationProgress>(vp =>
            {
                var isHashCalculation = vp.CurrentFile?.Contains("hash", StringComparison.OrdinalIgnoreCase) == true
                    || vp.Total > 100;

                var operation = vp.Total > 0
                    ? $"Validating: {vp.Processed}/{vp.Total} files"
                    : vp.CurrentFile ?? "Validating files";

                reportProgress(4, "Validating", vp.PercentComplete, operation, isHashCalculation && vp.Total > 100, isHashCalculation && vp.Total > 100 ? "Computing file hashes..." : null, 0, 0, vp.Processed, vp.Total, vp.CurrentFile, default);
            });
        }

        var fullValidation = await contentValidator.ValidateAllAsync(
            stagingDir,
            manifest,
            validationProgress,
            cancellationToken);

        if (!fullValidation.IsValid)
        {
            var errors = fullValidation.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                return OperationResult<bool>.CreateFailure(
                    errors.Select(e => $"Content validation failed: {e.Message}"));
            }
        }

        reportProgress(4, "Validating", 100, "Validation complete", false, null, 0, 0, 0, 0, null, default);
        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> StoreManifestInLibraryAsync(
        ContentManifest manifest,
        string stagingDir,
        Action<int, string, double, string?, bool, string?, long, long, int, int, string?, TimeSpan> reportProgress,
        CancellationToken cancellationToken)
    {
        reportProgress(5, "Storing", 0, "Adding to content library...", false, null, 0, 0, 0, 0, null, default);

        var alreadyStoredResult = await manifestPool.IsManifestAcquiredAsync(manifest.Id, cancellationToken);
        if (!alreadyStoredResult.Success || !alreadyStoredResult.Data)
        {
            logger.LogDebug("Manifest {ManifestId} not yet stored, storing now from staging directory", manifest.Id);

            if (manifest.ContentType == ContentType.GameClient)
            {
                var success = await EnsureInstallationPoolPathAsync(cancellationToken);
                if (!success)
                {
                    return OperationResult<bool>.CreateFailure(
                        "Could not ensure InstallationPoolRootPath for GameClient content. A valid game installation is required.");
                }
            }

            reportProgress(5, "Storing", 30, "Copying files to content store...", true, "Storing files in content-addressable storage...", 0, 0, 0, 0, null, default);

            var storageProgress = new Progress<ContentStorageProgress>(storage =>
            {
                var storagePercent = 30 + (Math.Clamp(storage.Percentage, 0, 100) * 0.6);
                reportProgress(5, "Storing", storagePercent, $"Storing: {storage.CurrentFileName} ({storage.ProcessedCount}/{storage.TotalCount})", true, "Storing files in content-addressable storage...", 0, 0, storage.ProcessedCount, storage.TotalCount, storage.CurrentFileName, default);
            });

            var addResult = await manifestPool.AddManifestAsync(
                manifest,
                stagingDir,
                progress: storageProgress,
                cancellationToken: cancellationToken);
            if (!addResult.Success)
            {
                return OperationResult<bool>.CreateFailure(
                    $"Failed to store content: {addResult.FirstError}");
            }

            reportProgress(5, "Storing", 90, "Registering manifest...", false, null, 0, 0, 0, 0, null, default);
        }
        else
        {
            logger.LogInformation("Manifest {ManifestId} is already present in the content store", manifest.Id);
            reportProgress(5, "Storing", 90, "Content already stored", false, null, 0, 0, 0, 0, null, default);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static ConcurrentDictionary<string, IContentResolver> InitializeResolvers(
        IEnumerable<IContentResolver> resolvers,
        ILogger<ContentOrchestrator> logger)
    {
        var dictionary = new ConcurrentDictionary<string, IContentResolver>(StringComparer.OrdinalIgnoreCase);
        foreach (var resolver in resolvers)
        {
            if (!dictionary.TryAdd(resolver.ResolverId, resolver))
            {
                logger.LogWarning("Duplicate ResolverId found: {ResolverId}. Skipping resolver.", resolver.ResolverId);
            }

            var normalized = resolver.ResolverId.Replace("-", string.Empty);
            if (!string.Equals(normalized, resolver.ResolverId, StringComparison.OrdinalIgnoreCase))
            {
                dictionary.TryAdd(normalized, resolver);
            }
        }

        return dictionary;
    }

    private static IEnumerable<ContentSearchResult> ApplySorting(
        IEnumerable<ContentSearchResult> results, ContentSortField sortOrder)
    {
        return sortOrder switch
        {
            ContentSortField.Name => results.OrderBy(r => r.Name),
            ContentSortField.DateCreated => results.OrderByDescending(r => r.LastUpdated),
            ContentSortField.DownloadCount => results.OrderByDescending(r => r.DownloadCount),
            ContentSortField.Rating => results.OrderByDescending(r => r.Rating),
            _ => results, // Relevance - keep original order
        };
    }

    private static ContentManifest SelectPrimaryManifest(
        IReadOnlyList<ContentManifest> manifests,
        ContentSearchResult searchResult)
    {
        if (manifests.Count == 0)
        {
            throw new InvalidOperationException("Cannot select primary manifest from empty collection");
        }

        if (manifests.Count == 1)
        {
            return manifests[0];
        }

        // direct manifest id match
        var directMatch = manifests.FirstOrDefault(m =>
            string.Equals(m.Id.Value, searchResult.Id, StringComparison.OrdinalIgnoreCase));
        if (directMatch != null)
        {
            return directMatch;
        }

        // match by SelectedVariantId against search result id or name
        var variantMatch = manifests.FirstOrDefault(m =>
        {
            var variantId = m.Metadata?.SelectedVariantId;
            if (string.IsNullOrEmpty(variantId))
            {
                return false;
            }

            var cleanVariantId = variantId.Replace("-", string.Empty).Trim();
            var searchId = searchResult.Id ?? string.Empty;
            var searchName = searchResult.Name ?? string.Empty;

            if (searchId.EndsWith($"-{variantId}", StringComparison.OrdinalIgnoreCase) ||
                searchId.EndsWith(variantId, StringComparison.OrdinalIgnoreCase) ||
                searchId.Replace("-", string.Empty).EndsWith(cleanVariantId, StringComparison.OrdinalIgnoreCase))
            {
                return searchResult.TargetGame == GameType.Unknown || m.TargetGame == searchResult.TargetGame;
            }

            if (searchName.Contains(variantId, StringComparison.OrdinalIgnoreCase))
            {
                return searchResult.TargetGame == GameType.Unknown || m.TargetGame == searchResult.TargetGame;
            }

            return false;
        });
        if (variantMatch != null)
        {
            return variantMatch;
        }

        // match by TargetGame if specified
        if (searchResult.TargetGame is GameType.Generals or GameType.ZeroHour)
        {
            var gameMatch = manifests.FirstOrDefault(m => m.TargetGame == searchResult.TargetGame);
            if (gameMatch != null)
            {
                return gameMatch;
            }
        }

        return manifests[0];
    }

    /// <summary>
    /// Ensures the InstallationPoolRootPath is set before storing GameClient content.
    /// This prevents content from being stored in the wrong CAS pool.
    /// </summary>
    /// <returns>True if the path was successfully ensured or auto-set.</returns>
    private async Task<bool> EnsureInstallationPoolPathAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Force installation detection and reset the path
            logger.LogInformation("Forcing installation detection to ensure correct InstallationPoolRootPath");
            installationService.InvalidateCache();

            // Get all installations (this will trigger detection if cache is empty)
            var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
            if (!installationsResult.Success || installationsResult.Data == null)
            {
                logger.LogWarning(
                    "Failed to get installations for CAS pool path resolution: {Error}; the primary CAS pool will be used",
                    installationsResult.FirstError);
                return true;
            }

            var installations = installationsResult.Data.ToList();
            return await installationCasPoolService.EnsurePoolPathAsync(installations, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure InstallationPoolRootPath is set");
            return false;
        }
    }
}
