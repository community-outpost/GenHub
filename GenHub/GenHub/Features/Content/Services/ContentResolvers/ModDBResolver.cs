using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.ModDB;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Parsers;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using MapDetails = GenHub.Core.Models.ModDB.MapDetails;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves ModDB content details from discovered items.
/// Uses the universal web page parser to extract rich content.
/// </summary>
public class ModDBResolver(
    HttpClient httpClient,
    ModDBManifestFactory manifestFactory,
    ModDBPageParser webPageParser,
    ILogger<ModDBResolver> logger) : IContentResolver
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ModDBManifestFactory _manifestFactory = manifestFactory;
    private readonly ModDBPageParser _webPageParser = webPageParser;
    private readonly ILogger<ModDBResolver> _logger = logger;

    /// <inheritdoc />
    public string ResolverId => "ModDB";

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        if (discoveredItem?.SourceUrl == null)
        {
            return OperationResult<ContentManifest>.CreateFailure("Invalid discovered item or source URL");
        }

        try
        {
            _logger.LogInformation("Resolving ModDB content from {Url}", discoveredItem.SourceUrl);

            // Use the universal parser to parse the page
            var parsedPage = await _webPageParser.ParseAsync(discoveredItem.SourceUrl, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Store the parsed page in the discovered item for UI display
            discoveredItem.SetData(parsedPage);

            // Extract the primary download URL from parsed files
            var primaryDownloadUrl = ExtractPrimaryDownloadUrl(parsedPage);

            if (string.IsNullOrEmpty(primaryDownloadUrl))
            {
                return OperationResult<ContentManifest>.CreateFailure("No download URL found in mod details");
            }

            // Convert the parsed page to MapDetails for the manifest factory
            var mapDetails = ConvertToMapDetails(parsedPage, discoveredItem, primaryDownloadUrl);

            // Use the factory to create the manifest
            var manifest = await _manifestFactory.CreateManifestAsync(mapDetails, discoveredItem.SourceUrl);

            _logger.LogInformation(
                "Successfully resolved ModDB content: {ManifestId} - {Name}",
                manifest.Id.Value,
                manifest.Name);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while resolving mod details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to fetch content: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve mod details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts the primary download URL from the parsed page.
    /// </summary>
    private static string? ExtractPrimaryDownloadUrl(ParsedWebPage parsedPage)
    {
        // Look for the first file section with a download URL
        var fileSection = parsedPage.Sections.OfType<File>().FirstOrDefault(f => !string.IsNullOrEmpty(f.DownloadUrl));
        return fileSection?.DownloadUrl;
    }

    /// <summary>
    /// Converts a parsed web page to MapDetails for the manifest factory.
    /// </summary>
    private static MapDetails ConvertToMapDetails(
        ParsedWebPage parsedPage,
        ContentSearchResult discoveredItem,
        string primaryDownloadUrl)
    {
        var context = parsedPage.Context;

        // Extract screenshots from image sections
        var screenshots = parsedPage.Sections.OfType<Image>()
            .Where(img => !string.IsNullOrEmpty(img.FullSizeUrl))
            .Select(img => img.FullSizeUrl!)
            .ToList();

        // Get file size from the first file section
        var fileSize = parsedPage.Sections.OfType<File>()
            .FirstOrDefault(f => f.SizeBytes.HasValue)
            ?.SizeBytes ?? 0;

        // Use submission date from context or fallback to current date
        var submissionDate = context.ReleaseDate ?? DateTime.UtcNow;

        // Use preview image from context or discovered item
        var previewImage = context.IconUrl ?? discoveredItem.IconUrl ?? string.Empty;

        // Use description from context or discovered item
        var description = context.Description ?? discoveredItem.Description ?? string.Empty;

        // Use author from context or discovered item
        var author = context.Developer ?? discoveredItem.AuthorName ?? "unknown";

        // Use name from context or discovered item
        var name = context.Title ?? discoveredItem.Name;

        // Use content type from discovered item
        var contentType = discoveredItem.ContentType;

        // Use target game from discovered item
        var targetGame = discoveredItem.TargetGame;

        // Extract additional files from the parsed page
        var allFiles = parsedPage.Sections.OfType<File>().ToList();

        // Note: Tags would need to be extracted from the parsed page
        // For now, keep existing tags from the discovered item
        return new MapDetails(
            Name: name,
            Description: description,
            Author: author,
            PreviewImage: previewImage,
            Screenshots: screenshots,
            FileSize: fileSize,
            DownloadCount: 0, // Would need to extract from page
            SubmissionDate: submissionDate,
            DownloadUrl: primaryDownloadUrl,
            TargetGame: targetGame,
            ContentType: contentType,
            AdditionalFiles: allFiles);
    }
}
