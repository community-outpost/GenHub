using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GenLauncher;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Services.Dependencies;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Initializes a new instance of the <see cref="GenLauncherResolver"/> class.
/// Resolves discovered GenLauncher content items into full ContentManifest instances.
/// Handles S3 bucket listing and direct cloud mirror downloads.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="catalogParser">The GenLauncher catalog parser.</param>
/// <param name="logger">The logger instance.</param>
public class GenLauncherResolver(
    IHttpClientFactory httpClientFactory,
    GenLauncherCatalogParser catalogParser,
    ILogger<GenLauncherResolver> logger)
    : IContentResolver
{
    /// <inheritdoc/>
    public string ResolverId => GenLauncherConstants.PublisherId;

    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(null, discoveredItem, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ProviderDefinition? provider,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discoveredItem);

        try
        {
            var client = httpClientFactory.CreateClient(PublisherTypeConstants.GenLauncher);
            var slug = GenLauncherCatalogParser.Slugify(discoveredItem.Name);
            if (!string.IsNullOrEmpty(discoveredItem.VariantGroupId) &&
                !string.Equals(discoveredItem.VariantGroupId, slug, StringComparison.OrdinalIgnoreCase))
            {
                slug = $"{discoveredItem.VariantGroupId}-{slug}";
            }

            var gameToken = discoveredItem.TargetGame == GameType.ZeroHour ? "zerohour" : "generals";
            var publisherToken = $"{GenLauncherConstants.PublisherId}-{gameToken}";

            discoveredItem.ResolverMetadata.TryGetValue(ContentConstants.ParentContentIdMetadataKey, out var parentContentId);
            var effectiveOriginalContentId = !string.IsNullOrWhiteSpace(parentContentId)
                ? parentContentId
                : discoveredItem.Id;

            var manifest = new ContentManifest
            {
                Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                    publisherToken,
                    discoveredItem.ContentType,
                    slug,
                    0)),
                Name = discoveredItem.Name,
                Version = !string.IsNullOrWhiteSpace(discoveredItem.Version) ? discoveredItem.Version : "1.0.0",
                ContentType = discoveredItem.ContentType,
                TargetGame = discoveredItem.TargetGame,
                OriginalProviderName = PublisherTypeConstants.GenLauncher,
                OriginalContentId = effectiveOriginalContentId,
                Publisher = new PublisherInfo
                {
                    Name = PublisherInfoConstants.GenLauncher.Name,
                    PublisherType = PublisherTypeConstants.GenLauncher,
                    Website = PublisherInfoConstants.GenLauncher.Website,
                    SupportUrl = PublisherInfoConstants.GenLauncher.SupportUrl,
                },
            };

            var versionManifest = await FetchVersionManifestIfNeededAsync(client, discoveredItem, cancellationToken);
            PopulateMetadataAndDependencies(manifest, discoveredItem, versionManifest, publisherToken);

            var s3Resolved = await TryResolveS3StoragePayloadAsync(manifest, client, versionManifest, discoveredItem, cancellationToken);
            if (!s3Resolved)
            {
                ResolveDirectDownloadPayload(manifest, versionManifest, discoveredItem, slug);
            }

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving GenLauncher content for {Name}", discoveredItem.Name);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to resolve GenLauncher content: {ex.Message}");
        }
    }

    private static void PopulateMetadataAndDependencies(
        ContentManifest manifest,
        ContentSearchResult discoveredItem,
        GenLauncherVersionManifest? versionManifest,
        string publisherToken)
    {
        manifest.Metadata = new ContentMetadata
        {
            Description = discoveredItem.Description ?? string.Empty,
            IconUrl = versionManifest?.UIImageSourceLink ?? discoveredItem.IconUrl ?? string.Empty,
            ChangelogUrl = versionManifest?.NewsLink ?? GetMetadata(discoveredItem.ResolverMetadata, "newsLink") ?? string.Empty,
        };

        if (discoveredItem.Tags.Count > 0)
        {
            manifest.Metadata.Tags = [.. discoveredItem.Tags];
        }

        // Base game installation dependency
        manifest.Dependencies.Add(discoveredItem.TargetGame == GameType.ZeroHour
            ? BaseDependencyBuilder.CreateZeroHour104Dependency()
            : BaseDependencyBuilder.CreateGenerals108Dependency());

        var dependenceName = versionManifest?.DependenceName ?? GetMetadata(discoveredItem.ResolverMetadata, "dependenceName");
        if (!string.IsNullOrWhiteSpace(dependenceName))
        {
            var parentSlug = GenLauncherCatalogParser.Slugify(dependenceName);
            manifest.Dependencies.Add(new ContentDependency
            {
                Id = ManifestIdGenerator.GeneratePublisherContentId(
                    publisherToken,
                    ContentType.Mod,
                    parentSlug,
                    0),
                Name = dependenceName,
                DependencyType = ContentType.Mod,
                StrictPublisher = false,
                CompatibleGameTypes = [discoveredItem.TargetGame],
                InstallBehavior = DependencyInstallBehavior.RequireExisting,
            });
        }
    }

    private static void ResolveDirectDownloadPayload(
        ContentManifest manifest,
        GenLauncherVersionManifest? versionManifest,
        ContentSearchResult discoveredItem,
        string slug)
    {
        var rawDownloadLink = versionManifest?.SimpleDownloadLink
            ?? discoveredItem.SelectedDownloadUrl
            ?? GetMetadata(discoveredItem.ResolverMetadata, "simpleDownloadLink");

        if (string.IsNullOrWhiteSpace(rawDownloadLink))
        {
            var sourceUrl = discoveredItem.SourceUrl;
            if (!string.IsNullOrWhiteSpace(sourceUrl) &&
                !sourceUrl.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) &&
                !sourceUrl.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                rawDownloadLink = sourceUrl;
            }
        }

        if (string.IsNullOrWhiteSpace(rawDownloadLink))
        {
            return;
        }

        var directUrl = GenLauncherDownloadLinkParser.ParseDownloadLink(rawDownloadLink);
        if (!ImageCacheService.IsSafeRemoteUrl(directUrl, out _))
        {
            return;
        }

        var fileName = GetFileNameFromUrl(directUrl, slug);

        manifest.Files.Add(new ManifestFile
        {
            RelativePath = fileName,
            DownloadUrl = directUrl,
            Size = discoveredItem.DownloadSize,
            SourceType = ContentSourceType.RemoteDownload,
            IsRequired = true,
        });
    }

    private static string GetFileNameFromUrl(string url, string defaultSlug)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name) && name.Contains('.'))
            {
                return name;
            }
        }

        return $"{defaultSlug}.zip";
    }

    private static string? GetMetadata(IDictionary<string, string>? dict, string key)
    {
        if (dict != null && dict.TryGetValue(key, out var value))
        {
            return value;
        }

        return null;
    }

    private async Task<GenLauncherVersionManifest?> FetchVersionManifestIfNeededAsync(
        HttpClient client,
        ContentSearchResult item,
        CancellationToken cancellationToken)
    {
        if (item.Data is GenLauncherVersionManifest manifest)
        {
            return manifest;
        }

        var yamlUrl = GetMetadata(item.ResolverMetadata, "yamlUrl") ?? item.SourceUrl;
        if (string.IsNullOrWhiteSpace(yamlUrl) || !ImageCacheService.IsSafeRemoteUrl(yamlUrl, out _))
        {
            return null;
        }

        try
        {
            var yaml = await client.GetStringAsync(yamlUrl, cancellationToken);
            return catalogParser.ParseVersionManifest(yaml);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch version manifest from {Url}", yamlUrl);
            return null;
        }
    }

    private async Task<(List<ManifestFile>? Files, bool HasMore, string? NextMarker)> FetchS3PageFilesAsync(
        HttpClient client,
        string s3Host,
        string s3Bucket,
        string s3Folder,
        string? currentMarker,
        CancellationToken cancellationToken)
    {
        var queryUrl = GenLauncherS3XmlParser.BuildS3QueryUrl(s3Host, s3Bucket, s3Folder, currentMarker);
        if (!ImageCacheService.IsSafeRemoteUrl(queryUrl, out _))
        {
            logger.LogWarning("Rejecting unsafe S3 query URL: {Url}", queryUrl);
            return (null, false, null);
        }

        logger.LogInformation("Querying GenLauncher S3 bucket at {Url}", queryUrl);

        var s3Xml = await client.GetStringAsync(queryUrl, cancellationToken);
        var fileEntries = GenLauncherS3XmlParser.ParseListBucketResult(
            s3Xml,
            s3Folder,
            s3Host,
            s3Bucket,
            out var isTruncated,
            out var nextMarker);

        var files = new List<ManifestFile>();
        foreach (var entry in fileEntries)
        {
            files.Add(new ManifestFile
            {
                RelativePath = entry.RelativePath,
                DownloadUrl = entry.DownloadUrl,
                Size = entry.Size,
                Hash = entry.ETag,
                SourceType = ContentSourceType.RemoteDownload,
                IsRequired = true,
            });
        }

        var hasMore = isTruncated && !string.IsNullOrEmpty(nextMarker);
        return (files, hasMore, nextMarker);
    }

    private async Task<bool> TryResolveS3StoragePayloadAsync(
        ContentManifest manifest,
        HttpClient client,
        GenLauncherVersionManifest? versionManifest,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken)
    {
        var s3Host = versionManifest?.S3HostLink ?? GetMetadata(discoveredItem.ResolverMetadata, "s3HostLink") ?? GetMetadata(discoveredItem.ResolverMetadata, "s3Host");
        var s3Bucket = versionManifest?.S3BucketName ?? GetMetadata(discoveredItem.ResolverMetadata, "s3BucketName") ?? GetMetadata(discoveredItem.ResolverMetadata, "s3Bucket");
        var s3Folder = versionManifest?.S3FolderName ?? GetMetadata(discoveredItem.ResolverMetadata, "s3FolderName") ?? GetMetadata(discoveredItem.ResolverMetadata, "s3Folder");

        if (string.IsNullOrWhiteSpace(s3Host) || string.IsNullOrWhiteSpace(s3Bucket) || string.IsNullOrWhiteSpace(s3Folder))
        {
            return false;
        }

        try
        {
            string? nextMarker = null;
            var s3Files = new List<ManifestFile>();
            var seenMarkers = new HashSet<string>(StringComparer.Ordinal);
            var pageCount = 0;
            const int maxPages = 100;

            while (pageCount++ < maxPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (files, hasMore, marker) = await FetchS3PageFilesAsync(
                    client, s3Host, s3Bucket, s3Folder, nextMarker, cancellationToken);

                if (files == null || (files.Count == 0 && s3Files.Count == 0))
                {
                    return false;
                }

                s3Files.AddRange(files);

                if (!hasMore || string.IsNullOrEmpty(marker) || !seenMarkers.Add(marker))
                {
                    break;
                }

                nextMarker = marker;
            }

            manifest.Files.AddRange(s3Files);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve S3 files for {Name}, falling back to download link", discoveredItem.Name);
            return false;
        }
    }
}
