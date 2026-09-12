using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace GenHub.Features.Content.Services.GitHub;

/// <summary>
/// Delivers GitHub content with special handling for releases containing ZIP archives.
/// Uses publisher-specific manifest factories for extensible content handling.
/// </summary>
public class GitHubContentDeliverer(
    IDownloadService downloadService,
    IFileHashProvider hashProvider,
    ILogger<GitHubContentDeliverer> logger) : IContentDeliverer
{
    /// <inheritdoc />
    public string SourceName => ContentSourceNames.GitHubDeliverer;

    /// <inheritdoc />
    public string Description => GitHubConstants.GitHubDelivererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        // Can deliver if files have GitHub download URLs
        return manifest.Files.Any(f =>
            !string.IsNullOrEmpty(f.DownloadUrl) &&
            IsGitHubUrl(f.DownloadUrl));
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
            // Download all files (validate no duplicate paths to prevent data loss)
            var filesToDownload = packageManifest.Files
                .Where(f => !string.IsNullOrEmpty(f.DownloadUrl))
                .ToList();

            // Check for duplicate relative paths
            var duplicatePaths = filesToDownload
                .GroupBy(f => f.RelativePath)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatePaths.Count > 0)
            {
                logger.LogError(
                    "Manifest {ManifestId} contains duplicate relative paths: {Duplicates}. This would cause data loss.",
                    packageManifest.Id,
                    string.Join(", ", duplicatePaths));
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Manifest contains duplicate file paths that would cause data loss: {string.Join(", ", duplicatePaths)}");
            }

            var downloadedFiles = new List<string>();
            int currentFileIndex = 0;
            int totalFiles = filesToDownload.Count;

            foreach (var file in filesToDownload)
            {
                currentFileIndex++;
                var localPath = Path.Combine(targetDirectory, file.RelativePath);
                var localDir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }

                // Create progress adapter for download progress
                IProgress<DownloadProgress>? downloadProgress = null;
                if (progress != null)
                {
                    downloadProgress = new Progress<DownloadProgress>(dp =>
                    {
                        // The orchestrator owns the overall five-stage scale. Report only
                        // relative delivery progress so it cannot regress the stage display.
                        double currentProgress = ((currentFileIndex - 1) + (dp.Percentage / 100.0)) / totalFiles * 100;

                        progress.Report(new ContentAcquisitionProgress
                        {
                            Phase = ContentAcquisitionPhase.Downloading,
                            ProgressPercentage = currentProgress,
                            CurrentOperation = $"{file.RelativePath} ({currentFileIndex}/{totalFiles}) - {dp.Percentage:F0}% ({dp.FormattedSpeed})",
                            FilesProcessed = currentFileIndex - 1,
                            TotalFiles = totalFiles,
                            TotalBytes = dp.TotalBytes,
                            BytesProcessed = dp.BytesReceived,
                            CurrentFile = file.RelativePath,
                        });
                    });
                }

                var downloadResult = await downloadService.DownloadFileAsync(
                    new Uri(file.DownloadUrl!), localPath, file.Hash, downloadProgress, cancellationToken);

                if (!downloadResult.Success)
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Failed to download {file.RelativePath}: {downloadResult.FirstError}");
                }

                downloadedFiles.Add(localPath);
                logger.LogInformation("Downloaded {FileName} to {Path}", file.RelativePath, localPath);
            }

            // Check if this is content with archive files (ZIP, 7z, tar.gz, etc.)
            var archiveFiles = downloadedFiles
                .Where(IsArchiveFile)
                .ToList();

            if (archiveFiles.Count > 0)
            {
                logger.LogInformation(
                    "Content detected with {Count} archive file(s). Extracting...",
                    archiveFiles.Count);

                // Extract all archives using SharpCompress
                foreach (var archiveFile in archiveFiles)
                {
                    try
                    {
                        await ExtractArchiveAsync(
                            archiveFile,
                            targetDirectory,
                            progress,
                            cancellationToken);

                        logger.LogInformation("Extracted {ArchiveFile}", Path.GetFileName(archiveFile));
                        File.Delete(archiveFile);
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogInformation(
                            "Extraction of {ArchiveFile} was cancelled; the downloaded archive is left in place",
                            Path.GetFileName(archiveFile));
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to extract {ArchiveFile}", Path.GetFileName(archiveFile));
                        return OperationResult<ContentManifest>.CreateFailure($"Failed to extract archive: {ex.Message}");
                    }
                }

                logger.LogInformation(
                    "Successfully extracted {Count} archive file(s) for {ManifestId}. Deferring manifest generation to the orchestrator.",
                    archiveFiles.Count,
                    packageManifest.Id);

                return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
            }

            // For content without archives, compute hashes directly for downloaded files
            foreach (var file in filesToDownload)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var localPath = Path.Combine(targetDirectory, file.RelativePath);
                if (File.Exists(localPath))
                {
                    file.Hash = await hashProvider.ComputeFileHashAsync(localPath, cancellationToken);
                    file.Size = new FileInfo(localPath).Length;
                    file.SourceType = ContentSourceType.ContentAddressable;
                }
            }

            return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver GitHub content for manifest {ManifestId}", packageManifest.Id);
            try
            {
                if (Directory.Exists(targetDirectory))
                {
                    Directory.Delete(targetDirectory, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup
            }

            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate that all required URLs are GitHub URLs
            foreach (var file in manifest.Files.Where(f => f.IsRequired && !string.IsNullOrEmpty(f.DownloadUrl)))
            {
                if (file.DownloadUrl != null && !IsGitHubUrl(file.DownloadUrl))
                {
                    return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
                }
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Validation failed for GitHub content manifest {ManifestId}", manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Validates that a URL is a legitimate GitHub URL.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <returns>True if the URL is a GitHub URL, false otherwise.</returns>
    private static bool IsGitHubUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines if a file is a supported archive format.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if the file is a supported archive format, false otherwise.</returns>
    private static bool IsArchiveFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext == FileTypes.ZipFileExtension ||
               ext == FileTypes.SevenZipFileExtension ||
               ext == FileTypes.TarFileExtension ||
               ext == FileTypes.GzipFileExtension ||
               ext == FileTypes.RarFileExtension;
    }

    /// <summary>
    /// Extracts an archive file asynchronously to prevent UI blocking. Release archives are remote
    /// input, so every entry is confined to <paramref name="targetDirectory"/> and the archive is
    /// held to entry-count and expansion budgets measured against the bytes actually decompressed.
    /// </summary>
    /// <param name="archiveFile">Path to the archive file.</param>
    /// <param name="targetDirectory">Directory to extract files to.</param>
    /// <param name="progress">Progress reporter for extraction updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ExtractArchiveAsync(
        string archiveFile,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            async () =>
            {
                using var archive = ArchiveFactory.OpenArchive(new FileInfo(archiveFile));
                var fileEntries = archive.Entries.Where(e => !e.IsDirectory).ToList();

                if (fileEntries.Count > GitHubConstants.MaxArchiveEntries)
                {
                    throw new InvalidOperationException(
                        $"Archive contains too many entries ({fileEntries.Count} > {GitHubConstants.MaxArchiveEntries}).");
                }

                int totalEntries = fileEntries.Count;
                int currentEntry = 0;
                long expandedBytes = 0;
                long expansionBudget = Math.Min(
                    GitHubConstants.MaxAggregateUncompressedBytes,
                    Math.Max(
                        GitHubConstants.MinArchiveExpansionBudgetBytes,
                        new FileInfo(archiveFile).Length * GitHubConstants.MaxArchiveExpansionRatio));

                foreach (var entry in fileEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!ArchiveEntryName.IsExtractable(entry.Key))
                    {
                        throw new InvalidOperationException(
                            $"Archive entry '{entry.Key}' has a name that cannot be extracted to a file.");
                    }

                    var destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, entry.Key));
                    if (!PathHelper.IsPathWithinDirectory(targetDirectory, destinationPath))
                    {
                        throw new InvalidOperationException($"Zip slip vulnerability detected: entry '{entry.Key}' attempts to extract outside target directory.");
                    }

                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    await using (var entryStream = entry.OpenEntryStream())
                    {
                        expandedBytes += await BoundedArchiveExtractor.CopyEntryToFileAsync(
                            entryStream,
                            destinationPath,
                            entry.Key,
                            GitHubConstants.MaxEntryUncompressedBytes,
                            expansionBudget - expandedBytes,
                            overwrite: true,
                            cancellationToken);
                    }

                    currentEntry++;

                    // Map extraction progress from ProgressStepValidatingFiles to ProgressStepExtracting
                    double extractStart = ContentConstants.ProgressStepValidatingFiles;
                    double extractEnd = ContentConstants.ProgressStepExtracting;
                    double progressRange = extractEnd - extractStart;
                    double currentPercentage = extractStart + ((double)currentEntry / totalEntries * progressRange);

                    progress?.Report(
                        new ContentAcquisitionProgress
                        {
                            Phase = ContentAcquisitionPhase.Extracting,
                            ProgressPercentage = currentPercentage,
                            CurrentOperation = $"{Path.GetFileName(entry.Key)} ({currentEntry}/{totalEntries})",
                            FilesProcessed = currentEntry,
                            TotalFiles = totalEntries,
                            CurrentFile = Path.GetFileName(entry.Key) ?? string.Empty,
                        });
                }
            },
            cancellationToken);
    }
}
