using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
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
public class HttpContentDeliverer(IDownloadService downloadService, ILogger<HttpContentDeliverer> logger) : IContentDeliverer
{
    private readonly IDownloadService _downloadService = downloadService;
    private readonly ILogger<HttpContentDeliverer> _logger = logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContentDeliverer"/> class with optional legacy dependencies.
    /// </summary>
    /// <param name="downloadService">The download service.</param>
    /// <param name="playwrightService">The playwright service (unused).</param>
    /// <param name="manifestBuilderFactory">The manifest builder factory (unused).</param>
    /// <param name="fileHashProvider">The file hash provider (unused).</param>
    /// <param name="logger">The logger.</param>
    public HttpContentDeliverer(
        IDownloadService downloadService,
        IPlaywrightService? playwrightService,
        Func<IContentManifestBuilder>? manifestBuilderFactory,
        IFileHashProvider? fileHashProvider,
        ILogger<HttpContentDeliverer> logger)
        : this(downloadService, logger)
    {
    }

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
        if (manifest?.Files == null)
        {
            return false;
        }

        // Dependency-only packages (e.g. ContentBundle) have no remote files to fetch.
        if (manifest.Files.Count == 0)
        {
            return true;
        }

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
            var filesToDownload = packageManifest.Files.Where(f => !string.IsNullOrEmpty(f.DownloadUrl)).ToList();
            var totalFiles = filesToDownload.Count;
            var processedFiles = 0;

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

                // Report progress
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Downloading,
                    ProgressPercentage = (double)processedFiles / totalFiles * 100,
                    CurrentOperation = $"Downloading {file.RelativePath}",
                    CurrentFile = file.RelativePath,
                    FilesProcessed = processedFiles,
                    TotalFiles = totalFiles,
                });

                // Download the file
                var downloadResult = await _downloadService.DownloadFileAsync(
                    new Uri(file.DownloadUrl!), localPath, file.Hash, null, cancellationToken);

                if (!downloadResult.Success)
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Failed to download {file.RelativePath}: {downloadResult.FirstError}");
                }

                cancellationToken.ThrowIfCancellationRequested();
                processedFiles++;
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
            _logger.LogError(ex, "Failed to deliver HTTP content for manifest {ManifestId}", packageManifest.Id);
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
            _logger.LogError(ex, "Validation failed for HTTP content manifest {ManifestId}", manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
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
}
