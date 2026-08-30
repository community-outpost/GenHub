using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Resolves Generals Online search results into ContentManifests with download URLs.
/// Creates the initial manifest structure; post-extraction processing is handled by the factory.
/// </summary>
public class GeneralsOnlineResolver(
    GeneralsOnlineManifestFactory manifestFactory,
    ILogger<GeneralsOnlineResolver> logger) : IContentResolver
{
    /// <inheritdoc />
    public string ResolverId => GeneralsOnlineConstants.ResolverId;

    /// <summary>
    /// Resolves a Generals Online search result into a content manifest.
    /// Creates the 30Hz variant manifest with download URL; deliverer will handle download.
    /// </summary>
    /// <param name="searchResult">The search result to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the resolved manifest.</returns>
    public Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult searchResult,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Resolving Generals Online manifest for: {Version}", searchResult.Version);

        try
        {
            var release = searchResult.GetData<GeneralsOnlineRelease>();
            if (release == null)
            {
                return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
                    "Release information not found in search result"));
            }

            var manifests = manifestFactory.CreateManifests(release);
            if (manifests.FirstOrDefault() is not { } primaryManifest)
            {
                return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
                    "Failed to create manifest from release"));
            }

            logger.LogInformation(
                "Successfully resolved Generals Online manifest ({Variant}) with download URL: {Url}",
                primaryManifest.Name,
                release.PortableUrl);

            return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(primaryManifest));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve Generals Online manifest");
            return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
                $"Resolution failed: {ex.Message}"));
        }
    }
}
