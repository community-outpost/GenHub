using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves CNC Labs map details from discovered content items.
/// </summary>
public class CNCLabsMapResolver(HttpClient httpClient, IContentManifestBuilder manifestBuilder, ILogger<CNCLabsMapResolver> logger) : IContentResolver
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IContentManifestBuilder _manifestBuilder = manifestBuilder;
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
    /// <returns>A <see cref="ContentOperationResult{ContentManifest}"/> containing the resolved details.</returns>
    public async Task<ContentOperationResult<ContentManifest>> ResolveAsync(ContentSearchResult discoveredItem, CancellationToken cancellationToken = default)
        {
            if (discoveredItem?.SourceUrl == null)
            {
                return ContentOperationResult<ContentManifest>.CreateFailure("Invalid discovered item or source URL");
            }

            try
            {
                var response = await _httpClient.GetStringAsync(discoveredItem.SourceUrl, cancellationToken);
                var mapDetails = ParseMapDetailPage(response);

                if (string.IsNullOrEmpty(mapDetails.downloadUrl))
                {
                    return ContentOperationResult<ContentManifest>.CreateFailure("No download URL found in map details");
                }

                var manifest = _manifestBuilder
                    .WithBasicInfo(discoveredItem.Id, mapDetails.name, mapDetails.version)
                    .WithContentType(ContentType.MapPack, GameType.ZeroHour)
                    .WithPublisher(mapDetails.author)
                    .WithMetadata(
                        mapDetails.description,
                        tags: new List<string> { "Map", "CNC Labs", "Community" },
                        iconUrl: mapDetails.previewImage,
                        screenshotUrls: mapDetails.ScreenshotUrls);

                // Add the map file
                await manifest.AddFileAsync(
                    Path.GetFileName(mapDetails.downloadUrl),
                    ManifestFileSourceType.Download,
                    mapDetails.downloadUrl);

                // Add required directories for maps
                manifest.AddRequiredDirectories("Maps");

                return ContentOperationResult<ContentManifest>.CreateSuccess(manifest.Build());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve map details from {Url}", discoveredItem.SourceUrl);
                return ContentOperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
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
            fileSize: 0,
            downloadCount: 0,
            submissionDate: DateTime.MinValue,
            downloadUrl: string.Empty,
            fileType: string.Empty,
            rating: 0f);
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
        long fileSize = 0,
        int downloadCount = 0,
        DateTime submissionDate = default,
        string downloadUrl = "",
        string fileType = "",
        float rating = 0f
    )
    {
        public List<string> ScreenshotUrls => screenshots ?? new List<string>();
    }
}
