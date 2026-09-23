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
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    private static string RedactUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";
        }

        return "[redacted]";
    }

    private async Task<OperationResult<bool>> DownloadAllFilesAsync(
        List<ManifestFile> files,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalFiles = files.Count;
        var totalBytes = files.Sum(f => Math.Max(f.Size, 0));
        logger.LogInformation("Beginning download of {TotalFiles} files ({TotalBytes} bytes)...", totalFiles, totalBytes);

        for (var i = 0; i < totalFiles; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[i];

            var result = await DownloadSingleFileAsync(file, i, totalFiles, targetDirectory, progress, cancellationToken);
            if (!result.Success)
            {
                return result;
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> DownloadSingleFileAsync(
        ManifestFile file,
        int fileIndex,
        int totalFiles,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
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
            ProgressPercentage = (fileIndex / (double)totalFiles) * 80,
            CurrentOperation = $"{file.RelativePath} ({fileIndex + 1}/{totalFiles})",
            FilesProcessed = fileIndex,
            TotalFiles = totalFiles,
        });

        if (string.IsNullOrWhiteSpace(file.DownloadUrl) || !ImageCacheService.IsSafeRemoteUrl(file.DownloadUrl, out var downloadUri))
        {
            logger.LogError("Invalid or unsafe download URL for file {File}", file.RelativePath);
            return OperationResult<bool>.CreateFailure($"Invalid download URL for file {file.RelativePath}: unsafe or malformed URL");
        }

        var safeLogUrl = RedactUrl(file.DownloadUrl);
        logger.LogInformation(
            "Downloading GenLauncher file [{Index}/{Total}]: {File} ({Size} bytes) from {Url}",
            fileIndex + 1,
            totalFiles,
            file.RelativePath,
            file.Size,
            safeLogUrl);

        var fileStopwatch = Stopwatch.StartNew();
        var fileProgress = CreateFileProgress(progress, fileIndex, totalFiles, file.RelativePath);

        var downloadResult = await DownloadAndValidateFileAsync(file, destinationPath, downloadUri, fileProgress, cancellationToken);
        fileStopwatch.Stop();

        if (!downloadResult.Success)
        {
            return OperationResult<bool>.CreateFailure(downloadResult.FirstError ?? $"Failed to download {file.RelativePath}");
        }

        logger.LogInformation(
            "Finished downloading GenLauncher file [{Index}/{Total}]: {File} in {ElapsedMs}ms",
            fileIndex + 1,
            totalFiles,
            file.RelativePath,
            fileStopwatch.ElapsedMilliseconds);

        return OperationResult<bool>.CreateSuccess(true);
    }

    private IProgress<DownloadProgress>? CreateFileProgress(
        IProgress<ContentAcquisitionProgress>? progress,
        int fileIndex,
        int totalFiles,
        string relativePath)
    {
        if (progress == null)
        {
            return null;
        }

        return new Progress<DownloadProgress>(p =>
        {
            var basePercent = (fileIndex / (double)totalFiles) * 80.0;
            var sliceWidth = (1.0 / totalFiles) * 80.0;
            var weightedPercent = basePercent + ((p.Percentage / 100.0) * sliceWidth);
            var speedStr = p.BytesPerSecond > 0 ? $" at {ByteFormatHelper.FormatBytes(p.BytesPerSecond)}/s" : string.Empty;
            var bytesStr = p.TotalBytes > 0 ? $" [{ByteFormatHelper.FormatBytes(p.BytesReceived)} / {ByteFormatHelper.FormatBytes(p.TotalBytes)}]" : string.Empty;

            progress.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = Math.Min(80.0, Math.Max(0.0, weightedPercent)),
                CurrentOperation = $"{relativePath} ({fileIndex + 1}/{totalFiles}){bytesStr}{speedStr}",
                BytesProcessed = p.BytesReceived,
                TotalBytes = p.TotalBytes,
                FilesProcessed = fileIndex,
                TotalFiles = totalFiles,
            });
        });
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

            if (attempt > 1)
            {
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 2));
                logger.LogInformation(
                    "Retrying download of {File} (attempt {Attempt}/{Max}) in {DelayMs}ms",
                    file.RelativePath,
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

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
            var expectedEtag = !string.IsNullOrWhiteSpace(file.ETag) ? file.ETag : file.Hash;
            if (!string.IsNullOrWhiteSpace(expectedEtag) &&
                GenLauncherChecksumValidator.RequiresValidation(file.RelativePath) &&
                !await GenLauncherChecksumValidator.ValidateFileAsync(destinationPath, expectedEtag, cancellationToken))
            {
                lastError = $"Checksum mismatch for {file.RelativePath}! Expected ETag: {expectedEtag}";
                logger.LogWarning("Attempt {Attempt}/{Max}: {Error}", attempt, maxAttempts, lastError);
                CleanupCorruptedFile(destinationPath);
                continue;
            }

            return OperationResult<bool>.CreateSuccess(true);
        }

        var safeLogUrl = RedactUrl(file.DownloadUrl);
        logger.LogError("Failed to download {File} from {Url} after {Max} attempts: {Error}", file.RelativePath, safeLogUrl, maxAttempts, lastError);
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
