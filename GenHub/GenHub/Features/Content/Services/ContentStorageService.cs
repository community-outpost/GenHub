using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Storage.Services;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Concrete implementation of content storage service.
/// </summary>
public class ContentStorageService : IContentStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, };
    private readonly string _storageRoot;
    private readonly ILogger<ContentStorageService> _logger;
    private readonly ICasService _casService;
    private readonly CasReferenceTracker _referenceTracker;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentStorageService"/> class.
    /// </summary>
    /// <param name="storageRoot">The root directory for content storage.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="casService">The CAS service for content-addressable storage.</param>
    /// <param name="referenceTracker">The CAS reference tracker.</param>
    public ContentStorageService(
        string storageRoot,
        ILogger<ContentStorageService> logger,
        ICasService casService,
        CasReferenceTracker referenceTracker)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new ArgumentException("Storage root path cannot be null or whitespace.", nameof(storageRoot));
        }

        _storageRoot = storageRoot;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _casService = casService ?? throw new ArgumentNullException(nameof(casService));
        _referenceTracker = referenceTracker ?? throw new ArgumentNullException(nameof(referenceTracker));

        // Ensure storage directory structure exists using FileOperationsService for future configurability.
        var requiredDirs = new[]
        {
            _storageRoot,
            Path.Combine(_storageRoot, FileTypes.ManifestsDirectory),
            Path.Combine(_storageRoot, DirectoryNames.Cache),
        };

        foreach (var dir in requiredDirs)
        {
            FileOperationsService.EnsureDirectoryExists(dir);
        }

        _logger.LogInformation("Content storage initialized at: {StorageRoot}", _storageRoot);
    }

    /// <inheritdoc/>
    public string GetContentStorageRoot() => _storageRoot;

    /// <inheritdoc/>
    public string GetManifestStoragePath(ManifestId manifestId) =>
        Path.Combine(_storageRoot, FileTypes.ManifestsDirectory, $"{manifestId}{FileTypes.ManifestFileExtension}");

    /// <inheritdoc/>
    public async Task<OperationResult<ContentManifest>> StoreContentAsync(
        ContentManifest manifest,
        string sourceDirectory,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            _logger.LogWarning("Source directory is null, empty, or does not exist: {SourceDirectory}. Storing metadata only.", sourceDirectory);
            return await StoreManifestOnlyAsync(manifest, sourceDirectory, cancellationToken);
        }

        // Validate manifest for security issues
        var securityValidation = ValidateManifestSecurity(manifest, _storageRoot);
        if (!securityValidation.Success)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Manifest security validation failed: {securityValidation.FirstError}");
        }

        // Check if source directory is on a potentially invalid or removable drive
        bool isInvalidDrive = IsInvalidOrRemovableDrive(sourceDirectory);

        // MapPacks and other local content might be created in temp directories on "invalid" drives (e.g. RAM disks)
        // We should allow storage if it's a MapPack to ensure it persists after temp cleanup.
        bool forceStorage = manifest.ContentType == ContentType.MapPack;

        if (isInvalidDrive && !forceStorage)
        {
            _logger.LogWarning("Source directory {SourceDirectory} is on an invalid or removable drive, storing metadata only", sourceDirectory);
            return await StoreManifestOnlyAsync(manifest, sourceDirectory, cancellationToken);
        }

        // Determine if this manifest requires physical file storage in CAS
        bool requiresPhysicalStorage = RequiresPhysicalStorage(manifest);

        if (!requiresPhysicalStorage)
        {
            _logger.LogInformation("Storing {ContentType} manifest {ManifestId} metadata only (content references external source)", manifest.ContentType, manifest.Id);
            return await StoreManifestOnlyAsync(manifest, sourceDirectory, cancellationToken);
        }

        var manifestPath = GetManifestStoragePath(manifest.Id);

        try
        {
            _logger.LogInformation("Storing content for manifest {ManifestId} from {SourceDirectory}", manifest.Id, sourceDirectory);

            // Store content files in CAS with integrity verification
            var updatedManifest = await StoreContentFilesAsync(manifest, sourceDirectory, progress, cancellationToken);

            // Track CAS references to ensure files are not prematurely garbage collected
            await _referenceTracker.TrackManifestReferencesAsync(updatedManifest.Id, updatedManifest, cancellationToken);

            // Ensure Manifests directory exists before writing manifest file
            var manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(manifestDirectory))
            {
                Directory.CreateDirectory(manifestDirectory);
            }

            var manifestJson = JsonSerializer.Serialize(updatedManifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

            _logger.LogInformation("Successfully stored content for manifest {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(updatedManifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store content for manifest {ManifestId}", manifest.Id);

            // Cleanup on failure - only manifest file needs cleanup, CAS has its own GC
            try
            {
                FileOperationsService.DeleteFileIfExists(manifestPath);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to cleanup after storage failure for {ManifestId}", manifest.Id);
            }

            return OperationResult<ContentManifest>.CreateFailure($"Storage failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> RetrieveContentAsync(
        ManifestId manifestId,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        // Load manifest to get file hashes
        var manifestPath = GetManifestStoragePath(manifestId);
        if (!File.Exists(manifestPath))
        {
            return OperationResult<string>.CreateFailure(
                $"Manifest not found for {manifestId}");
        }

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestJson, JsonOptions);
            if (manifest == null || manifest.Files.Count == 0)
            {
                return OperationResult<string>.CreateFailure(
                    $"Manifest is empty or invalid for {manifestId}");
            }

            Directory.CreateDirectory(targetDirectory);

            // Copy files from CAS to target directory
            foreach (var file in manifest.Files)
            {
                if (string.IsNullOrEmpty(file.Hash))
                {
                    _logger.LogWarning("File {RelativePath} has no hash, skipping", file.RelativePath);
                    continue;
                }

                var casPathResult = await _casService.GetContentPathAsync(file.Hash, cancellationToken);
                if (!casPathResult.Success || string.IsNullOrEmpty(casPathResult.Data))
                {
                    _logger.LogWarning("File {RelativePath} not found in CAS (hash: {Hash})", file.RelativePath, file.Hash);
                    continue;
                }

                var targetPath = Path.Combine(targetDirectory, file.RelativePath);
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(casPathResult.Data, targetPath, overwrite: true);
            }

            _logger.LogDebug("Retrieved content for manifest {ManifestId} to {TargetDirectory}", manifestId, targetDirectory);
            return OperationResult<string>.CreateSuccess(targetDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve content for manifest {ManifestId}", manifestId);
            return OperationResult<string>.CreateFailure($"Retrieval failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> IsContentStoredAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestStoragePath(manifestId);

        // Content is stored when manifest file exists - files are in CAS and validated separately
        bool exists = File.Exists(manifestPath);
        return await Task.FromResult(OperationResult<bool>.CreateSuccess(exists));
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> RemoveContentAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestStoragePath(manifestId);

        try
        {
            // Untrack CAS references before removing manifest file
            await _referenceTracker.UntrackManifestAsync(manifestId, cancellationToken);

            // Remove source.path mapping file if it exists
            var contentDir = Path.Combine(_storageRoot, DirectoryNames.Data, manifestId.Value);
            var sourcePathFile = Path.Combine(contentDir, "source.path");
            FileOperationsService.DeleteFileIfExists(sourcePathFile);

            // Clean up the data directory if empty
            if (Directory.Exists(contentDir) && !Directory.EnumerateFileSystemEntries(contentDir).Any())
            {
                try
                {
                    Directory.Delete(contentDir);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(deleteEx, "Failed to delete empty content directory {Directory}", contentDir);
                }
            }

            // Only remove manifest file - CAS files are cleaned up via garbage collection
            await Task.Run(() => FileOperationsService.DeleteFileIfExists(manifestPath), cancellationToken);

            _logger.LogInformation("Removed stored content for manifest {ManifestId}", manifestId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove content for manifest {ManifestId}", manifestId);
            return OperationResult<bool>.CreateFailure($"Removal failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<StorageStats> GetStorageStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = new StorageStats();

            if (Directory.Exists(_storageRoot))
            {
                var allFiles = Directory.GetFiles(_storageRoot, "*", SearchOption.AllDirectories);
                stats.TotalFileCount = allFiles.Length;
                stats.TotalSizeBytes = allFiles.Sum(f => new FileInfo(f).Length);

                var manifestFiles = Directory.GetFiles(Path.Combine(_storageRoot, FileTypes.ManifestsDirectory), FileTypes.ManifestFilePattern);
                stats.ManifestCount = manifestFiles.Length;

                var driveInfo = new DriveInfo(Path.GetPathRoot(_storageRoot)!);
                stats.AvailableFreeSpaceBytes = driveInfo.AvailableFreeSpace;
            }

            return await Task.FromResult(stats);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate storage stats");
            return new StorageStats();
        }
    }

    private static OperationResult<bool> ValidateManifestSecurity(ContentManifest manifest, string baseDirectory)
    {
        if (manifest.Files != null)
        {
            var normalizedBase = Path.GetFullPath(baseDirectory);
            foreach (var file in manifest.Files)
            {
                if (string.IsNullOrEmpty(file.RelativePath))
                {
                    return OperationResult<bool>.CreateFailure("File entries must have a relative path");
                }

                // path traversal check using normalization
                try
                {
                    var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, file.RelativePath));
                    if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                    {
                        return OperationResult<bool>.CreateFailure($"File {file.RelativePath} attempts path traversal outside base directory");
                    }
                }
                catch (ArgumentException)
                {
                    return OperationResult<bool>.CreateFailure($"Invalid path in file entry: {file.RelativePath}");
                }
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Determines whether a manifest requires physical file storage in the CAS system.
    /// </summary>
    /// <param name="manifest">The manifest to check.</param>
    /// <returns>True if files should be physically stored; false if metadata-only storage is sufficient.</returns>
    private static bool RequiresPhysicalStorage(ContentManifest manifest)
    {
        // GameInstallation content always references external installations - no storage needed
        if (manifest.ContentType == ContentType.GameInstallation)
        {
            return false;
        }

        // MapPacks created locally MUST be stored in CAS because the source (temp dir) will be deleted
        if (manifest.ContentType == ContentType.MapPack)
        {
            return true;
        }

        // GameClient content typically references external installations - no storage needed (old behavior)
        // Only store physically for GitHub content that requires it
        if (manifest.ContentType == ContentType.GameClient)
        {
            // Check if any file requires CAS storage based on its source type
            // This covers content from any GitHub publisher (thesuperhackers, generalsonline, etc.)
            return manifest.Files.Any(f =>
                f.SourceType == ContentSourceType.ContentAddressable ||
                f.SourceType == ContentSourceType.ExtractedPackage ||
                f.SourceType == ContentSourceType.LocalFile ||
                f.SourceType == ContentSourceType.Unknown);
        }

        // For other content types, check if files have source types that require CAS storage
        if (manifest.Files.Count == 0)
        {
            // No files to store
            return false;
        }

        // Check if any file requires CAS storage based on its source type
        bool hasStorableContent = manifest.Files.Any(f =>
            f.SourceType == ContentSourceType.ContentAddressable ||
            f.SourceType == ContentSourceType.ExtractedPackage ||
            f.SourceType == ContentSourceType.LocalFile ||
            f.SourceType == ContentSourceType.Unknown);

        return hasStorableContent;
    }

    private async Task<OperationResult<ContentManifest>> StoreManifestOnlyAsync(
        ContentManifest manifest,
        string? sourceDirectory,
        CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestStoragePath(manifest.Id);

        try
        {
            // Validate manifest for security issues
            var securityValidation = ValidateManifestSecurity(manifest, _storageRoot);
            if (!securityValidation.Success)
            {
                _logger.LogError("Manifest security validation failed for {ManifestId}: {Error}", manifest.Id, securityValidation.FirstError ?? "Unknown error");
                return OperationResult<ContentManifest>.CreateFailure($"Manifest security validation failed: {securityValidation.FirstError ?? "Unknown error"}");
            }

            // Create manifest directory if needed
            var manifestDir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(manifestDir))
                Directory.CreateDirectory(manifestDir);

            // Create source.path mapping if we have a valid source directory
            // This allows GetContentDirectoryAsync to resolve the content location
            if (!string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory))
            {
                var contentDir = Path.Combine(_storageRoot, DirectoryNames.Data, manifest.Id.Value);
                Directory.CreateDirectory(contentDir);

                var sourcePathFile = Path.Combine(contentDir, "source.path");
                await File.WriteAllTextAsync(sourcePathFile, sourceDirectory, cancellationToken);

                _logger.LogInformation(
                    "Created source path mapping for {ManifestId}: {SourcePath}",
                    manifest.Id,
                    sourceDirectory);
            }

            // Store manifest metadata only
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

            _logger.LogInformation("Successfully stored manifest metadata for {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store manifest metadata for {ManifestId}", manifest.Id);

            // Cleanup on failure
            try
            {
                FileOperationsService.DeleteFileIfExists(manifestPath);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to cleanup manifest file after storage failure for {ManifestId}", manifest.Id);
            }

            return OperationResult<ContentManifest>.CreateFailure($"Manifest storage failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a directory path is on an invalid or removable drive.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the drive is invalid or removable, false otherwise.</returns>
    private bool IsInvalidOrRemovableDrive(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
                return true;

            var rootPath = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(rootPath))
                return true;

            // Check if it's a UNC path or relative path
            if (rootPath.StartsWith(@"\\") || !Path.IsPathRooted(path))
                return false; // UNC paths are generally valid, let them through

            var driveInfo = new DriveInfo(rootPath);

            // Check drive type - avoid removable drives like floppy (A:), CD-ROM, etc.
            if (driveInfo.DriveType == DriveType.Removable ||
                driveInfo.DriveType == DriveType.CDRom ||
                driveInfo.DriveType == DriveType.Unknown)
            {
                _logger.LogWarning("Drive {Drive} is of type {DriveType}, considering invalid", rootPath, driveInfo.DriveType);
                return true;
            }

            // Check if drive is ready (this will catch cases where drive exists but is not accessible)
            if (!driveInfo.IsReady)
            {
                _logger.LogWarning("Drive {Drive} is not ready, considering invalid", rootPath);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate drive for path {Path}, considering invalid", path);
            return true; // If we can't validate it, treat it as invalid for safety
        }
    }

    /// <summary>
    /// Stores content files from source directory to CAS, updating manifest with hashes and paths.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <param name="sourceDirectory">Source directory containing content files.</param>
    /// <param name="progress">Optional progress reporter for tracking storage operations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated manifest with storage information.</returns>
    private async Task<ContentManifest> StoreContentFilesAsync(
        ContentManifest manifest,
        string sourceDirectory,
        IProgress<ContentStorageProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            _logger.LogWarning("Source directory does not exist: {SourceDirectory}", sourceDirectory);
            manifest.Files.Clear();
            return manifest;
        }

        if (IsInvalidOrRemovableDrive(sourceDirectory))
        {
            _logger.LogWarning("Source directory {SourceDirectory} is on an invalid or removable drive", sourceDirectory);
            manifest.Files.Clear();
            return manifest;
        }

        _logger.LogInformation(
            "Storing {FileCount} files from manifest to CAS for {ManifestId}",
            manifest.Files.Count,
            manifest.Id);

        var updatedFiles = new List<ManifestFile>();
        int processedCount = 0;
        int totalFiles = manifest.Files.Count;

        // Initialize progress report
        progress?.Report(new ContentStorageProgress
        {
            ProcessedCount = 0,
            TotalCount = totalFiles,
            CurrentFileName = "Initializing...",
        });

        // Show notification for large file operations (>100 files) to inform user
        if (totalFiles > 100)
        {
            _logger.LogInformation("Starting storage of {FileCount} files - this may take a while", totalFiles);
        }

        foreach (var manifestFile in manifest.Files)
        {
            try
            {
                // Use SourcePath if provided (e.g., for files where RelativePath differs from original location)
                // Otherwise, compute from sourceDirectory + RelativePath
                var sourcePath = !string.IsNullOrEmpty(manifestFile.SourcePath) && File.Exists(manifestFile.SourcePath)
                    ? manifestFile.SourcePath
                    : Path.Combine(sourceDirectory, manifestFile.RelativePath);

                // Special handling for content already in CAS (e.g. pre-downloaded)
                if (manifestFile.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(manifestFile.Hash))
                {
                    // Verify if it actually exists in CAS
                    var casPathResult = await _casService.GetContentPathAsync(manifestFile.Hash, cancellationToken);
                    if (casPathResult.Success && !string.IsNullOrEmpty(casPathResult.Data))
                    {
                        _logger.LogDebug(
                            "File {RelativePath} already exists in CAS (hash: {Hash}), skipping physical storage",
                            manifestFile.RelativePath,
                            manifestFile.Hash);

                        // Add directly to updated files
                        updatedFiles.Add(manifestFile);
                        processedCount++;
                        progress?.Report(new ContentStorageProgress
                        {
                            ProcessedCount = processedCount,
                            TotalCount = totalFiles,
                            CurrentFileName = manifestFile.RelativePath,
                        });
                        continue;
                    }
                }

                if (!File.Exists(sourcePath))
                {
                    _logger.LogWarning(
                        "File {RelativePath} not found at source path {SourcePath} or computed path",
                        manifestFile.RelativePath,
                        sourcePath);
                    processedCount++;
                    progress?.Report(new ContentStorageProgress
                    {
                        ProcessedCount = processedCount,
                        TotalCount = totalFiles,
                        CurrentFileName = manifestFile.RelativePath,
                    });
                    continue;
                }

                // Update progress before starting heavy CAS operation
                progress?.Report(new ContentStorageProgress
                {
                    ProcessedCount = processedCount,
                    TotalCount = totalFiles,
                    CurrentFileName = manifestFile.RelativePath,
                });

                // Store file based on its SourceType
                string hash;
                long fileSize;

                if (manifestFile.SourceType == ContentSourceType.ContentAddressable)
                {
                    // For ContentAddressable files, store in CAS by hash
                    var casResult = await _casService.StoreContentAsync(sourcePath, null, cancellationToken);
                    if (!casResult.Success || string.IsNullOrEmpty(casResult.Data))
                    {
                        _logger.LogWarning(
                            "Failed to store {RelativePath} in CAS: {Error}",
                            manifestFile.RelativePath,
                            casResult.FirstError);
                        continue;
                    }

                    hash = casResult.Data;
                    fileSize = new FileInfo(sourcePath).Length;

                    _logger.LogDebug(
                        "Stored {RelativePath} in CAS with hash {Hash}",
                        manifestFile.RelativePath,
                        hash);
                }
                else
                {
                    // For other source types (ExtractedPackage, LocalFile, etc.), also store in CAS
                    // This ensures all files end up in CAS for proper validation and workspace resolution
                    var casResult = await _casService.StoreContentAsync(sourcePath, null, cancellationToken);
                    if (!casResult.Success || string.IsNullOrEmpty(casResult.Data))
                    {
                        _logger.LogWarning(
                            "Failed to store {RelativePath} in CAS: {Error}",
                            manifestFile.RelativePath,
                            casResult.FirstError);
                        continue;
                    }

                    hash = casResult.Data;
                    fileSize = new FileInfo(sourcePath).Length;

                    _logger.LogDebug(
                        "Stored {RelativePath} (from {SourceType}) in CAS with hash {Hash}",
                        manifestFile.RelativePath,
                        manifestFile.SourceType,
                        hash);
                }

                // After storing, all files become ContentAddressable since they're now in CAS
                var updatedFile = new ManifestFile
                {
                    RelativePath = manifestFile.RelativePath,
                    Size = fileSize,
                    Hash = hash,
                    SourceType = ContentSourceType.ContentAddressable,
                    InstallTarget = manifestFile.InstallTarget, // Preserve install target (UserMapsDirectory, etc.)
                    IsRequired = manifestFile.IsRequired,
                    IsExecutable = manifestFile.IsExecutable,
                    DownloadUrl = manifestFile.DownloadUrl,
                    SourcePath = manifestFile.SourcePath,
                    PatchSourceFile = manifestFile.PatchSourceFile,
                    PackageInfo = manifestFile.PackageInfo,
                    Permissions = manifestFile.Permissions,
                };

                updatedFiles.Add(updatedFile);
                processedCount++;

                // Update progress after completion
                progress?.Report(new ContentStorageProgress
                {
                    ProcessedCount = processedCount,
                    TotalCount = totalFiles,
                    CurrentFileName = manifestFile.RelativePath,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to store file {RelativePath} for manifest {ManifestId}",
                    manifestFile.RelativePath,
                    manifest.Id);

                processedCount++;
                progress?.Report(new ContentStorageProgress
                {
                    ProcessedCount = processedCount,
                    TotalCount = totalFiles,
                    CurrentFileName = manifestFile.RelativePath,
                });
            }
        }

        manifest.Files = updatedFiles;

        _logger.LogInformation(
            "Successfully stored {StoredCount} of {TotalCount} files to CAS for {ManifestId}",
            updatedFiles.Count,
            manifest.Files.Count,
            manifest.Id);

        return manifest;
    }
}
