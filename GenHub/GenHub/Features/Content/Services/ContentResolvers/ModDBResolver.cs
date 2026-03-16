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
/// Creates separate manifest items for releases and addons based on FileSectionType.
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
        // [TEMP] DEBUG: ResolveAsync entry point
        _logger.LogInformation(
            "[TEMP] ModDBResolver.ResolveAsync called - Item: {Name}, SourceUrl: {Url}",
            discoveredItem?.Name,
            discoveredItem?.SourceUrl);

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

            // Extract all files from the parsed page
            var allFiles = parsedPage.Sections.OfType<File>().ToList();

            if (allFiles.Count == 0)
            {
                return OperationResult<ContentManifest>.CreateFailure("No files found in mod details");
            }

            // Create separate manifests for each file based on FileSectionType and release date
            // For now, return the first file's manifest (primary file from Downloads section)
            // In the future, this could be enhanced to return all manifests or let the user choose
            var primaryFile = allFiles
                .Where(f => f.FileSectionType == FileSectionType.Downloads)
                .FirstOrDefault() ?? allFiles.First();

            // Convert the file to MapDetails for the manifest factory
            var mapDetails = ConvertFileToMapDetails(primaryFile, parsedPage, discoveredItem);

            // Use the factory to create the manifest
            var manifest = await _manifestFactory.CreateManifestAsync(mapDetails, discoveredItem.SourceUrl);

            // Store file section type and release date in metadata for UI filtering
            if (manifest.Metadata != null && primaryFile.ReleaseDate.HasValue)
            {
                // Add release date as a tag for filtering
                var releaseDateTag = $"release-date:{primaryFile.ReleaseDate.Value:yyyy-MM-dd}";
                if (!manifest.Metadata.Tags.Contains(releaseDateTag))
                {
                    manifest.Metadata.Tags.Add(releaseDateTag);
                }

                // Add file section type as a tag for filtering
                var sectionTypeTag = $"section:{primaryFile.FileSectionType.ToString().ToLowerInvariant()}";
                if (!manifest.Metadata.Tags.Contains(sectionTypeTag))
                {
                    manifest.Metadata.Tags.Add(sectionTypeTag);
                }
            }

            _logger.LogInformation(
                "Successfully resolved ModDB content: {ManifestId} - {Name} (Section: {Section}, ReleaseDate: {ReleaseDate})",
                manifest.Id.Value,
                manifest.Name,
                primaryFile.FileSectionType,
                primaryFile.ReleaseDate?.ToString("yyyy-MM-dd") ?? "unknown");

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
    /// Converts a single file from the parsed page to MapDetails for the manifest factory.
    /// Uses the file's release date and FileSectionType to create unique manifest IDs.
    /// </summary>
    private static MapDetails ConvertFileToMapDetails(
        File file,
        ParsedWebPage parsedPage,
        ContentSearchResult discoveredItem)
    {
        var context = parsedPage.Context;

        // Extract screenshots from image sections
        var screenshots = parsedPage.Sections.OfType<Image>()
            .Where(img => !string.IsNullOrEmpty(img.FullSizeUrl))
            .Select(img => img.FullSizeUrl!)
            .ToList();

        // Use file's release date or fallback to context release date or current date
        var releaseDate = file.ReleaseDate ?? file.UploadDate ?? context.ReleaseDate ?? DateTime.UtcNow;

        // Use preview image from context or discovered item
        var previewImage = context.IconUrl ?? discoveredItem.IconUrl ?? string.Empty;

        // Use description from context or discovered item
        var description = context.Description ?? discoveredItem.Description ?? string.Empty;

        // Use author from context or discovered item
        var author = context.Developer ?? discoveredItem.AuthorName ?? "unknown";

        // Use file name or context name or discovered item name
        var name = file.Name ?? context.Title ?? discoveredItem.Name;

        // Determine content type based on FileSectionType
        // Downloads section -> Mod, Addons section -> Addon
        var contentType = file.FileSectionType == FileSectionType.Addons
            ? ContentType.Addon
            : discoveredItem.ContentType;

        // Use target game from discovered item
        var targetGame = discoveredItem.TargetGame;

        // Use file size from the file
        var fileSize = file.SizeBytes ?? 0;

        // No additional files - each file gets its own manifest
        return new MapDetails(
            Name: name,
            Description: description,
            Author: author,
            PreviewImage: previewImage,
            Screenshots: screenshots,
            FileSize: fileSize,
            DownloadCount: 0, // Would need to extract from page
            SubmissionDate: releaseDate,
            DownloadUrl: file.DownloadUrl ?? string.Empty,
            TargetGame: targetGame,
            ContentType: contentType,
            AdditionalFiles: null);
    }
}
