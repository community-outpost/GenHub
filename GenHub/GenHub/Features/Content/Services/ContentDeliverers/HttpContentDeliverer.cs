using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Common;
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
    IPlaywrightService playwrightService,
    Func<IContentManifestBuilder> manifestBuilderFactory,
    IFileHashProvider fileHashProvider,
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
        if (manifest?.Files == null)
        {
            return false;
        }

        // Dependency-only packages (e.g. ContentBundle) have no remote files to fetch.
        if (manifest.Files.Count == 0)
        {
            return true;
        }

        // Can deliver if files have HTTP/HTTPS download URLs
        return manifest.Files.Any(f =>
            !string.IsNullOrEmpty(f.DownloadUrl) &&
            Uri.TryCreate(f.DownloadUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
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
            var builder = manifestBuilderFactory();
            var deliveredManifest = InitializeDeliveredManifest(builder, packageManifest);

            var filesToDownload = packageManifest.Files.Where(f => !string.IsNullOrEmpty(f.DownloadUrl)).ToList();
            var totalFiles = filesToDownload.Count;
            var processedFiles = 0;

            foreach (var file in filesToDownload)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var downloadResult = await DownloadFileItemAsync(
                    file,
                    targetDirectory,
                    packageManifest,
                    deliveredManifest,
                    progress,
                    processedFiles,
                    totalFiles,
                    cancellationToken);

                if (!downloadResult.Success)
                {
                    return downloadResult;
                }

                processedFiles++;
            }

            foreach (var file in packageManifest.Files.Where(f => string.IsNullOrEmpty(f.DownloadUrl)))
            {
                await deliveredManifest.AddLocalFileAsync(
                    file.RelativePath,
                    file.SourcePath ?? string.Empty,
                    ContentSourceType.ContentAddressable,
                    isExecutable: file.IsExecutable,
                    permissions: file.Permissions);
            }

            deliveredManifest.AddRequiredDirectories([.. packageManifest.RequiredDirectories]);

            if (packageManifest.InstallationInstructions != null)
            {
                deliveredManifest.WithInstallationInstructions(packageManifest.InstallationInstructions.WorkspaceStrategy);
            }

            return OperationResult<ContentManifest>.CreateSuccess(deliveredManifest.Build());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("HTTP delivery cancelled for manifest {ManifestId}", packageManifest.Id);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Validation failed for HTTP content manifest {ManifestId}", manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    private static IContentManifestBuilder InitializeDeliveredManifest(
        IContentManifestBuilder builder,
        ContentManifest packageManifest)
    {
        var idSegments = packageManifest.Id.Value.Split('.');
        var publisherId = idSegments.Length >= 3 ? idSegments[2] : "unknown";

        var deliveredManifest = builder
            .WithBasicInfo(publisherId, packageManifest.Name, packageManifest.Version)
            .WithContentType(packageManifest.ContentType, packageManifest.TargetGame)
            .WithPublisher(
                packageManifest.Publisher?.Name ?? string.Empty,
                packageManifest.Publisher?.Website ?? string.Empty,
                packageManifest.Publisher?.SupportUrl ?? string.Empty,
                packageManifest.Publisher?.ContactEmail ?? string.Empty,
                packageManifest.Publisher?.PublisherType ?? publisherId)
            .WithMetadata(
                packageManifest.Metadata?.Description ?? string.Empty,
                packageManifest.Metadata?.Tags,
                packageManifest.Metadata?.IconUrl ?? string.Empty,
                packageManifest.Metadata?.ScreenshotUrls,
                packageManifest.Metadata?.ChangelogUrl ?? string.Empty);

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

        return deliveredManifest;
    }

    private async Task<OperationResult<ContentManifest>> DownloadFileItemAsync(
        ManifestFile file,
        string targetDirectory,
        ContentManifest packageManifest,
        IContentManifestBuilder deliveredManifest,
        IProgress<ContentAcquisitionProgress>? progress,
        int processedFiles,
        int totalFiles,
        CancellationToken cancellationToken)
    {
        var pathResult = ContentPathPolicy.ResolveContainedFile(targetDirectory, file.RelativePath);
        if (!pathResult.Success)
        {
            return OperationResult<ContentManifest>.CreateFailure(pathResult);
        }

        var localPath = pathResult.Data;
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        progress?.Report(new ContentAcquisitionProgress
        {
            Phase = ContentAcquisitionPhase.Downloading,
            ProgressPercentage = (double)processedFiles / totalFiles * 100,
            CurrentOperation = $"Downloading {file.RelativePath}",
            CurrentFile = file.RelativePath,
            FilesProcessed = processedFiles,
            TotalFiles = totalFiles,
        });

        if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
            (downloadUri.Scheme != Uri.UriSchemeHttp && downloadUri.Scheme != Uri.UriSchemeHttps))
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Invalid remote download URL for {file.RelativePath}: '{file.DownloadUrl}'. Remote content must use HTTP/HTTPS.");
        }

        var downloadProgress = new Progress<DownloadProgress>(download =>
        {
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = totalFiles == 0
                    ? 0
                    : (((double)processedFiles + (download.Percentage / 100)) / totalFiles) * 100,
                CurrentOperation = $"Downloading {download.FormattedProgress} at {download.FormattedSpeed}",
                CurrentFile = file.RelativePath,
                BytesProcessed = download.BytesReceived,
                TotalBytes = download.TotalBytes,
                FilesProcessed = processedFiles,
                TotalFiles = totalFiles,
                EstimatedTimeRemaining = download.EstimatedTimeRemaining ?? TimeSpan.Zero,
            });
        });

        var downloadResult = await ExecuteFileDownloadAsync(downloadUri, localPath, file, packageManifest, downloadProgress, cancellationToken);
        if (!downloadResult.Success)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Failed to download {file.RelativePath}: {downloadResult.FirstError}");
        }

        await deliveredManifest.AddLocalFileAsync(
            file.RelativePath,
            localPath,
            ContentSourceType.ContentAddressable,
            isExecutable: file.IsExecutable,
            permissions: file.Permissions);

        return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
    }

    private async Task<DownloadResult> ExecuteFileDownloadAsync(
        Uri downloadUri,
        string localPath,
        ManifestFile file,
        ContentManifest packageManifest,
        IProgress<DownloadProgress> downloadProgress,
        CancellationToken cancellationToken)
    {
        if (IsModDbUrl(downloadUri))
        {
            var downloadResult = await DownloadProtectedModDbFileAsync(downloadUri, localPath, packageManifest, cancellationToken);
            if (downloadResult.Success && !string.IsNullOrEmpty(file.Hash))
            {
                var computedHash = await fileHashProvider.ComputeFileHashAsync(localPath, cancellationToken);
                if (!string.Equals(computedHash, file.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    return DownloadResult.CreateFailure($"Hash verification failed for {file.RelativePath}");
                }
            }

            return downloadResult;
        }

        return await downloadService.DownloadFileAsync(downloadUri, localPath, file.Hash, downloadProgress, cancellationToken);
    }

    private bool IsModDbUrl(Uri uri)
    {
        return uri.Host.Equals("moddb.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".moddb.com", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DownloadResult> DownloadProtectedModDbFileAsync(
        Uri downloadUri,
        string destinationPath,
        ContentManifest packageManifest,
        CancellationToken cancellationToken)
    {
        var configuration = new DownloadConfiguration
        {
            Url = downloadUri,
            DestinationPath = destinationPath,
            OverwriteExisting = true,
        };

        if (!string.IsNullOrWhiteSpace(packageManifest.Publisher?.SupportUrl))
        {
            configuration.Headers["Referer"] = packageManifest.Publisher.SupportUrl;
        }

        return await playwrightService.DownloadFileAsync(configuration, cancellationToken);
    }
}
