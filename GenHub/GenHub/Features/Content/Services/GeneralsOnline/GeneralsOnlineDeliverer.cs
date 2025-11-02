using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Specialized deliverer for Generals Online content.
/// Downloads ZIP packages, extracts files, and creates dual variant manifests (30Hz/60Hz).
/// </summary>
public class GeneralsOnlineDeliverer : IContentDeliverer
{
    private readonly IDownloadService _downloadService;
    private readonly ICasService _casService;
    private readonly IFileHashProvider _hashProvider;
    private readonly IContentManifestBuilder _manifestBuilder;
    private readonly IContentManifestPool _manifestPool;
    private readonly ILogger<GeneralsOnlineDeliverer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineDeliverer"/> class.
    /// </summary>
    /// <param name="downloadService">The download service for retrieving files.</param>
    /// <param name="casService">The CAS service for content-addressable storage.</param>
    /// <param name="hashProvider">The file hash provider for computing file hashes.</param>
    /// <param name="manifestBuilder">The manifest builder for creating manifests.</param>
    /// <param name="manifestPool">The manifest pool for registering created manifests.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    public GeneralsOnlineDeliverer(
        IDownloadService downloadService,
        ICasService casService,
        IFileHashProvider hashProvider,
        IContentManifestBuilder manifestBuilder,
        IContentManifestPool manifestPool,
        ILogger<GeneralsOnlineDeliverer> logger)
    {
        _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
        _casService = casService ?? throw new ArgumentNullException(nameof(casService));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _manifestBuilder = manifestBuilder ?? throw new ArgumentNullException(nameof(manifestBuilder));
        _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string SourceName => "Generals Online Deliverer";

    /// <inheritdoc />
    public string Description => "Delivers Generals Online content via ZIP extraction and CAS storage";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        // Can deliver if it's a Generals Online manifest with a portable ZIP URL
        return manifest.Publisher?.Name?.Equals(GeneralsOnlineConstants.PublisherName, StringComparison.OrdinalIgnoreCase) == true &&
               manifest.Files.Any(f => f.DownloadUrl?.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase) == true);
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
            _logger.LogInformation("Starting Generals Online content delivery for {Version}", packageManifest.Version);

            // Step 1: Download ZIP file
            var zipFile = packageManifest.Files.FirstOrDefault(f => f.DownloadUrl?.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase) == true);
            if (zipFile == null)
            {
                return OperationResult<ContentManifest>.CreateFailure("No ZIP file found in manifest");
            }

            var zipPath = Path.Combine(targetDirectory, "GeneralsOnline.zip");

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = 10,
                CurrentOperation = "Downloading Generals Online ZIP package",
                CurrentFile = zipFile.RelativePath,
            });

            _logger.LogDebug("Downloading ZIP from {Url} to {Path}", zipFile.DownloadUrl, zipPath);
            var downloadResult = await _downloadService.DownloadFileAsync(
                zipFile.DownloadUrl!,
                zipPath,
                expectedHash: null,
                progress: null,
                cancellationToken);

            if (!downloadResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to download ZIP: {downloadResult.FirstError}");
            }

            // Step 2: Extract ZIP
            var extractPath = Path.Combine(targetDirectory, "extracted");
            Directory.CreateDirectory(extractPath);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                ProgressPercentage = 40,
                CurrentOperation = "Extracting Generals Online files",
            });

            _logger.LogDebug("Extracting ZIP to {Path}", extractPath);
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

            // Step 3: Create release info from package manifest
            var release = CreateReleaseFromManifest(packageManifest);

            // Step 4: Create both variant manifests (30Hz and 60Hz)
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 50,
                CurrentOperation = "Creating manifests for both variants",
            });

            _logger.LogInformation("Creating dual manifests for GeneralsOnline variants");
            var manifests = GeneralsOnlineManifestFactory.CreateManifests(release);

            if (manifests.Count != 2)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Expected 2 manifests but got {manifests.Count}");
            }

            // Step 5: Update both manifests with extracted files
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 60,
                CurrentOperation = "Processing extracted files and updating manifests",
            });

            manifests = await GeneralsOnlineManifestFactory.UpdateManifestsWithExtractedFiles(
                manifests,
                extractPath,
                _logger,
                cancellationToken);

            // Step 6: Register BOTH variant manifests to CAS
            // This ensures all files (including both executables) are stored before validation
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 90,
                CurrentOperation = "Registering both variant manifests to content library",
            });

            _logger.LogInformation("Registering both variant manifests (30Hz and 60Hz) in pool");

            // Register 30Hz manifest first
            var hz30Manifest = manifests[0];
            var add30Result = await _manifestPool.AddManifestAsync(hz30Manifest, extractPath, cancellationToken);
            if (!add30Result.Success)
            {
                _logger.LogWarning(
                    "Failed to register 30Hz manifest {ManifestId}: {Error}",
                    hz30Manifest.Id,
                    add30Result.FirstError);
            }
            else
            {
                _logger.LogInformation("Successfully registered 30Hz manifest: {ManifestId}", hz30Manifest.Id);
            }

            // Register 60Hz manifest
            var hz60Manifest = manifests[1];
            var add60Result = await _manifestPool.AddManifestAsync(hz60Manifest, extractPath, cancellationToken);
            if (!add60Result.Success)
            {
                _logger.LogWarning(
                    "Failed to register 60Hz manifest {ManifestId}: {Error}",
                    hz60Manifest.Id,
                    add60Result.FirstError);
            }
            else
            {
                _logger.LogInformation("Successfully registered 60Hz manifest: {ManifestId}", hz60Manifest.Id);
            }

            var parentDir = Directory.GetParent(extractPath)?.FullName;
            if (parentDir != null)
            {
                _logger.LogInformation("Moving extracted files from {ExtractPath} to parent {ParentDir}", extractPath, parentDir);
                foreach (var file in Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(extractPath, file);
                    var targetPath = Path.Combine(parentDir, relativePath);
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Move(file, targetPath, overwrite: true);
                }

                try
                {
                    Directory.Delete(extractPath, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete extracted directory {ExtractPath}", extractPath);
                }
            }

            try
            {
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete ZIP file {ZipPath}", zipPath);
            }

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Completed,
                ProgressPercentage = 100,
                CurrentOperation = "Generals Online content delivered successfully (both variants)",
            });

            var primaryManifest = manifests[0];
            _logger.LogInformation(
                "Successfully delivered Generals Online content: 2 manifests created, both registered to pool");

            return OperationResult<ContentManifest>.CreateSuccess(primaryManifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver Generals Online content");
            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hasZipFile = manifest.Files.Any(f =>
                !string.IsNullOrEmpty(f.DownloadUrl) &&
                f.DownloadUrl.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(OperationResult<bool>.CreateSuccess(hasZipFile));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for Generals Online manifest {ManifestId}", manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    private static GeneralsOnlineRelease CreateReleaseFromManifest(ContentManifest manifest)
    {
        // Extract release info from package manifest
        var zipFile = manifest.Files.FirstOrDefault(f =>
            f.DownloadUrl?.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase) == true);

        return new GeneralsOnlineRelease
        {
            Version = manifest.Version ?? "unknown",
            VersionDate = DateTime.Now,
            ReleaseDate = DateTime.Now,
            PortableUrl = zipFile?.DownloadUrl ?? string.Empty,
            PortableSize = zipFile?.Size ?? GeneralsOnlineConstants.DefaultPortableSize,
            Changelog = manifest.Metadata?.Description,
        };
    }
}
