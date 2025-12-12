using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Resolves Generals Online search results into ContentManifests with download URLs.
/// Creates the initial manifest structure; post-extraction processing is handled by the factory.
/// </summary>
public class GeneralsOnlineResolver(
    IProviderDefinitionLoader providerLoader,
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

            // Create the primary manifest (30Hz variant) directly
            var primaryManifest = CreateVariantManifest(
                release,
                GameClientConstants.GeneralsOnline30HzExecutable,
                GeneralsOnlineConstants.Variant30HzSuffix,
                GameClientConstants.GeneralsOnline30HzDisplayName);

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

    /// <summary>
    /// Parses a Generals Online version string to extract a numeric user version for manifest IDs.
    /// Converts versions like "111825_QFE2" (Nov 18, 2025) to a numeric value like 1118252.
    /// NOTE: Format is dictated by Generals Online CDN API (MMDDYY_QFE#), not our choice.
    /// This method converts it to a sortable numeric format.
    /// </summary>
    /// <param name="version">The version string (e.g., "111825_QFE2").</param>
    /// <returns>A numeric version suitable for manifest IDs.</returns>
    private static int ParseVersionForManifestId(string version)
    {
        try
        {
            var parts = version.Split(new[] { GeneralsOnlineConstants.QfeSeparator }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return 0;
            }

            var datePart = parts[0]; // "101525"
            var qfePart = parts[1].Replace("QFE", string.Empty, StringComparison.OrdinalIgnoreCase);

            if (!int.TryParse(datePart, out var dateValue) || !int.TryParse(qfePart, out var qfeValue))
            {
                return 0;
            }

            // Combine: 101525 * 10 + 5 = 1015255
            return (dateValue * 10) + qfeValue;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Creates a content manifest for a specific Generals Online variant.
    /// This creates an INITIAL manifest with download URLs - file hashes are added later by the Factory.
    /// </summary>
    /// <param name="release">The Generals Online release information.</param>
    /// <param name="executableName">The executable filename for this variant.</param>
    /// <param name="variantSuffix">The suffix for the manifest ID (e.g., "30hz").</param>
    /// <param name="displayName">The display name for this variant (e.g., "GeneralsOnline 30Hz").</param>
    /// <returns>A content manifest for the specified variant.</returns>
    private ContentManifest CreateVariantManifest(
        GeneralsOnlineRelease release,
        string executableName,
        string variantSuffix,
        string displayName)
    {
        // Parse version to extract numeric version (remove dots and QFE markers)
        var userVersion = ParseVersionForManifestId(release.Version);

        // Content name for GeneralsOnline (publisher is "generalsonline", content is the variant)
        // This will create IDs like: 1.1015255.generalsonline.gameclient.30hz
        var contentName = variantSuffix;

        var manifestId = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.GeneralsOnline,
            ContentType.GameClient,
            contentName,
            userVersion));

        // Get URLs from provider definition
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? string.Empty;
        var supportUrl = provider?.Endpoints.SupportUrl ?? string.Empty;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint("downloadPageUrl") ?? string.Empty;
        var iconUrl = provider?.Endpoints.GetEndpoint("iconUrl") ?? string.Empty;

        return new ContentManifest
        {
            Id = manifestId,
            Name = displayName,
            Version = release.Version,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                Name = GeneralsOnlineConstants.PublisherName,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Website = websiteUrl,
                SupportUrl = supportUrl,
                ContentIndexUrl = downloadPageUrl,
                UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
            },
            Metadata = new ContentMetadata
            {
                Description = GeneralsOnlineConstants.ShortDescription,
                ReleaseDate = release.ReleaseDate,
                IconUrl = iconUrl,
                Tags = new List<string>(GeneralsOnlineConstants.Tags),
                ChangelogUrl = release.Changelog,
            },
            Files = new List<ManifestFile>
            {
                new ManifestFile
                {
                    RelativePath = Path.GetFileName(release.PortableUrl),
                    DownloadUrl = release.PortableUrl,
                    Size = release.PortableSize ?? 0, // Use 0 when size is unknown
                    SourceType = ContentSourceType.RemoteDownload,
                    Hash = string.Empty, // Hash will be computed after extraction by Factory
                },
            },
            Dependencies = variantSuffix == GeneralsOnlineConstants.Variant60HzSuffix
                ? GeneralsOnlineDependencyBuilder.GetDependenciesFor60Hz(userVersion)
                : GeneralsOnlineDependencyBuilder.GetDependenciesFor30Hz(userVersion),
        };
    }
}
