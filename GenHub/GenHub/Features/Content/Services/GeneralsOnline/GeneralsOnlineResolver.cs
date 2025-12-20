using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Resolves Generals Online search results into ContentManifests.
/// </summary>
public class GeneralsOnlineResolver(ILogger<GeneralsOnlineResolver> logger) : IContentResolver
{
    /// <inheritdoc />
    public string ResolverId => GeneralsOnlineConstants.ResolverId;

    /// <summary>
    /// Resolves a Generals Online search result into a content manifest.
    /// Returns the 30Hz variant; deliverer will create and register both variants.
    /// </summary>
    /// <param name="searchResult">The search result to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the resolved manifest.</returns>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult searchResult,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Resolving Generals Online manifest for: {Version}", searchResult.Version);

        try
        {
            var release = searchResult.GetData<GeneralsOnlineRelease>();
            if (release == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    "Release information not found in search result");
            }

            var manifests = GeneralsOnlineManifestFactory.CreateManifests(release);
            var primaryManifest = manifests.FirstOrDefault();

            if (primaryManifest == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    "Failed to create manifests from release");
            }

            logger.LogInformation("Successfully resolved Generals Online manifest ({Variant})", primaryManifest.Name);

            return await Task.FromResult(
                OperationResult<ContentManifest>.CreateSuccess(primaryManifest));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve Generals Online manifest");
            return OperationResult<ContentManifest>.CreateFailure(
                $"Resolution failed: {ex.Message}");
        }
    }
}
