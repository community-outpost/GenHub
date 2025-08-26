using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
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

namespace GenHub.Features.Content.Services.ContentDeliverers;

/// <summary>
/// Delivers remote HTTP content.
/// Pure delivery - downloads and extracts content.
/// </summary>
public class HttpContentDeliverer(IDownloadService downloadService, IContentManifestBuilder manifestBuilder, ILogger<HttpContentDeliverer> logger) : IContentDeliverer
{
    private readonly IDownloadService _downloadService = downloadService;
    private readonly IContentManifestBuilder _manifestBuilder = manifestBuilder;
    private readonly ILogger<HttpContentDeliverer> _logger = logger;

    /// <inheritdoc />
    public string SourceName => "HTTP Content Deliverer";

    /// <inheritdoc />
    public string Description => "Delivers content from HTTP/HTTPS URLs";

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
    public async Task<ContentOperationResult<ContentManifest>> DeliverContentAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use builder to create delivered manifest
            var deliveredManifest = _manifestBuilder
                .WithBasicInfo(packageManifest.Id, packageManifest.Name, packageManifest.Version)
                .WithContentType(packageManifest.ContentType, packageManifest.TargetGame)
                .WithPublisher(
                    packageManifest.Publisher?.Name ?? string.Empty,
                    packageManifest.Publisher?.Website ?? string.Empty,
                    packageManifest.Publisher?.SupportUrl ?? string.Empty,
                    packageManifest.Publisher?.ContactEmail ?? string.Empty)
                .WithMetadata(
                    packageManifest.Metadata?.Description ?? string.Empty,
                    packageManifest.Metadata?.Tags,
                    packageManifest.Metadata?.IconUrl ?? string.Empty,
                    packageManifest.Metadata?.ScreenshotUrls,
                    packageManifest.Metadata?.ChangelogUrl ?? string.Empty);

            // Add dependencies
            foreach (var dep in packageManifest.Dependencies)
            {
                deliveredManifest.AddDependency(
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

            var filesToDownload = packageManifest.Files.Where(f => !string.IsNullOrEmpty(f.DownloadUrl)).ToList();
            var totalFiles = filesToDownload.Count;
            var processedFiles = 0;

            // Download and add files
            foreach (var file in filesToDownload)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var localPath = Path.Combine(targetDirectory, file.RelativePath);

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
                    file.DownloadUrl!, localPath, file.Hash, null, cancellationToken);

                if (!downloadResult.Success)
                {
                    return ContentOperationResult<ContentManifest>.CreateFailure(
                        $"Failed to download {file.RelativePath}: {downloadResult.ErrorMessage}");
                }

                // Add the delivered file using the builder
                await deliveredManifest.AddFileAsync(
                    file.RelativePath,
                    ManifestFileSourceType.Content,
                    isExecutable: file.IsExecutable,
                    permissions: file.Permissions);

                processedFiles++;
            }

            // Add any other files (without DownloadUrl) as-is
            foreach (var file in packageManifest.Files.Where(f => string.IsNullOrEmpty(f.DownloadUrl)))
            {
                await deliveredManifest.AddFileAsync(
                    file.RelativePath,
                    ManifestFileSourceType.Content,
                    isExecutable: file.IsExecutable,
                    permissions: file.Permissions);
            }

            // Add required directories
            deliveredManifest.AddRequiredDirectories(packageManifest.RequiredDirectories.ToArray());

            // Add installation instructions if present
            if (packageManifest.InstallationInstructions != null)
            {
                deliveredManifest.WithInstallationInstructions(packageManifest.InstallationInstructions.WorkspaceStrategy);
            }

            return ContentOperationResult<ContentManifest>.CreateSuccess(deliveredManifest.Build());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver HTTP content for manifest {ManifestId}", packageManifest.Id);
            return ContentOperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<ContentOperationResult<bool>> ValidateContentAsync(
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
                    return Task.FromResult(ContentOperationResult<bool>.CreateSuccess(false));
                }
            }

            return Task.FromResult(ContentOperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for HTTP content manifest {ManifestId}", manifest.Id);
            return Task.FromResult(ContentOperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }
}
