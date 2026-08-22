using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Slugify;
using ParsedContentDetails = GenHub.Core.Models.Content.ParsedContentDetails;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Factory for creating CNC Labs content manifests from parsed map/mission details.
/// Generates manifest IDs following the format: 1.0.cnclabs-{author}.{contentType}.{contentName}.
/// </summary>
public partial class CNCLabsManifestFactory(
    Func<IContentManifestBuilder> manifestBuilderFactory,
    IProviderDefinitionLoader providerLoader,
    IFileHashProvider hashProvider,
    ILogger<CNCLabsManifestFactory> logger) : IPublisherManifestFactory
{
    private static string SlugifyContentName(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return CNCLabsConstants.DefaultContentName;
        }

        try
        {
            var slugHelper = new SlugHelper();
            var slug = slugHelper.GenerateSlug(title);
            return string.IsNullOrEmpty(slug) ? CNCLabsConstants.DefaultContentName : slug;
        }
        catch
        {
            // Fallback to default if slugification fails
            return CNCLabsConstants.DefaultContentName;
        }
    }

    private static List<string> GetTags(ParsedContentDetails details)
    {
        List<string> tags = [.. CNCLabsConstants.DefaultTags];

        // Add game-specific tag
        tags.Add(details.TargetGame == GameType.Generals ? GameClientConstants.GeneralsShortName : GameClientConstants.ZeroHourShortName);

        // Add content type tag
        tags.Add(details.ContentType switch
        {
            ContentType.Map => ManifestConstants.MapTag,
            ContentType.Mission => ManifestConstants.MissionTag,
            ContentType.Mod => ManifestConstants.ModTag,
            ContentType.Patch => ManifestConstants.PatchTag,
            ContentType.Skin => ManifestConstants.SkinTag,
            ContentType.Video => ManifestConstants.VideoTag,
            ContentType.Screensaver => ManifestConstants.ScreensaverTag,
            ContentType.Replay => ManifestConstants.ReplayTag,
            ContentType.ModdingTool => ManifestConstants.ModdingToolTag,
            _ => ManifestConstants.OtherTag,
        });

        return tags;
    }

    private static string GetDownloadFilename(ParsedContentDetails details)
    {
        if (!string.IsNullOrWhiteSpace(details.DownloadUrl))
        {
            try
            {
                var uri = new Uri(details.DownloadUrl);

                // Skip dynamic download scripts (fetch.aspx, download.php, etc.)
                var filename = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(filename) &&
                    filename.Contains('.') &&
                    !filename.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase) &&
                    !filename.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
                {
                    return filename;
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        // Fallback: generate a filename based on content name, stripping all invalid filename
        // characters (details.Name is parsed from a remote CNC Labs HTML page).
        var safeName = string.Join("_", details.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safeName) ? CNCLabsConstants.DefaultDownloadFilename : $"{safeName}.zip";
    }

    /// <inheritdoc/>
    public string PublisherId => CNCLabsConstants.PublisherPrefix;

    /// <inheritdoc/>
    public bool CanHandle(ContentManifest manifest)
    {
        return manifest.Publisher.PublisherType == CNCLabsConstants.PublisherId;
    }

    /// <inheritdoc/>
    public Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        return CreateManifestsFromExtractedContentAsync(originalManifest, extractedDirectory, progress: null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        IProgress<GenHub.Core.Models.Content.ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Processing CNC Labs extracted content for manifest {ManifestId} from directory {Directory}",
            originalManifest.Id,
            extractedDirectory);

        // Check if directory contains ZIP files
        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogWarning("Extracted directory does not exist: {Directory}", extractedDirectory);
            return new List<ContentManifest> { originalManifest };
        }

        var zipFiles = Directory.GetFiles(extractedDirectory, "*.zip", SearchOption.AllDirectories);
        if (zipFiles.Length == 0)
        {
            logger.LogInformation("No ZIP files found in directory, returning original manifest");
            return new List<ContentManifest> { originalManifest };
        }

        logger.LogInformation("Found {Count} ZIP files to extract", zipFiles.Length);

        // Extract all ZIP files
        var extractedFiles = new List<ManifestFile>();
        foreach (var zipPath in zipFiles)
        {
            try
            {
                logger.LogInformation("Extracting ZIP file: {ZipPath}", zipPath);

                // Extract ZIP to a subdirectory
                var extractPath = Path.Combine(extractedDirectory, Path.GetFileNameWithoutExtension(zipPath));
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                Directory.CreateDirectory(extractPath);

                ZipFile.ExtractToDirectory(zipPath, extractPath);
                logger.LogInformation("Extracted ZIP to: {ExtractPath}", extractPath);

                // Scan extracted files
                var files = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories);
                logger.LogInformation("Found {Count} files in extracted ZIP", files.Length);

                foreach (var filePath in files)
                {
                    var relativePath = Path.GetRelativePath(extractedDirectory, filePath);
                    var fileInfo = new FileInfo(filePath);
                    var hash = await hashProvider.ComputeFileHashAsync(filePath, cancellationToken);

                    var manifestFile = new ManifestFile
                    {
                        RelativePath = relativePath,
                        SourceType = ContentSourceType.ContentAddressable,
                        InstallTarget = originalManifest.ContentType == ContentType.Map
                            ? ContentInstallTarget.UserMapsDirectory
                            : ContentInstallTarget.Workspace,
                        Size = fileInfo.Length,
                        Hash = hash,
                        IsExecutable = false,
                    };

                    extractedFiles.Add(manifestFile);
                    logger.LogDebug(
                        "Added file to manifest: {RelativePath}, Hash: {Hash}, Size: {Size}",
                        relativePath,
                        hash,
                        fileInfo.Length);
                }

                // Delete ZIP file after successful extraction
                File.Delete(zipPath);
                logger.LogInformation("Deleted ZIP file after extraction: {ZipPath}", zipPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to extract ZIP file: {ZipPath}", zipPath);
                throw new InvalidOperationException($"Failed to extract ZIP file '{zipPath}': {ex.Message}", ex);
            }
        }

        if (extractedFiles.Count == 0)
        {
            logger.LogWarning("No files extracted from ZIP archives");
            return new List<ContentManifest> { originalManifest };
        }

        // Create updated manifest with extracted files
        var updatedManifest = new ContentManifest
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
            Files = extractedFiles,
            RequiredDirectories = originalManifest.RequiredDirectories,
            InstallationInstructions = originalManifest.InstallationInstructions,
        };

        logger.LogInformation(
            "Successfully extracted and processed {Count} files for manifest {ManifestId}",
            extractedFiles.Count,
            originalManifest.Id);

        return new List<ContentManifest> { updatedManifest };
    }

    /// <inheritdoc/>
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        return extractedDirectory;
    }

    /// <summary>
    /// Creates a manifest from map details.
    /// </summary>
    /// <param name="details">The map details.</param>
    /// <returns>A task that represents the asynchronous operation, containing the created manifest.</returns>
    public async Task<ContentManifest> CreateManifestAsync(
        object details)
    {
        if (details is not ParsedContentDetails mapDetails)
        {
            throw new ArgumentException($"Details must be of type {nameof(ParsedContentDetails)}", nameof(details));
        }

        return await CreateManifestInternalAsync(mapDetails);
    }

    private async Task<ContentManifest> CreateManifestInternalAsync(
        ParsedContentDetails details)
    {
        // 1. Load provider metadata to get website/support URLs if possible
        var provider = providerLoader.GetProvider(CNCLabsConstants.PublisherPrefix);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? CNCLabsConstants.PublisherWebsite;
        var detailPageUrl = details.DownloadUrl ?? websiteUrl; // Fallback if source omitted

        // 2. Prepare manifest information
        var contentName = SlugifyContentName(details.Name);
        var publisherId = CNCLabsConstants.PublisherId;

        // 3. Format submission date as YYYYMMDD for version
        var releaseDate = details.SubmissionDate.ToString(CNCLabsConstants.ReleaseDateFormat);

        // 4. Obtain a fresh builder for this operation: the shared builder's internal state is
        // never reset, so a reused singleton would accumulate files/dependencies across calls.
        var builder = manifestBuilderFactory();

        // 5. Configure manifest
        builder
            .WithBasicInfo(publisherId, contentName, releaseDate)
            .WithContentType(details.ContentType, details.TargetGame)
            .WithPublisher(
                CNCLabsConstants.PublisherName,
                websiteUrl,
                detailPageUrl,
                string.Empty,
                CNCLabsConstants.PublisherId)
            .WithMetadata(
                details.Description,
                GetTags(details),
                details.PreviewImage,
                details.Screenshots)
            .WithInstallationInstructions(WorkspaceConstants.DefaultWorkspaceStrategy); // Default strategy

        // 6. Add the archive for the staged HTTP deliverer. CAS storage belongs to Stage 5.
        var fileName = GetDownloadFilename(details);
        logger.LogInformation(
            "Preparing to download CNC Labs content: {Name}, URL: {Url}, Filename: {Filename}",
            details.Name,
            details.DownloadUrl,
            fileName);

        if (string.IsNullOrEmpty(details.DownloadUrl))
        {
            throw new InvalidOperationException($"Download URL is missing for {details.Name}");
        }

        await builder.AddRemoteFileAsync(fileName, details.DownloadUrl);

        return builder.Build();
    }
}
