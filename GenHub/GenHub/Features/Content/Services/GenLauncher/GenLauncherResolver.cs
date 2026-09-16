using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GenLauncher;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Services.Dependencies;
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
/// Resolves discovered GenLauncher content items into full ContentManifest instances.
/// Handles S3 bucket listing and direct cloud mirror downloads.
/// </summary>
public class GenLauncherResolver : IContentResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GenLauncherCatalogParser _catalogParser;
    private readonly ILogger<GenLauncherResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenLauncherResolver"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="catalogParser">The GenLauncher catalog parser.</param>
    /// <param name="logger">The logger instance.</param>
    public GenLauncherResolver(
        IHttpClientFactory httpClientFactory,
        GenLauncherCatalogParser catalogParser,
        ILogger<GenLauncherResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _catalogParser = catalogParser;
        _logger = logger;
    }

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
            var client = _httpClientFactory.CreateClient(PublisherTypeConstants.GenLauncher);
            var slug = GenLauncherCatalogParser.Slugify(discoveredItem.Name);
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
            _logger.LogError(ex, "Error resolving GenLauncher content for {Name}", discoveredItem.Name);
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
            CoverUrl = discoveredItem.BannerUrl ?? string.Empty,
            Tags = [.. discoveredItem.Tags],
        };

        if (versionManifest != null && !string.IsNullOrWhiteSpace(versionManifest.SupportLink))
        {
            manifest.Publisher.SupportUrl = versionManifest.SupportLink;
        }

        // Base game installation dependency
        manifest.Dependencies.Add(discoveredItem.TargetGame == GameType.ZeroHour
            ? BaseDependencyBuilder.CreateZeroHour104Dependency()
            : BaseDependencyBuilder.CreateGenerals108Dependency());

        // Parent mod dependency
        if (versionManifest != null && !string.IsNullOrWhiteSpace(versionManifest.DependenceName))
        {
            var depSlug = GenLauncherCatalogParser.Slugify(versionManifest.DependenceName);
            var parentModId = ManifestId.Create(
                ManifestIdGenerator.GeneratePublisherContentId(
                    publisherToken,
                    ContentType.Mod,
                    depSlug,
                    0));

            manifest.Dependencies.Add(new ContentDependency
            {
                Id = parentModId,
                Name = versionManifest.DependenceName,
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
            ?? GetMetadata(discoveredItem.ResolverMetadata, "simpleDownloadLink")
            ?? discoveredItem.SourceUrl;

        if (string.IsNullOrWhiteSpace(rawDownloadLink))
        {
            return;
        }

        var directUrl = GenLauncherDownloadLinkParser.ParseDownloadLink(rawDownloadLink);
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
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken)
    {
        if (discoveredItem.Data is GenLauncherVersionManifest manifest)
        {
            return manifest;
        }

        var yamlUrl = GetMetadata(discoveredItem.ResolverMetadata, "yamlUrl");
        if (string.IsNullOrWhiteSpace(yamlUrl))
        {
            return null;
        }

        try
        {
            var yaml = await client.GetStringAsync(yamlUrl, cancellationToken);
            return _catalogParser.ParseVersionManifest(yaml);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch/parse version manifest from {Url}", yamlUrl);
            return null;
        }
    }

    [SuppressMessage("Security", "S5332:Using http protocol is insecure", Justification = "GenLauncher MinIO remotes operate over plain HTTP without TLS")]
    private async Task<bool> TryResolveS3StoragePayloadAsync(
        ContentManifest manifest,
        HttpClient client,
        GenLauncherVersionManifest? versionManifest,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken)
    {
        var s3Host = versionManifest?.S3HostLink ?? GetMetadata(discoveredItem.ResolverMetadata, "s3Host");
        var s3Bucket = versionManifest?.S3BucketName ?? GetMetadata(discoveredItem.ResolverMetadata, "s3Bucket");
        var s3Folder = versionManifest?.S3FolderName ?? GetMetadata(discoveredItem.ResolverMetadata, "s3Folder");

        if (string.IsNullOrWhiteSpace(s3Host) || string.IsNullOrWhiteSpace(s3Bucket) || string.IsNullOrWhiteSpace(s3Folder))
        {
            return false;
        }

        try
        {
            var hasMorePages = true;
            string? nextMarker = null;
            var anyFiles = false;

            while (hasMorePages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var queryUrl = GenLauncherS3XmlParser.BuildS3QueryUrl(s3Host, s3Bucket, s3Folder, nextMarker);
                _logger.LogInformation("Querying GenLauncher S3 bucket at {Url}", queryUrl);

                var s3Xml = await client.GetStringAsync(queryUrl, cancellationToken);
                var fileEntries = GenLauncherS3XmlParser.ParseListBucketResult(
                    s3Xml,
                    s3Folder,
                    s3Host,
                    s3Bucket,
                    out var isTruncated,
                    out nextMarker);

                if (fileEntries.Count == 0 && !anyFiles)
                {
                    return false;
                }

                foreach (var entry in fileEntries)
                {
                    manifest.Files.Add(new ManifestFile
                    {
                        RelativePath = entry.RelativePath,
                        DownloadUrl = entry.DownloadUrl,
                        Size = entry.Size,
                        Hash = entry.ETag,
                        SourceType = ContentSourceType.RemoteDownload,
                        IsRequired = true,
                    });
                    anyFiles = true;
                }

                hasMorePages = isTruncated && !string.IsNullOrEmpty(nextMarker);
            }

            return anyFiles;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve S3 files for {Name}, falling back to download link", discoveredItem.Name);
            return false;
        }
    }
}
