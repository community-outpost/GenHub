using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Manages the storage of manifest files and coordinates with the CAS service to store content.
/// </summary>
public class ContentStorageService : IContentStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _storageRoot;
    private readonly ILogger<ContentStorageService> _logger;
    private readonly ICasService _casService;
    private readonly IFileHashProvider _hashProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentStorageService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configurationProvider">The configuration provider service.</param>
    /// <param name="casService">The Content-Addressable Storage service.</param>
    /// <param name="hashProvider">The file hash provider service.</param>
    public ContentStorageService(ILogger<ContentStorageService> logger, IConfigurationProviderService configurationProvider, ICasService casService, IFileHashProvider hashProvider)
    {
        _logger = logger;
        _casService = casService;
        _hashProvider = hashProvider;

        // Use configuration provider for content storage path
        _storageRoot = configurationProvider.GetContentStoragePath();

        // Ensure storage directory structure exists
        var requiredDirs = new[]
        {
            _storageRoot,
            Path.Combine(_storageRoot, "Manifests"),
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
    public string GetManifestStoragePath(string manifestId) =>
        Path.Combine(_storageRoot, "Manifests", $"{manifestId}.manifest.json");

    /// <inheritdoc/>
    public string GetContentDirectoryPath(string manifestId) =>
        Path.Combine(_storageRoot, "Data", manifestId); // This path for non-CAS content.

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

        var manifestPath = GetManifestStoragePath(manifest.Id);

        try
        {
            _logger.LogInformation("Storing content for manifest {ManifestId} in CAS from {SourceDirectory}", manifest.Id, sourceDirectory);

            // Store content files in CAS and get an updated manifest
            var updatedManifest = await StoreContentFilesInCasAsync(manifest, sourceDirectory, cancellationToken);

            // Store manifest metadata
            var manifestJson = JsonSerializer.Serialize(updatedManifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

            _logger.LogInformation("Successfully stored content for manifest {ManifestId} in CAS", manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(updatedManifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store content for manifest {ManifestId}", manifest.Id);

            // Cleanup on failure
            try
            {
                FileOperationsService.DeleteFileIfExists(manifestPath);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to cleanup manifest after storage failure for {ManifestId}", manifest.Id);
            }

            return OperationResult<ContentManifest>.CreateFailure($"Storage failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> RetrieveContentAsync(
        string manifestId,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        // This method is likely deprecated by CAS-aware workspace strategies.
        // It could be reimplemented to retrieve from CAS if needed.
        _logger.LogWarning("RetrieveContentAsync is a legacy operation. Workspaces should be prepared using CAS-aware strategies.");
        var manifestPath = GetManifestStoragePath(manifestId);
        if (!File.Exists(manifestPath))
        {
            return OperationResult<string>.CreateFailure($"Manifest not found: {manifestId}");
        }

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestJson);

            if (manifest == null)
            {
                return OperationResult<string>.CreateFailure($"Could not deserialize manifest: {manifestId}");
            }

            foreach (var file in manifest.Files.Where(f => !string.IsNullOrEmpty(f.Hash)))
            {
                var destPath = Path.Combine(targetDirectory, file.RelativePath);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null)
                {
                    Directory.CreateDirectory(destDir);
                }

                var result = await _casService.GetContentPathAsync(file.Hash, cancellationToken);
                if (result.Success && result.Data != null)
                {
                    File.Copy(result.Data, destPath, true);
                }
                else
                {
                    _logger.LogWarning("Failed to retrieve {RelativePath} from CAS for manifest {ManifestId}", file.RelativePath, manifestId);
                }
            }

            return OperationResult<string>.CreateSuccess(targetDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve content for manifest {ManifestId}", manifestId);
            return OperationResult<string>.CreateFailure($"Retrieval failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> IsContentStoredAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        try
        {
            var manifestPath = GetManifestStoragePath(manifestId);
            bool exists = File.Exists(manifestPath);

            if (exists)
            {
                return await Task.FromResult(OperationResult<bool>.CreateSuccess(true));
            }
            else
            {
                return await Task.FromResult(OperationResult<bool>.CreateSuccess(false));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if content is stored for manifest {ManifestId}", manifestId);
            return OperationResult<bool>.CreateFailure($"Failed to check storage status: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> RemoveContentAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        // Note: This only removes the manifest. It does NOT trigger CAS garbage collection.
        // Garbage collection is a separate maintenance task.
        var manifestPath = GetManifestStoragePath(manifestId);

        try
        {
            await Task.Run(
                () =>
            {
                FileOperationsService.DeleteFileIfExists(manifestPath);
            }, cancellationToken);

            _logger.LogInformation("Removed manifest for {ManifestId}. Associated CAS content will be cleaned up by garbage collection.", manifestId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove manifest for {ManifestId}", manifestId);
            return OperationResult<bool>.CreateFailure($"Removal failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<StorageStats> GetStorageStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = new StorageStats();
            var casStats = await _casService.GetStatsAsync(cancellationToken);

            if (Directory.Exists(_storageRoot))
            {
                var manifestFiles = Directory.GetFiles(Path.Combine(_storageRoot, "Manifests"), "*.manifest.json");
                stats.ManifestCount = manifestFiles.Length;
                stats.TotalFileCount = casStats.ObjectCount + stats.ManifestCount;
                stats.TotalSizeBytes = casStats.TotalSize;

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

    private async Task<ContentManifest> StoreContentFilesInCasAsync(
        ContentManifest manifest,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        var updatedFiles = new List<ManifestFile>();

        foreach (var manifestFile in manifest.Files)
        {
            var sourcePath = Path.Combine(sourceDirectory, manifestFile.RelativePath);

            if (!File.Exists(sourcePath))
            {
                if (manifestFile.IsRequired)
                {
                    throw new FileNotFoundException($"Required file not found: {manifestFile.RelativePath}");
                }

                _logger.LogWarning("Optional file not found, skipping: {FilePath}", manifestFile.RelativePath);
                continue;
            }

            var storeResult = await _casService.StoreContentAsync(sourcePath, manifestFile.Hash, cancellationToken);

            if (!storeResult.Success)
            {
                throw new IOException($"Failed to store file in CAS: {manifestFile.RelativePath}. Error: {storeResult.ErrorMessage}");
            }

            var fileInfo = new FileInfo(sourcePath);
            var updatedFile = new ManifestFile
            {
                RelativePath = manifestFile.RelativePath,
                Size = fileInfo.Length,
                Hash = storeResult.Data!, // Use the hash returned by CAS
                SourceType = ContentSourceType.ContentAddressable, // Update source type
                Permissions = manifestFile.Permissions,
                IsExecutable = manifestFile.IsExecutable,
                IsRequired = manifestFile.IsRequired,
                DownloadUrl = manifestFile.DownloadUrl,
                PackageInfo = manifestFile.PackageInfo,
                SourcePath = manifestFile.SourcePath,
                PatchSourceFile = manifestFile.PatchSourceFile,
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
