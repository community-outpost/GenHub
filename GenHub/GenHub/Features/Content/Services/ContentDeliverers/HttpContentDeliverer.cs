using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDeliverers;

/// <summary>
/// Delivers remote HTTP content.
/// Pure delivery - downloads and extracts content.
/// </summary>
public class HttpContentDeliverer(
    IDownloadService downloadService,
    IContentManifestBuilder manifestBuilder,
    ILogger<HttpContentDeliverer> logger) : IContentDeliverer
{
    /// <inheritdoc />
    public string SourceName => ContentSourceNames.HttpDeliverer;

    /// <inheritdoc />
    public string Description => ContentSourceNames.HttpDelivererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        // Can deliver if files have HTTP download URLs
        return manifest.Files.Any(f =>
            !string.IsNullOrEmpty(f.DownloadUrl) &&
            Uri.TryCreate(f.DownloadUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"));
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> DeliverContentAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deliveredManifest = InitializeManifestBuilder(packageManifest);

            var filesToDownload = packageManifest.Files.Where(f => !string.IsNullOrEmpty(f.DownloadUrl)).ToList();
            var downloadResult = await DownloadDeliveredFilesAsync(
                deliveredManifest,
                filesToDownload,
                targetDirectory,
                progress,
                cancellationToken);

            if (!downloadResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(downloadResult.Errors);
            }

            AddNonDownloadFiles(deliveredManifest, packageManifest.Files.Where(f => string.IsNullOrEmpty(f.DownloadUrl)));

            deliveredManifest.AddRequiredDirectories([.. packageManifest.RequiredDirectories]);

            if (packageManifest.InstallationInstructions != null)
            {
                deliveredManifest.WithInstallationInstructions(packageManifest.InstallationInstructions);
            }

            return OperationResult<ContentManifest>.CreateSuccess(deliveredManifest.Build());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver HTTP content for manifest {ManifestId}", packageManifest.Id);
            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate that all required URLs are accessible
            foreach (var file in manifest.Files.Where(f => f.IsRequired && !string.IsNullOrEmpty(f.DownloadUrl)))
            {
                if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var uri) ||
                    !(uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
                }
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Validation failed for HTTP content manifest {ManifestId}", manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    private static void AddDeliveredFile(
        IContentManifestBuilder deliveredManifest,
        ManifestFile file,
        string localPath)
    {
        var fileInfo = new FileInfo(localPath);
        var deliveredFile = new ManifestFile
        {
            RelativePath = file.RelativePath,
            SourceType = ContentSourceType.ContentAddressable,
            InstallTarget = file.InstallTarget,
            IsExecutable = file.IsExecutable,
            Hash = !string.IsNullOrEmpty(file.Hash) ? file.Hash : string.Empty,
            DownloadUrl = file.DownloadUrl,
            Size = fileInfo.Exists ? fileInfo.Length : file.Size,
            Permissions = file.Permissions ?? new FilePermissions { UnixPermissions = file.IsExecutable ? "755" : "644" },
        };
        deliveredManifest.AddFile(deliveredFile);
    }

    private static void AddNonDownloadFiles(
        IContentManifestBuilder deliveredManifest,
        IEnumerable<ManifestFile> files)
    {
        foreach (var file in files)
        {
            var otherFile = new ManifestFile
            {
                RelativePath = file.RelativePath,
                SourcePath = file.SourcePath ?? string.Empty,
                SourceType = ContentSourceType.ContentAddressable,
                InstallTarget = file.InstallTarget,
                IsExecutable = file.IsExecutable,
                Hash = file.Hash,
                DownloadUrl = file.DownloadUrl,
                Size = file.Size,
                Permissions = file.Permissions ?? new FilePermissions { UnixPermissions = file.IsExecutable ? "755" : "644" },
            };
            deliveredManifest.AddFile(otherFile);
        }
    }

    private IContentManifestBuilder InitializeManifestBuilder(ContentManifest packageManifest)
    {
        var idSegments = packageManifest.Id.Value.Split('.');
        var publisherId = idSegments.Length >= 3 ? idSegments[2] : "unknown";
        var manifestVersionInt = int.TryParse(packageManifest.Version, out var parsedVersion) ? parsedVersion : 0;

        var builder = manifestBuilder
            .WithBasicInfo(publisherId, packageManifest.Name, manifestVersionInt)
            .WithContentType(packageManifest.ContentType, packageManifest.TargetGame)
            .WithPublisher(
                packageManifest.Publisher?.Name ?? string.Empty,
                packageManifest.Publisher?.Website ?? string.Empty,
                packageManifest.Publisher?.SupportUrl ?? string.Empty,
                packageManifest.Publisher?.ContactEmail ?? string.Empty,
                packageManifest.Publisher?.PublisherType ?? string.Empty)
            .WithMetadata(
                packageManifest.Metadata?.Description ?? string.Empty,
                packageManifest.Metadata?.Tags,
                packageManifest.Metadata?.IconUrl ?? string.Empty,
                packageManifest.Metadata?.ScreenshotUrls,
                packageManifest.Metadata?.ChangelogUrl ?? string.Empty);

        foreach (var dep in packageManifest.Dependencies)
        {
            builder.AddDependency(
                dep.Id,
                dep.Name,
                dep.DependencyType,
                dep.InstallBehavior,
                dep.MinVersion ?? string.Empty,
                dep.MaxVersion ?? string.Empty,
                dep.CompatibleVersions,
                dep.IsExclusive,
                dep.ConflictsWith);
        }

        return builder;
    }

    private async Task<OperationResult<bool>> DownloadDeliveredFilesAsync(
        IContentManifestBuilder deliveredManifest,
        IReadOnlyList<ManifestFile> filesToDownload,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalFiles = filesToDownload.Count;
        var processedFiles = 0;

        foreach (var file in filesToDownload)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var localPath = Path.Combine(targetDirectory, file.RelativePath);
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                CurrentOperation = $"Downloading {file.RelativePath}",
                CurrentFile = file.RelativePath,
                FilesProcessed = processedFiles,
                TotalFiles = totalFiles,
            });

            var downloadResult = await downloadService.DownloadFileAsync(
                new Uri(file.DownloadUrl!), localPath, file.Hash, null, cancellationToken);

            if (!downloadResult.Success)
            {
                return OperationResult<bool>.CreateFailure(
                    $"Failed to download {file.RelativePath}: {downloadResult.FirstError}");
            }

            AddDeliveredFile(deliveredManifest, file, localPath);
            processedFiles++;
        }

        return OperationResult<bool>.CreateSuccess(true);
    }
}
