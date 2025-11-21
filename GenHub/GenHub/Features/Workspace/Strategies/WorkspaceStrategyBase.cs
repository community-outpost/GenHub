using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace.Strategies;

/// <summary>
/// Provides a base implementation for workspace strategies with common functionality.
/// </summary>
/// <typeparam name="T">The concrete strategy type.</typeparam>
public abstract class WorkspaceStrategyBase<T>(
    IFileOperationsService fileOperations,
    ILogger<T> logger)
    : IWorkspaceStrategy
    where T : WorkspaceStrategyBase<T>
{
    private static readonly HashSet<string> EssentialExtensions =
    [
        ".exe", ".dll", ".ini", ".cfg", ".dat", ".xml", FileTypes.JsonFileExtension, ".txt", ".log",
    ];

    private static readonly HashSet<string> CncEssentialExtensions =
    [
        ".big", ".str", ".csf", ".w3d",
    ];

    private static readonly HashSet<string> EssentialDirectories =
    [
        "mods", "patch", "config", "data", "maps", "scripts",
    ];

    private static readonly string[] EssentialPatterns =
    [
        "mod", "patch", "config", "generals", "zerohour", "settings",
    ];

    private static readonly HashSet<string> NonEssentialExtensions =
    [
        ".tga", ".dds", ".bmp", ".jpg", ".jpeg", ".png", ".gif",
        ".wav", ".mp3", ".ogg", ".flac",
        ".avi", ".mp4", ".wmv", ".bik",
    ];

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<T> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// The file operations service.
    /// </summary>
    private readonly IFileOperationsService _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract string Description { get; }

    /// <inheritdoc/>
    public abstract bool RequiresAdminRights { get; }

    /// <inheritdoc/>
    public abstract bool RequiresSameVolume { get; }

    /// <summary>
    /// Gets the logger instance.
    /// </summary>
    protected ILogger<T> Logger => _logger;

    /// <summary>
    /// Gets the file operations service.
    /// </summary>
    protected IFileOperationsService FileOperations => _fileOperations;

    /// <inheritdoc/>
    public abstract bool CanHandle(WorkspaceConfiguration configuration);

    /// <inheritdoc/>
    public abstract long EstimateDiskUsage(WorkspaceConfiguration configuration);

    /// <inheritdoc/>
    public abstract Task<WorkspaceInfo> PrepareAsync(
        WorkspaceConfiguration configuration,
        IProgress<WorkspacePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports progress to the provided progress callback.
    /// </summary>
    /// <param name="progress">The progress callback.</param>
    /// <param name="processedFiles">The number of files processed.</param>
    /// <param name="totalFiles">The total number of files.</param>
    /// <param name="currentOperation">The current operation description.</param>
    /// <param name="currentFile">The current file being processed.</param>
    /// <param name="downloadProgress">Optional download progress for the current file.</param>
    protected static void ReportProgress(
        IProgress<WorkspacePreparationProgress>? progress,
        int processedFiles,
        int totalFiles,
        string currentOperation,
        string currentFile,
        DownloadProgress? downloadProgress = null)
    {
        if (progress == null)
        {
            return;
        }

        progress.Report(new WorkspacePreparationProgress
        {
            FilesProcessed = processedFiles,
            TotalFiles = totalFiles,
            CurrentOperation = currentOperation,
            CurrentFile = currentFile,
            DownloadProgress = downloadProgress,
        });
    }

    /// <summary>
    /// Determines if a file should be considered essential based on built-in rules.
    /// Essential files are those critical for game functionality and should be copied rather than symlinked.
    /// </summary>
    /// <param name="relativePath">The relative path of the file.</param>
    /// <param name="fileSize">The size of the file in bytes.</param>
    /// <returns>True if the file is essential; otherwise, false.</returns>
    protected static bool IsEssentialFile(string relativePath, long fileSize)
    {
        var fileName = Path.GetFileName(relativePath).ToLowerInvariant();
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();
        var directory = Path.GetDirectoryName(relativePath)?.ToLowerInvariant() ?? string.Empty;

        // Always copy small files (under 1MB) - they're usually config/executable files
        if (fileSize < 1024 * 1024)
        {
            return true;
        }

        // Essential file extensions - executables, libraries, configs
        if (EssentialExtensions.Contains(extension))
        {
            return true;
        }

        // Command & Conquer specific essential files
        if (CncEssentialExtensions.Contains(extension))
        {
            return true;
        }

        // Essential directories - always copy content from these
        if (EssentialDirectories.Any(dir => directory.Contains(dir)))
        {
            return true;
        }

        // Essential file patterns
        if (EssentialPatterns.Any(pattern => fileName.Contains(pattern)))
        {
            return true;
        }

        // Large media files are typically non-essential (textures, videos, sounds)
        if (NonEssentialExtensions.Contains(extension))
        {
            return false;
        }

        // Default to essential for unknown files
        return true;
    }

    /// <summary>
    /// Cleans up the workspace directory if a failure occurs during workspace preparation.
    /// Ensures that no partial or corrupted workspace directories are left behind.
    /// Logs the cleanup operation and any exceptions encountered.
    /// </summary>
    /// <param name="workspacePath">The path to the workspace directory to clean up.</param>
    protected void CleanupWorkspaceOnFailure(string workspacePath)
    {
        try
        {
            if (FileOperationsService.DeleteDirectoryIfExists(workspacePath))
            {
                Logger.LogDebug("Successfully cleaned up workspace directory after failure: {WorkspacePath}", workspacePath);
            }
            else
            {
                Logger.LogWarning("Workspace directory did not exist or could not be deleted: {WorkspacePath}", workspacePath);
            }
        }
        catch (Exception cleanupEx)
        {
            Logger.LogWarning(cleanupEx, "Failed to cleanup workspace directory after failure: {WorkspacePath}", workspacePath);
        }
    }

    /// <summary>
    /// Creates a base workspace info object with common properties populated.
    /// </summary>
    /// <param name="configuration">The workspace configuration.</param>
    /// <returns>A new workspace info object.</returns>
    protected WorkspaceInfo CreateBaseWorkspaceInfo(WorkspaceConfiguration configuration)
    {
        var workspacePath = Path.Combine(configuration.WorkspaceRootPath, configuration.Id);

        return new WorkspaceInfo
        {
            Id = configuration.Id,
            WorkspacePath = workspacePath,
            GameClientId = configuration.GameClient.Id,
            Strategy = configuration.Strategy,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            IsValid = true,
        };
    }

    /// <summary>
    /// Updates the workspace info with file count, total size, and configuration details.
    /// </summary>
    /// <param name="workspaceInfo">The workspace info to update.</param>
    /// <param name="fileCount">The number of files processed.</param>
    /// <param name="totalSize">The total size in bytes.</param>
    /// <param name="configuration">The workspace configuration.</param>
    protected void UpdateWorkspaceInfo(
        WorkspaceInfo workspaceInfo,
        int fileCount,
        long totalSize,
        WorkspaceConfiguration configuration)
    {
        workspaceInfo.FileCount = fileCount;
        workspaceInfo.TotalSizeBytes = totalSize;
        workspaceInfo.WorkingDirectory = workspaceInfo.WorkspacePath;

        var gameClientManifest = configuration.Manifests
            .FirstOrDefault(m => m.ContentType == ContentType.GameClient);

        if (gameClientManifest != null)
        {
            var executableFile = gameClientManifest.Files?
                .FirstOrDefault(f => f.IsExecutable);

            if (executableFile != null)
            {
                // Use the full relative path from the manifest
                workspaceInfo.ExecutablePath = Path.Combine(
                    workspaceInfo.WorkspacePath,
                    executableFile.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                _logger.LogInformation(
                    "Executable resolved from GameClient manifest: {ExecutablePath} (marked as IsExecutable)",
                    workspaceInfo.ExecutablePath);
            }
            else
            {
                // Fallback: Try finding any .exe file
                executableFile = gameClientManifest.Files?
                    .FirstOrDefault(f => f.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                if (executableFile != null)
                {
                    workspaceInfo.ExecutablePath = Path.Combine(
                        workspaceInfo.WorkspacePath,
                        executableFile.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                    _logger.LogWarning(
                        "Executable resolved from GameClient manifest by .exe extension (IsExecutable not set): {ExecutablePath}",
                        workspaceInfo.ExecutablePath);
                }
                else
                {
                    _logger.LogWarning(
                        "GameClient manifest '{ManifestId}' does not contain an executable file",
                        gameClientManifest.Id);
                }
            }
        }
        else if (!string.IsNullOrEmpty(configuration.GameClient.ExecutablePath))
        {
            // Fallback: Search for executable by filename in any manifest
            // This supports legacy scenarios and simple workspaces
            var executableFileName = Path.GetFileName(configuration.GameClient.ExecutablePath);

            var executableExistsInManifest = configuration.Manifests
                .SelectMany(m => m.Files ?? Enumerable.Empty<ManifestFile>())
                .Any(f => Path.GetFileName(f.RelativePath).Equals(executableFileName, StringComparison.OrdinalIgnoreCase));

            if (executableExistsInManifest)
            {
                workspaceInfo.ExecutablePath = Path.Combine(workspaceInfo.WorkspacePath, executableFileName);
                _logger.LogDebug(
                    "Executable path resolved by filename search: {ExecutablePath}",
                    workspaceInfo.ExecutablePath);
            }
            else
            {
                _logger.LogDebug(
                    "No executable found in manifests for filename: {ExecutableFileName}",
                    executableFileName);
            }
        }
        else
        {
            _logger.LogDebug("No GameClient configuration or manifest available - executable path not set");
        }
    }

    /// <summary>
    /// Validates that the source file exists and logs appropriate warnings.
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="relativePath">The relative path for logging.</param>
    /// <returns>True if the file exists; otherwise, false.</returns>
    protected bool ValidateSourceFile(string sourcePath, string relativePath)
    {
        if (File.Exists(sourcePath))
        {
            return true;
        }

        _logger.LogWarning("Source file not found: {SourcePath} (relative: {RelativePath})", sourcePath, relativePath);
        return false;
    }

    /// <summary>
    /// Resolves the source path for a manifest file based on configuration and manifest details.
    /// </summary>
    /// <param name="file">The manifest file.</param>
    /// <param name="manifest">The manifest containing the file.</param>
    /// <param name="configuration">The workspace configuration.</param>
    /// <returns>The resolved absolute source path.</returns>
    protected string ResolveSourcePath(ManifestFile file, ContentManifest manifest, WorkspaceConfiguration configuration)
    {
        // Use file's SourcePath if already an absolute path
        if (!string.IsNullOrEmpty(file.SourcePath) && Path.IsPathRooted(file.SourcePath))
        {
            return file.SourcePath;
        }

        // Look up manifest-specific source path from configuration (if manifest has an ID)
        // Note: manifest.Id could be default (empty struct) in tests, so check the value
        var manifestIdValue = manifest.Id.Value;
        if (!string.IsNullOrEmpty(manifestIdValue) &&
            configuration.ManifestSourcePaths != null &&
            configuration.ManifestSourcePaths.TryGetValue(manifestIdValue, out var manifestSourcePath))
        {
            // If file has a relative SourcePath, combine it with manifest's source directory
            var relativePath = !string.IsNullOrEmpty(file.SourcePath) ? file.SourcePath : file.RelativePath;
            return Path.Combine(manifestSourcePath, relativePath);
        }

        // Fallback to BaseInstallationPath for GameInstallation manifests
        if (manifest.ContentType == ContentType.GameInstallation)
        {
            var relativePath = !string.IsNullOrEmpty(file.SourcePath) ? file.SourcePath : file.RelativePath;
            return Path.Combine(configuration.BaseInstallationPath, relativePath);
        }

        // If file has SourcePath, treat as relative to BaseInstallationPath
        if (!string.IsNullOrEmpty(file.SourcePath))
        {
            return Path.Combine(configuration.BaseInstallationPath, file.SourcePath);
        }

        // Final fallback - use RelativePath with BaseInstallationPath
        return Path.Combine(configuration.BaseInstallationPath, file.RelativePath);
    }

    /// <summary>
    /// Gets file size safely with error handling.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>The file size in bytes, or 0 if the file cannot be accessed.</returns>
    protected long GetFileSizeSafe(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Exists ? fileInfo.Length : 0L;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get file size for {FilePath}", filePath);
            return 0L;
        }
    }

    /// <summary>
    /// Calculates total size of manifest files from the source directory.
    /// </summary>
    /// <param name="configuration">The workspace configuration.</param>
    /// <returns>Total size in bytes of all valid files.</returns>
    protected long CalculateActualTotalSize(WorkspaceConfiguration configuration)
    {
        long totalSize = 0L;

        foreach (var file in configuration.Manifests.SelectMany(m => m.Files))
        {
            var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);
            var actualSize = GetFileSizeSafe(sourcePath);
            totalSize += actualSize;

            // Update manifest file size if it's zero or incorrect
            if (file.Size == 0 && actualSize > 0)
            {
                file.Size = actualSize;
            }
        }

        return totalSize;
    }

    /// <summary>
    /// Strategy-specific implementation for linking/copying CAS content.
    /// </summary>
    /// <param name="hash">The hash of the CAS content.</param>
    /// <param name="targetPath">The target path for the CAS file in the workspace.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected abstract Task CreateCasLinkAsync(string hash, string targetPath, CancellationToken cancellationToken);

    /// <summary>
    /// Processes a manifest file according to its source type. Dispatcher for all strategies.
    /// </summary>
    /// <param name="file">The manifest file to process.</param>
    /// <param name="manifest">The manifest containing the file.</param>
    /// <param name="workspacePath">The root path of the workspace.</param>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual async Task ProcessManifestFileAsync(ManifestFile file, ContentManifest manifest, string workspacePath, WorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        var targetPath = Path.Combine(workspacePath, file.RelativePath);
        switch (file.SourceType)
        {
            case ContentSourceType.ContentAddressable:
                await ProcessCasFileAsync(file, targetPath, cancellationToken);
                break;
            case ContentSourceType.GameInstallation:
                await ProcessGameInstallationFileAsync(file, targetPath, configuration, cancellationToken);
                break;
            case ContentSourceType.LocalFile:
                await ProcessLocalFileAsync(file, manifest, targetPath, configuration, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Unsupported content source type: {file.SourceType}");
        }
    }

    /// <summary>
    /// Processes a CAS file with fallback logic. Strategies should call this for CAS files.
    /// </summary>
    /// <param name="file">The manifest file representing the CAS content.</param>
    /// <param name="targetPath">The target path for the file in the workspace.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual async Task ProcessCasFileAsync(ManifestFile file, string targetPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(file.Hash))
        {
            throw new ArgumentException($"ManifestFile {file.RelativePath} has no hash for CAS retrieval");
        }

        try
        {
            // First try the strategy-specific CAS link creation
            await CreateCasLinkAsync(file.Hash, targetPath, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Strategy-specific CAS link creation failed for hash {Hash} at {Path}, attempting direct service fallback", file.Hash, targetPath);

            // Fallback to direct service operations
            try
            {
                var linked = await FileOperations.LinkFromCasAsync(file.Hash, targetPath, useHardLink: false, cancellationToken);
                if (!linked)
                {
                    // Final fallback to copy
                    var copied = await FileOperations.CopyFromCasAsync(file.Hash, targetPath, cancellationToken);
                    if (!copied)
                    {
                        throw new CasStorageException($"CAS content not available for hash {file.Hash}", ex);
                    }
                }
            }
            catch (Exception fallbackEx)
            {
                Logger.LogError(fallbackEx, "All CAS operations failed for hash {Hash} at {Path}", file.Hash, targetPath);
                throw new CasStorageException($"CAS content not available for hash {file.Hash}", fallbackEx);
            }
        }
    }

    /// <summary>
    /// Stub for processing game installation files. Should be implemented in concrete strategies as needed.
    /// </summary>
    /// <param name="file">The manifest file representing the game installation content.</param>
    /// <param name="targetPath">The target path for the file in the workspace.</param>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual Task ProcessGameInstallationFileAsync(ManifestFile file, string targetPath, WorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        // Default: throw if not implemented
        throw new NotImplementedException("ProcessGameInstallationFileAsync must be implemented in the strategy if used.");
    }

    /// <summary>
    /// Stub for processing local files. Should be implemented in concrete strategies as needed.
    /// </summary>
    /// <param name="file">The manifest file representing the local file content.</param>
    /// <param name="manifest">The manifest containing the file.</param>
    /// <param name="targetPath">The target path for the file in the workspace.</param>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected virtual Task ProcessLocalFileAsync(ManifestFile file, ContentManifest manifest, string targetPath, WorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        // Default: throw if not implemented
        throw new NotImplementedException("ProcessLocalFileAsync must be implemented in the strategy if used.");
    }
}