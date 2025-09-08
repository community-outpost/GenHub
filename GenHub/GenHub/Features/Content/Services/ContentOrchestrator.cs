using GenHub.Core.Constants;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Primary orchestrator for the GenHub content system. Coordinates multiple content providers
/// and manages the complete content lifecycle focused on discovery, resolution, and delivery.
/// </summary>
public class ContentOrchestrator : IContentOrchestrator
{
    private readonly ILogger<ContentOrchestrator> _logger;
    private readonly ConcurrentBag<IContentProvider> _providers;
    private readonly ConcurrentBag<IContentDiscoverer> _discoverers;
    private readonly ConcurrentDictionary<string, IContentResolver> _resolvers;
    private readonly IDynamicContentCache _cache;
    private readonly IContentValidator _contentValidator;
    private readonly IContentManifestPool _manifestPool;
    private readonly object _providerLock = new object();

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentOrchestrator"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="providers">The content providers that orchestrate discovery→resolution→delivery pipelines.</param>
    /// <param name="discoverers">The content discoverers (used for direct orchestrator operations if needed).</param>
    /// <param name="resolvers">The content resolvers (used for direct orchestrator operations if needed).</param>
    /// <param name="cache">The dynamic content cache service for performance optimization.</param>
    /// <param name="contentValidator">The content validator service for manifest and content integrity.</param>
    /// <param name="manifestPool">The manifest pool for acquired content.</param>
    public ContentOrchestrator(
        ILogger<ContentOrchestrator> logger,
        IEnumerable<IContentProvider> providers,
        IEnumerable<IContentDiscoverer> discoverers,
        IEnumerable<IContentResolver> resolvers,
        IDynamicContentCache cache,
        IContentValidator contentValidator,
        IContentManifestPool manifestPool)
    {
        _logger = logger;
        _providers = new ConcurrentBag<IContentProvider>(providers);
        _discoverers = new ConcurrentBag<IContentDiscoverer>(discoverers);
        _resolvers = new ConcurrentDictionary<string, IContentResolver>();
        foreach (var resolver in resolvers)
        {
            if (!_resolvers.TryAdd(resolver.ResolverId, resolver))
            {
                _logger.LogWarning("Duplicate ResolverId found: {ResolverId}. Skipping resolver.", resolver.ResolverId);
            }
        }

        _cache = cache;
        _contentValidator = contentValidator;
        _manifestPool = manifestPool;

        _logger.LogInformation("ContentOrchestrator initialized with {ProviderCount} providers, {DiscovererCount} discoverers, {ResolverCount} resolvers", _providers.Count, _discoverers.Count, _resolvers.Count);
    }

    /// <summary>
    /// Searches for content across all enabled providers, leveraging their internal pipelines.
    /// Each provider orchestrates its own discovery→resolution→delivery pipeline internally.
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

        _logger.LogDebug("Starting orchestrated content search with query: {SearchTerm}, ContentType: {ContentType}", query.SearchTerm, query.ContentType);

        // Check cache first
        var cacheKey = $"search::{query.SearchTerm}::{query.ContentType}::{query.Skip}::{query.Take}::{query.SortOrder}";
        var cachedResults = await _cache.GetAsync<List<ContentSearchResult>>(cacheKey, cancellationToken);
        if (cachedResults != null)
        {
            _logger.LogDebug("Returning cached search results for query: {SearchTerm}", query.SearchTerm);
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(cachedResults);
        }

        var allResults = new List<ContentSearchResult>();
        var errors = new List<string>();

        // Orchestrate search across all enabled providers concurrently
        // Each provider handles its own internal discovery→resolution→delivery pipeline
        var searchTasks = _providers
            .Where(p => p.IsEnabled)
            .ToList();

        if (!searchTasks.Any())
        {
            _logger.LogWarning("No enabled providers available for search");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure("No enabled providers available");
        }

        var searchTasksAsync = searchTasks
            .Select(async provider =>
            {
                try
                {
                    _logger.LogDebug("Executing search via provider: {ProviderName}", provider.SourceName);
                    var result = await provider.SearchAsync(query, cancellationToken);

                    if (result.Success && result.Data != null)
                    {
                        lock (allResults)
                        {
                            foreach (var item in result.Data)
                            {
                                // Ensure provider name is set correctly
                                if (string.IsNullOrEmpty(item.ProviderName))
                                {
                                    item.ProviderName = provider.SourceName;
                                }
                            }

                            allResults.AddRange(result.Data);
                        }

                        _logger.LogDebug("Provider {ProviderName} returned {ResultCount} results", provider.SourceName, result.Data.Count());
                    }
                    else
                    {
                        lock (errors)
                        {
                            errors.Add($"{provider.SourceName}: {result.FirstError}");
                        }

                        _logger.LogWarning("Provider {ProviderName} failed: {Error}", provider.SourceName, result.FirstError);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Search failed for provider: {ProviderName}", provider.SourceName);
                    lock (errors)
                    {
                        errors.Add($"{provider.SourceName}: {ex.Message}");
                    }
                }
            });

        await Task.WhenAll(searchTasksAsync);

        // Apply orchestrator-level sorting and pagination
        var sortedResults = ApplySorting(allResults, query.SortOrder)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();

        // Cache results for future queries
        if (sortedResults.Any())
        {
            await _cache.SetAsync(cacheKey, sortedResults, TimeSpan.FromMinutes(5), cancellationToken);
        }

        _logger.LogInformation("Content search completed. Total results: {ResultCount}, Errors: {ErrorCount}", sortedResults.Count, errors.Count);

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
        var cachedManifest = await _cache.GetAsync<ContentManifest>(cacheKey, cancellationToken);
        if (cachedManifest != null)
        {
            _logger.LogDebug("Returning cached manifest for {ProviderName}::{ContentId}", providerName, contentId);
            return OperationResult<ContentManifest>.CreateSuccess(cachedManifest);
        }

        var provider = _providers.FirstOrDefault(p => p.SourceName == providerName);
        if (provider == null)
        {
            return OperationResult<ContentManifest>.CreateFailure($"Provider not found: {providerName}");
        }

        _logger.LogDebug("Retrieving manifest from provider {ProviderName} for content {ContentId}", providerName, contentId);

        var result = await provider.GetValidatedContentAsync(contentId, cancellationToken);

        // Cache successful results
        if (result.Success && result.Data != null)
        {
            await _cache.SetAsync(cacheKey, result.Data, TimeSpan.FromHours(1), cancellationToken);
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
            ? OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(result.Data ?? Enumerable.Empty<ContentSearchResult>())
            : OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(result.Errors);
    }

    /// <summary>
    /// Gets all available content providers currently registered with the orchestrator.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that returns a result containing available content providers.</returns>
    public Task<OperationResult<IEnumerable<IContentProvider>>> GetAvailableProvidersAsync(CancellationToken cancellationToken = default)
    {
        var providers = _providers.ToList();
        return Task.FromResult(OperationResult<IEnumerable<IContentProvider>>.CreateSuccess(providers));
    }

    /// <summary>
    /// Registers a new content provider with the orchestrator.
    /// </summary>
    /// <param name="provider">The content provider to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="provider"/> is null.</exception>
    public void RegisterProvider(IContentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!_providers.ToList().Any(p => p.SourceName == provider.SourceName))
        {
            _providers.Add(provider);
            _logger.LogInformation("Registered content provider: {ProviderName}", provider.SourceName);
        }
        else
        {
            _logger.LogWarning("Attempted to register duplicate provider: {ProviderName}", provider.SourceName);
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
                _logger.LogInformation("Unregistered content provider: {ProviderName}", providerName);
            }
            else
            {
                _logger.LogWarning("Attempted to unregister non-existent provider: {ProviderName}", providerName);
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

        if (!_resolvers.TryGetValue(contentSearchResult.ResolverId, out IContentResolver? resolver))
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"No resolver found for ResolverId: {contentSearchResult.ResolverId}");
        }

        var manifestResult = await resolver.ResolveAsync(contentSearchResult, cancellationToken);
        if (manifestResult.Success && manifestResult.Data != null)
        {
            var validationResult = await _contentValidator.ValidateManifestAsync(manifestResult.Data, cancellationToken);
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

        _logger.LogInformation("Acquiring content {ContentName} from {ProviderName}", searchResult.Name, searchResult.ProviderName);

        try
        {
            // Step 1: Get provider
            var provider = _providers.FirstOrDefault(p => p.SourceName == searchResult.ProviderName);
            if (provider == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Provider not found: {searchResult.ProviderName}");
            }

            // Step 2: Get complete manifest
            ContentManifest manifest;
            var embeddedManifest = searchResult.GetData<ContentManifest>();
            if (embeddedManifest != null)
            {
                manifest = embeddedManifest;
            }
            else
            {
                var manifestResult = await provider.GetValidatedContentAsync(searchResult.Id, cancellationToken);
                if (!manifestResult.Success || manifestResult.Data == null)
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Failed to get manifest: {manifestResult.FirstError}");
                }

                manifest = manifestResult.Data;
            }

            // Step 3: Validate manifest structure only
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.ValidatingManifest,
                ProgressPercentage = ContentConstants.ProgressStepValidatingManifest,
                CurrentOperation = "Validating content manifest",
            });

            var validationResult = await _contentValidator.ValidateManifestAsync(manifest, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
                if (errors.Any())
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        errors.Select(e => $"Manifest validation failed: {e.Message}"));
                }
            }

            // Step 4: Provider content preparation (download to staging)
            var stagingDir = Path.Combine(Path.GetTempPath(), "GenHub", "Staging", manifest.Id);
            Directory.CreateDirectory(stagingDir);

            try
            {
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Downloading,
                    ProgressPercentage = ContentConstants.ProgressStepDownloading,
                    CurrentOperation = "Preparing content via provider pipeline",
                });

                var prepareResult = await provider.PrepareContentAsync(manifest, stagingDir, progress, cancellationToken);
                if (!prepareResult.Success || prepareResult.Data == null)
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Content preparation failed: {prepareResult.FirstError}");
                }

                // Step 5: Full validation (manifest + files)
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.ValidatingFiles,
                    ProgressPercentage = ContentConstants.ProgressStepValidatingFiles,
                    CurrentOperation = "Validating prepared content files",
                });

                // Forward orchestrator progress into validator
                IProgress<ValidationProgress>? validationProgress = null;
                if (progress != null)
                {
                    validationProgress = new Progress<ValidationProgress>(vp =>
                    {
                        // Map validation progress (0-100) into 70-80% range for acquisition
                        var pct = ContentConstants.ProgressStepValidatingFiles + (int)(vp.PercentComplete / 10.0);
                        progress.Report(new ContentAcquisitionProgress
                        {
                            Phase = ContentAcquisitionPhase.ValidatingFiles,
                            ProgressPercentage = pct,
                            CurrentOperation = vp.CurrentFile ?? "Validating files",
                            FilesProcessed = vp.Processed,
                            TotalFiles = vp.Total,
                        });
                    });
                }

                var fullValidation = await _contentValidator.ValidateAllAsync(
                    stagingDir,
                    prepareResult.Data,
                    validationProgress,
                    cancellationToken);

                if (!fullValidation.IsValid)
                {
                    var errors = fullValidation.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
                    if (errors.Any())
                    {
                        return OperationResult<ContentManifest>.CreateFailure(
                            errors.Select(e => $"Content validation failed: {e.Message}"));
                    }
                }

                // Step 6: Store in permanent storage
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Extracting,
                    ProgressPercentage = ContentConstants.ProgressStepExtracting,
                    CurrentOperation = "Adding to content library",
                });

                // Store the prepared manifest in the pool
                await _manifestPool.AddManifestAsync(prepareResult.Data, stagingDir, cancellationToken);

                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Completed,
                    ProgressPercentage = ContentConstants.ProgressStepCompleted,
                    CurrentOperation = "Content acquired successfully",
                });

                _logger.LogInformation("Content {ContentName} acquired and stored in manifest pool", searchResult.Name);
                return OperationResult<ContentManifest>.CreateSuccess(prepareResult.Data);
            }
            finally
            {
                // Cleanup staging directory
                try
                {
                    FileOperationsService.DeleteDirectoryIfExists(stagingDir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup staging directory: {StagingDir}", stagingDir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire content {ContentId}", searchResult.Id);
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
        var manifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
        if (manifestsResult.Success)
        {
            return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifestsResult.Data ?? Enumerable.Empty<ContentManifest>());
        }
        else
        {
            return OperationResult<IEnumerable<ContentManifest>>.CreateFailure(manifestsResult.Errors);
        }
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
            await _manifestPool.RemoveManifestAsync(manifestId, cancellationToken);
            _logger.LogInformation("Removed content {ManifestId} from pool", manifestId);

            // Invalidate related cache entries
            await _cache.InvalidateAsync($"manifest::{manifestId}", cancellationToken);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove content {ManifestId} from pool", manifestId);
            return OperationResult<bool>.CreateFailure($"Failed to remove content: {ex.Message}");
        }
    }

    private IEnumerable<ContentSearchResult> ApplySorting(
        IEnumerable<ContentSearchResult> results, ContentSortField sortOrder)
    {
        return sortOrder switch
        {
            ContentSortField.Name => results.OrderBy(r => r.Name),
            ContentSortField.DateCreated => results.OrderByDescending(r => r.LastUpdated),
            ContentSortField.DownloadCount => results.OrderByDescending(r => r.DownloadCount),
            ContentSortField.Rating => results.OrderByDescending(r => r.Rating),
            _ => results // Relevance - keep original order
        };
    }
}
