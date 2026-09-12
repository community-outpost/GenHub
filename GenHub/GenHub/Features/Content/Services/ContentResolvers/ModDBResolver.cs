using System;
using System.Collections.Generic;
using System.IO;
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
    ModDBManifestFactory manifestFactory,
    ModDBPageParser webPageParser,
    ILogger<ModDBResolver> logger) : IContentResolver
{
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
            logger.LogInformation("Resolving ModDB content from {Url}", discoveredItem.SourceUrl);

            var parsedPage = await EnsureParsedPageAsync(discoveredItem, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var allFiles = parsedPage.Sections.OfType<DownloadableFile>()
                .OrderByDescending(GetFileFormatPriority)
                .ThenByDescending(file => file.ReleaseDate ?? file.UploadDate ?? DateTime.MinValue)
                .ThenByDescending(file => file.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (allFiles.Count == 0)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    "ModDB is blocking automated access from this machine, so the download link could not be retrieved. Use 'View on Website' to download it in your browser.");
            }

            var primaryFile = SelectPrimaryFile(allFiles, discoveredItem);
            if (primaryFile == null)
            {
                return OperationResult<ContentManifest>.CreateFailure("The selected ModDB download is no longer available on the content page.");
            }

            primaryFile = await ResolveDetailedPrimaryFileAsync(primaryFile, discoveredItem, cancellationToken);

            var mapDetails = ConvertFileToMapDetails(primaryFile, parsedPage, discoveredItem);
            var manifest = await manifestFactory.CreateManifestAsync(mapDetails, discoveredItem.SourceUrl, cancellationToken);

            ApplyManifestTags(manifest, primaryFile);

            logger.LogInformation(
                "Successfully resolved ModDB content: {ManifestId} - {Name} (Section: {Section}, ReleaseDate: {ReleaseDate})",
                manifest.Id.Value,
                manifest.Name,
                primaryFile.FileSectionType,
                primaryFile.ReleaseDate?.ToString("yyyy-MM-dd") ?? "unknown");

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error while resolving mod details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to fetch content: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve mod details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Computes the format priority for automated workspace reconciliation.
    /// Raw archive packages (.zip, .7z, .rar) are prioritized over executable installers (.exe, .msi).
    /// </summary>
    private static int GetFileFormatPriority(DownloadableFile file)
    {
        var name = file.Name ?? string.Empty;
        var fileName = file.Filename ?? string.Empty;

        // Archive releases are highest priority for automated extraction and hardlinking
        if (name.Contains("(Archive)", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("[Archive]", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        // Setup wizards / executable installers are lowest priority
        if (name.Contains("(Setup)", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("[Setup]", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return 1;
    }

    private static DownloadableFile? SelectPrimaryFile(
        List<DownloadableFile> allFiles,
        ContentSearchResult discoveredItem)
    {
        DownloadableFile? primaryFile = null;

        if (!string.IsNullOrWhiteSpace(discoveredItem.SelectedDownloadUrl))
        {
            primaryFile = allFiles.FirstOrDefault(file => string.Equals(
                file.DownloadUrl,
                discoveredItem.SelectedDownloadUrl,
                StringComparison.OrdinalIgnoreCase));

            primaryFile ??= allFiles.FirstOrDefault(file => !string.IsNullOrEmpty(file.DetailsUrl) && string.Equals(
                file.DetailsUrl,
                discoveredItem.SelectedDownloadUrl,
                StringComparison.OrdinalIgnoreCase));

            if (primaryFile == null && !string.IsNullOrWhiteSpace(discoveredItem.Name))
            {
                primaryFile = allFiles.FirstOrDefault(file => string.Equals(
                    file.Name,
                    discoveredItem.Name,
                    StringComparison.OrdinalIgnoreCase));
            }
        }

        if (primaryFile != null)
        {
            return primaryFile;
        }

        var downloadFiles = allFiles.Where(file => file.FileSectionType == FileSectionType.Downloads).ToList();
        var candidates = downloadFiles.Count > 0 ? downloadFiles : allFiles;

        return candidates
            .OrderByDescending(GetFileFormatPriority)
            .ThenByDescending(file => file.ReleaseDate ?? file.UploadDate ?? DateTime.MinValue)
            .ThenByDescending(file => file.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void ApplyManifestTags(ContentManifest manifest, DownloadableFile primaryFile)
    {
        if (manifest.Metadata == null || !primaryFile.ReleaseDate.HasValue)
        {
            return;
        }

        var releaseDateTag = $"release-date:{primaryFile.ReleaseDate.Value:yyyy-MM-dd}";
        if (!manifest.Metadata.Tags.Contains(releaseDateTag))
        {
            manifest.Metadata.Tags.Add(releaseDateTag);
        }

        var sectionTypeTag = $"section:{primaryFile.FileSectionType.ToString().ToLowerInvariant()}";
        if (!manifest.Metadata.Tags.Contains(sectionTypeTag))
        {
            manifest.Metadata.Tags.Add(sectionTypeTag);
        }
    }

    /// <summary>
    /// Converts a single file from the parsed page to MapDetails for the manifest factory.
    /// Uses the file's release date and FileSectionType to create unique manifest IDs.
    /// </summary>
    private static MapDetails ConvertFileToMapDetails(
        DownloadableFile file,
        ParsedWebPage parsedPage,
        ContentSearchResult discoveredItem)
    {
        var context = parsedPage.Context;

        // Extract screenshots from image sections
        var screenshots = parsedPage.Sections.OfType<Image>()
            .Where(img => !string.IsNullOrEmpty(img.FullSizeUrl))
            .Select(img => img.FullSizeUrl)
            .OfType<string>()
            .ToList();

        // Use file's release date or fallback to context release date or current date
        var releaseDate = file.ReleaseDate ?? file.UploadDate ?? context.ReleaseDate ?? DateTime.UtcNow;

        // Use preview image from context or discovered item
        var previewImage = context.IconUrl ?? discoveredItem.IconUrl ?? string.Empty;

        // Use description from context or discovered item
        var description = context.Description ?? discoveredItem.Description ?? string.Empty;

        // Use author from context or discovered item
        var author = context.Developer ?? discoveredItem.AuthorName ?? "unknown";

        // The page title is the user-facing content name. The file name is retained separately
        // as FileType's extension so the manifest can stage the real archive name (for example
        // Improved_AI_1.2.rar) instead of a display title with a misleading/no extension.
        var name = !string.IsNullOrWhiteSpace(discoveredItem.Name)
            ? discoveredItem.Name
            : context.Title ?? file.Name ?? "ModDB content";
        var fileExtension = Path.GetExtension(file.Filename ?? file.Name ?? string.Empty);
        if (fileExtension.Length == 0 || fileExtension.Any(char.IsWhiteSpace))
        {
            fileExtension = string.Empty;
        }

        // A ModDB addons list also contains map files. Its file category is more precise than
        // the parent page's type, so use it when available and only fall back to that parent type
        // when ModDB did not supply a category.
        var contentType = discoveredItem.ContentType;
        if (!string.IsNullOrWhiteSpace(file.Category))
        {
            contentType = ModDBCategoryMapper.MapCategoryByName(file.Category);
        }
        else if (file.FileSectionType == FileSectionType.Downloads)
        {
            contentType = ContentType.Mod;
        }

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
            FileType: fileExtension,
            AdditionalFiles: null);
    }

    private async Task<ParsedWebPage> EnsureParsedPageAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken)
    {
        var parsedPage = discoveredItem.ParsedPageData ?? discoveredItem.GetData<ParsedWebPage>();
        if (parsedPage != null)
        {
            return parsedPage;
        }

        var sourceUrl = discoveredItem.SourceUrl ?? string.Empty;
        var isFileDetail = sourceUrl.Contains("/mods/", StringComparison.OrdinalIgnoreCase)
            && (sourceUrl.Contains("/downloads/", StringComparison.OrdinalIgnoreCase)
                || sourceUrl.Contains("/addons/", StringComparison.OrdinalIgnoreCase));

        parsedPage = isFileDetail
            ? await webPageParser.ParseFileDetailAsync(sourceUrl, cancellationToken)
            : await webPageParser.ParseAsync(sourceUrl, cancellationToken);

        discoveredItem.ParsedPageData = parsedPage;
        discoveredItem.SetData(parsedPage);
        return parsedPage;
    }

    private async Task<DownloadableFile> ResolveDetailedPrimaryFileAsync(
        DownloadableFile primaryFile,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(discoveredItem.SelectedDownloadUrl) &&
            ModDBPageParser.IsDirectDownloadUrl(discoveredItem.SelectedDownloadUrl))
        {
            primaryFile = primaryFile with { DownloadUrl = discoveredItem.SelectedDownloadUrl };
        }

        if (!ModDBPageParser.IsDirectDownloadUrl(primaryFile.DownloadUrl))
        {
            var detailUrl = primaryFile.DetailsUrl ?? primaryFile.DownloadUrl;
            if (!string.IsNullOrWhiteSpace(detailUrl))
            {
                logger.LogInformation("Resolving ModDB file detail for {Name} from {Url}", primaryFile.Name, detailUrl);
                var detailPage = await webPageParser.ParseFileDetailAsync(detailUrl, cancellationToken);
                var detailedFile = detailPage?.Sections?.OfType<DownloadableFile>().FirstOrDefault();
                if (detailedFile != null)
                {
                    primaryFile = detailedFile;
                }
            }
        }

        return primaryFile;
    }
}
