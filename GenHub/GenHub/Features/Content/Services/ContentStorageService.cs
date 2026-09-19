using GenHub.Core.Constants;
using GenHub.Core.Extensions.Storage;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.Storage.Services;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Concrete implementation of content storage service.
/// </summary>
public class ContentStorageService : IContentStorageService
{
    /// <summary>
    /// Outcome of processing a single manifest file for CAS storage.
    /// </summary>
    /// <param name="Success">Whether processing succeeded (stored or legitimately skipped).</param>
    /// <param name="StoredFile">The stored file entry, or null when the file was skipped or failed.</param>
    /// <param name="Error">The failure message when a required file could not be stored.</param>
    private sealed record FileProcessResult(bool Success, ManifestFile? StoredFile, string? Error);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new ManifestIdJsonConverter() },
    };

    private readonly string _storageRoot;
    private readonly ILogger<ContentStorageService> _logger;
    private readonly ICasService _casService;
    private readonly ICasReferenceTracker _referenceTracker;
    private readonly CasWriteFence _writeFence;

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
                    if (!PathHelper.IsPathWithinDirectory(normalizedBase, fullPath))
                    {
                        return OperationResult<bool>.CreateFailure($"File {file.RelativePath} attempts path traversal outside base directory");
                    }
                }
                catch (ArgumentException)
                {
                    return OperationResult<bool>.CreateFailure($"Invalid path in file entry: {file.RelativePath}");
                }

                // Security check: SourcePath should generally not be set in manifests to avoid
                // arbitrary file reads, unless explicitly allowed for local ingestion.
                // For now, we enforce that if SourcePath IS set, it must check for traversal if relative,
                // and we warn on absolute paths if they look suspicious (though we can't easily distinguish
                // legitimate local imports from malicious ones without more context).
                if (!string.IsNullOrEmpty(file.SourcePath))
                {
                    try
                    {
                        // If SourcePath is absolute, we strictly enforce it must be within baseDirectory
                        // If it is relative, we combine and check traversal
                        var fullSource = Path.IsPathRooted(file.SourcePath)
                            ? Path.GetFullPath(file.SourcePath)
                            : Path.GetFullPath(Path.Combine(baseDirectory, file.SourcePath));

                        if (!PathHelper.IsPathWithinDirectory(normalizedBase, fullSource))
                        {
                            return OperationResult<bool>.CreateFailure($"File {file.RelativePath} specifies SourcePath {file.SourcePath} which traverses outside base directory");
                        }
                    }
                    catch (ArgumentException)
                    {
                        return OperationResult<bool>.CreateFailure($"Invalid SourcePath in file entry: {file.RelativePath}");
                    }
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

    private static bool TryCopy(string sourcePath, string targetPath)
    {
        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            // Copy semantics keep CAS objects immutable. Staging supports in-place
            // overwrites, so a hard link here would let edits mutate shared CAS content.
            File.Copy(sourcePath, targetPath, overwrite: false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether a path is located within a system temporary directory.
    /// </summary>
    private static bool IsTempDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var tempPath = Path.GetFullPath(Path.GetTempPath());
            if (!tempPath.EndsWith(Path.DirectorySeparatorChar) && !tempPath.EndsWith(Path.AltDirectorySeparatorChar))
            {
                tempPath += Path.DirectorySeparatorChar;
            }

            var fullPath = Path.GetFullPath(path);
            if (!fullPath.EndsWith(Path.DirectorySeparatorChar) && !fullPath.EndsWith(Path.AltDirectorySeparatorChar))
            {
                fullPath += Path.DirectorySeparatorChar;
            }

            var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return fullPath.StartsWith(tempPath, comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether physical storage should be forced regardless of drive type,
    /// e.g. for MapPacks or Addons created in temporary directories or user imports.
    /// </summary>
    private static bool ShouldForceStorage(ContentManifest manifest, string? sourceDirectory)
    {
        return manifest.ContentType is ContentType.MapPack or ContentType.Addon ||
               IsTempDirectory(sourceDirectory) ||
               IsTempDirectory(manifest.SourcePath);
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

        // Addons created locally or imported MUST be stored in CAS for durability
        if (manifest.ContentType == ContentType.Addon)
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

    private static ManifestFile CloneManifestFileForCas(ManifestFile original, string? hash = null, long? size = null)
    {
        return new ManifestFile
        {
            RelativePath = original.RelativePath,
            Size = size ?? original.Size,
            Hash = hash ?? original.Hash,
            ETag = original.ETag,
            SourceType = ContentSourceType.ContentAddressable,
            InstallTarget = original.InstallTarget,
            IsRequired = original.IsRequired,
            IsExecutable = original.IsExecutable,
            DownloadUrl = original.DownloadUrl,
            SourcePath = null,
            PatchSourceFile = original.PatchSourceFile,
            PackageInfo = original.PackageInfo,
            Permissions = original.Permissions,
        };
    }

    /// <summary>
    /// Resolves the source path for a manifest file, preferring its explicit source path when available.
    /// </summary>
    /// <param name="manifestFile">The manifest file entry.</param>
    /// <param name="sourceDirectory">Source directory containing content files.</param>
    /// <returns>The resolved source file path.</returns>
    private static string ResolveManifestFileSourcePath(ManifestFile manifestFile, string sourceDirectory)
    {
        // Use SourcePath if provided (e.g., for files where RelativePath differs from original location)
        // Otherwise, compute from sourceDirectory + RelativePath
        return !string.IsNullOrEmpty(manifestFile.SourcePath) && File.Exists(manifestFile.SourcePath)
            ? manifestFile.SourcePath
            : Path.Combine(sourceDirectory, manifestFile.RelativePath);
    }

    /// <summary>
    /// Reports content storage progress.
    /// </summary>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="processedCount">The number of files processed so far.</param>
    /// <param name="totalFiles">The total number of files.</param>
    /// <param name="currentFileName">The current file name.</param>
    private static void ReportStorageProgress(
        IProgress<ContentStorageProgress>? progress,
        int processedCount,
        int totalFiles,
        string currentFileName)
    {
        progress?.Report(new ContentStorageProgress
        {
            ProcessedCount = processedCount,
            TotalCount = totalFiles,
            CurrentFileName = currentFileName,
        });
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentStorageService"/> class.
    /// </summary>
    /// <param name="storageRoot">The root directory for content storage.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="casService">The CAS service for content-addressable storage.</param>
    /// <param name="referenceTracker">The CAS reference tracker.</param>
    /// <param name="writeFence">The fence marking in-flight CAS imports for garbage collection.</param>
    public ContentStorageService(
        string storageRoot,
        ILogger<ContentStorageService> logger,
        ICasService casService,
        ICasReferenceTracker referenceTracker,
        CasWriteFence writeFence)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new ArgumentException("Storage root path cannot be null or whitespace.", nameof(storageRoot));
        }

        _storageRoot = storageRoot;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _casService = casService ?? throw new ArgumentNullException(nameof(casService));
        _referenceTracker = referenceTracker ?? throw new ArgumentNullException(nameof(referenceTracker));
        _writeFence = writeFence ?? throw new ArgumentNullException(nameof(writeFence));

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
            return await HandleMissingSourceDirectoryAsync(manifest, sourceDirectory, cancellationToken).ConfigureAwait(false);
        }

        // Validate manifest for security issues
        // Use sourceDirectory as base for validation to allow importing from external locations
        var validationBase = sourceDirectory;
        var securityValidation = ValidateManifestSecurity(manifest, validationBase);
        if (!securityValidation.Success)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Manifest security validation failed: {securityValidation.FirstError}");
        }

        // Check if source directory is on a potentially invalid or removable drive
        bool isInvalidDrive = IsInvalidOrRemovableDrive(sourceDirectory);

        // MapPacks, Addons, and other local content might be created in temp directories on "invalid" drives (e.g. RAM disks)
        // We should allow storage if it's a MapPack or Addon to ensure it persists after temp cleanup.
        bool forceStorage = ShouldForceStorage(manifest, sourceDirectory);

        if (isInvalidDrive && !forceStorage)
        {
            return await HandleInvalidDriveAsync(manifest, sourceDirectory, cancellationToken).ConfigureAwait(false);
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

            // Hold the write fence until references are tracked and the manifest is
            // persisted so forced garbage collection cannot delete these blobs while
            // they are still invisible to the GC live set.
            using var writeLease = await _writeFence.TrackWriteAsync(cancellationToken);

            // Store content files in CAS with integrity verification
            var storeFilesResult = await StoreContentFilesAsync(manifest, sourceDirectory, progress, cancellationToken);
            if (!storeFilesResult.Success || storeFilesResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    storeFilesResult.FirstError ?? $"Failed to store content files for manifest {manifest.Id}");
            }

            var updatedManifest = storeFilesResult.Data;

            return await FinalizeStoredContentAsync(updatedManifest, manifestPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store content for manifest {ManifestId}", manifest.Id);

            // Cleanup on failure - only manifest file needs cleanup, CAS has its own GC
            try
            {
                FileOperationsService.DeleteFileIfExists(manifestPath);

                // Untrack manifest if we failed to save it but had already tracked references.
                // This prevents orphan references from protecting CAS objects that aren't actually associated with a manifest.
                var untrackResult = await _referenceTracker.UntrackManifestAsync(manifest.Id, CancellationToken.None);
                if (!untrackResult.Success)
                {
                    _logger.LogWarning("Failed to cleanup CAS references after storage failure for manifest '{ManifestId}': {ErrorCount} errors.", manifest.Id.Value, untrackResult.Errors?.Count ?? 0);
                }
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
                var materializeResult = await MaterializeManifestFileAsync(file, manifest.ContentType, targetDirectory, cancellationToken).ConfigureAwait(false);
                if (!materializeResult.Success)
                {
                    return materializeResult;
                }
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

        if (!File.Exists(manifestPath))
        {
            return OperationResult<bool>.CreateSuccess(false);
        }

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestJson, JsonOptions);
            if (manifest == null)
            {
                return OperationResult<bool>.CreateSuccess(false);
            }

            var allExist = await VerifyAllRequiredCasFilesExistAsync(manifest, cancellationToken).ConfigureAwait(false);
            return OperationResult<bool>.CreateSuccess(allExist);
        }
        catch (IOException ex)
        {
            return LogVerificationFailure(manifestId, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return LogVerificationFailure(manifestId, ex);
        }
        catch (JsonException ex)
        {
            return LogVerificationFailure(manifestId, ex);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> RemoveContentAsync(ManifestId manifestId, bool skipUntrack = false, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestStoragePath(manifestId);

        try
        {
            // Untrack CAS references before removing manifest file
            if (!skipUntrack)
            {
                var untrackResult = await _referenceTracker.UntrackManifestAsync(manifestId, cancellationToken);
                if (!untrackResult.Success)
                {
                    _logger.LogWarning("Failed to untrack CAS references during removal of manifest '{ManifestId}': {ErrorCount} errors.", manifestId.Value, untrackResult.Errors?.Count ?? 0);
                }
            }

            // Remove source.path mapping file if it exists
            var contentDir = Path.Combine(_storageRoot, DirectoryNames.Data, manifestId.Value);
            var sourcePathFile = Path.Combine(contentDir, FileTypes.SourcePathFileName);
            FileOperationsService.DeleteFileIfExists(sourcePathFile);

            // Clean up the data directory if empty
            if (Directory.Exists(contentDir) && !Directory.EnumerateFileSystemEntries(contentDir).Any())
            {
                try
                {
                    Directory.Delete(contentDir, recursive: false);
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
    public async Task<OperationResult<StorageStats>> GetStorageStatsAsync(CancellationToken cancellationToken = default)
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

            return await Task.FromResult(OperationResult<StorageStats>.CreateSuccess(stats));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate storage stats");
            return OperationResult<StorageStats>.CreateFailure($"Failed to calculate storage stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles storage when the source directory sits on an invalid or removable drive,
    /// refusing metadata-only storage when required CAS objects are missing.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <param name="sourceDirectory">Source directory containing content files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored manifest, or a failure when required CAS objects are missing.</returns>
    internal async Task<OperationResult<ContentManifest>> HandleInvalidDriveAsync(
        ContentManifest manifest,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        var gateResult = await CheckRequiredCasFilesAsync(manifest, sourceDirectory, "is on an invalid or removable drive", cancellationToken).ConfigureAwait(false);
        if (!gateResult.Success)
        {
            return gateResult;
        }

        _logger.LogWarning("Source directory {SourceDirectory} is on an invalid or removable drive, storing metadata only", sourceDirectory);
        return await StoreManifestOnlyAsync(manifest, sourceDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tracks CAS references and persists the manifest file after content files were stored.
    /// </summary>
    /// <param name="updatedManifest">The manifest with CAS-backed file entries.</param>
    /// <param name="manifestPath">The manifest storage path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored manifest, or a failure when references cannot be tracked.</returns>
    private async Task<OperationResult<ContentManifest>> FinalizeStoredContentAsync(
        ContentManifest updatedManifest,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        // Track CAS references to ensure files are not prematurely garbage collected
        var trackResult = await _referenceTracker.TrackManifestReferencesAsync(updatedManifest.Id, updatedManifest, cancellationToken);
        if (!trackResult.Success)
        {
            _logger.LogError("Failed to track CAS references for manifest {ManifestId}: {Error}", updatedManifest.Id, trackResult.FirstError);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to track CAS references: {trackResult.FirstError}");
        }

        // Ensure Manifests directory exists before writing manifest file
        var manifestDirectory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(manifestDirectory))
        {
            Directory.CreateDirectory(manifestDirectory);
        }

        var manifestJson = JsonSerializer.Serialize(updatedManifest, JsonOptions);
        await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

        // Create source.path marker for CAS-stored content
        // This prevents "Could not resolve source path" warnings when GetContentDirectoryAsync is called
        try
        {
            var contentDir = Path.Combine(_storageRoot, DirectoryNames.Data, updatedManifest.Id.Value);
            Directory.CreateDirectory(contentDir);
            var sourcePathFile = Path.Combine(contentDir, FileTypes.SourcePathFileName);
            await File.WriteAllTextAsync(sourcePathFile, FileTypes.CasOnlySourceMarker, cancellationToken);
        }
        catch (Exception markerEx)
        {
            _logger.LogWarning(markerEx, "Stored manifest {ManifestId} but failed to write CAS source-path marker", updatedManifest.Id);
        }

        _logger.LogInformation("Successfully stored content for manifest {ManifestId}", updatedManifest.Id);
        return OperationResult<ContentManifest>.CreateSuccess(updatedManifest);
    }

    private async Task<OperationResult<ContentManifest>> StoreManifestOnlyAsync(
        ContentManifest manifest,
        string? sourceDirectory,
        CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestStoragePath(manifest.Id);

        // Declare cleanup tracking variables outside try block so they're accessible in catch
        bool contentDirCreatedByThisCall = false;
        bool sourcePathWrittenByThisCall = false;
        string? previousSourcePathContent = null;

        try
        {
            // Validate manifest for security issues
            // Use sourceDirectory if available, otherwise fallback to storage root (though typically sourceDirectory should be provided)
            var validationBase = !string.IsNullOrEmpty(sourceDirectory) && Directory.Exists(sourceDirectory)
                ? sourceDirectory
                : _storageRoot;

            var securityValidation = ValidateManifestSecurity(manifest, validationBase);
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
                bool dirAlreadyExisted = Directory.Exists(contentDir);
                Directory.CreateDirectory(contentDir);
                contentDirCreatedByThisCall = !dirAlreadyExisted;

                var sourcePathFile = Path.Combine(contentDir, FileTypes.SourcePathFileName);

                // Backup existing source.path content before overwriting
                if (File.Exists(sourcePathFile))
                    previousSourcePathContent = await File.ReadAllTextAsync(sourcePathFile, cancellationToken);

                await File.WriteAllTextAsync(sourcePathFile, sourceDirectory, cancellationToken);
                sourcePathWrittenByThisCall = true;

                _logger.LogInformation(
                    "Created source path mapping for {ManifestId}: {SourcePath}",
                    manifest.Id,
                    sourceDirectory);
            }

            // Ensure CAS references are tracked even for metadata-only storage
            var trackResult = await _referenceTracker.TrackManifestReferencesAsync(manifest.Id, manifest, cancellationToken);
            if (!trackResult.Success)
            {
                _logger.LogError("Failed to track CAS references for metadata-only manifest {ManifestId}: {Error}", manifest.Id, trackResult.FirstError);
                return OperationResult<ContentManifest>.CreateFailure($"Failed to track CAS references: {trackResult.FirstError}");
            }

            // Sanitize file entries: CAS files must never retain transient staging source paths
            if (manifest.Files != null)
            {
                manifest.Files = manifest.Files.Select(f =>
                {
                    if (f.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(f.SourcePath))
                    {
                        return CloneManifestFileForCas(f);
                    }

                    return f;
                }).ToList();
            }

            // Store manifest metadata only
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

            _logger.LogInformation("Successfully stored manifest metadata for {ManifestId} and refreshed CAS tracking", manifest.Id);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store manifest metadata for {ManifestId}", manifest.Id);

            // Cleanup on failure
            try
            {
                FileOperationsService.DeleteFileIfExists(manifestPath);

                // Clean up the content dir created for source.path mapping
                var contentDir = Path.Combine(_storageRoot, DirectoryNames.Data, manifest.Id.Value);
                if (contentDirCreatedByThisCall)
                {
                    // We created this directory in the current call — safe to remove entirely.
                    if (Directory.Exists(contentDir))
                        Directory.Delete(contentDir, recursive: true);
                }
                else if (sourcePathWrittenByThisCall && Directory.Exists(contentDir))
                {
                    // We overwrote an existing source.path file - restore it or delete if it was new
                    var sourcePathFile = Path.Combine(contentDir, FileTypes.SourcePathFileName);
                    if (previousSourcePathContent != null)
                    {
                        // Restore the previous content
                        await File.WriteAllTextAsync(sourcePathFile, previousSourcePathContent, CancellationToken.None);
                    }
                    else
                    {
                        // We created a new file, safe to delete
                        FileOperationsService.DeleteFileIfExists(sourcePathFile);
                    }
                }

                // Untrack manifest if we failed to save its metadata but had already tracked/refreshed references.
                await _referenceTracker.UntrackManifestAsync(manifest.Id, CancellationToken.None);
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
    /// <returns>The updated manifest with storage information, or a failure when a required file cannot be stored.</returns>
    private async Task<OperationResult<ContentManifest>> StoreContentFilesAsync(
        ContentManifest manifest,
        string sourceDirectory,
        IProgress<ContentStorageProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            _logger.LogWarning("Source directory does not exist: {SourceDirectory}", sourceDirectory);
            manifest.Files.Clear();
            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }

        if (IsInvalidOrRemovableDrive(sourceDirectory) && !ShouldForceStorage(manifest, sourceDirectory))
        {
            _logger.LogWarning("Source directory {SourceDirectory} is on an invalid or removable drive", sourceDirectory);
            manifest.Files.Clear();
            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }

        _logger.LogInformation(
            "Storing {FileCount} files from manifest to CAS for {ManifestId}",
            manifest.Files.Count,
            manifest.Id);

        var updatedFiles = new List<ManifestFile>();
        int processedCount = 0;
        int totalFiles = manifest.Files.Count;

        // Initialize progress report
        ReportStorageProgress(progress, 0, totalFiles, "Initializing...");

        // Show notification for large file operations (>100 files) to inform user
        if (totalFiles > 100)
        {
            _logger.LogInformation("Starting storage of {FileCount} files - this may take a while", totalFiles);
        }

        foreach (var manifestFile in manifest.Files)
        {
            var outcome = await ProcessManifestFileAsync(
                manifestFile,
                manifest,
                sourceDirectory,
                progress,
                processedCount,
                cancellationToken);

            if (!outcome.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    outcome.Error ?? $"Failed to store file '{manifestFile.RelativePath}' for manifest {manifest.Id}");
            }

            if (outcome.StoredFile != null)
            {
                updatedFiles.Add(outcome.StoredFile);
            }

            processedCount++;
            ReportStorageProgress(progress, processedCount, totalFiles, manifestFile.RelativePath);
        }

        var validationError = ValidateAllRequiredFilesStored(manifest, updatedFiles);
        if (validationError != null)
        {
            return OperationResult<ContentManifest>.CreateFailure(validationError);
        }

        manifest.Files = updatedFiles;

        _logger.LogInformation(
            "Successfully stored {StoredCount} of {TotalCount} files to CAS for {ManifestId}",
            updatedFiles.Count,
            totalFiles,
            manifest.Id);

        return OperationResult<ContentManifest>.CreateSuccess(manifest);
    }

    /// <summary>
    /// Processes a single manifest file for CAS storage, reusing existing CAS content when available.
    /// </summary>
    /// <param name="manifestFile">The manifest file entry.</param>
    /// <param name="manifest">The content manifest.</param>
    /// <param name="sourceDirectory">Source directory containing content files.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="processedCount">The number of files processed so far.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The processing outcome: stored file, skip, or required-file failure.</returns>
    private async Task<FileProcessResult> ProcessManifestFileAsync(
        ManifestFile manifestFile,
        ContentManifest manifest,
        string sourceDirectory,
        IProgress<ContentStorageProgress>? progress,
        int processedCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourcePath = ResolveManifestFileSourcePath(manifestFile, sourceDirectory);

            // Special handling for content already in CAS (e.g. pre-downloaded)
            var reusedFile = await TryReuseExistingCasContentAsync(manifestFile, manifest.ContentType, cancellationToken);
            if (reusedFile != null)
            {
                return new FileProcessResult(true, reusedFile, null);
            }

            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning(
                    "File {RelativePath} not found at source path {SourcePath} or computed path",
                    manifestFile.RelativePath,
                    sourcePath);

                if (manifestFile.IsRequired)
                {
                    return new FileProcessResult(false, null, $"Required file '{manifestFile.RelativePath}' not found at source path '{sourcePath}'");
                }

                return new FileProcessResult(true, null, null);
            }

            // Update progress before starting heavy CAS operation
            ReportStorageProgress(progress, processedCount, manifest.Files.Count, manifestFile.RelativePath);

            var storeResult = await StoreFileInCasAsync(manifestFile, manifest.ContentType, sourcePath, cancellationToken);
            if (!storeResult.Success)
            {
                if (manifestFile.IsRequired)
                {
                    return new FileProcessResult(false, null, $"Failed to store required file '{manifestFile.RelativePath}' in CAS: {storeResult.FirstError}");
                }

                return new FileProcessResult(true, null, null);
            }

            return new FileProcessResult(true, storeResult.Data, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to store file {RelativePath} for manifest {ManifestId}",
                manifestFile.RelativePath,
                manifest.Id);

            if (manifestFile.IsRequired)
            {
                return new FileProcessResult(false, null, $"Failed to store required file '{manifestFile.RelativePath}' for manifest {manifest.Id}: {ex.Message}");
            }

            return new FileProcessResult(true, null, null);
        }
    }

    /// <summary>
    /// Returns the CAS-backed file entry when the content already exists in CAS, or null otherwise.
    /// </summary>
    /// <param name="manifestFile">The manifest file entry.</param>
    /// <param name="contentType">The content type for pool routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reused file entry, or null when content must be stored from disk.</returns>
    private async Task<ManifestFile?> TryReuseExistingCasContentAsync(
        ManifestFile manifestFile,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        if (manifestFile.SourceType != ContentSourceType.ContentAddressable || string.IsNullOrEmpty(manifestFile.Hash))
        {
            return null;
        }

        // Verify if it actually exists in CAS. Try the typed pool first, then fall back
        // to a pool-agnostic lookup: publisher factories may store under a content-type
        // pool that differs from the manifest's (e.g. a CNC Labs map stored under the Map
        // pool while the manifest type is still being inferred).
        var casPathResult = await _casService.GetContentPathAsync(manifestFile.Hash, contentType, cancellationToken).ConfigureAwait(false);
        if (!casPathResult.Success || string.IsNullOrEmpty(casPathResult.Data))
        {
            casPathResult = await _casService.GetContentPathAsync(manifestFile.Hash, cancellationToken).ConfigureAwait(false);
        }

        if (!casPathResult.Success || string.IsNullOrEmpty(casPathResult.Data))
        {
            return null;
        }

        _logger.LogDebug(
            "File {RelativePath} already exists in CAS (hash: {Hash}), skipping physical storage",
            manifestFile.RelativePath,
            manifestFile.Hash);

        // Add to updated files with SourcePath = null to ensure Hash resolution
        return CloneManifestFileForCas(manifestFile);
    }

    /// <summary>
    /// Stores a single file in CAS and returns the CAS-backed file entry.
    /// </summary>
    /// <param name="manifestFile">The manifest file entry.</param>
    /// <param name="contentType">The content type for pool routing.</param>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored file entry, or a failure when CAS storage fails.</returns>
    private async Task<OperationResult<ManifestFile>> StoreFileInCasAsync(
        ManifestFile manifestFile,
        ContentType contentType,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        // All source types (ContentAddressable, ExtractedPackage, LocalFile, etc.) are stored
        // in CAS by hash with pool awareness. This ensures all files end up in CAS for proper
        // validation and workspace resolution.
        var casResult = await _casService.StoreContentAsync(sourcePath, contentType, null, cancellationToken).ConfigureAwait(false);
        if (!casResult.Success || string.IsNullOrEmpty(casResult.Data))
        {
            _logger.LogWarning(
                "Failed to store {RelativePath} in CAS: {Error}",
                manifestFile.RelativePath,
                casResult.FirstError);
            return OperationResult<ManifestFile>.CreateFailure(
                casResult.FirstError ?? $"CAS storage failed for '{manifestFile.RelativePath}'");
        }

        var hash = casResult.Data;
        var fileSize = new FileInfo(sourcePath).Length;

        _logger.LogDebug(
            "Stored {RelativePath} (from {SourceType}) in CAS with hash {Hash}",
            manifestFile.RelativePath,
            manifestFile.SourceType,
            hash);

        // After storing, all files become ContentAddressable since they're now in CAS.
        // Clear SourcePath for CAS-stored content so workspace preparation uses Hash instead.
        return OperationResult<ManifestFile>.CreateSuccess(CloneManifestFileForCas(manifestFile, hash, fileSize));
    }

    /// <summary>
    /// Validates that every required manifest file was stored.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <param name="updatedFiles">The stored file entries.</param>
    /// <returns>An error message when required files are missing; otherwise, null.</returns>
    private string? ValidateAllRequiredFilesStored(ContentManifest manifest, List<ManifestFile> updatedFiles)
    {
        var missingRequiredFiles = manifest.Files
            .Where(f => f.IsRequired && !updatedFiles.Any(u => string.Equals(u.RelativePath, f.RelativePath, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingRequiredFiles.Count == 0)
        {
            return null;
        }

        var missingList = string.Join(", ", missingRequiredFiles.Select(f => f.RelativePath));
        _logger.LogError("Failed to store all required files for manifest {ManifestId}. Missing: {MissingFiles}", manifest.Id, missingList);
        return $"Failed to store required files for manifest {manifest.Id}: {missingList}";
    }

    private async Task<OperationResult<string>> MaterializeManifestFileAsync(
        ManifestFile file,
        ContentType contentType,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(file.Hash))
        {
            _logger.LogWarning("File {RelativePath} has no hash, skipping", file.RelativePath);
            return OperationResult<string>.CreateSuccess(string.Empty);
        }

        var casPathResult = await _casService.GetContentPathAsync(file.Hash, contentType, cancellationToken).ConfigureAwait(false);
        if (!casPathResult.Success || string.IsNullOrEmpty(casPathResult.Data))
        {
            _logger.LogWarning("File {RelativePath} not found in CAS (hash: {Hash})", file.RelativePath, file.Hash);
            return OperationResult<string>.CreateSuccess(string.Empty);
        }

        var targetPath = Path.Combine(targetDirectory, file.RelativePath);
        if (!PathHelper.IsPathWithinDirectory(targetDirectory, targetPath))
        {
            return OperationResult<string>.CreateFailure(
                $"Manifest entry escapes target directory: {file.RelativePath}");
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (!TryCopy(casPathResult.Data, targetPath))
        {
            return OperationResult<string>.CreateFailure(
                $"Failed to materialize file {file.RelativePath} from CAS.");
        }

        return OperationResult<string>.CreateSuccess(targetPath);
    }

    private async Task<List<string>> GetMissingRequiredCasFilesAsync(ContentManifest manifest, CancellationToken cancellationToken)
    {
        var missingCasFiles = new List<string>();
        if (manifest.Files == null)
        {
            return missingCasFiles;
        }

        foreach (var file in manifest.Files.Where(f => f.SourceType == ContentSourceType.ContentAddressable && f.IsRequired))
        {
            var exists = !string.IsNullOrEmpty(file.Hash) &&
                await _casService.ExistsInAnyPoolAsync(file.Hash, manifest.ContentType, cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                missingCasFiles.Add(file.RelativePath);
            }
        }

        return missingCasFiles;
    }

    private async Task<OperationResult<ContentManifest>> HandleMissingSourceDirectoryAsync(
        ContentManifest manifest,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        var gateResult = await CheckRequiredCasFilesAsync(manifest, sourceDirectory, "does not exist", cancellationToken).ConfigureAwait(false);
        if (!gateResult.Success)
        {
            return gateResult;
        }

        _logger.LogWarning("Source directory is null, empty, or does not exist: {SourceDirectory}. Storing metadata only.", sourceDirectory);
        return await StoreManifestOnlyAsync(manifest, sourceDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fails when the manifest requires physical storage but required CAS objects are missing.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <param name="sourceDirectory">Source directory containing content files.</param>
    /// <param name="sourceProblem">Description of the source directory problem for log and error messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The manifest when metadata-only storage may proceed, or a failure describing the missing CAS objects.</returns>
    private async Task<OperationResult<ContentManifest>> CheckRequiredCasFilesAsync(
        ContentManifest manifest,
        string sourceDirectory,
        string sourceProblem,
        CancellationToken cancellationToken)
    {
        if (!RequiresPhysicalStorage(manifest))
        {
            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }

        var missingCasFiles = await GetMissingRequiredCasFilesAsync(manifest, cancellationToken).ConfigureAwait(false);
        if (missingCasFiles.Count == 0)
        {
            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }

        _logger.LogError(
            "Cannot store manifest {ManifestId} metadata only: source directory {SourceProblem} and required files are missing from CAS: {MissingFiles}",
            manifest.Id,
            sourceProblem,
            string.Join(", ", missingCasFiles));
        return OperationResult<ContentManifest>.CreateFailure(
            $"Source directory '{sourceDirectory}' {sourceProblem} and required content is missing from CAS: {string.Join(", ", missingCasFiles)}");
    }

    private async Task<bool> VerifyAllRequiredCasFilesExistAsync(ContentManifest manifest, CancellationToken cancellationToken)
    {
        if (manifest.Files == null)
        {
            return true;
        }

        foreach (var file in manifest.Files.Where(f => f.SourceType == ContentSourceType.ContentAddressable && f.IsRequired))
        {
            var exists = !string.IsNullOrEmpty(file.Hash) &&
                await _casService.ExistsInAnyPoolAsync(file.Hash, manifest.ContentType, cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                _logger.LogWarning(
                    "Content {ManifestId} is missing required CAS object {Hash} for file {RelativePath}",
                    manifest.Id,
                    file.Hash,
                    file.RelativePath);
                return false;
            }
        }

        return true;
    }

    private OperationResult<bool> LogVerificationFailure(ManifestId manifestId, Exception ex)
    {
        _logger.LogWarning(ex, "Failed to verify stored content for manifest {ManifestId}", manifestId);
        return OperationResult<bool>.CreateSuccess(false);
    }
}
