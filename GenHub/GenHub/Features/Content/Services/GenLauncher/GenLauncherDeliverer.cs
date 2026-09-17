using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Initializes a new instance of the <see cref="GenLauncherDeliverer"/> class.
/// Delivers GenLauncher content files, downloads S3 or cloud archive mirrors, validates engine MD5s, and stores acquired content.
/// </summary>
/// <param name="downloadService">The download service.</param>
/// <param name="manifestPool">The content manifest pool.</param>
/// <param name="manifestFactory">The GenLauncher manifest factory.</param>
/// <param name="logger">The logger instance.</param>
public class GenLauncherDeliverer(
    IDownloadService downloadService,
    IContentManifestPool manifestPool,
    GenLauncherManifestFactory manifestFactory,
    ILogger<GenLauncherDeliverer> logger)
    : IContentDeliverer
{
    /// <inheritdoc/>
    public string SourceName => PublisherTypeConstants.GenLauncher;

    /// <inheritdoc/>
    public string Description => "Delivers content files from GenLauncher repositories";

    /// <inheritdoc/>
    public bool IsEnabled => true;

    /// <inheritdoc/>
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc/>
    public bool CanDeliver(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.Publisher?.PublisherType?.Equals(PublisherTypeConstants.GenLauncher, StringComparison.OrdinalIgnoreCase) == true
            || manifest.OriginalProviderName?.Equals(PublisherTypeConstants.GenLauncher, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var isValid = manifest.Files.Any(f => !string.IsNullOrWhiteSpace(f.DownloadUrl));
        return Task.FromResult(OperationResult<bool>.CreateSuccess(isValid));
    }

    /// <inheritdoc/>
    public async Task<OperationResult<ContentManifest>> DeliverContentAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageManifest);

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return OperationResult<ContentManifest>.CreateFailure("Target directory cannot be empty");
        }

        try
        {
            logger.LogInformation("Delivering GenLauncher package {Name} v{Version} to {Dir}", packageManifest.Name, packageManifest.Version, targetDirectory);
            Directory.CreateDirectory(targetDirectory);

            var filesToDownload = packageManifest.Files.Where(f => !string.IsNullOrWhiteSpace(f.DownloadUrl)).ToList();
            if (filesToDownload.Count == 0)
            {
                return OperationResult<ContentManifest>.CreateFailure("Manifest does not contain any downloadable files");
            }

            var downloadResult = await DownloadAllFilesAsync(filesToDownload, targetDirectory, progress, cancellationToken);
            if (!downloadResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(downloadResult.FirstError ?? "Failed to download files");
            }

            // Extract archives and compute CAS hashes via ManifestFactory
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                ProgressPercentage = 85,
                CurrentOperation = "Extracting archives and calculating content-addressable hashes",
            });

            var factoryResult = await manifestFactory.CreateManifestsFromExtractedContentAsync(
                packageManifest,
                targetDirectory,
                cancellationToken);

            if (!factoryResult.Success || factoryResult.Data == null || factoryResult.Data.Count == 0)
            {
                return OperationResult<ContentManifest>.CreateFailure(factoryResult.FirstError ?? "Failed to create manifests from delivered content");
            }

            var finalManifest = factoryResult.Data[0];

            // Add to ContentManifestPool
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.ValidatingFiles,
                ProgressPercentage = 95,
                CurrentOperation = "Storing content manifest in storage pool",
            });

            var addResult = await manifestPool.AddManifestAsync(finalManifest, targetDirectory, null, cancellationToken);
            if (!addResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(addResult.FirstError ?? "Failed to store content manifest in storage pool");
            }

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Completed,
                ProgressPercentage = 100,
                CurrentOperation = "Delivery complete",
            });

            return OperationResult<ContentManifest>.CreateSuccess(finalManifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error delivering GenLauncher content for {Name}", packageManifest.Name);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to deliver GenLauncher content: {ex.Message}");
        }
    }

    private async Task<OperationResult<bool>> DownloadAllFilesAsync(
        List<ManifestFile> files,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalFiles = files.Count;

        for (var i = 0; i < totalFiles; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var destinationPath = Path.Combine(targetDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            // Prevent path traversal
            if (!ContentPathPolicy.IsContained(targetDirectory, destinationPath))
            {
                logger.LogError("File {File} relative path traverses outside target directory {Dir}", file.RelativePath, targetDirectory);
                return OperationResult<bool>.CreateFailure($"File '{file.RelativePath}' traverses outside target directory.");
            }

            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = (int)((i / (double)totalFiles) * 80),
                CurrentOperation = $"Downloading {file.RelativePath} ({i + 1}/{totalFiles})",
            });

            if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var downloadUri))
            {
                logger.LogError("Invalid download URL for file {File}: {Url}", file.RelativePath, file.DownloadUrl);
                return OperationResult<bool>.CreateFailure($"Invalid download URL for file {file.RelativePath}: {file.DownloadUrl}");
            }

            var fileIndex = i;
            IProgress<DownloadProgress>? fileProgress = progress == null ? null : new Progress<DownloadProgress>(p =>
            {
                var basePercent = (fileIndex / (double)totalFiles) * 80.0;
                var sliceWidth = (1.0 / totalFiles) * 80.0;
                var weightedPercent = basePercent + ((p.Percentage / 100.0) * sliceWidth);
                progress.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Downloading,
                    ProgressPercentage = Math.Min(80.0, Math.Max(0.0, weightedPercent)),
                    CurrentOperation = $"Downloading {file.RelativePath} ({fileIndex + 1}/{totalFiles})",
                    BytesProcessed = p.BytesReceived,
                    TotalBytes = p.TotalBytes,
                });
            });

            var downloadResult = await DownloadAndValidateFileAsync(file, destinationPath, downloadUri, fileProgress, cancellationToken);
            if (!downloadResult.Success)
            {
                return OperationResult<bool>.CreateFailure(downloadResult.FirstError ?? $"Failed to download {file.RelativePath}");
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> DownloadAndValidateFileAsync(
        ManifestFile file,
        string destinationPath,
        Uri downloadUri,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        string? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var downloadResult = await downloadService.DownloadFileAsync(
                downloadUri,
                destinationPath,
                expectedHash: null,
                progress: progress,
                cancellationToken);

            if (!downloadResult.Success)
            {
                lastError = downloadResult.FirstError;
                logger.LogWarning("Attempt {Attempt}/{Max} failed downloading {File}: {Error}", attempt, maxAttempts, file.RelativePath, lastError);
                continue;
            }

            // MD5 checksum validation against S3 ETag for engine extensions
            if (!string.IsNullOrWhiteSpace(file.Hash) &&
                GenLauncherChecksumValidator.RequiresValidation(file.RelativePath) &&
                !await GenLauncherChecksumValidator.ValidateFileAsync(destinationPath, file.Hash, cancellationToken))
            {
                lastError = $"Checksum mismatch for {file.RelativePath}! Expected ETag: {file.Hash}";
                logger.LogWarning("Attempt {Attempt}/{Max}: {Error}", attempt, maxAttempts, lastError);
                CleanupCorruptedFile(destinationPath);
                continue;
            }

            return OperationResult<bool>.CreateSuccess(true);
        }

        logger.LogError("Failed to download {File} from {Url} after {Max} attempts: {Error}", file.RelativePath, file.DownloadUrl, maxAttempts, lastError);
        return OperationResult<bool>.CreateFailure($"Failed to download {file.RelativePath}: {lastError}");
    }

    private void CleanupCorruptedFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete corrupted file {File}", filePath);
        }
    }
}
