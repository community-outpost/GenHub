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
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Concrete implementation of content storage service.
/// </summary>
public class ContentStorageService : IContentStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _storageRoot;
    private readonly ILogger<ContentStorageService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentStorageService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configurationProviderService">The configuration provider service.</param>
    public ContentStorageService(
        ILogger<ContentStorageService> logger,
        IConfigurationProviderService configurationProviderService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var configService = configurationProviderService ?? throw new ArgumentNullException(nameof(configurationProviderService));
        _storageRoot = configService.GetContentStoragePath();

        // Ensure storage directory structure exists using FileOperationsService for future configurability.
        var requiredDirs = new[]
        {
            _storageRoot,
            Path.Combine(_storageRoot, FileTypes.ManifestsDirectory),
            Path.Combine(_storageRoot, DirectoryNames.Data),
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
    public string GetContentDirectoryPath(ManifestId manifestId) =>
        Path.Combine(_storageRoot, DirectoryNames.Data, manifestId.Value);

    /// <inheritdoc/>
    public async Task<OperationResult<ContentManifest>> StoreContentAsync(
        ContentManifest manifest,
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Source directory does not exist: {sourceDirectory}");
        }

        // Validate manifest for security issues
        var securityValidation = ValidateManifestSecurity(manifest, _storageRoot);
        if (!securityValidation.Success)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Manifest security validation failed: {securityValidation.FirstError}");
        }

        // For GameInstallation content, skip physical file copying and just store metadata
        if (manifest.ContentType == Core.Models.Enums.ContentType.GameInstallation)
        {
            _logger.LogInformation("Storing GameInstallation manifest {ManifestId} metadata only (skipping file copy)", manifest.Id);
            return await StoreManifestOnlyAsync(manifest, cancellationToken);
        }

        var contentDir = GetContentDirectoryPath(manifest.Id);
        var manifestPath = GetManifestStoragePath(manifest.Id);

        try
        {
            _logger.LogInformation("Storing content for manifest {ManifestId} from {SourceDirectory}", manifest.Id, sourceDirectory);

            // Create content directory
            Directory.CreateDirectory(contentDir);

            // Store content files with integrity verification
            var updatedManifest = await StoreContentFilesAsync(manifest, sourceDirectory, contentDir, cancellationToken);

            // Store manifest metadata
            var manifestJson = JsonSerializer.Serialize(updatedManifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

            _logger.LogInformation("Successfully stored content for manifest {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(updatedManifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store content for manifest {ManifestId}", manifest.Id);

            // Cleanup on failure
            try
            {
                FileOperationsService.DeleteDirectoryIfExists(contentDir);
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
        var contentDir = GetContentDirectoryPath(manifestId);

        if (!Directory.Exists(contentDir))
        {
            return OperationResult<string>.CreateFailure(
                $"Content not found for manifest {manifestId}");
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            await CopyDirectoryAsync(contentDir, targetDirectory, cancellationToken);

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
        var contentDir = GetContentDirectoryPath(manifestId);
        var manifestPath = GetManifestStoragePath(manifestId);

        bool exists = Directory.Exists(contentDir) && File.Exists(manifestPath);
        if (exists)
        {
            return await Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        else
        {
            return await Task.FromResult(OperationResult<bool>.CreateFailure($"Content not found for manifest {manifestId}"));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> RemoveContentAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
    {
        var contentDir = GetContentDirectoryPath(manifestId);
        var manifestPath = GetManifestStoragePath(manifestId);

        try
        {
            await Task.Run(
                () =>
            {
                FileOperationsService.DeleteDirectoryIfExists(contentDir);
                FileOperationsService.DeleteFileIfExists(manifestPath);
            });

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

    private static async Task CopyDirectoryAsync(string sourceDir, string targetDir, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var targetPath = Path.Combine(targetDir, relativePath);
            var targetDirPath = Path.GetDirectoryName(targetPath)!;

            Directory.CreateDirectory(targetDirPath);
            File.Copy(file, targetPath, overwrite: true);
        }

        await Task.CompletedTask;
    }

    private async Task<OperationResult<ContentManifest>> StoreManifestOnlyAsync(
        ContentManifest manifest,
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

    private async Task<ContentManifest> StoreContentFilesAsync(
        ContentManifest manifest,
        string sourceDirectory,
        string contentDirectory,
        CancellationToken cancellationToken)
    {
        var updatedFiles = new List<ManifestFile>();

        foreach (var manifestFile in manifest.Files)
        {
            var sourcePath = Path.Combine(sourceDirectory, manifestFile.RelativePath);
            var targetPath = Path.Combine(contentDirectory, manifestFile.RelativePath);

            if (!File.Exists(sourcePath))
            {
                if (manifestFile.IsRequired)
                {
                    throw new FileNotFoundException($"Required file not found: {manifestFile.RelativePath}");
                }

                _logger.LogWarning("Optional file not found, skipping: {FilePath}", manifestFile.RelativePath);
                continue;
            }

            var targetDir = Path.GetDirectoryName(targetPath)!;
            Directory.CreateDirectory(targetDir);

            File.Copy(sourcePath, targetPath, overwrite: true);

            var fileInfo = new FileInfo(targetPath);
            var updatedFile = new ManifestFile
            {
                RelativePath = manifestFile.RelativePath,
                Size = fileInfo.Length,
                Hash = await CalculateFileHashAsync(targetPath, cancellationToken),
                SourceType = manifestFile.SourceType,
                Permissions = manifestFile.Permissions,
                IsExecutable = manifestFile.IsExecutable,
                DownloadUrl = manifestFile.DownloadUrl,
                IsRequired = manifestFile.IsRequired,
                SourcePath = manifestFile.SourcePath,
                PatchSourceFile = manifestFile.PatchSourceFile,
                PackageInfo = manifestFile.PackageInfo,
            };

            updatedFiles.Add(updatedFile);
        }

        var updatedManifest = new ContentManifest
        {
            ManifestVersion = manifest.ManifestVersion,
            Id = manifest.Id,
            Name = manifest.Name,
            Version = manifest.Version,
            ContentType = manifest.ContentType,
            TargetGame = manifest.TargetGame,
            Publisher = manifest.Publisher,
            Metadata = manifest.Metadata,
            Dependencies = manifest.Dependencies,
            ContentReferences = manifest.ContentReferences,
            KnownAddons = manifest.KnownAddons,
            Files = updatedFiles,
            RequiredDirectories = manifest.RequiredDirectories,
            InstallationInstructions = manifest.InstallationInstructions,
        };

        return updatedManifest;
    }
}
