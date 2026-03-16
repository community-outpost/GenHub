using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Resolves content from generic publisher catalogs into ContentManifest objects.
/// Leverages existing ContentManifestBuilder for archive extraction and CAS storage.
/// </summary>
public class GenericCatalogResolver(
    ILogger<GenericCatalogResolver> logger,
    IContentManifestBuilder manifestBuilder) : IContentResolver
{
    private readonly ILogger<GenericCatalogResolver> _logger = logger;
    private readonly IContentManifestBuilder _manifestBuilder = manifestBuilder;

    /// <inheritdoc />
    public string ResolverId => CatalogConstants.GenericCatalogResolverId;

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult searchResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchResult);

        try
        {
            // Extract catalog metadata from search result (stored as JSON strings)
            if (!searchResult.ResolverMetadata.TryGetValue("releaseJson", out var releaseJson))
            {
                return OperationResult<ContentManifest>.CreateFailure("Missing release metadata");
            }

            if (!searchResult.ResolverMetadata.TryGetValue("catalogItemJson", out var contentItemJson))
            {
                return OperationResult<ContentManifest>.CreateFailure("Missing content item metadata");
            }

            if (!searchResult.ResolverMetadata.TryGetValue("publisherProfileJson", out var publisherJson))
            {
                return OperationResult<ContentManifest>.CreateFailure("Missing publisher profile");
            }

            // Deserialize from JSON
            var release = System.Text.Json.JsonSerializer.Deserialize<ContentRelease>(releaseJson);
            var contentItem = System.Text.Json.JsonSerializer.Deserialize<CatalogContentItem>(contentItemJson);
            var publisher = System.Text.Json.JsonSerializer.Deserialize<PublisherProfile>(publisherJson);

            if (release == null || contentItem == null || publisher == null)
            {
                return OperationResult<ContentManifest>.CreateFailure("Failed to deserialize catalog metadata");
            }

            _logger.LogInformation(
                "Resolving content '{ContentName}' v{Version} from publisher '{PublisherId}'",
                contentItem.Name,
                release.Version,
                publisher.Id);

            // Build manifest using existing ContentManifestBuilder
            var builder = _manifestBuilder
                .WithBasicInfo(publisher.Id, contentItem.Name, release.Version)
                .WithContentType(contentItem.ContentType, contentItem.TargetGame)
                .WithPublisher(publisher.Name, publisher.Website ?? string.Empty, publisher.SupportUrl ?? string.Empty, publisher.ContactEmail ?? string.Empty, publisher.Id)
                .WithMetadata(
                    description: contentItem.Description,
                    tags: [.. contentItem.Tags],
                    iconUrl: contentItem.Metadata?.BannerUrl ?? string.Empty,
                    screenshotUrls: contentItem.Metadata?.ScreenshotUrls?.ToList());

            // Add primary artifact for download
            var primaryArtifact = release.Artifacts.FirstOrDefault(a => a.IsPrimary) ?? release.Artifacts.First();

            // ContentManifestBuilder.AddDownloadedFileAsync will:
            // 1. Download the file
            // 2. Auto-detect if it's an archive (ZIP/RAR/7z)
            // 3. Extract all files if archive
            // 4. Store each file in CAS
            // 5. Add ManifestFile entries
            await builder.AddRemoteFileAsync(
                relativePath: primaryArtifact.Filename,
                downloadUrl: primaryArtifact.DownloadUrl,
                sourceType: ContentSourceType.ContentAddressable,
                isExecutable: false,
                permissions: null);

            // Add dependencies
            foreach (var dependency in release.Dependencies)
            {
                var dependencyId = ManifestIdGenerator.GeneratePublisherContentId(
                    dependency.PublisherId,
                    ContentType.Mod, // Default to Mod for catalog dependencies if not specified
                    dependency.ContentId,
                    ExtractVersionNumber(dependency.VersionConstraint ?? "0"));

                builder.AddDependency(
                    id: ManifestId.Create(dependencyId),
                    name: dependency.ContentId,
                    dependencyType: ContentType.Mod,
                    installBehavior: dependency.IsOptional ? DependencyInstallBehavior.Optional : DependencyInstallBehavior.RequireExisting,
                    minVersion: dependency.VersionConstraint ?? string.Empty);
            }

            var manifest = builder.Build();

            _logger.LogInformation(
                "Successfully resolved manifest for '{ContentName}' with {FileCount} files",
                manifest.Name,
                manifest.Files.Count);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve content from catalog");
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    private static int ExtractVersionNumber(string version)
    {
        if (int.TryParse(new string([.. version.Where(char.IsDigit)]), out var result))
        {
            return result;
        }

        return 0;
    }
}
