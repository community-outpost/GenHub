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
                Version = !string.IsNullOrWhiteSpace(discoveredItem.Version) ? discoveredItem.Version : GenLauncherConstants.DefaultVersion,
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
            ChangelogUrl = versionManifest?.NewsLink ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.NewsLinkMetadataKey) ?? string.Empty,
        };

        if (discoveredItem.Tags.Count > 0)
        {
            manifest.Metadata.Tags = [.. discoveredItem.Tags];
        }

        // Base game installation dependency
        manifest.Dependencies.Add(discoveredItem.TargetGame == GameType.ZeroHour
            ? BaseDependencyBuilder.CreateZeroHour104Dependency()
            : BaseDependencyBuilder.CreateGenerals108Dependency());

        var dependenceName = versionManifest?.DependenceName ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.DependenceNameMetadataKey);
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
            ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.SimpleDownloadLinkMetadataKey);

        if (!string.IsNullOrWhiteSpace(rawDownloadLink) &&
            (rawDownloadLink.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
             rawDownloadLink.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)))
        {
            rawDownloadLink = null;
        }

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

    private static string GetFileNameFromUrl(string url, string defaultName)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var fileName = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(fileName) &&
                    !GenLauncherConstants.IsYamlDescriptorPath(fileName))
                {
                    return Uri.UnescapeDataString(fileName);
                }
            }
        }
        catch (ArgumentException)
        {
            // Fall back to default on invalid URI path formatting
        }

        return $"{defaultName}.zip";
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

        var yamlUrl = GetMetadata(item.ResolverMetadata, GenLauncherConstants.YamlUrlMetadataKey);
        if (string.IsNullOrWhiteSpace(yamlUrl) &&
            !string.IsNullOrWhiteSpace(item.SourceUrl) &&
            (item.SourceUrl.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
             item.SourceUrl.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)))
        {
            yamlUrl = item.SourceUrl;
        }

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

    private async Task<(List<ManifestFile>? Files, bool IsTruncated, string? NextMarker)> FetchS3PageFilesAsync(
        HttpClient client,
        S3BucketQuery query,
        string? currentMarker,
        CancellationToken cancellationToken)
    {
        var (s3Xml, usedAuth) = await FetchS3PageXmlAsync(client, query, currentMarker, cancellationToken);
        if (string.IsNullOrWhiteSpace(s3Xml))
        {
            return (null, false, null);
        }

        var fileEntries = GenLauncherS3XmlParser.ParseListBucketResult(
            s3Xml,
            query.Folder,
            query.Host,
            query.Bucket,
            out var isTruncated,
            out var nextMarker,
            query.PublicKey,
            query.SecretKey,
            useAuth: usedAuth);

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

        return (files, isTruncated, nextMarker);
    }

    private async Task<(string? Xml, bool UsedAuth)> FetchS3PageXmlAsync(
        HttpClient client,
        S3BucketQuery query,
        string? currentMarker,
        CancellationToken cancellationToken)
    {
        var hasExplicitKeys = !string.IsNullOrWhiteSpace(query.PublicKey) && !string.IsNullOrWhiteSpace(query.SecretKey);
        if (hasExplicitKeys)
        {
            return await FetchSignedS3PageXmlAsync(client, query, currentMarker, cancellationToken);
        }

        return await FetchAnonymousWithSignedFallbackXmlAsync(client, query, currentMarker, cancellationToken);
    }

    private async Task<(string? Xml, bool UsedAuth)> FetchSignedS3PageXmlAsync(
        HttpClient client,
        S3BucketQuery query,
        string? currentMarker,
        CancellationToken cancellationToken)
    {
        var queryUrl = GenLauncherS3XmlParser.BuildS3QueryUrl(
            query.Host,
            query.Bucket,
            query.Folder,
            currentMarker,
            query.PublicKey,
            query.SecretKey,
            useAuth: true);

        if (!ImageCacheService.IsSafeRemoteUrl(queryUrl, out _))
        {
            logger.LogWarning("Rejecting unsafe S3 query URL for host={Host}, bucket={Bucket}, prefix={Prefix}", query.Host, query.Bucket, query.Folder);
            return (null, true);
        }

        logger.LogInformation("Querying GenLauncher S3 bucket (signed) at host={Host}, bucket={Bucket}, prefix={Prefix}", query.Host, query.Bucket, query.Folder);
        var xml = await client.GetStringAsync(queryUrl, cancellationToken);
        return (xml, true);
    }

    private async Task<(string? Xml, bool UsedAuth)> FetchAnonymousWithSignedFallbackXmlAsync(
        HttpClient client,
        S3BucketQuery query,
        string? currentMarker,
        CancellationToken cancellationToken)
    {
        // Try unsigned query first since ~70% of GenLauncher MinIO buckets (rotr, forgenerals, contra, zhreborn, etc.) are public anonymous
        var unsignedUrl = GenLauncherS3XmlParser.BuildS3QueryUrl(
            query.Host,
            query.Bucket,
            query.Folder,
            currentMarker,
            publicKey: null,
            secretKey: null,
            useAuth: false);

        if (!ImageCacheService.IsSafeRemoteUrl(unsignedUrl, out _))
        {
            logger.LogWarning("Rejecting unsafe S3 query URL for host={Host}, bucket={Bucket}, prefix={Prefix}", query.Host, query.Bucket, query.Folder);
            return (null, false);
        }

        string? s3Xml = null;
        try
        {
            logger.LogInformation("Querying GenLauncher S3 bucket (anonymous) at host={Host}, bucket={Bucket}, prefix={Prefix}", query.Host, query.Bucket, query.Folder);
            var resp = await client.GetAsync(unsignedUrl, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                s3Xml = await resp.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Anonymous S3 query failed, will try signed query");
        }

        if (string.IsNullOrWhiteSpace(s3Xml) || s3Xml.Contains("<Error>"))
        {
            // Fallback to signed query using default InSave credentials (for improved-ai, tpotw, cncpowerplay)
            return await FetchSignedS3PageXmlAsync(client, query, currentMarker, cancellationToken);
        }

        return (s3Xml, false);
    }

    private async Task<bool> TryResolveS3StoragePayloadAsync(
        ContentManifest manifest,
        HttpClient client,
        GenLauncherVersionManifest? versionManifest,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken)
    {
        var s3Host = versionManifest?.S3HostLink ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.S3HostLinkMetadataKey) ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.S3HostMetadataKey);
        var s3Bucket = versionManifest?.S3BucketName ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.S3BucketNameMetadataKey) ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.S3BucketMetadataKey);
        var s3Folder = versionManifest?.S3FolderName ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.S3FolderNameMetadataKey) ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.S3FolderMetadataKey);
        var s3PublicKey = versionManifest?.S3HostPublicKey ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.S3HostPublicKeyMetadataKey);
        var s3SecretKey = versionManifest?.S3HostSecretKey ?? GetMetadata(discoveredItem.ResolverMetadata, GenLauncherConstants.S3HostSecretKeyMetadataKey);

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
            const int maxPages = GenLauncherConstants.MaxS3ResolverPages;
            var reachedTerminalPage = false;

            var query = new S3BucketQuery(s3Host, s3Bucket, s3Folder, s3PublicKey, s3SecretKey);

            while (pageCount++ < maxPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (files, isTruncated, marker) = await FetchS3PageFilesAsync(
                    client, query, nextMarker, cancellationToken);

                if (files == null || (files.Count == 0 && s3Files.Count == 0))
                {
                    return false;
                }

                s3Files.AddRange(files);

                if (!isTruncated)
                {
                    reachedTerminalPage = true;
                    break;
                }

                if (string.IsNullOrEmpty(marker) || !seenMarkers.Add(marker))
                {
                    logger.LogWarning("S3 pagination indicated truncation but provided missing or repeated marker for {Name}", discoveredItem.Name);
                    return false;
                }

                nextMarker = marker;
            }

            if (!reachedTerminalPage)
            {
                logger.LogWarning("S3 pagination exceeded max page limit ({MaxPages}) for {Name}", maxPages, discoveredItem.Name);
                return false;
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

    private sealed record S3BucketQuery(
        string Host,
        string Bucket,
        string Folder,
        string? PublicKey,
        string? SecretKey);
}
