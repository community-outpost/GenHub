using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GitHub;

/// <summary>
/// GitHub content provider that orchestrates discovery→resolution→delivery pipeline
/// for GitHub-hosted content (releases, repositories).
/// </summary>
public class GitHubContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    ILogger<GitHubContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    /// <inheritdoc />
    public override string SourceName => GitHubTopicsConstants.DiscovererSourceName;

    /// <inheritdoc />
    public override string Description => "GitHub releases and repository content";

    /// <inheritdoc />
    public override bool IsEnabled => true;

    /// <inheritdoc />
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer =>
        ResolveDiscoverer(discoverers, ContentSourceNames.GitHubDiscoverer);

    /// <inheritdoc />
    protected override IContentResolver Resolver =>
        ResolveResolver(resolvers, ContentSourceNames.GitHubResolverId);

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer =>
        ResolveDeliverer(deliverers, ContentSourceNames.GitHubDeliverer);

    /// <inheritdoc />
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId, CancellationToken cancellationToken = default)
    {
        return await SearchManifestByIdAsync(contentId, requireExactIdMatch: false, cancellationToken);
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
            var deliveryResult = await DeliverContentOnlyAsync(
                Deliverer,
                manifest,
                workingDirectory,
                progress,
                cancellationToken);
            if (!deliveryResult.Success || deliveryResult.Data == null)
            {
                return deliveryResult;
            }

            // Ensure we have valid data before validation
            var resultManifest = deliveryResult.Data;

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
