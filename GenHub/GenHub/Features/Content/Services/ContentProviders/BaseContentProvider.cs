using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// Base class for content providers with common pipeline orchestration logic.
/// </summary>
public abstract class BaseContentProvider(
    IContentValidator contentValidator,
    ILogger logger
) : IContentProvider
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IContentValidator _contentValidator = contentValidator ?? throw new ArgumentNullException(nameof(contentValidator));

    /// <inheritdoc />
    public abstract string SourceName { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public virtual bool IsEnabled => true;

    /// <inheritdoc />
    public virtual ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public virtual async Task<OperationResult<IEnumerable<ContentSearchResult>>> SearchAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("Starting {ProviderName} search for: {SearchTerm}", SourceName, query.SearchTerm);

        // Get provider definition for data-driven configuration (if available)
        var providerDefinition = GetProviderDefinition();

        // Step 1: Discovery - use provider-aware overload if definition is available
        var discoveryResult = await Discoverer.DiscoverAsync(providerDefinition, query, cancellationToken);
        if (!discoveryResult.Success || discoveryResult.Data == null)
        {
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                $"Discovery failed: {discoveryResult.FirstError}");
        }

        var resolvedResults = new List<ContentSearchResult>();

        // Step 2: Resolution & Validation
        foreach (var discovered in discoveryResult.Data)
        {
            if (discovered.RequiresResolution)
            {
                var resolutionResult = await Resolver.ResolveAsync(providerDefinition, discovered, cancellationToken);
                if (resolutionResult.Success && resolutionResult.Data != null)
                {
                    var validationResult = await ContentValidator.ValidateManifestAsync(
                        resolutionResult.Data, cancellationToken);

                    if (validationResult.IsValid)
                    {
                        var resolvedSearchResult = CreateResolvedSearchResult(discovered, resolutionResult.Data);
                        resolvedResults.Add(resolvedSearchResult);
                    }
                    else
                    {
                        Logger.LogWarning(
                            "Manifest validation failed for {ContentName}: {Errors}",
                            discovered.Name,
                            string.Join(", ", validationResult.Issues.Select(i => i.Message)));
                    }
                }
                else
                {
                    Logger.LogWarning(
                        "Resolution failed for {ContentName}: {Error}",
                        discovered.Name,
                        resolutionResult.FirstError ?? "Unknown error");
                }
            }
            else
            {
                resolvedResults.Add(discovered);
            }
        }

        return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(resolvedResults);
    }

    /// <summary>
    /// Gets the manifest for the specified content ID.
    /// </summary>
    /// <param name="contentId">The content identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result containing the game manifest.</returns>
    public abstract Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual async Task<OperationResult<ContentManifest>> PrepareContentAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogDebug("Preparing content for manifest {ManifestId}", manifest.Id);

            // Validate manifest before preparation
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.ValidatingManifest,
                CurrentOperation = "Validating manifest structure...",
            });

            var validationResult = await ContentValidator.ValidateManifestAsync(manifest, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
                if (errors.Any())
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        errors.Select(e => $"Manifest validation failed: {e.Message}"));
                }
            }

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                CurrentOperation = "Preparing content files...",
            });

            // Delegate to implementation-specific preparation
            var result = await PrepareContentInternalAsync(manifest, workingDirectory, progress, cancellationToken);

            if (result.Success)
            {
                // Final validation of prepared content
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.ValidatingFiles,
                    CurrentOperation = "Validating prepared content...",
                });

                // Forward provider progress into validation by adapting ValidationProgress -> ContentAcquisitionProgress
                IProgress<ValidationProgress>? validationProgress = null;
                if (progress != null)
                {
                    validationProgress = new Progress<ValidationProgress>(vp =>
                    {
                        // Map validation progress to content acquisition progress for UI display
                        progress.Report(new ContentAcquisitionProgress
                        {
                            Phase = ContentAcquisitionPhase.ValidatingFiles,
                            ProgressPercentage = vp.PercentComplete,
                            CurrentOperation = vp.CurrentFile ?? "Validating files",
                            FilesProcessed = vp.Processed,
                            TotalFiles = vp.Total,
                        });
                    });
                }

                var fullResult = await ContentValidator.ValidateAllAsync(
                    workingDirectory,
                    result.Data!,
                    validationProgress,
                    cancellationToken: cancellationToken);

                if (!fullResult.IsValid)
                {
                    Logger.LogWarning("Content validation found {IssueCount} issues for {ManifestId}", fullResult.Issues.Count, manifest.Id);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare content for manifest {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateFailure($"Content preparation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the logger for this provider.
    /// </summary>
    protected ILogger Logger => _logger;

    /// <summary>
    /// Gets the content validator for manifest validation.
    /// </summary>
    protected IContentValidator ContentValidator => _contentValidator;

    /// <summary>
    /// Gets the discoverer for this provider.
    /// </summary>
    protected abstract IContentDiscoverer Discoverer { get; }

    /// <summary>
    /// Gets the resolver for this provider.
    /// </summary>
    protected abstract IContentResolver Resolver { get; }

    /// <summary>
    /// Gets the deliverer for this provider.
    /// </summary>
    protected abstract IContentDeliverer Deliverer { get; }

    /// <summary>
    /// Gets the provider definition for data-driven configuration.
    /// Override this method to provide a ProviderDefinition loaded from JSON configuration.
    /// </summary>
    /// <returns>The provider definition, or null if the provider uses hardcoded configuration.</returns>
    protected virtual ProviderDefinition? GetProviderDefinition() => null;

    /// <summary>
    /// Implementation-specific content preparation logic.
    /// </summary>
    /// <param name="manifest">The manifest to prepare.</param>
    /// <param name="workingDirectory">Working directory for content preparation.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The prepared manifest.</returns>
    protected abstract Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a resolved <see cref="ContentSearchResult"/> from a discovered item and manifest.
    /// </summary>
    /// <param name="discovered">The discovered search result.</param>
    /// <param name="manifest">The resolved manifest.</param>
    /// <returns>A resolved <see cref="ContentSearchResult"/>.</returns>
    private ContentSearchResult CreateResolvedSearchResult(ContentSearchResult discovered, ContentManifest manifest)
    {
        var resolved = new ContentSearchResult
        {
            Id = discovered.Id,
            Name = manifest.Name,
            Description = manifest.Metadata?.Description ?? discovered.Description,
            Version = manifest.Version,
            ContentType = manifest.ContentType,
            TargetGame = manifest.TargetGame,
            ProviderName = SourceName,
            AuthorName = manifest.Publisher?.Name ?? discovered.AuthorName,
            IconUrl = manifest.Metadata?.IconUrl ?? discovered.IconUrl,
            LastUpdated = manifest.Metadata?.ReleaseDate ?? discovered.LastUpdated,
            DownloadSize = manifest.Files?.Sum(f => f.Size) ?? discovered.DownloadSize,
            RequiresResolution = false,
            SourceUrl = discovered.SourceUrl,
        };

        // Copy screenshots and tags
        resolved.ScreenshotUrls.Clear();
        if (manifest.Metadata?.ScreenshotUrls != null && manifest.Metadata.ScreenshotUrls.Count > 0)
        {
            foreach (var s in manifest.Metadata.ScreenshotUrls)
            {
                resolved.ScreenshotUrls.Add(s);
            }
        }
        else
        {
            foreach (var s in discovered.ScreenshotUrls)
            {
                resolved.ScreenshotUrls.Add(s);
            }
        }

        resolved.Tags.Clear();
        if (manifest.Metadata?.Tags != null && manifest.Metadata.Tags.Count > 0)
        {
            foreach (var t in manifest.Metadata.Tags)
            {
                resolved.Tags.Add(t);
            }
        }
        else
        {
            foreach (var t in discovered.Tags)
            {
                resolved.Tags.Add(t);
            }
        }

        resolved.SetData(manifest);
        return resolved;
    }
}