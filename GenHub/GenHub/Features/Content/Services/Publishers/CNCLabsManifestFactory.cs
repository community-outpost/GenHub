using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
using Microsoft.Extensions.Logging;
using Slugify;
using ParsedContentDetails = GenHub.Core.Models.Content.ParsedContentDetails;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Factory for creating CNC Labs content manifests from parsed map/mission details.
/// Generates manifest IDs following the format: 1.0.cnclabs-{author}.{contentType}.{contentName}.
/// </summary>
public partial class CNCLabsManifestFactory(
    IContentManifestBuilder manifestBuilder,
    IProviderDefinitionLoader providerLoader,
    IDownloadService downloadService,
    ICasService casService,
    IConfigurationProviderService configurationProvider,
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
                var filename = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(filename) && filename.Contains('.'))
                {
                    return filename;
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        // Fallback: generate a generic filename
        return CNCLabsConstants.DefaultDownloadFilename;
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
        // CNCLabs content is delivered directly, no extra processing needed post-extraction.
        return Task.FromResult(new List<ContentManifest> { originalManifest });
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

        // 4. Use injected builder
        // Note: Since the builder is stateful and injected as Transient (likely), we can use it directly.
        // If it's Scoped/Singleton, we might need a factory. Assuming proper DI setup.
        var builder = manifestBuilder;

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

        // 6. Add download file - Download and store in CAS
        var fileName = GetDownloadFilename(details);

        if (!string.IsNullOrEmpty(details.DownloadUrl))
        {
            await DownloadAndAddFileAsync(
                builder,
                fileName,
                details.DownloadUrl,
                details.RefererUrl);
        }
        else
        {
            logger.LogWarning("Download URL is missing for {ContentName}", details.Name);
        }

        return builder.Build();
    }

    private async Task DownloadAndAddFileAsync(
        IContentManifestBuilder builder,
        string relativePath,
        string downloadUrl,
        string? refererUrl)
    {
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
        }

        // Standard download for CNC Labs
        var downloadResult = await downloadService.DownloadFileAsync(downloadConfig);
        if (!downloadResult.Success)
        {
            throw new InvalidOperationException($"Failed to download file from {downloadUrl}: {downloadResult.FirstError}");
        }

        // Store in CAS
        var storeResult = await casService.StoreContentAsync(tempFilePath, ContentType.Map);
        if (!storeResult.Success)
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            throw new InvalidOperationException($"Failed to store content in CAS: {storeResult.FirstError}");
        }

        var hash = storeResult.Data;
        var fileSize = new FileInfo(tempFilePath).Length;

        // Cleanup temp file after successful store (ICasService might move it, but keeping it safe)
        if (File.Exists(tempFilePath)) File.Delete(tempFilePath);

        await builder.AddContentAddressableFileAsync(relativePath, hash, fileSize);
    }
}
