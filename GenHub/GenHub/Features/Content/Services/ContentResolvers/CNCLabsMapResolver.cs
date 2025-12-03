using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves CNC Labs map details from discovered content items.
/// </summary>
public class CNCLabsMapResolver(HttpClient httpClient, IServiceProvider serviceProvider, ILogger<CNCLabsMapResolver> logger) : IContentResolver
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<CNCLabsMapResolver> _logger = logger;

    /// <summary>
    /// Gets the unique resolver ID for CNC Labs Map.
    /// </summary>
    public string ResolverId => "CNCLabsMap";

    /// <summary>
    /// Resolves the details of a discovered CNC Labs map item.
    /// </summary>
    /// <param name="discoveredItem">The discovered content item to resolve.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="OperationResult{ContentManifest}"/> containing the resolved details.</returns>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(ContentSearchResult discoveredItem, CancellationToken cancellationToken = default)
    {
        if (discoveredItem?.SourceUrl == null)
        {
            return OperationResult<ContentManifest>.CreateFailure("Invalid discovered item or source URL");
        }

        try
        {
            var response = await _httpClient.GetStringAsync(discoveredItem.SourceUrl, cancellationToken);
            var mapDetails = ParseMapDetailPage(response);

            if (string.IsNullOrEmpty(mapDetails.downloadUrl))
            {
                return OperationResult<ContentManifest>.CreateFailure("No download URL found in map details");
            }

            // Create a new manifest builder for each resolve operation to ensure clean state
            var manifestBuilder = _serviceProvider.GetRequiredService<IContentManifestBuilder>();

            var manifestVersionInt = int.TryParse(mapDetails.version, out var parsedVersion) ? parsedVersion : 0;
            var manifest = manifestBuilder
                .WithBasicInfo(discoveredItem.Id, mapDetails.name, manifestVersionInt)
                .WithContentType(ContentType.MapPack, GameType.ZeroHour)
                .WithPublisher(mapDetails.author)
                .WithMetadata(
                    mapDetails.description,
                    tags: new List<string> { "Map", "CNC Labs", "Community" },
                    iconUrl: mapDetails.previewImage,
                    screenshotUrls: mapDetails.ScreenshotUrls);

            // Add the map file
            await manifest.AddRemoteFileAsync(
                Path.GetFileName(mapDetails.downloadUrl),
                mapDetails.downloadUrl,
                ContentSourceType.RemoteDownload);

            // Add required directories for maps
            manifest.AddRequiredDirectories("Maps");

            return OperationResult<ContentManifest>.CreateSuccess(manifest.Build());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve map details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses the HTML detail page for a CNC Labs map and extracts map details.
    /// </summary>
    /// <param name="html">The HTML content of the map detail page.</param>
    /// <returns>A <see cref="MapDetails"/> record containing parsed details.</returns>
    private MapDetails ParseMapDetailPage(string html)
    {
        // TODO: Implement HTML parsing logic
        return new MapDetails(
            name: string.Empty,
            description: string.Empty,
            version: string.Empty,
            author: string.Empty,
            previewImage: string.Empty,
            screenshots: new List<string>(),
            fileSize: ContentConstants.DefaultFileSize,
            downloadCount: ContentConstants.DefaultDownloadCount,
            submissionDate: DateTime.MinValue,
            downloadUrl: string.Empty,
            fileType: string.Empty,
            rating: ContentConstants.DefaultRating);
    }

    /// <summary>
    /// Represents the details of a CNC Labs map.
    /// </summary>
    /// <param name="name">The name of the map.</param>
    /// <param name="description">The description of the map.</param>
    /// <param name="version">The version of the map.</param>
    /// <param name="author">The author of the map.</param>
    /// <param name="previewImage">The preview image URL.</param>
    /// <param name="screenshots">A list of screenshot URLs.</param>
    /// <param name="fileSize">The file size in bytes.</param>
    /// <param name="downloadCount">The number of downloads.</param>
    /// <param name="submissionDate">The date the map was submitted.</param>
    /// <param name="downloadUrl">The download URL.</param>
    /// <param name="fileType">The file type.</param>
    /// <param name="rating">The rating of the map.</param>
    private record MapDetails(
        string name = "",
        string description = "",
        string version = "",
        string author = "",
        string previewImage = "",
        List<string>? screenshots = null,
        long fileSize = ContentConstants.DefaultFileSize,
        int downloadCount = ContentConstants.DefaultDownloadCount,
        DateTime submissionDate = default,
        string downloadUrl = "",
        string fileType = "",
        float rating = ContentConstants.DefaultRating
    )
    {
        public List<string> ScreenshotUrls => screenshots ?? new List<string>();
    }
}