using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.ModDB;
using Microsoft.Extensions.Logging;
using Slugify;
using MapDetails = GenHub.Core.Models.ModDB.MapDetails;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Factory for creating ModDB content manifests from parsed content details.
/// Generates manifest IDs following the format: 1.YYYYMMDD.moddb.{contentType}.{contentName}.
/// Uses ManifestIdGenerator with release date for unique versioning.
/// </summary>
public partial class ModDBManifestFactory(
    IContentManifestBuilder manifestBuilder,
    IProviderDefinitionLoader providerLoader,
    ILogger<ModDBManifestFactory> logger) : IPublisherManifestFactory
{
    /// <inheritdoc />
    public string PublisherId => ModDBConstants.PublisherPrefix;

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        // ModDB publishes many content types
        var publisherMatches = manifest.Publisher?.PublisherType?.StartsWith(ModDBConstants.PublisherPrefix, StringComparison.OrdinalIgnoreCase) == true;

        var supportedTypes = manifest.ContentType switch
        {
            ContentType.Mod => true,
            ContentType.Patch => true,
            ContentType.Map => true,
            ContentType.MapPack => true,
            ContentType.Skin => true,
            ContentType.Video => true,
            ContentType.ModdingTool => true,
            ContentType.LanguagePack => true,
            ContentType.Addon => true,
            _ => false,
        };

        return publisherMatches && supportedTypes;
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        // ModDB content is typically delivered as-is from downloads
        // This method can be enhanced later for multi-variant content if needed
        logger.LogInformation("Processing ModDB extracted content from: {Directory}", extractedDirectory);

        // For now, return the original manifest
        // Future enhancement: scan extracted directory for additional metadata or variants
        return await Task.FromResult<List<ContentManifest>>([originalManifest]);
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        // ModDB content is delivered directly to the target directory
        return extractedDirectory;
    }

    /// <summary>
    /// Creates a content manifest from ModDB content details.
    /// Uses the file's release date to generate a unique manifest ID.
    /// </summary>
    /// <param name="details">The parsed ModDB content details.</param>
    /// <param name="detailPageUrl">The detail page URL.</param>
    /// <returns>A fully constructed ContentManifest.</returns>
    public async Task<ContentManifest> CreateManifestAsync(MapDetails details, string detailPageUrl)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (string.IsNullOrWhiteSpace(details.DownloadUrl))
        {
            throw new ArgumentException("Download URL is required to create a manifest", nameof(details));
        }

        // 1. Normalize author for publisher ID
        var normalizedAuthor = NormalizeAuthorForPublisherId(details.Author);
        var publisherId = $"{ModDBConstants.PublisherPrefix}-{normalizedAuthor}";

        // 2. Slugify content name
        var contentName = SlugifyTitle(details.Name);

        // 3. Use release date for manifest ID generation
        // Format: 1.YYYYMMDD.moddb-{author}.{contentType}.{contentName}
        var releaseDate = details.SubmissionDate;

        // 4. Generate manifest ID with release date using ManifestIdGenerator
        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(
            "moddb",
            details.ContentType,
            contentName,
            releaseDate);

        logger.LogInformation(
            "Creating ModDB manifest: ID={ManifestId}, Name={Name}, Author={Author}, Type={ContentType}, ReleaseDate={Date}",
            manifestId,
            details.Name,
            details.Author,
            details.ContentType,
            releaseDate.ToString("yyyy-MM-dd"));

        // 5. Build manifest using the pre-generated manifest ID
        var provider = providerLoader.GetProvider(ModDBConstants.PublisherPrefix);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? ModDBConstants.PublisherWebsite;
        var publisherName = string.Format(System.Globalization.CultureInfo.InvariantCulture, ModDBConstants.PublisherNameFormat, details.Author);
        var supportUrl = provider?.Endpoints.SupportUrl ?? detailPageUrl;

        // Format release date as YYYYMMDD for the manifest version
        var releaseDateVersion = releaseDate.ToString("yyyyMMdd");

        var manifest = manifestBuilder
            .WithBasicInfo(publisherId, details.Name, releaseDateVersion)
            .WithContentType(details.ContentType, details.TargetGame)
            .WithPublisher(
                name: publisherName,
                website: websiteUrl,
                supportUrl: supportUrl,
                publisherType: publisherId)
            .WithMetadata(
                description: details.Description,
                tags: [.. GetTags(details)],
                iconUrl: details.PreviewImage,
                screenshotUrls: details.Screenshots ?? []);

        // 6. Add custom metadata
        manifest = AddCustomMetadata(manifest);

        // 7. Add the download files
        var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add primary file
        var primaryFileName = ExtractFileNameFromUrl(details.DownloadUrl);
        logger.LogInformation("[TEMP] ModDBManifestFactory - Adding primary file: {FileName} from URL: {Url}", primaryFileName, details.DownloadUrl);

        manifest = await manifest.AddRemoteFileAsync(
            primaryFileName,
            details.DownloadUrl,
            ContentSourceType.ContentAddressable,
            isExecutable: false,
            permissions: null);

        addedUrls.Add(details.DownloadUrl);

        // Add any additional files discovered on the page (e.g. patches, mirrors, addons)
        if (details.AdditionalFiles != null)
        {
            foreach (var file in details.AdditionalFiles)
            {
                if (string.IsNullOrEmpty(file.DownloadUrl) || addedUrls.Contains(file.DownloadUrl))
                    continue;

                var fileName = !string.IsNullOrEmpty(file.Name) ? file.Name : ExtractFileNameFromUrl(file.DownloadUrl);

                logger.LogInformation("[TEMP] ModDBManifestFactory - Adding additional file: {FileName} from URL: {Url}", fileName, file.DownloadUrl);

                manifest = await manifest.AddRemoteFileAsync(
                    fileName,
                    file.DownloadUrl,
                    ContentSourceType.ContentAddressable,
                    isExecutable: false,
                    permissions: null);

                addedUrls.Add(file.DownloadUrl);
            }
        }

        logger.LogInformation("[TEMP] ModDBManifestFactory - {Count} total files added to manifest with CAS storage", addedUrls.Count);

        // 8. Add dependencies based on target game
        manifest = AddGameDependencies(manifest, details.TargetGame);

        var builtManifest = manifest.Build();

        // Override the manifest ID with our pre-generated ID that uses the release date
        // This ensures the ID matches the format: 1.YYYYMMDD.moddb.{contentType}.{contentName}
        builtManifest.Id = ManifestId.Create(manifestId);

        return builtManifest;
    }

    /// <summary>
    /// Normalizes an author name for use in a publisher ID.
    /// Removes special characters, converts to lowercase.
    /// </summary>
    /// <param name="author">The raw author name.</param>
    /// <returns>A normalized publisher ID component.</returns>
    private static string NormalizeAuthorForPublisherId(string author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return ModDBConstants.DefaultAuthor;
        }

        // Remove all non-alphanumeric characters and convert to lowercase
        // Using Slugify to normalize the author name
        var slugHelper = new SlugHelper();
        var normalized = slugHelper.GenerateSlug(author).Replace("-", string.Empty);

        // If the result is empty after normalization, use default
        return string.IsNullOrEmpty(normalized) ? ModDBConstants.DefaultAuthor : normalized;
    }

    /// <summary>
    /// Converts a title into a URL-friendly slug.
    /// </summary>
    /// <param name="title">The content title.</param>
    /// <returns>A slugified version of the title.</returns>
    private static string SlugifyTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return ModDBConstants.DefaultContentName;
        }

        try
        {
            var slugHelper = new SlugHelper();
            var slug = slugHelper.GenerateSlug(title);
            return string.IsNullOrEmpty(slug) ? ModDBConstants.DefaultContentName : slug;
        }
        catch
        {
            // Fallback to default if slugification fails
            return ModDBConstants.DefaultContentName;
        }
    }

    /// <summary>
    /// Generates appropriate tags for ModDB content.
    /// </summary>
    /// <param name="details">The content details.</param>
    /// <returns>A list of tags.</returns>
    private static List<string> GetTags(MapDetails details)
    {
        List<string> tags = [.. ModDBConstants.Tags];

        // Add game-specific tag
        tags.Add(details.TargetGame == GameType.Generals ? GameClientConstants.GeneralsShortName : GameClientConstants.ZeroHourShortName);

        // Add content type tag
        tags.Add(details.ContentType switch
        {
            ContentType.Mod => ManifestConstants.ModTag,
            ContentType.Patch => ManifestConstants.PatchTag,
            ContentType.Map => ManifestConstants.MapTag,
            ContentType.MapPack => ManifestConstants.MapPackTag,
            ContentType.Skin => ManifestConstants.SkinTag,
            ContentType.Video => ManifestConstants.VideoTag,
            ContentType.ModdingTool => ManifestConstants.ModdingToolTag,
            ContentType.LanguagePack => ManifestConstants.LanguagePackTag,
            ContentType.Addon => ManifestConstants.AddonTag,
            _ => ManifestConstants.OtherTag,
        });

        // Add author tag
        if (!string.IsNullOrWhiteSpace(details.Author) && details.Author != ModDBConstants.DefaultAuthor)
        {
            tags.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, ModDBConstants.AuthorTagFormat, details.Author));
        }

        return tags;
    }

    /// <summary>
    /// Adds custom metadata fields specific to ModDB content.
    /// </summary>
    /// <param name="builder">The manifest builder.</param>
    /// <returns>The updated manifest builder.</returns>
    private static IContentManifestBuilder AddCustomMetadata(IContentManifestBuilder builder)
    {
        // Store ModDB-specific metadata in the manifest's custom metadata collection
        // This can be accessed later for display in UI or for special handling

        // Note: ContentManifest doesn't have a CustomMetadata dictionary exposed
        // If needed, this can store information in the description or tags
        // For now, this is a placeholder for future enhancement.
        return builder;
    }

    /// <summary>
    /// Adds game installation dependencies based on target game.
    /// </summary>
    /// <param name="builder">The manifest builder.</param>
    /// <param name="targetGame">The target game type.</param>
    /// <returns>The updated manifest builder.</returns>
    private static IContentManifestBuilder AddGameDependencies(IContentManifestBuilder builder, GameType targetGame)
    {
        // Add dependency on the appropriate game installation
        // Note: Using RequireExisting since game installations must already exist
        if (targetGame == GameType.ZeroHour)
        {
            // Zero Hour manifest ID: 1.104.ea.gameinstallation.zerohour
            builder.AddDependency(
                id: ManifestId.Create("1.104.ea.gameinstallation.zerohour"),
                name: "Zero Hour Installation",
                dependencyType: ContentType.GameInstallation,
                installBehavior: DependencyInstallBehavior.RequireExisting,
                minVersion: ManifestConstants.ZeroHourManifestVersion);
        }
        else if (targetGame == GameType.Generals)
        {
            // Generals manifest ID: 1.108.ea.gameinstallation.generals
            builder.AddDependency(
                id: ManifestId.Create("1.108.ea.gameinstallation.generals"),
                name: "Generals Installation",
                dependencyType: ContentType.GameInstallation,
                installBehavior: DependencyInstallBehavior.RequireExisting,
                minVersion: ManifestConstants.GeneralsManifestVersion);
        }

        return builder;
    }

    /// <summary>
    /// Extracts a filename from a download URL.
    /// </summary>
    /// <param name="downloadUrl">The download URL.</param>
    /// <returns>The extracted filename.</returns>
    private string ExtractFileNameFromUrl(string downloadUrl)
    {
        try
        {
            // Try to get filename from URL path
            var uri = new Uri(downloadUrl);
            var fileName = Path.GetFileName(uri.LocalPath);

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }
        catch (UriFormatException ex)
        {
            logger.LogWarning(ex, "Invalid download URL format: {Url}", downloadUrl);
        }

        // Fallback: generate a generic filename
        return ModDBConstants.DefaultDownloadFilename;
    }
}
