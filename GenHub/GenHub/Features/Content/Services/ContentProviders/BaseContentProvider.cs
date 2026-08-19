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
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// Base class for content providers with common pipeline orchestration logic.
/// </summary>
public abstract class BaseContentProvider(
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService,
    ILogger logger
) : IContentProvider
{
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

        // Step 2: Resolution for each discovered item
        var results = new List<ContentSearchResult>();
        foreach (var manifest in discoveryResult.Data)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolveResult = await Resolver.ResolveAsync(manifest, cancellationToken);
            if (resolveResult.Success && resolveResult.Data != null)
            {
                results.Add(new ContentSearchResult
                {
                    Manifest = resolveResult.Data,
                    SourceName = SourceName,
                    Score = CalculateRelevanceScore(query.SearchTerm, resolveResult.Data),
                });
            }
            else
            {
                Logger.LogWarning("Failed to resolve manifest {ManifestId}: {Error}", manifest.Id, resolveResult.FirstError);
            }
        }

        Logger.LogInformation("Found {Count} items matching '{SearchTerm}' from {ProviderName}", results.Count, query.SearchTerm, SourceName);
        return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
    }

    /// <inheritdoc />
    public virtual async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("Fetching content by ID: {ContentId} from {ProviderName}", contentId, SourceName);

        var query = new ContentSearchQuery { SearchTerm = contentId };
        var searchResult = await SearchAsync(query, cancellationToken);

        if (!searchResult.Success || searchResult.Data == null)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Failed to fetch content: {searchResult.FirstError}");
        }

        var match = searchResult.Data.FirstOrDefault(r => r.Manifest.Id.Value == contentId);
        if (match?.Manifest == null)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content with ID '{contentId}' not found in {SourceName}");
        }

        return OperationResult<ContentManifest>.CreateSuccess(match.Manifest);
    }

    /// <inheritdoc />
    public virtual async Task<OperationResult<ContentManifest>> PrepareContentAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        Logger.LogInformation("Starting content preparation for manifest: {ManifestId} in {Directory}", manifest.Id, workingDirectory);

        try
        {
            // Initial manifest structure validation
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.ValidatingFiles,
                CurrentOperation = "Validating manifest structure...",
            });

            var manifestValidationResult = await ContentValidator.ValidateManifestAsync(manifest, cancellationToken);
            if (manifestValidationResult.HasErrors)
            {
                var errors = string.Join("; ", manifestValidationResult.Issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message));
                Logger.LogError("Manifest validation failed for {ManifestId}: {Errors}", manifest.Id, errors);
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Manifest validation failed: {errors}");
            }

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                CurrentOperation = "Preparing content files...",
            });

            // Delegate to implementation-specific preparation
            var result = await PrepareContentInternalAsync(manifest, workingDirectory, progress, cancellationToken);

            if (result.Success && result.Data != null)
            {
                // Execute post-installation steps if declared on the delivered manifest
                var stepExecutionResult = await installationInstructionsService.ExecutePostInstallStepsAsync(
                    result.Data,
                    workingDirectory,
                    progress,
                    cancellationToken);

                if (!stepExecutionResult.Success)
                {
                    Logger.LogError("Post-installation steps failed for manifest {ManifestId}: {Error}", manifest.Id, stepExecutionResult.FirstError);
                    return OperationResult<ContentManifest>.CreateFailure(stepExecutionResult.Errors);
                }

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
                    result.Data,
                    validationProgress,
                    cancellationToken: cancellationToken);

                if (!fullResult.IsValid)
                {
                    // Log as warning only - content may have been moved to CAS already
                    // CAS storage validates content hash on store, so this is informational
                    Logger.LogWarning("Content validation found {IssueCount} issues for {ManifestId}", fullResult.Issues.Count, manifest.Id);
                    foreach (var issue in fullResult.Issues.Take(5))
                    {
                        Logger.LogDebug("Validation issue: {Message}", issue.Message);
                    }
                }
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("Content preparation was canceled for manifest {ManifestId}", manifest.Id);
            throw;
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
    protected ILogger Logger => logger;

    /// <summary>
    /// Gets the content validator for manifest validation.
    /// </summary>
    protected IContentValidator ContentValidator => contentValidator;

    /// <summary>
    /// Gets the installation instructions service.
    /// </summary>
    protected IInstallationInstructionsService InstallationInstructionsService => installationInstructionsService;

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
    /// Implementation-specific content preparation logic.
    /// Override this method to provide custom delivery orchestration.
    /// Default implementation uses the Deliverer component.
    /// </summary>
    /// <param name="manifest">The content manifest to prepare.</param>
    /// <param name="workingDirectory">The working directory for preparation.</param>
    /// <param name="progress">Progress reporter for tracking progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the prepared manifest with updated file details.</returns>
    protected virtual async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        return await Deliverer.DeliverContentAsync(manifest, workingDirectory, progress, cancellationToken);
    }

    /// <summary>
    /// Gets the provider definition for data-driven configuration.
    /// Override in derived classes to provide provider definition from loader.
    /// </summary>
    /// <returns>The provider definition, or null if not available.</returns>
    protected virtual ProviderDefinition? GetProviderDefinition() => null;

    /// <summary>
    /// Calculates a simple relevance score for search results.
    /// </summary>
    private static double CalculateRelevanceScore(string searchTerm, ContentManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return 1.0;
        }

        var score = 0.0;
        var term = searchTerm.ToLowerInvariant();

        if (manifest.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            score += 10.0;
        }

        if (manifest.Metadata?.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 5.0;
        }

        if (manifest.Metadata?.Tags?.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)) == true)
        {
            score += 3.0;
        }

        return score;
    }
}
