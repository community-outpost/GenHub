using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Specialized deliverer for Community Outpost content.
/// Downloads packages (ZIP or 7z/.dat files), extracts files, and creates manifests via factory.
/// Supports multiple download mirrors for fallback.
/// </summary>
public class CommunityOutpostDeliverer(
   IDownloadService downloadService,
   IContentManifestPool manifestPool,
   CommunityOutpostManifestFactory manifestFactory,
   IGameInstallationService installationService,
   IUserSettingsService userSettingsService,
   ICasPoolManager? casPoolManager,
   CompressedImageToTgaConverter avifConverter,
   ILogger<CommunityOutpostDeliverer> logger)
   : IContentDeliverer
{
    private static (string Code, GenPatcherContentMetadata Metadata) NormalizeContentCode(string contentCode)
    {
        // For some content (like cbprc), the code may have a language suffix (e - english)
        // Strip it if it's there and try that way too
        var actualContentCode = contentCode.ToLowerInvariant();
        var depMetadata = GenPatcherContentRegistry.GetMetadata(actualContentCode);

        if (depMetadata.ContentType == ContentType.UnknownContentType && actualContentCode.Length == 5)
        {
            var strippedCode = actualContentCode[..4];
            var strippedMetadata = GenPatcherContentRegistry.GetMetadata(strippedCode);
            if (strippedMetadata.ContentType != ContentType.UnknownContentType)
            {
                actualContentCode = strippedCode;
                depMetadata = strippedMetadata;
            }
        }

        return (actualContentCode, depMetadata);
    }

    /// <summary>
    /// Extracts the content code from the manifest metadata.
    /// </summary>
    private static string GetContentCodeFromManifest(ContentManifest manifest)
    {
        // Look for contentCode tag in metadata
        var contentCodeTag = manifest.Metadata?.Tags?
            .FirstOrDefault(t => t.StartsWith("contentCode:", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(contentCodeTag))
        {
            return contentCodeTag["contentCode:".Length..];
        }

        return "unknown";
    }

    /// <summary>
    /// Gets the installation path for a game installation.
    /// </summary>
    private static string? GetInstallationPath(GameInstallation installation)
    {
        // For Zero Hour installations, use the installation path directly
        // For Generals-only installations, use the Generals path
        if (!string.IsNullOrEmpty(installation.InstallationPath))
        {
            // If the path points to a file (e.g. generals.exe), return the directory
            if (Path.HasExtension(installation.InstallationPath))
            {
                return Path.GetDirectoryName(installation.InstallationPath);
            }

            return installation.InstallationPath;
        }

        if (!string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            return installation.ZeroHourPath;
        }

        if (!string.IsNullOrEmpty(installation.GeneralsPath))
        {
            return installation.GeneralsPath;
        }

        return null;
    }

    /// <summary>
    /// Extracts an archive (ZIP, 7z, etc.) asynchronously using SharpCompress.
    /// Automatically detects format.
    /// </summary>
    private static async Task ExtractArchiveAsync(
        string archivePath,
        string extractPath,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            () =>
            {
                var fileInfo = new FileInfo(archivePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    throw new FileNotFoundException($"Archive file not found or empty: {archivePath}");
                }

                using var archive = ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    entry.WriteToDirectory(
                        extractPath,
                        new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true,
                        });
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Creates a generic manifest when no specialized content types are detected.
    /// </summary>
    private static async Task<List<ContentManifest>> CreateGenericManifestAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            return [];
        }

        List<ManifestFile> manifestFiles = [];

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var relativePath = Path.GetRelativePath(extractedDirectory, file);
            var fileInfo = new FileInfo(file);

            manifestFiles.Add(new ManifestFile
            {
                RelativePath = relativePath,
                Size = fileInfo.Length,
                IsRequired = true,
                IsExecutable = relativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
                SourceType = ContentSourceType.ExtractedPackage,
            });
        }

        var manifest = new ContentManifest
        {
            Id = originalManifest.Id,
            Name = originalManifest.Name,
            Version = originalManifest.Version,
            ManifestVersion = originalManifest.ManifestVersion,
            ContentType = originalManifest.ContentType,
            TargetGame = originalManifest.TargetGame,
            Publisher = originalManifest.Publisher,
            Metadata = originalManifest.Metadata,
            Dependencies = originalManifest.Dependencies,
            Files = manifestFiles,
            InstallationInstructions = originalManifest.InstallationInstructions,
        };

        return await Task.FromResult(new List<ContentManifest> { manifest });
    }

    /// <inheritdoc />
    public string SourceName => CommunityOutpostConstants.PublisherId;

    /// <inheritdoc />
    public string Description => CommunityOutpostConstants.DelivererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        // Can deliver if it's a Community Outpost manifest with a downloadable file
        // Note: PublisherType in manifest is "communityoutpost" (no hyphen)
        return manifest.Publisher?.PublisherType?.Equals(
                   CommunityOutpostConstants.PublisherType,
                   StringComparison.OrdinalIgnoreCase) == true &&
               manifest.Files.Any(f =>
                   !string.IsNullOrEmpty(f.DownloadUrl) &&
                   (f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    f.DownloadUrl.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                    f.DownloadUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)));
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
            logger.LogInformation(
                "Starting Community Outpost content delivery for {ManifestId} (v{Version})",
                packageManifest.Id,
                packageManifest.Version);

            // Step 1: Download archive file
            var archiveFile = packageManifest.Files.FirstOrDefault(f =>
                !string.IsNullOrEmpty(f.DownloadUrl) &&
                (f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)));

            if (archiveFile == null)
            {
                return OperationResult<ContentManifest>.CreateFailure("No downloadable archive found in manifest");
            }

            // Determine archive type from file extension or SourcePath marker
            var isSevenZip = archiveFile.SourcePath == "archive:7z" ||
                            archiveFile.DownloadUrl!.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                            archiveFile.DownloadUrl!.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

            var archiveExtension = isSevenZip ? ".7z" : ".zip";
            var archivePath = Path.Combine(targetDirectory, $"content{archiveExtension}");

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = 10,
                CurrentOperation = "Downloading Community Outpost package",
                CurrentFile = archiveFile.RelativePath,
            });

            // Try downloading with mirror fallback
            var downloadResult = await DownloadWithMirrorFallbackAsync(
                archiveFile.DownloadUrl!,
                archivePath,
                cancellationToken);

            if (!downloadResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to download package: {downloadResult.FirstError}");
            }

            // Step 2: Extract archive
            var extractPath = Path.Combine(targetDirectory, "extracted");
            Directory.CreateDirectory(extractPath);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                ProgressPercentage = 40,
                CurrentOperation = isSevenZip
                    ? "Extracting 7z archive"
                    : "Extracting ZIP archive",
            });

            logger.LogDebug("Extracting {ArchiveType} to {Path}", isSevenZip ? "7z" : "ZIP", extractPath);

            try
            {
                await ExtractArchiveAsync(archivePath, extractPath, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to extract archive from {Path}", archivePath);
                return OperationResult<ContentManifest>.CreateFailure($"Extraction failed: {ex.Message}");
            }

            // Step 2.5: Repack main content if needed (e.g. for Hotkeys)
            await RepackContentIfNeededAsync(
                packageManifest,
                extractPath,
                cancellationToken);

            // Step 2.6: Process AutoInstall dependencies and add their BIG files
            // MUST happen AFTER repacking because repacking clears the extract directory
            await ProcessAndMergeDependencyBigFilesAsync(
                packageManifest,
                extractPath,
                cancellationToken);

            // Step 3: Create manifests using the factory
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 50,
                CurrentOperation = "Creating manifests from extracted content",
            });

            logger.LogInformation("Creating manifests for Community Outpost content");
            var manifests = await manifestFactory.CreateManifestsFromExtractedContentAsync(
                packageManifest,
                extractPath,
                cancellationToken);

            if (manifests.Count == 0)
            {
                // If no specialized manifests were created, create a single manifest from all files
                logger.LogWarning(
                    "No specialized content detected, creating generic manifest");

                manifests = await CreateGenericManifestAsync(
                    packageManifest,
                    extractPath,
                    cancellationToken);
            }

            // Step 4: Register manifests to CAS
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 70,
                CurrentOperation = "Registering manifests to content library",
            });

            logger.LogInformation(
                "Registering {Count} manifest(s) to pool",
                manifests.Count);

            // For GameClient content, ensure InstallationPoolRootPath is set before storing
            // This prevents content from being stored in the wrong CAS pool (e.g., C: drive instead of game-adjacent pool)
            var hasGameClientManifest = manifests.Any(m => m.ContentType == ContentType.GameClient);
            if (hasGameClientManifest)
            {
                await EnsureInstallationPoolPathAsync(cancellationToken);

                // CRITICAL: Force the CAS pool manager to reinitialize the Installation pool
                // after we've updated the path in settings. Without this, the pool manager
                // will still use the old (or non-existent) Installation pool.
                casPoolManager?.ReinitializeInstallationPool();
            }

            foreach (var manifest in manifests)
            {
                var addResult = await manifestPool.AddManifestAsync(
                    manifest,
                    extractPath,
                    null,
                    cancellationToken);

                if (!addResult.Success)
                {
                    logger.LogWarning(
                        "Failed to register manifest {ManifestId}: {Error}",
                        manifest.Id,
                        addResult.FirstError);
                }
                else
                {
                    // After successful storage, update SourceType to ContentAddressable
                    // since the files are now in CAS
                    foreach (var file in manifest.Files)
                    {
                        file.SourceType = ContentSourceType.ContentAddressable;
                    }

                    logger.LogInformation(
                        "Successfully registered manifest: {ManifestId}",
                        manifest.Id);
                }
            }

            // Step 5: Cleanup temporary files
            await CleanupTemporaryFilesAsync(archivePath, extractPath);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Completed,
                ProgressPercentage = 100,
                CurrentOperation = "Community Outpost content delivered successfully",
            });

            var primaryManifest = manifests.FirstOrDefault() ?? packageManifest;
            logger.LogInformation(
                "Successfully delivered Community Outpost content: {ManifestCount} manifest(s) created",
                manifests.Count);

            return OperationResult<ContentManifest>.CreateSuccess(primaryManifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver Community Outpost content");
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
            var hasArchiveFile = manifest.Files.Any(f =>
                !string.IsNullOrEmpty(f.DownloadUrl) &&
                (f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)));

            return Task.FromResult(OperationResult<bool>.CreateSuccess(hasArchiveFile));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Validation failed for Community Outpost manifest {ManifestId}",
                manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Downloads a file with mirror fallback support.
    /// </summary>
    private async Task<OperationResult<bool>> DownloadWithMirrorFallbackAsync(
        string primaryUrl,
        string targetPath,
        CancellationToken cancellationToken)
    {
        // Try primary URL first
        logger.LogDebug("Downloading from primary URL: {Url}", primaryUrl);
        var result = await downloadService.DownloadFileAsync(
            new Uri(primaryUrl),
            targetPath,
            expectedHash: null,
            progress: null,
            cancellationToken);

        if (result.Success)
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        logger.LogWarning("Primary download failed: {Error}", result.FirstError);

        // Note: Mirror URLs would be stored in the original search result metadata
        // For now, we only try the primary URL since we don't have easy access
        // to the original metadata here. In a future enhancement, we could
        // store mirror URLs in the manifest or pass them through.
        return OperationResult<bool>.CreateFailure($"Download failed: {result.FirstError}");
    }

    /// <summary>
    /// Cleans up temporary files after extraction.
    /// </summary>
    private async Task CleanupTemporaryFilesAsync(string archivePath, string extractPath)
    {
        await Task.Run(() =>
        {
            // Delete archive file
            try
            {
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                    logger.LogDebug("Deleted archive file: {Path}", archivePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete archive file {Path}", archivePath);
            }

            // Delete extracted directory
            try
            {
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, recursive: true);
                    logger.LogDebug("Deleted extracted directory: {Path}", extractPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete extracted directory {Path}", extractPath);
            }
        });
    }

    /// <summary>
    /// Repacks extracted content into a single .big file if required by metadata.
    /// </summary>
    private async Task RepackContentIfNeededAsync(
        ContentManifest manifest,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var contentCode = GetContentCodeFromManifest(manifest);
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        if (metadata.RequiresRepacking && !string.IsNullOrEmpty(metadata.OutputFilename))
        {
            // Variant-based output filenames (e.g., 340_ControlBarPro{variant}ZH.big)
            // must be handled later when a specific variant is selected.
            if (metadata.OutputFilename.Contains("{variant}", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug(
                    "Skipping repack at delivery stage for {ContentCode} because output filename is variant-based: {OutputFilename}",
                    contentCode,
                    metadata.OutputFilename);
                return;
            }

            // If a correctly named BIG file already exists in the extracted content, do not repack.
            var existingBig = Directory.GetFiles(extractPath, metadata.OutputFilename, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(existingBig))
            {
                logger.LogInformation(
                    "Skipping repack for {ContentCode} because {OutputFilename} already exists in extracted content",
                    contentCode,
                    metadata.OutputFilename);
                return;
            }

            logger.LogInformation(
                "Repacking content for {ContentCode} into {OutputFilename}",
                contentCode,
                metadata.OutputFilename);

            // Create a temporary directory for the packed file
            var packDir = Path.Combine(Directory.GetParent(extractPath)!.FullName, "packed");
            Directory.CreateDirectory(packDir);
            var destinationPath = Path.Combine(packDir, metadata.OutputFilename);

            // Pack the files
            // GenPatcher archives often extract to nested ZH\BIG or CCG\BIG folders. We must pack the BIG folder contents,
            // not the parent folder, to avoid embedding extra path prefixes inside the .big.
            var bigDirectories = Directory.GetDirectories(extractPath, "BIG*", SearchOption.AllDirectories);
            var packSource = extractPath;

            if (bigDirectories.Length > 0)
            {
                bool IsUnder(string path, string folder)
                {
                    return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(segment => segment.Equals(folder, StringComparison.OrdinalIgnoreCase));
                }

                bool EndsWithSegment(string path, string segment)
                {
                    return path.EndsWith(segment, StringComparison.OrdinalIgnoreCase);
                }

                var preferred = bigDirectories
                    .FirstOrDefault(d => IsUnder(d, "ZH") && EndsWithSegment(d, "BIG EN"))
                    ?? bigDirectories.FirstOrDefault(d => IsUnder(d, "ZH") && EndsWithSegment(d, "BIG"))
                    ?? bigDirectories.FirstOrDefault(d => IsUnder(d, "CCG") && EndsWithSegment(d, "BIG EN"))
                    ?? bigDirectories.FirstOrDefault(d => IsUnder(d, "CCG") && EndsWithSegment(d, "BIG"))
                    ?? bigDirectories.First();

                packSource = preferred;
            }

            // Convert compressed image files (AVIF, WebP) to TGA format before packing
            // GenPatcher dat archives contain AVIF/WebP for compression, but the game requires TGA textures
            var compressedImageCount = Directory.GetFiles(packSource, "*.avif", SearchOption.AllDirectories).Length
                + Directory.GetFiles(packSource, "*.webp", SearchOption.AllDirectories).Length;
            if (compressedImageCount > 0)
            {
                logger.LogInformation(
                    "Converting {Count} compressed image files to TGA format for game compatibility",
                    compressedImageCount);

                var convertedCount = await avifConverter.ConvertDirectoryAsync(packSource, cancellationToken);
                logger.LogInformation("Converted {Converted} compressed image files to TGA", convertedCount);
            }

            await BigFilePacker.PackAsync(packSource, destinationPath);

            // Clear the ExtractPath and move the packed file there
            // This ensures the manifest factory only sees the packed file
            try
            {
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                Directory.CreateDirectory(extractPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reset extract path {ExtractPath} during repacking", extractPath);
                throw new IOException($"Failed to prepare extraction directory: {ex.Message}", ex);
            }

            File.Move(destinationPath, Path.Combine(extractPath, metadata.OutputFilename));

            // Cleanup packDir
            try
            {
                if (Directory.Exists(packDir))
                {
                    Directory.Delete(packDir, true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to cleanup temporary pack directory {PackDir}", packDir);
            }

            logger.LogInformation("Repacking completed successfully");
        }
    }

    /// <summary>
    /// Ensures the InstallationPoolRootPath is set before storing GameClient content.
    /// This prevents content from being stored in the wrong CAS pool.
    /// </summary>
    private async Task EnsureInstallationPoolPathAsync(CancellationToken cancellationToken)
    {
        try
        {
            // ALWAYS force installation detection and reset the path
            // Even if a path is set, it might be stale (from before user deleted data)
            // or point to the wrong installation
            logger.LogInformation("Forcing installation detection to ensure correct InstallationPoolRootPath");
            installationService.InvalidateCache();

            // Get all installations (this will trigger detection if cache is empty)
            var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
            if (!installationsResult.Success || installationsResult.Data == null)
            {
                logger.LogWarning("Failed to get installations for CAS pool path resolution: {Error}", installationsResult.FirstError);
                return;
            }

            var installations = installationsResult.Data.ToList();

            if (installations.Count == 0)
            {
                logger.LogWarning("No installations detected - cannot set InstallationPoolRootPath");
                return;
            }

            // If only one installation, use it
            if (installations.Count == 1)
            {
                var installation = installations[0];
                var installationPath = GetInstallationPath(installation);
                if (!string.IsNullOrEmpty(installationPath))
                {
                    var casPoolPath = Path.Combine(installationPath, ".genhub-cas");
                    logger.LogInformation("Auto-setting InstallationPoolRootPath to single installation: {Path}", casPoolPath);

                    var saved = await userSettingsService.TryUpdateAndSaveAsync(s =>
                    {
                        s.CasConfiguration.InstallationPoolRootPath = casPoolPath;
                        s.PreferredStorageInstallationId = installation.Id;
                        s.MarkAsExplicitlySet(nameof(s.CasConfiguration.InstallationPoolRootPath));
                        return true;
                    });

                    if (!saved)
                    {
                        logger.LogError("Failed to save installation pool path settings for installation {InstallationId}", installation.Id);
                    }

                    // Verify the setting was applied
                    var updatedSettings = userSettingsService.Get();
                    logger.LogInformation("Verified InstallationPoolRootPath is now: {Path}", updatedSettings.CasConfiguration.InstallationPoolRootPath);
                    return;
                }
            }

            // If multiple installations, prefer Steam over EA App
            var preferredInstallation = installations.FirstOrDefault(i => i.InstallationType == GameInstallationType.Steam)
                ?? installations.FirstOrDefault(i => i.InstallationType == GameInstallationType.EaApp)
                ?? installations.FirstOrDefault();

            if (preferredInstallation != null)
            {
                var installationPath = GetInstallationPath(preferredInstallation);
                if (!string.IsNullOrEmpty(installationPath))
                {
                    var casPoolPath = Path.Combine(installationPath, ".genhub-cas");
                    logger.LogInformation("Auto-setting InstallationPoolRootPath to preferred installation ({InstallationType}): {Path}", preferredInstallation.InstallationType, casPoolPath);

                    await userSettingsService.TryUpdateAndSaveAsync(s =>
                    {
                        s.CasConfiguration.InstallationPoolRootPath = casPoolPath;
                        s.PreferredStorageInstallationId = preferredInstallation.Id;
                        s.MarkAsExplicitlySet(nameof(s.CasConfiguration.InstallationPoolRootPath));
                        return true;
                    });

                    // Verify the setting was applied
                    var updatedSettings = userSettingsService.Get();
                    logger.LogInformation("Verified InstallationPoolRootPath is now: {Path}", updatedSettings.CasConfiguration.InstallationPoolRootPath);
                }
            }
            else
            {
                logger.LogWarning("No valid installation found for CAS pool path resolution");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure InstallationPoolRootPath is set");
        }
    }

    /// <summary>
    /// Processes AutoInstall dependencies by downloading, repacking them, and copying their BIG files
    /// into the main extract path so they become part of the same manifest.
    /// </summary>
    private async Task ProcessAndMergeDependencyBigFilesAsync(
        ContentManifest packageManifest,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var packageContentCode = GetContentCodeFromManifest(packageManifest);
        var packageMetadata = GenPatcherContentRegistry.GetMetadata(packageContentCode);
        var hasControlBarProBigs = false;

        if (packageMetadata.Category == GenPatcherContentCategory.ControlBar && packageMetadata.SupportsVariants)
        {
            hasControlBarProBigs = Directory.GetFiles(extractPath, "*ControlBarPro*ZH.big", SearchOption.AllDirectories)
                .Any(path => !Path.GetFileName(path).Contains("Core", StringComparison.OrdinalIgnoreCase));
        }

        var autoInstallDeps = (packageManifest.Dependencies ?? Enumerable.Empty<ContentDependency>())
            .Where(d => d.InstallBehavior == DependencyInstallBehavior.AutoInstall)
            .ToList();

        if (autoInstallDeps.Count == 0)
        {
            logger.LogDebug("No auto-install dependencies to process");
            return;
        }

        logger.LogInformation(
            "Processing {Count} auto-install dependencies - their BIG files will be added to the main manifest",
            autoInstallDeps.Count);

        foreach (var dep in autoInstallDeps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Extract content code from manifest ID
                var manifestIdStr = dep.Id.Value;
                var lastDotIndex = manifestIdStr.LastIndexOf('.');
                if (lastDotIndex < 0)
                {
                    logger.LogWarning("Cannot extract content code from dependency ID: {Id}", manifestIdStr);
                    continue;
                }

                var depContentCode = manifestIdStr[(lastDotIndex + 1)..];

                // Look up in registry to get metadata
                var (actualContentCode, depMetadata) = NormalizeContentCode(depContentCode);

                logger.LogInformation(
                    "Processing dependency: {Name} (code: {Code}) - will add its BIG file to main manifest",
                    dep.Name ?? dep.Id.Value,
                    actualContentCode);

                if (hasControlBarProBigs &&
                    packageMetadata.Category == GenPatcherContentCategory.ControlBar &&
                    (string.Equals(depMetadata.OutputFilename, "400_ControlBarProCoreZH.big", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(depMetadata.OutputFilename, "400_ControlBarHDBaseZH.big", StringComparison.OrdinalIgnoreCase)))
                {
                    logger.LogInformation(
                        "Skipping dependency {Name} because Control Bar Pro BIGs already exist in extracted content",
                        dep.Name ?? dep.Id.Value);
                    continue;
                }

                // Download dependency archive
                var urlsToTry = new List<string>
                {
                    $"https://legi.cc/gp2/f/{actualContentCode}.dat",
                    $"https://legi.cc/patch/{actualContentCode}.dat",
                };

                var uniqueId = Guid.NewGuid().ToString("N");
                var tempDir = Path.Combine(Path.GetTempPath(), "GenHub", "DepBigFiles", uniqueId);
                var depArchive = Path.Combine(tempDir, $"{actualContentCode}.dat");
                Directory.CreateDirectory(tempDir);

                OperationResult<bool> downloadResult = OperationResult<bool>.CreateFailure("No URLs attempted");
                foreach (var depUrl in urlsToTry)
                {
                    logger.LogDebug("Trying dependency download from {Url}", depUrl);
                    downloadResult = await DownloadWithMirrorFallbackAsync(depUrl, depArchive, cancellationToken);
                    if (downloadResult.Success) break;
                }

                if (!downloadResult.Success)
                {
                    logger.LogError("Failed to download dependency {Name}: {Error}", dep.Name, downloadResult.FirstError);
                    continue;
                }

                // Extract dependency
                var depExtractPath = Path.Combine(tempDir, actualContentCode);
                if (Directory.Exists(depExtractPath))
                {
                    Directory.Delete(depExtractPath, recursive: true);
                }

                Directory.CreateDirectory(depExtractPath);
                await ExtractArchiveAsync(depArchive, depExtractPath, cancellationToken);

                // Convert AVIF to TGA
                await avifConverter.ConvertDirectoryAsync(depExtractPath, cancellationToken);

                // Create a temporary package manifest for repacking
                var depPackageManifest = new ContentManifest
                {
                    Id = dep.Id,
                    Name = dep.Name ?? depMetadata.DisplayName,
                    Version = "1.0",
                    ContentType = depMetadata.ContentType,
                    TargetGame = depMetadata.TargetGame,
                    Metadata = new ContentMetadata
                    {
                        Tags = [$"contentCode:{actualContentCode}"],
                    },
                };

                // Repack if needed (this creates the BIG file)
                await RepackContentIfNeededAsync(depPackageManifest, depExtractPath, cancellationToken);

                // Copy the resulting BIG file(s) to the main extractPath
                var bigFiles = Directory.GetFiles(depExtractPath, "*.big", SearchOption.AllDirectories);
                if (bigFiles.Length == 0)
                {
                    logger.LogWarning("No BIG files found for dependency {Name} after repacking", dep.Name);
                }
                else
                {
                    foreach (var bigFile in bigFiles)
                    {
                        var bigFileName = Path.GetFileName(bigFile);
                        var targetPath = Path.Combine(extractPath, bigFileName);
                        File.Copy(bigFile, targetPath, overwrite: true);
                        logger.LogInformation(
                            "Copied dependency BIG file {FileName} to main extract path",
                            bigFileName);
                    }
                }

                // Cleanup
                try
                {
                    File.Delete(depArchive);

                    // Delete the unique temp directory and everything in it
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process dependency {Name}", dep.Name);
            }
        }

        logger.LogInformation("Finished processing auto-install dependencies");
    }
}
