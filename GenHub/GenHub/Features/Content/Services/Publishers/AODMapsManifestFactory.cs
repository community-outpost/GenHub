using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Slugify;
using MapDetails = GenHub.Core.Models.ModDB.MapDetails;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Factory for creating AODMaps content manifests from parsed content details.
/// </summary>
public partial class AODMapsManifestFactory(
    IContentManifestBuilder manifestBuilder,
    IManifestIdService manifestIdService,
    IProviderDefinitionLoader providerLoader,
    ILogger<AODMapsManifestFactory> logger) : IPublisherManifestFactory
{
    /// <inheritdoc />
    public string PublisherId => AODMapsConstants.PublisherType;

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        return manifest.Publisher?.PublisherType == AODMapsConstants.PublisherType;
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Processing AODMaps extracted content from: {Directory}", extractedDirectory);
        return await Task.FromResult<List<ContentManifest>>([originalManifest]);
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        return extractedDirectory;
    }

    /// <summary>
    /// Creates a content manifest from AODMaps content details.
    /// </summary>
    /// <param name="details">The map details to create the manifest from.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<ContentManifest> CreateManifestAsync(MapDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (string.IsNullOrWhiteSpace(details.DownloadUrl))
        {
            throw new ArgumentException("Download URL is required to create a manifest", nameof(details));
        }

        // 1. Normalize author
        var publisherId = AODMapsConstants.PublisherType;

        // 2. Slugify content name
        var contentName = SlugifyTitle(details.Name);

        // 3. Format release date
        var releaseDate = details.SubmissionDate.ToString("yyyyMMdd");
        if (releaseDate == "00010101") releaseDate = DateTime.Now.ToString("yyyyMMdd");

        // 4. Generate manifest ID
        // User requested Version 0 for downloaded content
        var manifestIdResult = manifestIdService.GeneratePublisherContentId(
            publisherId,
            details.ContentType,
            contentName,
            userVersion: 0);

        if (!manifestIdResult.Success)
        {
            logger.LogError("Failed to generate manifest ID: {Error}", manifestIdResult.FirstError);
            throw new InvalidOperationException($"Failed to generate manifest ID: {manifestIdResult.FirstError}");
        }

        // 5. Build manifest
        var provider = providerLoader.GetProvider(publisherId);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? AODMapsConstants.BaseUrl;

        var manifest = manifestBuilder
            .WithBasicInfo(publisherId, details.Name, int.Parse(releaseDate))
            .WithContentType(details.ContentType, details.TargetGame)
            .WithPublisher(
                name: details.Author ?? "Unknown Author",
                website: websiteUrl,
                supportUrl: websiteUrl,
                publisherType: publisherId)
            .WithMetadata(
                description: details.Description,
                tags: [.. GetTags(details)],
                iconUrl: details.PreviewImage,
                screenshotUrls: details.Screenshots ?? []);

        // 6. Add download file - Download and store in CAS
        var fileName = ExtractFileNameFromUrl(details.DownloadUrl);

        manifest = await manifest.AddRemoteFileAsync(
            fileName,
            details.DownloadUrl,
            ContentSourceType.ContentAddressable,
            isExecutable: false,
            permissions: null);

        // 7. Add dependencies
        manifest = AddGameDependencies(manifest, details.TargetGame);

        return manifest.Build();
    }

    private static string NormalizeAuthorForPublisherId(string author)
    {
        if (string.IsNullOrWhiteSpace(author)) return "unknown";
        var slugHelper = new SlugHelper();
        var normalized = slugHelper.GenerateSlug(author).Replace("-", string.Empty);
        return string.IsNullOrEmpty(normalized) ? "unknown" : normalized;
    }

    private static string SlugifyTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "content";
        var slugHelper = new SlugHelper();
        var slug = slugHelper.GenerateSlug(title);
        return string.IsNullOrEmpty(slug) ? "content" : slug;
    }

    private static List<string> GetTags(MapDetails details)
    {
        var tags = new List<string> { "aodmaps" };
        if (details.TargetGame == GameType.Generals) tags.Add("generals");
        if (details.TargetGame == GameType.ZeroHour) tags.Add("zh");

        if (!string.IsNullOrWhiteSpace(details.Author))
        {
            tags.Add($"author:{details.Author.ToLowerInvariant()}");
        }

        return tags;
    }

    private static IContentManifestBuilder AddGameDependencies(IContentManifestBuilder builder, GameType targetGame)
    {
        if (targetGame == GameType.ZeroHour)
        {
            builder.AddDependency(id: ManifestId.Create("1.104.ea.gameinstallation.zerohour"), name: "Zero Hour Installation", dependencyType: ContentType.GameInstallation, installBehavior: DependencyInstallBehavior.RequireExisting, minVersion: ManifestConstants.ZeroHourManifestVersion);
        }
        else if (targetGame == GameType.Generals)
        {
            builder.AddDependency(id: ManifestId.Create("1.108.ea.gameinstallation.generals"), name: "Generals Installation", dependencyType: ContentType.GameInstallation, installBehavior: DependencyInstallBehavior.RequireExisting, minVersion: ManifestConstants.GeneralsManifestVersion);
        }

        return builder;
    }

    private static string ExtractFileNameFromUrl(string downloadUrl)
    {
        try
        {
            var uri = new Uri(downloadUrl);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName)) return fileName;
        }
        catch (UriFormatException)
        {
        }

        return "download.zip";
    }
}
