using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDeliverers;

/// <summary>
/// Delivers remote HTTP content.
/// Pure delivery - downloads and extracts content.
/// </summary>
public class HttpContentDeliverer(
    IDownloadService downloadService,
    ILogger<HttpContentDeliverer> logger,
    IPlaywrightService? playwrightService = null,
    IArchivePayloadProcessor? archivePayloadProcessor = null) : IContentDeliverer
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
        if (manifest == null || !ManifestVariantResolver.SupportsRuntime(manifest))
        {
            return false;
        }

        var files = ManifestVariantResolver.ResolveFiles(manifest);

        // Dependency-only packages (bundles or meta-packages) have no remote files to fetch,
        // but must declare dependencies to be deliverable.
        if (files.Count == 0)
        {
            return manifest.Dependencies is { Count: > 0 };
        }

        // Can deliver if files have HTTP download URLs
        return files.Any(f =>
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
            if (!ManifestVariantResolver.SupportsRuntime(packageManifest))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Manifest {packageManifest.Id} has no variant for runtime {ManifestVariantResolver.CurrentRuntimeIdentifier}.");
            }

            var filesToDownload = ManifestVariantResolver.ResolveFiles(packageManifest)
                .Where(f => !string.IsNullOrEmpty(f.DownloadUrl))
                .ToList();
            if (filesToDownload.Count == 0)
            {
                logger.LogInformation(
                    "Manifest {ManifestId} has no remote files to download (dependency-only bundle); delivery succeeded",
                    packageManifest.Id);
                return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
            }

            var downloadResult = await DownloadAllFilesAsync(packageManifest, filesToDownload, targetDirectory, progress, cancellationToken);
            if (!downloadResult.Success)
            {
                return downloadResult;
            }

            await ExtractArchivesAsync(packageManifest, filesToDownload, targetDirectory, progress, cancellationToken);

            // Delivery changes filesystem state only. The resolved manifest remains authoritative
            // for identity, version, hashes, source types, and installation metadata.
            return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
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
            if (!ManifestVariantResolver.SupportsRuntime(manifest))
            {
                return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
            }

            // Validate that all required URLs are accessible
            foreach (var file in ManifestVariantResolver.ResolveFiles(manifest).Where(f => f.IsRequired && !string.IsNullOrEmpty(f.DownloadUrl)))
            {
                if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var uri) ||
                    !(uri.Scheme == "http" || uri.Scheme == "https") ||
                    (ModDBConstants.IsModDbOrDbolicalUri(uri) && uri.Scheme != Uri.UriSchemeHttps))
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

    private static IProgress<DownloadProgress>? CreateFileDownloadProgress(
        IProgress<ContentAcquisitionProgress>? progress,
        string relativePath,
        int currentFileIndex,
        int totalFiles,
        long previousFilesBytes,
        long totalBytesAllFiles)
    {
        if (progress == null)
        {
            return null;
        }

        return new Progress<DownloadProgress>(dp =>
        {
            double currentProgress = 0.0;
            long aggregateBytesProcessed = 0L;
            long aggregateTotalBytes = 0L;

            if (totalBytesAllFiles > 0)
            {
                aggregateBytesProcessed = previousFilesBytes + dp.BytesReceived;
                aggregateTotalBytes = totalBytesAllFiles;
                currentProgress = Math.Clamp((double)aggregateBytesProcessed / totalBytesAllFiles * 100.0, 0, 100);
            }
            else
            {
                double fileProgressRange = 100.0 / totalFiles;
                double baseProgress = (currentFileIndex - 1) * fileProgressRange;
                currentProgress = Math.Clamp(baseProgress + (dp.Percentage / 100.0 * fileProgressRange), 0, 100);
                aggregateBytesProcessed = dp.BytesReceived;
                aggregateTotalBytes = dp.TotalBytes;
            }

            progress.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = currentProgress,
                CurrentOperation = totalFiles > 1
                    ? $"{relativePath} ({currentFileIndex}/{totalFiles}) - {dp.Percentage:F0}% ({dp.FormattedSpeed})"
                    : $"{relativePath} - {dp.Percentage:F0}% ({dp.FormattedSpeed})",
                FilesProcessed = currentFileIndex - 1,
                TotalFiles = totalFiles,
                TotalBytes = aggregateTotalBytes,
                BytesProcessed = aggregateBytesProcessed,
                CurrentFile = relativePath,
            });
        });
    }

    private static void ExtractArchivesFallback(
        IEnumerable<ManifestFile> filesToDownload,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken,
        ILogger logger)
    {
        foreach (var relativePath in filesToDownload.Select(file => file.RelativePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var localPath = ResolveTargetPath(targetDirectory, relativePath);
            if (!IsArchive(localPath) || !File.Exists(localPath))
            {
                continue;
            }

            // The fallback extractor only handles zip archives: anything else would be
            // left packed while delivery reports success, so surface it explicitly.
            if (!ZipValidation.IsValidZipFile(localPath))
            {
                logger.LogWarning("Skipping extraction of non-zip archive '{Path}': no archive processor is configured", localPath);
                continue;
            }

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                ProgressPercentage = 0,
                CurrentOperation = $"Extracting {relativePath}...",
                CurrentFile = relativePath,
            });

            var destDir = Path.GetDirectoryName(localPath) ?? targetDirectory;
            ZipArchiveGuard.ExtractToDirectory(localPath, destDir, cancellationToken);
            File.Delete(localPath);
        }
    }

    private static bool IsArchive(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".zip" or ".tar" or ".gz" or ".7z" or ".rar";
    }

    private static string ResolveTargetPath(string targetDirectory, string relativePath)
    {
        var targetRoot = Path.GetFullPath(targetDirectory);
        var targetPath = Path.GetFullPath(relativePath, targetRoot);
        var relativeTargetPath = Path.GetRelativePath(targetRoot, targetPath);

        if (relativeTargetPath.Equals("..", StringComparison.Ordinal) ||
            relativeTargetPath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativeTargetPath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeTargetPath))
        {
            throw new InvalidOperationException(
                $"Content path '{relativePath}' resolves outside target directory.");
        }

        return targetPath;
    }

    private async Task<OperationResult<ContentManifest>> DownloadAllFilesAsync(
        ContentManifest packageManifest,
        IReadOnlyList<ManifestFile> filesToDownload,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalFiles = filesToDownload.Count;
        var processedFiles = 0;
        var totalBytesAllFiles = filesToDownload.Sum(f => f.Size > 0 ? f.Size : 0L);
        var previousFilesBytes = 0L;

        foreach (var file in filesToDownload)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentFileIndex = processedFiles + 1;
            var progressContext = new FileProgressContext(
                currentFileIndex,
                totalFiles,
                previousFilesBytes,
                totalBytesAllFiles);

            var downloadResult = await DownloadSingleFileWithProgressAsync(
                packageManifest,
                file,
                targetDirectory,
                progressContext,
                progress,
                cancellationToken);

            if (!downloadResult.Success)
            {
                logger.LogError(
                    "Failed to download file {File}: {Error}",
                    file.RelativePath,
                    downloadResult.FirstError);
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to download file: {downloadResult.FirstError}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            processedFiles++;
            if (file.Size > 0)
            {
                previousFilesBytes += file.Size;
            }

            var currentPercentage = totalBytesAllFiles > 0
                ? Math.Clamp((double)previousFilesBytes / totalBytesAllFiles * 100.0, 0, 100)
                : (double)processedFiles / totalFiles * 100;

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = currentPercentage,
                CurrentOperation = $"Downloaded {file.RelativePath} ({processedFiles}/{totalFiles})",
                CurrentFile = file.RelativePath,
                FilesProcessed = processedFiles,
                TotalFiles = totalFiles,
                TotalBytes = totalBytesAllFiles > 0 ? totalBytesAllFiles : 0,
                BytesProcessed = previousFilesBytes,
            });
        }

        return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
    }

    private sealed record FileProgressContext(
        int CurrentFileIndex,
        int TotalFiles,
        long PreviousFilesBytes,
        long TotalBytesAllFiles);

    private async Task<DownloadResult> DownloadSingleFileWithProgressAsync(
        ContentManifest packageManifest,
        ManifestFile file,
        string targetDirectory,
        FileProgressContext progressContext,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var localPath = ResolveTargetPath(targetDirectory, file.RelativePath);
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var downloadProgress = CreateFileDownloadProgress(
            progress,
            file.RelativePath,
            progressContext.CurrentFileIndex,
            progressContext.TotalFiles,
            progressContext.PreviousFilesBytes,
            progressContext.TotalBytesAllFiles);

        var startingProgress = progressContext.TotalBytesAllFiles > 0
            ? Math.Clamp((double)progressContext.PreviousFilesBytes / progressContext.TotalBytesAllFiles * 100.0, 0, 100)
            : (double)(progressContext.CurrentFileIndex - 1) / progressContext.TotalFiles * 100;

        progress?.Report(new ContentAcquisitionProgress
        {
            Phase = ContentAcquisitionPhase.Downloading,
            ProgressPercentage = startingProgress,
            CurrentOperation = progressContext.TotalFiles > 1
                ? $"Connecting to download {file.RelativePath} ({progressContext.CurrentFileIndex}/{progressContext.TotalFiles})..."
                : $"Connecting to download {file.RelativePath}...",
            CurrentFile = file.RelativePath,
            FilesProcessed = progressContext.CurrentFileIndex - 1,
            TotalFiles = progressContext.TotalFiles,
            TotalBytes = progressContext.TotalBytesAllFiles > 0 ? progressContext.TotalBytesAllFiles : 0,
            BytesProcessed = progressContext.PreviousFilesBytes,
        });

        return await DownloadFileAsync(packageManifest, file, localPath, downloadProgress, cancellationToken);
    }

    private async Task ExtractArchivesAsync(
        ContentManifest packageManifest,
        IReadOnlyList<ManifestFile> filesToDownload,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (archivePayloadProcessor != null)
        {
            await archivePayloadProcessor.ExtractArchivesSafelyAsync(
                targetDirectory,
                packageManifest.ContentType,
                progress,
                cancellationToken);
            return;
        }

        ExtractArchivesFallback(filesToDownload, targetDirectory, progress, cancellationToken, logger);
    }

    private async Task<DownloadResult> DownloadFileAsync(
        ContentManifest manifest,
        ManifestFile file,
        string localPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var fileUri) &&
            ModDBConstants.IsModDbOrDbolicalUri(fileUri))
        {
            if (fileUri.Scheme != Uri.UriSchemeHttps)
            {
                return DownloadResult.CreateFailure("ModDB and DBolical downloads must use HTTPS.");
            }

            if (playwrightService != null)
            {
                logger.LogInformation("Routing ModDB download through Playwright for {Url}", file.DownloadUrl);
                var playwrightConfig = new DownloadConfiguration
                {
                    Url = fileUri,
                    DestinationPath = localPath,
                    OverwriteExisting = true,
                    ExpectedHash = file.Hash,
                };
                DownloadTelemetryHelper.ApplyManifestAttribution(playwrightConfig, manifest);
                return await playwrightService.DownloadFileAsync(playwrightConfig, cancellationToken);
            }

            return await DownloadAttributedFileAsync(manifest, fileUri, localPath, file.Hash, progress, cancellationToken);
        }

        return await DownloadAttributedFileAsync(manifest, new Uri(file.DownloadUrl!), localPath, file.Hash, progress, cancellationToken);
    }

    private async Task<DownloadResult> DownloadAttributedFileAsync(
        ContentManifest manifest,
        Uri fileUri,
        string localPath,
        string? expectedHash,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var downloadConfig = new DownloadConfiguration
        {
            Url = fileUri,
            DestinationPath = localPath,
            ExpectedHash = expectedHash,
        };
        DownloadTelemetryHelper.ApplyManifestAttribution(downloadConfig, manifest);
        return await downloadService.DownloadFileAsync(downloadConfig, progress, cancellationToken);
    }
}
