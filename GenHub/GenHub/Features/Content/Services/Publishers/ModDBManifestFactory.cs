using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.ModDB;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;
using Slugify;
using MapDetails = GenHub.Core.Models.ModDB.MapDetails;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Factory for creating ModDB content manifests from parsed content details.
/// Generates manifest IDs following the format: 1.YYYYMMDD.moddb.{contentType}.{contentName}.
/// Uses ManifestIdGenerator with release date for unique versioning.
/// </summary>
public partial class ModDBManifestFactory(
    Func<IContentManifestBuilder> manifestBuilderFactory,
    IProviderDefinitionLoader providerLoader,
    ICasService casService,
    IConfigurationProviderService configurationProvider,
    IHttpClientFactory httpClientFactory,
    IPlaywrightService playwrightService,
    IFileHashProvider hashProvider,
    IArchivePayloadProcessor archivePayloadProcessor,
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
            ContentType.Executable => true,
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
        logger.LogInformation("Processing ModDB extracted content from: {Directory}", extractedDirectory);

        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogWarning("Extracted directory does not exist: {Directory}", extractedDirectory);
            return [];
        }

        // Playwright saves a download to the requested destination path. ModDB's redirect often
        // omits the filename extension, so archive detection must use its signature rather than
        // relying on a .zip suffix.
        var stagedPayloads = originalManifest.Files
            .Select(file => Path.Combine(extractedDirectory, file.RelativePath))
            .Where(File.Exists)
            .ToArray();

        await archivePayloadProcessor.ProcessPayloadAsync(
            extractedDirectory,
            originalManifest.ContentType,
            originalManifest.TargetGame,
            cancellationToken);

        // A ModDB /start route occasionally gives Playwright only the display title, so an
        // archive may arrive with no usable extension. It must either be recognised by its
        // signature and extracted above or fail here; storing an opaque transport artifact in
        // CAS produces a manifest that cannot be installed into a profile.
        var unresolvedPayload = stagedPayloads.FirstOrDefault(path =>
            File.Exists(path) && !HasUsableExtension(path) && !IsSupportedArchive(path));
        if (unresolvedPayload != null)
        {
            throw new InvalidDataException(
                $"ModDB returned an extensionless non-archive payload '{Path.GetFileName(unresolvedPayload)}'. " +
                "The download was not stored because its installable format could not be identified.");
        }

        var allFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        var files = new List<ManifestFile>();
        foreach (var filePath in allFiles)
        {
            var fileInfo = new FileInfo(filePath);
            var relativePath = Path.GetRelativePath(extractedDirectory, filePath);
            files.Add(new ManifestFile
            {
                RelativePath = relativePath,
                SourceType = ContentSourceType.ContentAddressable,
                InstallTarget = originalManifest.ContentType is ContentType.Map or ContentType.MapPack
                    ? ContentInstallTarget.UserMapsDirectory
                    : ContentInstallTarget.Workspace,
                Size = fileInfo.Length,
                Hash = await hashProvider.ComputeFileHashAsync(filePath, cancellationToken),
                IsExecutable = ExecutableFileClassifier.RequiresExecutePermission(relativePath, filePath),
                IsRequired = true,
            });
        }

        if (files.Count == 0)
        {
            throw new InvalidDataException("ModDB download did not produce any usable files.");
        }

        if (files.Count == 1 &&
            originalManifest.ContentType is ContentType.Mod or ContentType.Patch or ContentType.Addon &&
            files[0].RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            (files[0].RelativePath.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
             files[0].RelativePath.Contains("install", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"ModDB download produced only an unextracted installer executable '{files[0].RelativePath}' for a {originalManifest.ContentType}. " +
                "The installer archive could not be unpacked into valid game modification files.");
        }

        return
        [
            new ContentManifest
            {
                SchemaVersion = originalManifest.SchemaVersion,
                Id = originalManifest.Id,
                Name = originalManifest.Name,
                Version = originalManifest.Version,
                ContentType = originalManifest.ContentType,
                TargetGame = originalManifest.TargetGame,
                Publisher = originalManifest.Publisher,
                Metadata = originalManifest.Metadata,
                OriginalProviderName = originalManifest.OriginalProviderName,
                OriginalContentId = originalManifest.OriginalContentId,
                SourcePath = originalManifest.SourcePath,
                Dependencies = originalManifest.Dependencies,
                ContentReferences = originalManifest.ContentReferences,
                KnownAddons = originalManifest.KnownAddons,
                Files = files,
                Variants = originalManifest.Variants,
                EntryPoint = originalManifest.EntryPoint,
                RequiredDirectories = originalManifest.RequiredDirectories,
                InstallationInstructions = originalManifest.InstallationInstructions,
            },
        ];
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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A fully constructed ContentManifest.</returns>
    public async Task<ContentManifest> CreateManifestAsync(MapDetails details, string detailPageUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(details);

        if (string.IsNullOrWhiteSpace(details.DownloadUrl))
        {
            throw new ArgumentException("Download URL is required to create a manifest", nameof(details));
        }

        // Fresh builder per operation: the shared builder's internal state is never reset, so a
        // reused singleton would accumulate files/dependencies across calls.
        var manifestBuilder = manifestBuilderFactory();

        // 1. Normalize author for publisher ID
        var normalizedAuthor = NormalizeAuthorForPublisherId(details.Author);
        var publisherId = $"{ModDBConstants.PublisherPrefix}-{normalizedAuthor}";

        // 2. Slugify content name
        var contentName = SlugifyTitle(details.Name);

        // 3. Use release date for manifest ID generation
        // Format: 1.YYYYMMDD.moddb.{contentType}.{contentName}
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
            releaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        // 5. Build manifest using the pre-generated manifest ID
        var provider = providerLoader.GetProvider(ModDBConstants.PublisherPrefix);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? ModDBConstants.PublisherWebsite;
        var publisherName = string.Format(CultureInfo.InvariantCulture, ModDBConstants.PublisherNameFormat, details.Author);
        var supportUrl = !string.IsNullOrWhiteSpace(detailPageUrl) ? detailPageUrl : (provider?.Endpoints.SupportUrl ?? websiteUrl);

        // Format release date as YYYYMMDD for the manifest version
        var releaseDateVersion = releaseDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

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

        // 7. Describe the remote archives. Delivery is intentionally deferred to Stage 2,
        // where the shared HTTP deliverer can place the downloaded file in staging and the
        // factory can extract it before validation and CAS storage.
        var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var primaryFileName = BuildPrimaryFileName(details);
        await manifest.AddRemoteFileAsync(primaryFileName, details.DownloadUrl);
        addedUrls.Add(details.DownloadUrl);

        // Add any additional files discovered on the page (e.g. patches, mirrors, addons)
        if (details.AdditionalFiles != null)
        {
            foreach (var file in details.AdditionalFiles)
            {
                if (string.IsNullOrEmpty(file.DownloadUrl) || addedUrls.Contains(file.DownloadUrl))
                    continue;

                var fileName = SanitizeFileName(file.Name ?? ModDBConstants.DefaultDownloadFilename);
                await manifest.AddRemoteFileAsync(fileName, file.DownloadUrl);
                addedUrls.Add(file.DownloadUrl);
            }
        }

        logger.LogInformation("{Count} remote file(s) added to the ModDB manifest for staged delivery", addedUrls.Count);

        // 8. Add dependencies based on target game
        manifest = AddGameDependencies(manifest, details.TargetGame);

        var builtManifest = manifest.Build();

        // Override the manifest ID with our pre-generated ID that uses the release date
        // This ensures the ID matches the format: 1.YYYYMMDD.moddb.{contentType}.{contentName}
        builtManifest.Id = ManifestId.Create(manifestId);
        builtManifest.OriginalProviderName = ModDBConstants.DiscovererSourceName;
        builtManifest.OriginalContentId = !string.IsNullOrWhiteSpace(detailPageUrl) ? detailPageUrl : manifestId;

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
            // Type-only constraint: any platform's ZH installation satisfies this.
            builder.AddDependency(
                id: ManifestId.Create("1.104.any.gameinstallation.zerohour"),
                name: "Zero Hour Installation",
                dependencyType: ContentType.GameInstallation,
                installBehavior: DependencyInstallBehavior.RequireExisting,
                minVersion: ManifestConstants.ZeroHourManifestVersion);
        }
        else if (targetGame == GameType.Generals)
        {
            // Type-only constraint: any platform's Generals installation satisfies this.
            builder.AddDependency(
                id: ManifestId.Create("1.108.any.gameinstallation.generals"),
                name: "Generals Installation",
                dependencyType: ContentType.GameInstallation,
                installBehavior: DependencyInstallBehavior.RequireExisting,
                minVersion: ManifestConstants.GeneralsManifestVersion);
        }

        return builder;
    }

    /// <summary>
    /// Sanitizes a filename by removing invalid characters.
    /// </summary>
    /// <param name="fileName">The filename to sanitize.</param>
    /// <returns>A sanitized filename.</returns>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ModDBConstants.DefaultDownloadFilename;
        }

        // Remove invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Ensure the filename has an extension
        if (!Path.HasExtension(sanitized))
        {
            sanitized += ".zip"; // Default to .zip for ModDB downloads
        }

        return sanitized;
    }

    /// <summary>
    /// Builds the primary archive filename for a ModDB download, normalizing the parsed file type
    /// into a conventional extension before it reaches staging.
    /// </summary>
    /// <param name="details">The parsed ModDB content details.</param>
    /// <returns>The sanitized filename with a restricted extension.</returns>
    private static string BuildPrimaryFileName(MapDetails details)
    {
        var fileName = SanitizeFileName(details.Name);
        var extension = details.FileType?.Trim() ?? string.Empty;
        if (extension.Length == 0)
        {
            return fileName;
        }

        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        // FileType comes from the parsed ModDB filename. Restrict it to a conventional extension
        // before putting it on a staging path; the archive signature remains authoritative later.
        if (extension.Length > 12 || extension.Skip(1).Any(character => !char.IsLetterOrDigit(character)))
        {
            return fileName;
        }

        return Path.ChangeExtension(fileName, extension);
    }

    private static bool HasUsableExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Length > 1 && extension.All(character => character == '.' || char.IsLetterOrDigit(character));
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

    /// <summary>
    /// Follows the download URL redirect to extract the actual filename.
    /// </summary>
    private async Task<string> ResolveActualFilenameAsync(string downloadUrl, string fallbackName, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(ModDBConstants.PublisherPrefix);
            using var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // Generate final URL after redirects if any
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? downloadUrl;

            // Check Content-Disposition first
            if (response.Content.Headers.ContentDisposition != null)
            {
                var contentDisposition = response.Content.Headers.ContentDisposition;
                var filename = contentDisposition.FileNameStar ?? contentDisposition.FileName;

                if (!string.IsNullOrEmpty(filename))
                {
                    filename = filename.Trim('"');
                    return SanitizeFileName(filename);
                }
            }

            // Fallback to ExtractFileNameFromUrl with final URL
            string extracted = ExtractFileNameFromUrl(finalUrl);
            if (!string.Equals(extracted, ModDBConstants.DefaultDownloadFilename, StringComparison.OrdinalIgnoreCase))
            {
                return SanitizeFileName(extracted);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve actual filename for {Url}. Using fallback.", downloadUrl);
        }

        // Final fallback: Use the sanitized fallback name
        return !string.IsNullOrEmpty(fallbackName) && fallbackName != ModDBConstants.DefaultContentName
            ? SanitizeFileName(fallbackName)
            : ModDBConstants.DefaultDownloadFilename;
    }

    /// <summary>
    /// Downloads a file, stores it in CAS, and adds it to the manifest with computed hash.
    /// </summary>
    /// <param name="builder">The manifest builder.</param>
    /// <param name="relativePath">The relative path for the file in the manifest.</param>
    /// <param name="downloadUrl">The URL to download from.</param>
    /// <param name="refererUrl">Optional referer URL for the download request.</param>
    private async Task DownloadAndAddFileAsync(
        IContentManifestBuilder builder,
        string relativePath,
        string downloadUrl,
        string? refererUrl)
    {
        logger.LogInformation(
            "Starting download: URL={Url}, Filename={Filename}, Referer={Referer}",
            downloadUrl,
            relativePath,
            refererUrl ?? "(none)");

        var tempDir = Path.Combine(configurationProvider.GetApplicationDataPath(), DirectoryNames.Temp);
        if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

        var tempFilePath = Path.Combine(tempDir, $"{Guid.NewGuid()}{Path.GetExtension(relativePath)}");

        var downloadConfig = new DownloadConfiguration
        {
            Url = new Uri(downloadUrl),
            DestinationPath = tempFilePath,
            OverwriteExisting = true,
        };

        if (!string.IsNullOrEmpty(refererUrl))
        {
            downloadConfig.Headers.Add("Referer", refererUrl);
            logger.LogInformation("Added Referer header: {Referer}", refererUrl);
        }

        // ModDB is Cloudflare-protected. The persistent browser profile contains the user's
        // clearance cookie, so downloading through it is required; raw HTTP cannot reuse it.
        logger.LogInformation("Initiating protected ModDB download to temp file: {TempPath}", tempFilePath);
        var downloadResult = await playwrightService.DownloadFileAsync(downloadConfig);
        if (!downloadResult.Success)
        {
            logger.LogError("Failed to download file from {DownloadUrl}: {Error}", downloadUrl, downloadResult.FirstError);
            var error = $"Failed to download file from {downloadUrl}: {downloadResult.FirstError}";
            throw new InvalidOperationException(error);
        }

        logger.LogInformation("Download completed, file size: {Size} bytes", new FileInfo(tempFilePath).Length);

        // Store in CAS
        var storeResult = await casService.StoreContentAsync(tempFilePath, ContentType.Mod);
        if (!storeResult.Success)
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            logger.LogError("Failed to store content in CAS: {Error}", storeResult.FirstError);
            var error = $"Failed to store content in CAS: {storeResult.FirstError}";
            throw new InvalidOperationException(error);
        }

        var hash = storeResult.Data;
        var fileSize = new FileInfo(tempFilePath).Length;

        logger.LogInformation("Content stored in CAS with hash: {Hash}, size: {Size}", hash, fileSize);

        // Cleanup temp file after successful store
        if (File.Exists(tempFilePath)) File.Delete(tempFilePath);

        await builder.AddContentAddressableFileAsync(relativePath, hash, fileSize);
        logger.LogInformation("Added content-addressable file to manifest: {RelativePath}", relativePath);
    }

    private bool IsSupportedArchive(string filePath)
    {
        try
        {
            return ArchiveFactory.IsArchive(filePath, out _);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to check if {FilePath} is a supported archive", filePath);
            return false;
        }
    }
}
