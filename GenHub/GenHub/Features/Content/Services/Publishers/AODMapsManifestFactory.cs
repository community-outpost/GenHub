using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Slugify;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Factory for creating AODMaps content manifests from parsed content details.
/// </summary>
public partial class AODMapsManifestFactory(
    Func<IContentManifestBuilder> manifestBuilderFactory,
    IManifestIdService manifestIdService,
    IProviderDefinitionLoader providerLoader,
    IFileHashProvider hashProvider,
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
    public Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        return CreateManifestsFromExtractedContentAsync(originalManifest, extractedDirectory, progress: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Processing AODMaps extracted content from: {Directory}", extractedDirectory);

        var zipFiles = Directory.GetFiles(extractedDirectory, "*.zip", SearchOption.AllDirectories);
        if (zipFiles.Length == 0)
        {
            throw new InvalidDataException("AODMaps download did not produce a ZIP archive.");
        }

        foreach (var zipPath in zipFiles)
        {
            var extractPath = Path.Combine(extractedDirectory, Path.GetFileNameWithoutExtension(zipPath));
            Directory.CreateDirectory(extractPath);
            ExtractZipSafely(zipPath, extractPath);
            File.Delete(zipPath);
        }

        var files = new List<ManifestFile>();
        foreach (var filePath in Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fileInfo = new FileInfo(filePath);
            files.Add(new ManifestFile
            {
                RelativePath = Path.GetRelativePath(extractedDirectory, filePath),
                SourceType = ContentSourceType.ContentAddressable,
                Size = fileInfo.Length,
                Hash = await hashProvider.ComputeFileHashAsync(filePath, cancellationToken),

                // AODMaps is a map-only publisher. Maps must be linked into the user's Documents
                // map directory rather than the profile workspace or they will never appear in-game.
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            });
        }

        if (files.Count == 0)
        {
            throw new InvalidDataException("AODMaps archive contained no files.");
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
                RequiredDirectories = originalManifest.RequiredDirectories,
                InstallationInstructions = originalManifest.InstallationInstructions,
            },
        ];
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
    public async Task<ContentManifest> CreateManifestAsync(ParsedContentDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (string.IsNullOrWhiteSpace(details.DownloadUrl))
        {
            throw new ArgumentException("Download URL is required to create a manifest", nameof(details));
        }

        // Fresh builder per operation: the shared builder's internal state is never reset, so a
        // reused singleton would accumulate files/dependencies across calls.
        var manifestBuilder = manifestBuilderFactory();

        // 1. Normalize author
        var publisherId = AODMapsConstants.PublisherType;

        // 2. Slugify content name
        var contentName = SlugifyTitle(details.Name);

        // 3. Format release date for display (YYYYMMDD). If no real date was available,
        //    use a fixed epoch string so the display version is stable rather than changing daily.
        //    The manifest ID always uses userVersion: 0 regardless of date (see step 4).
        var releaseDate = details.SubmissionDate > DateTime.MinValue
            ? details.SubmissionDate.ToString("yyyyMMdd")
            : "00000000";

        // 4. Generate manifest ID — always uses version 0 for AODMaps content because
        //    AODMaps does not expose semantic versioning and ContentStateService also
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
        // AODMaps exposes a click-counter URL. The HTTP stack follows its redirect, while
        // this stable ZIP name ensures Stage 3 recognizes and extracts the real archive.
        var fileName = $"{contentName}.zip";
        await manifestBuilder.AddRemoteFileAsync(fileName, details.DownloadUrl);

        // 7. Add dependencies
        manifest = AddGameDependencies(manifest, details.TargetGame);

        return manifest.Build();
    }

    private static string SlugifyTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "content";
        var slugHelper = new SlugHelper();
        var slug = slugHelper.GenerateSlug(title);
        return string.IsNullOrEmpty(slug) ? "content" : slug;
    }

    private static List<string> GetTags(ParsedContentDetails details)
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
            // Type-only constraint: any platform's ZH installation satisfies this.
            builder.AddDependency(id: ManifestId.Create(ManifestConstants.ZeroHourFoundationDependencyId), name: "Zero Hour Installation", dependencyType: ContentType.GameInstallation, installBehavior: DependencyInstallBehavior.RequireExisting, minVersion: ManifestConstants.ZeroHourManifestVersion);
        }
        else if (targetGame == GameType.Generals)
        {
            // Type-only constraint: any platform's Generals installation satisfies this.
            builder.AddDependency(id: ManifestId.Create("1.108.any.gameinstallation.generals"), name: "Generals Installation", dependencyType: ContentType.GameInstallation, installBehavior: DependencyInstallBehavior.RequireExisting, minVersion: ManifestConstants.GeneralsManifestVersion);
        }

        return builder;
    }

    private static void ExtractZipSafely(string zipPath, string extractPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var rootPath = Path.GetFullPath(extractPath) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            var destinationPath = Path.GetFullPath(Path.Combine(extractPath, entry.FullName));
            if (!destinationPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"ZIP entry has an unsafe path: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }
}
