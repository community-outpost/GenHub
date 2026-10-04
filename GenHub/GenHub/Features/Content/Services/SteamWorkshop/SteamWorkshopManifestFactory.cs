using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ParsedContentDetails = GenHub.Core.Models.Content.ParsedContentDetails;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Factory for creating Steam Workshop content manifests from parsed item details.
/// Generates manifest IDs following the format: 1.0.steamworkshop.{contentName}.{releaseDate}.
/// </summary>
public class SteamWorkshopManifestFactory(
    Func<IContentManifestBuilder> manifestBuilderFactory,
    IProviderDefinitionLoader providerLoader,
    IFileHashProvider hashProvider,
    ILogger<SteamWorkshopManifestFactory> logger) : IPublisherManifestFactory
{
    /// <inheritdoc/>
    public string PublisherId => SteamWorkshopConstants.PublisherPrefix;

    /// <inheritdoc/>
    public bool CanHandle(ContentManifest manifest)
    {
        return manifest.Publisher?.PublisherType?.Equals(SteamWorkshopConstants.PublisherId, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <inheritdoc/>
    public Task<OperationResult<List<ContentManifest>>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        return CreateManifestsFromExtractedContentAsync(originalManifest, extractedDirectory, progress: null, cancellationToken);
    }

    /// <summary>
    /// Creates enriched manifests from delivered workshop files with optional progress reporting.
    /// Workshop content arrives as plain files (no archives), so the staging directory is scanned directly.
    /// </summary>
    /// <param name="originalManifest">The original manifest.</param>
    /// <param name="extractedDirectory">The directory where content was delivered.</param>
    /// <param name="progress">Progress reporter for tracking progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing enriched content manifests.</returns>
    public async Task<OperationResult<List<ContentManifest>>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        IProgress<GenHub.Core.Models.Content.ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Processing Steam Workshop delivered content for manifest {ManifestId} from directory {Directory}",
            originalManifest.Id,
            extractedDirectory);

        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogWarning("Delivered directory does not exist: {Directory}", extractedDirectory);
            return OperationResult<List<ContentManifest>>.CreateFailure(
                $"Steam Workshop delivery failed: directory '{extractedDirectory}' does not exist.");
        }

        var files = CollectDeliveredFiles(extractedDirectory, cancellationToken);
        if (files.Count == 0)
        {
            logger.LogWarning("No files found in delivered directory: {Directory}", extractedDirectory);
            return OperationResult<List<ContentManifest>>.CreateFailure(
                $"Steam Workshop delivery failed: no files were delivered to '{extractedDirectory}'.");
        }

        var totalFiles = files.Count;
        var processedFiles = 0;
        var extractedFiles = new List<ManifestFile>();
        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(extractedDirectory, filePath);
            var fileInfo = new FileInfo(filePath);
            var hash = await hashProvider.ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false);

            extractedFiles.Add(new ManifestFile
            {
                RelativePath = relativePath,
                SourceType = ContentSourceType.ExtractedPackage,
                InstallTarget = originalManifest.ContentType is ContentType.Map or ContentType.MapPack or ContentType.Mission
                    ? ContentInstallTarget.UserMapsDirectory
                    : ContentInstallTarget.Workspace,
                Size = fileInfo.Length,
                Hash = hash,
                IsExecutable = false,
            });

            processedFiles++;
            progress?.Report(new GenHub.Core.Models.Content.ContentAcquisitionProgress
            {
                Phase = GenHub.Core.Models.Content.ContentAcquisitionPhase.ValidatingFiles,
                ProgressPercentage = (double)processedFiles / totalFiles * 100,
                CurrentOperation = $"Processing {relativePath}",
                CurrentFile = relativePath,
                FilesProcessed = processedFiles,
                TotalFiles = totalFiles,
            });
        }

        var updatedManifest = ManifestVariantResolver.CopyWithResolvedFiles(originalManifest, extractedFiles);

        logger.LogInformation(
            "Successfully processed {Count} files for manifest {ManifestId}",
            extractedFiles.Count,
            originalManifest.Id);

        return OperationResult<List<ContentManifest>>.CreateSuccess([updatedManifest]);
    }

    /// <inheritdoc/>
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        return extractedDirectory;
    }

    /// <summary>
    /// Creates a manifest from workshop item details.
    /// </summary>
    /// <param name="details">The item details.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing the created manifest.</returns>
    public Task<ContentManifest> CreateManifestAsync(
        object details,
        CancellationToken cancellationToken = default)
    {
        if (details is not ParsedContentDetails workshopDetails)
        {
            throw new ArgumentException($"Details must be of type {nameof(ParsedContentDetails)}", nameof(details));
        }

        return Task.FromResult(CreateManifestInternal(workshopDetails, cancellationToken));
    }

    private static List<string> GetTags(ParsedContentDetails details)
    {
        List<string> tags = [.. SteamWorkshopConstants.DefaultTags];
        ManifestTagHelper.AddGameAndContentTypeTags(tags, details.TargetGame, details.ContentType);
        if (details.Tags != null)
        {
            foreach (var tag in details.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    private static List<string> CollectDeliveredFiles(string extractedDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var files = new List<string>();
        foreach (var filePath in Directory.EnumerateFiles(extractedDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!filePath.EndsWith(SteamWorkshopConstants.StagingFileExtension, StringComparison.OrdinalIgnoreCase) &&
                !filePath.EndsWith(SteamWorkshopConstants.BackupFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(filePath);
            }
        }

        return files;
    }

    private ContentManifest CreateManifestInternal(
        ParsedContentDetails details,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = providerLoader.GetProvider(SteamWorkshopConstants.PublisherPrefix);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? PublisherInfoConstants.SteamWorkshop.Website;
        var detailPageUrl = !string.IsNullOrWhiteSpace(details.DownloadUrl) ? details.DownloadUrl : websiteUrl;

        var contentName = ManifestTagHelper.SlugifyTitle(details.Name, SteamWorkshopConstants.DefaultContentName);
        var releaseDate = details.SubmissionDate > DateTime.MinValue
            ? details.SubmissionDate.ToString(SteamWorkshopConstants.ReleaseDateFormat, CultureInfo.InvariantCulture)
            : "1";

        var contentType = details.ContentType != ContentType.UnknownContentType
            ? details.ContentType
            : ContentType.Map;
        var targetGame = details.TargetGame != GameType.Unknown
            ? details.TargetGame
            : GameType.ZeroHour;

        var builder = manifestBuilderFactory();
        builder
            .WithBasicInfo(SteamWorkshopConstants.PublisherId, contentName, releaseDate)
            .WithContentType(contentType, targetGame)
            .WithPublisher(
                PublisherInfoConstants.SteamWorkshop.Name,
                websiteUrl,
                detailPageUrl,
                string.Empty,
                SteamWorkshopConstants.PublisherId)
            .WithMetadata(
                details.Description,
                GetTags(details with { ContentType = contentType, TargetGame = targetGame }),
                details.PreviewImage,
                details.Screenshots)
            .WithInstallationInstructions(WorkspaceConstants.DefaultWorkspaceStrategy);

        return builder.Build();
    }
}
