using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// GitHub content provider that orchestrates discovery→resolution→delivery pipeline
/// for GitHub-hosted content (releases, repositories).
/// </summary>
public class GitHubContentProvider : BaseContentProvider
{
    private readonly IGitHubApiClient _gitHubApiClient;
    private readonly IContentDiscoverer _gitHubDiscoverer;
    private readonly IContentResolver _gitHubResolver;
    private readonly IContentDeliverer _httpDeliverer;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubContentProvider"/> class.
    /// </summary>
    /// <param name="gitHubApiClient">GitHub API client.</param>
    /// <param name="discoverers">Available content discoverers.</param>
    /// <param name="resolvers">Available content resolvers.</param>
    /// <param name="deliverers">Available content deliverers.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="contentValidator">Content validator.</param>
    public GitHubContentProvider(
        IGitHubApiClient gitHubApiClient,
        IEnumerable<IContentDiscoverer> discoverers,
        IEnumerable<IContentResolver> resolvers,
        IEnumerable<IContentDeliverer> deliverers,
        ILogger<GitHubContentProvider> logger,
        IContentValidator contentValidator)
        : base(contentValidator, logger)
    {
        _gitHubApiClient = gitHubApiClient;
        _gitHubDiscoverer = discoverers.FirstOrDefault(d => d.SourceName.Contains("GitHub"))
            ?? throw new InvalidOperationException("No GitHub discoverer found. Ensure a discoverer with 'GitHub' in its SourceName is registered.");
        _gitHubResolver = resolvers.FirstOrDefault(r => r.ResolverId.Contains("GitHub"))
            ?? throw new InvalidOperationException("No GitHub resolver found. Ensure a resolver with 'GitHub' in its ResolverId is registered.");
        _httpDeliverer = deliverers.FirstOrDefault(d => d.SourceName.Contains("HTTP"))
            ?? throw new InvalidOperationException("No HTTP deliverer found. Ensure a deliverer with 'HTTP' in its SourceName is registered.");
    }

    /// <inheritdoc />
    public override string SourceName => "GitHub";

    /// <inheritdoc />
    public override string Description => "GitHub releases and repository content";

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _gitHubDiscoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _gitHubResolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _httpDeliverer;

    /// <inheritdoc />
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId, CancellationToken cancellationToken = default)
    {
        var query = new ContentSearchQuery { SearchTerm = contentId, Take = ContentConstants.SingleResultQueryLimit };
        var searchResult = await SearchAsync(query, cancellationToken);

        if (!searchResult.Success || !searchResult.Data!.Any())
        {
            return OperationResult<ContentManifest>.CreateFailure($"Content not found: {contentId}");
        }

        var result = searchResult.Data!.First();
        var manifest = result.GetData<ContentManifest>();

        return manifest != null
            ? OperationResult<ContentManifest>.CreateSuccess(manifest)
            : OperationResult<ContentManifest>.CreateFailure("Manifest not available in search result");
    }

    /// <inheritdoc />
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogDebug("Preparing GitHub content for {ManifestId}", manifest.Id);

            // Use the deliverer to handle content acquisition
            if (!Deliverer.CanDeliver(manifest))
            {
                return OperationResult<ContentManifest>.CreateFailure($"Cannot deliver content for manifest {manifest.Id}");
            }

            var deliveryResult = await Deliverer.DeliverContentAsync(manifest, workingDirectory, progress, cancellationToken);
            if (!deliveryResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {deliveryResult.FirstError}");
            }

            // Ensure we have valid data before validation
            var resultManifest = deliveryResult.Data ?? manifest;

            // Validate the delivered content (full validation)
            // Forward the provider progress reporter to the validator for user-visible progress
            IProgress<ValidationProgress>? validationProgress = null;
            if (progress != null)
            {
                // Signal start of file validation phase
                progress.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.ValidatingFiles,
                    CurrentOperation = "Validating prepared content...",
                });

                validationProgress = new Progress<ValidationProgress>(vp =>
                {
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

            var validationResult = await ContentValidator.ValidateAllAsync(workingDirectory, resultManifest, validationProgress, cancellationToken);
            if (!validationResult.IsValid)
            {
                Logger.LogWarning("Content validation found issues for {ManifestId}: {Issues}", manifest.Id, string.Join(", ", validationResult.Issues.Select(i => i.Message)));
            }

            Logger.LogInformation("Successfully prepared GitHub content {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(resultManifest);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare GitHub content for {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateFailure($"GitHub content preparation failed: {ex.Message}");
        }
    }
}
