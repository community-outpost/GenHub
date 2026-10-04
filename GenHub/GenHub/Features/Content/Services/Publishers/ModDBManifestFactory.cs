using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.ModDB;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Dependencies;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.Common;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapDetails = GenHub.Core.Models.ModDB.MapDetails;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Factory for creating ModDB content manifests from parsed content details.
/// Generates manifest IDs following the format: 1.YYYYMMDD.moddb.{contentType}.{contentName}.
/// Uses ManifestIdGenerator with release date for unique versioning.
/// </summary>
public class ModDBManifestFactory(
    Func<IContentManifestBuilder> manifestBuilderFactory,
    IProviderDefinitionLoader providerLoader,
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
    public Task<OperationResult<List<ContentManifest>>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        return CreateManifestsFromExtractedContentAsync(originalManifest, extractedDirectory, progress: null, cancellationToken);
    }

    /// <summary>
    /// Creates enriched manifests from extracted content with optional progress reporting.
    /// </summary>
    /// <param name="originalManifest">The original manifest.</param>
    /// <param name="extractedDirectory">The directory where content was extracted.</param>
    /// <param name="progress">Progress reporter for tracking progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing a list of enriched content manifests.</returns>
    public async Task<OperationResult<List<ContentManifest>>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Processing ModDB extracted content from: {Directory}", extractedDirectory);

        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogWarning("Extracted directory does not exist: {Directory}", extractedDirectory);
            return OperationResult<List<ContentManifest>>.CreateSuccess([originalManifest]);
        }

        // Playwright saves a download to the requested destination path. ModDB's redirect often
        // omits the filename extension, so archive detection must use its signature rather than
        // relying on a .zip suffix.
        var stagedPayloads = ManifestVariantResolver.ResolveFiles(originalManifest)
            .Select(file => Path.Combine(extractedDirectory, file.RelativePath))
            .Where(File.Exists)
            .ToArray();

        await archivePayloadProcessor.ProcessPayloadAsync(
            extractedDirectory,
            originalManifest.ContentType,
            originalManifest.TargetGame,
            normalizeInactiveArchives: true,
            progress: progress,
            cancellationToken: cancellationToken);

        // A ModDB /start route occasionally gives Playwright only the display title, so an
        // archive may arrive with no usable extension. It must either be recognised by its
        // signature and extracted above or fail here; storing an opaque transport artifact in
        // CAS produces a manifest that cannot be installed into a profile.
        var unresolvedPayload = stagedPayloads.FirstOrDefault(path =>
            File.Exists(path) && !HasUsableExtension(path) && !IsSupportedArchive(path));
        if (unresolvedPayload != null)
        {
            return OperationResult<List<ContentManifest>>.CreateFailure(
                $"ModDB returned an extensionless non-archive payload '{Path.GetFileName(unresolvedPayload)}'. " +
                "The download was not stored because its installable format could not be identified.");
        }

        var allFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = new List<ManifestFile>();
        for (var i = 0; i < allFiles.Count; i++)
        {
            var filePath = allFiles[i];
            var fileInfo = new FileInfo(filePath);
            var relativePath = Path.GetRelativePath(extractedDirectory, filePath);
            var stageProgress = (double)(i + 1) / allFiles.Count * 100;
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                ProgressPercentage = stageProgress,
                CurrentStage = 3,
                TotalStages = 5,
                StageDescription = "Processing files",
                CurrentOperation = $"Hashing {relativePath} ({i + 1}/{allFiles.Count})",
                CurrentFile = relativePath,
                FilesProcessed = i + 1,
                TotalFiles = allFiles.Count,
                StageProgress = stageProgress,
            });

            logger.LogDebug("Hashing file {Current}/{Total}: {RelativePath}", i + 1, allFiles.Count, relativePath);

            files.Add(new ManifestFile
            {
                RelativePath = relativePath,
                SourceType = ContentSourceType.ExtractedPackage,
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
            return OperationResult<List<ContentManifest>>.CreateFailure("ModDB download did not produce any usable files.");
        }

        if (files.Count == 1 &&
            originalManifest.ContentType is ContentType.Mod or ContentType.Patch or ContentType.Addon &&
            files[0].RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            (files[0].RelativePath.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
             files[0].RelativePath.Contains("install", StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<List<ContentManifest>>.CreateFailure(
                $"ModDB download produced only an unextracted installer executable '{files[0].RelativePath}' for a {originalManifest.ContentType}. " +
                "The installer archive could not be unpacked into valid game modification files.");
        }

        return OperationResult<List<ContentManifest>>.CreateSuccess(
        [
            ManifestVariantResolver.CopyWithResolvedFiles(originalManifest, files),
        ]);
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
        var contentName = ManifestTagHelper.SlugifyTitle(details.Name, ModDBConstants.DefaultContentName);

        // 3. Use release date for manifest ID generation
        // Format: 1.YYYYMMDD.moddb.{contentType}.{contentName}
        var releaseDate = details.SubmissionDate;

        // 4. Generate manifest ID with release date using ManifestIdGenerator
        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(
            ModDBConstants.PublisherPrefix,
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

        // 6. Describe the remote archives. Delivery is intentionally deferred to Stage 2,
        // where the shared HTTP deliverer can place the downloaded file in staging and the
        // factory can extract it before validation and CAS storage.
        var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var primaryFileName = BuildPrimaryFileName(details);
        await manifest.AddRemoteFileAsync(primaryFileName, details.DownloadUrl, ContentSourceType.RemoteDownload);
        addedUrls.Add(details.DownloadUrl);

        // Add any additional files discovered on the page (e.g. patches, mirrors, addons)
        if (details.AdditionalFiles != null)
        {
            foreach (var file in details.AdditionalFiles)
            {
                if (string.IsNullOrEmpty(file.DownloadUrl) || addedUrls.Contains(file.DownloadUrl))
                    continue;

                var fileName = ManifestTagHelper.SanitizeFileName(file.Name, ModDBConstants.DefaultDownloadFilename);
                await manifest.AddRemoteFileAsync(fileName, file.DownloadUrl, ContentSourceType.RemoteDownload);
                addedUrls.Add(file.DownloadUrl);
            }
        }

        logger.LogInformation("{Count} remote file(s) added to the ModDB manifest for staged delivery", addedUrls.Count);

        // 7. Add dependencies based on target game (unless standalone)
        if (!details.ContentType.IsStandalone())
        {
            manifest = BaseDependencyBuilder.AddGameDependencies(manifest, details.TargetGame);
        }

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
        var normalized = ManifestTagHelper.SlugifyTitle(author, ModDBConstants.DefaultAuthor).Replace("-", string.Empty);

        // If the result is empty after normalization, use default
        return string.IsNullOrEmpty(normalized) ? ModDBConstants.DefaultAuthor : normalized;
    }

    /// <summary>
    /// Generates appropriate tags for ModDB content.
    /// </summary>
    /// <param name="details">The content details.</param>
    /// <returns>A list of tags.</returns>
    private static List<string> GetTags(MapDetails details)
    {
        List<string> tags = [.. ModDBConstants.Tags];
        ManifestTagHelper.AddGameAndContentTypeTags(tags, details.TargetGame, details.ContentType);

        // Add author tag
        if (!string.IsNullOrWhiteSpace(details.Author) && details.Author != ModDBConstants.DefaultAuthor)
        {
            tags.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, ModDBConstants.AuthorTagFormat, details.Author));
        }

        return tags;
    }

    /// <summary>
    /// Builds the primary archive filename for a ModDB download, normalizing the parsed file type
    /// into a conventional extension before it reaches staging.
    /// </summary>
    /// <param name="details">The parsed ModDB content details.</param>
    /// <returns>The sanitized filename with a restricted extension.</returns>
    private static string BuildPrimaryFileName(MapDetails details)
    {
        var fileName = ManifestTagHelper.SanitizeFileName(details.Name, ModDBConstants.DefaultDownloadFilename);
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
