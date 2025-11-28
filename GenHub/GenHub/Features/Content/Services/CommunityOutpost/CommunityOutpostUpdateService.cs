using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Background service that periodically checks for new Community Outpost patches.
/// </summary>
/// <param name="discoverer">Content discoverer.</param>
/// <param name="resolver">Content resolver.</param>
/// <param name="manifestPool">Manifest pool.</param>
/// <param name="logger">Logger instance.</param>
public class CommunityOutpostUpdateService(
    CommunityOutpostDiscoverer discoverer,
    CommunityOutpostResolver resolver,
    IContentManifestPool manifestPool,
    ILogger<CommunityOutpostUpdateService> logger)
    : ContentUpdateServiceBase(logger)
{
    /// <inheritdoc/>
    protected override string ServiceName => CommunityOutpostConstants.PublisherName;

    /// <inheritdoc/>
    protected override TimeSpan UpdateCheckInterval => TimeSpan.FromHours(24); // Check daily

    /// <inheritdoc/>
    public override async Task<ContentUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Checking for Community Outpost patch updates...");

            // Discover latest content
            var discoveryResult = await discoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);

            if (!discoveryResult.Success || discoveryResult.Data?.Any() != true)
            {
                logger.LogWarning("No Community Outpost content discovered");
                return ContentUpdateCheckResult.CreateNoUpdateAvailable();
            }

            var latestDiscovered = discoveryResult.Data.First();
            var latestVersion = latestDiscovered.Version;

            logger.LogInformation("Latest Community Outpost version discovered: {Version}", latestVersion);

            // Check if we already have this version in the manifest pool
            var manifestsResult = await manifestPool.GetAllManifestsAsync();
            var existingCommunityPatches = (manifestsResult.Data ?? Enumerable.Empty<GenHub.Core.Models.Manifest.ContentManifest>())
                .Where(m => m.Publisher?.PublisherType == CommunityOutpostConstants.PublisherId)
                .OrderByDescending(m => m.ManifestVersion)
                .ToList();

            var currentVersion = existingCommunityPatches.FirstOrDefault()?.ManifestVersion;

            if (existingCommunityPatches.Any(m => m.ManifestVersion == latestVersion))
            {
                logger.LogInformation("Community Outpost version {Version} already exists in manifest pool", latestVersion);
                return ContentUpdateCheckResult.CreateNoUpdateAvailable(currentVersion, latestVersion);
            }

            // Resolve new content to manifest
            var resolveResult = await resolver.ResolveAsync(latestDiscovered, cancellationToken);

            if (!resolveResult.Success || resolveResult.Data == null)
            {
                logger.LogError("Failed to resolve Community Outpost content: {Error}", resolveResult.FirstError);
                return ContentUpdateCheckResult.CreateFailure($"Failed to resolve: {resolveResult.FirstError}", currentVersion);
            }

            // Add to manifest pool
            var addResult = await manifestPool.AddManifestAsync(resolveResult.Data);

            if (!addResult.Success)
            {
                logger.LogError("Failed to add Community Outpost manifest to pool: {Error}", addResult.FirstError);
                return ContentUpdateCheckResult.CreateFailure($"Failed to add manifest: {addResult.FirstError}", currentVersion);
            }

            logger.LogInformation("Successfully added Community Outpost patch v{Version} to manifest pool", latestVersion);
            return ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, currentVersion);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking for Community Outpost updates");
            return ContentUpdateCheckResult.CreateFailure(ex.Message);
        }
    }
}
