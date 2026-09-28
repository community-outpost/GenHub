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
using Microsoft.Extensions.Logging;
using System;
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
    IPlaywrightService? playwrightService = null) : IContentDeliverer
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
        if (manifest == null)
        {
            return false;
        }

        var files = manifest.Files;

        // Dependency-only packages (bundles or meta-packages) have no remote files to fetch,
        // but must declare dependencies to be deliverable.
        if ((files?.Count ?? 0) == 0)
        {
            return manifest.Dependencies is { Count: > 0 };
        }

        // Can deliver if files have HTTP download URLs
        return files!.Any(f =>
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
            var filesToDownload = packageManifest.Files?.Where(f => !string.IsNullOrEmpty(f.DownloadUrl)).ToList() ?? [];
            var totalFiles = filesToDownload.Count;
            var processedFiles = 0;

            if (totalFiles == 0)
            {
                logger.LogInformation(
                    "Manifest {ManifestId} has no remote files to download (dependency-only bundle); delivery succeeded",
                    packageManifest.Id);
                return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
            }

            // Download and add files
            foreach (var file in filesToDownload)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var localPath = ResolveTargetPath(targetDirectory, file.RelativePath);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var currentFileIndex = processedFiles + 1;

                IProgress<DownloadProgress>? downloadProgress = null;
                if (progress != null)
                {
                    downloadProgress = new Progress<DownloadProgress>(dp =>
                    {
                        double fileProgressRange = 100.0 / totalFiles;
                        double baseProgress = (currentFileIndex - 1) * fileProgressRange;
                        double currentProgress = Math.Clamp(baseProgress + (dp.Percentage / 100.0 * fileProgressRange), 0, 100);

                        progress.Report(new ContentAcquisitionProgress
                        {
                            Phase = ContentAcquisitionPhase.Downloading,
                            ProgressPercentage = currentProgress,
                            CurrentOperation = totalFiles > 1
                                ? $"{file.RelativePath} ({currentFileIndex}/{totalFiles}) - {dp.Percentage:F0}% ({dp.FormattedSpeed})"
                                : $"{file.RelativePath} - {dp.Percentage:F0}% ({dp.FormattedSpeed})",
                            FilesProcessed = currentFileIndex - 1,
                            TotalFiles = totalFiles,
                            TotalBytes = dp.TotalBytes,
                            BytesProcessed = dp.BytesReceived,
                            CurrentFile = file.RelativePath,
                        });
                    });
                }

                // Initial report before download starts
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Downloading,
                    ProgressPercentage = (double)processedFiles / totalFiles * 100,
                    CurrentOperation = totalFiles > 1
                        ? $"Downloading {file.RelativePath} ({currentFileIndex}/{totalFiles})..."
                        : $"Downloading {file.RelativePath}...",
                    CurrentFile = file.RelativePath,
                    FilesProcessed = processedFiles,
                    TotalFiles = totalFiles,
                });

                // Download the file
                var downloadResult = await DownloadFileAsync(packageManifest, file, localPath, downloadProgress, cancellationToken);

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

                var currentPercentage = (double)processedFiles / totalFiles * 100;
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Downloading,
                    ProgressPercentage = currentPercentage,
                    CurrentOperation = $"Downloaded {file.RelativePath} ({processedFiles}/{totalFiles})",
                    CurrentFile = file.RelativePath,
                    FilesProcessed = processedFiles,
                    TotalFiles = totalFiles,
                });
            }

            // Extract archives if needed
            foreach (var file in filesToDownload)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var localPath = ResolveTargetPath(targetDirectory, file.RelativePath);

                if (IsArchive(localPath))
                {
                    progress?.Report(new ContentAcquisitionProgress
                    {
                        Phase = ContentAcquisitionPhase.Extracting,
                        ProgressPercentage = 0,
                        CurrentOperation = $"Extracting {file.RelativePath}...",
                        CurrentFile = file.RelativePath,
                    });

                    // Extraction logic would go here
                    // For now, assume files are ready to use
                }
            }

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
            // Validate that all required URLs are accessible
            foreach (var file in manifest.Files.Where(f => f.IsRequired && !string.IsNullOrEmpty(f.DownloadUrl)))
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
