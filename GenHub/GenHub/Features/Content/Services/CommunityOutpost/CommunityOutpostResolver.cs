using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Resolves Community Outpost content into manifests.
/// </summary>
/// <param name="manifestBuilder">The manifest builder.</param>
/// <param name="logger">The logger.</param>
public class CommunityOutpostResolver(
    IContentManifestBuilder manifestBuilder,
    ILogger<CommunityOutpostResolver> logger) : IContentResolver
{
    /// <inheritdoc/>
    public string ResolverId => CommunityOutpostConstants.PublisherId;

    /// <inheritdoc/>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "Resolving Community Outpost content: {Name} v{Version}",
                discoveredItem.Name,
                discoveredItem.Version);

            // Safely get metadata values
            var filename = CommunityOutpostConstants.DefaultPatchFilename;
            if (discoveredItem.ResolverMetadata != null && discoveredItem.ResolverMetadata.TryGetValue("filename", out var fn))
            {
                filename = fn;
            }

            var publishDate = discoveredItem.Version;
            if (discoveredItem.ResolverMetadata != null && discoveredItem.ResolverMetadata.TryGetValue("publishDate", out var pd))
            {
                publishDate = pd;
            }

            // Build manifest with correct parameters
            var manifest = manifestBuilder
                .WithBasicInfo(
                    CommunityOutpostConstants.PublisherId,
                    CommunityOutpostConstants.ContentName,
                    discoveredItem.Version)
                .WithContentType(ContentType.Patch, GameType.ZeroHour)
                .WithPublisher(
                    name: CommunityOutpostConstants.PublisherName,
                    website: CommunityOutpostConstants.PublisherWebsite,
                    supportUrl: CommunityOutpostConstants.PatchPageUrl,
                    contactEmail: string.Empty,
                    publisherType: CommunityOutpostConstants.PublisherId)
                .WithMetadata(
                    string.Format(CommunityOutpostConstants.DescriptionTemplate, publishDate),
                    tags: new List<string>(CommunityOutpostConstants.PatchTags),
                    changelogUrl: CommunityOutpostConstants.PatchPageUrl)
                .WithInstallationInstructions(WorkspaceStrategy.HybridCopySymlink);

            // Add the zip file as a remote download
            // The zip should be extracted during installation
            var downloadUrl = discoveredItem.SourceUrl ?? throw new InvalidOperationException(
                "SourceUrl cannot be null for Community Outpost content");

            await manifest.AddRemoteFileAsync(
                filename,
                downloadUrl,
                ContentSourceType.RemoteDownload,
                isExecutable: false);

            logger.LogInformation(
                "Successfully resolved Community Outpost manifest for version {Version}",
                discoveredItem.Version);

            return OperationResult<ContentManifest>.CreateSuccess(manifest.Build());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve Community Outpost content");
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }
}
