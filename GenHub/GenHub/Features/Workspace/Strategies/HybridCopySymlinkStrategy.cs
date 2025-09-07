using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace.Strategies;

/// <summary>
/// Workspace strategy that copies essential files and creates symbolic links for others.
/// Balanced disk usage and compatibility.
/// </summary>
/// <remarks>
/// HybridCopySymlinkStrategy provides a balance between copying essential files and symlinking large files.
/// </remarks>
public sealed class HybridCopySymlinkStrategy(IFileOperationsService fileOperations, ILogger<HybridCopySymlinkStrategy> logger) : WorkspaceStrategyBase<HybridCopySymlinkStrategy>(fileOperations, logger)
{
    private const long LinkOverheadBytes = 1024L;

    /// <inheritdoc/>
    public override string Name => "Hybrid Copy-Symlink";

    /// <inheritdoc/>
    public override string Description => "Copies essential files (executables, configs, small files) and creates symlinks for large media files. Balanced disk usage and compatibility.";

    /// <inheritdoc/>
    public override bool RequiresAdminRights => OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public override bool RequiresSameVolume => false;

    /// <inheritdoc/>
    public override bool CanHandle(WorkspaceConfiguration configuration)
    {
        return configuration.Strategy == WorkspaceStrategy.HybridCopySymlink;
    }

    /// <inheritdoc/>
    public override long EstimateDiskUsage(WorkspaceConfiguration configuration)
    {
        long estimatedSize = 0;
        foreach (var file in configuration.Manifest.Files)
        {
            if (IsEssentialFile(file.RelativePath, file.Size))
            {
                estimatedSize += file.Size; // Essential files are copied
            }
            else
            {
                estimatedSize += LinkOverheadBytes; // Non-essential files are symlinked (minimal overhead)
            }
        }

        return estimatedSize;
    }

    /// <inheritdoc/>
    public override async Task<WorkspaceInfo> PrepareAsync(
        WorkspaceConfiguration configuration,
        IProgress<WorkspacePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Logger.LogInformation("Preparing workspace using hybrid copy-symlink strategy for {WorkspaceId}", configuration.Id);
        var workspaceInfo = CreateBaseWorkspaceInfo(configuration);
        var workspacePath = workspaceInfo.WorkspacePath;
        try
        {
            // Clean existing workspace if force recreate is requested
            if (Directory.Exists(workspacePath) && configuration.ForceRecreate)
            {
                Logger.LogDebug("Removing existing workspace directory: {WorkspacePath}", workspacePath);
                Directory.Delete(workspacePath, true);
            }

            // Create workspace directory
            Directory.CreateDirectory(workspacePath);
            var totalFiles = configuration.Manifest.Files.Count;
            var processedFiles = 0;
            long totalBytesProcessed = 0;
            var estimatedTotalBytes = EstimateDiskUsage(configuration);
            var copiedFiles = 0;
            var symlinkedFiles = 0;
            Logger.LogDebug("Processing {TotalFiles} files with estimated size {EstimatedSize} bytes", totalFiles, estimatedTotalBytes);

            // Pre-classify files for reporting
            var essentialCount = configuration.Manifest.Files.Count(f => IsEssentialFile(f.RelativePath, f.Size));
            var nonEssentialCount = totalFiles - essentialCount;
            Logger.LogDebug(
                "Classified {EssentialCount} essential files (will copy) and {NonEssentialCount} non-essential files (will symlink)",
                essentialCount,
                nonEssentialCount);
            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            // Process each file
            foreach (var file in configuration.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = Path.Combine(workspacePath, file.RelativePath);
                var isEssential = IsEssentialFile(file.RelativePath, file.Size);

                try
                {
                    FileOperationsService.EnsureDirectoryExists(destinationPath);

                    // Handle different source types
                    if (file.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(file.Hash))
                    {
                        // Use CAS content
                        await CreateCasLinkAsync(file.Hash, destinationPath, cancellationToken);
                        if (isEssential)
                        {
                            copiedFiles++;
                            totalBytesProcessed += file.Size;
                        }
                        else
                        {
                            symlinkedFiles++;
                            totalBytesProcessed += LinkOverheadBytes;
                        }
                    }
                    else
                    {
                        // Use regular file from base installation
                        var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);

                        if (!ValidateSourceFile(sourcePath, file.RelativePath))
                        {
                            continue;
                        }

                        if (isEssential)
                        {
                            // Copy essential files
                            await FileOperations.CopyFileAsync(sourcePath, destinationPath, cancellationToken);
                            copiedFiles++;
                            totalBytesProcessed += file.Size;

                            // Verify file integrity if hash is provided
                            if (!string.IsNullOrEmpty(file.Hash))
                            {
                                var hashValid = await FileOperations.VerifyFileHashAsync(destinationPath, file.Hash, cancellationToken);
                                if (!hashValid)
                                {
                                    throw new InvalidOperationException($"Hash verification failed for essential file: {file.RelativePath}");
                                }
                            }
                        }
                        else
                        {
                            // Create symlinks for non-essential files
                            await FileOperations.CreateSymlinkAsync(destinationPath, sourcePath, cancellationToken);
                            symlinkedFiles++;
                            totalBytesProcessed += LinkOverheadBytes; // Approximate symlink overhead
                        }
                    }
                }
                catch (Exception ex)
                {
                    var operation = isEssential ? "copy" : "create symlink for";
                    Logger.LogError(
                        ex,
                        "Failed to {Operation} file {RelativePath} to {DestinationPath}",
                        operation,
                        file.RelativePath,
                        destinationPath);
                    throw new InvalidOperationException($"Failed to {operation} file {file.RelativePath}: {ex.Message}", ex);
                }

                processedFiles++;
                var currentOperation = isEssential ? "Copying essential file" : "Creating symlink";
                ReportProgress(progress, processedFiles, totalFiles, currentOperation, file.RelativePath);
            }

            UpdateWorkspaceInfo(workspaceInfo, processedFiles, totalBytesProcessed, configuration);
            Logger.LogInformation(
                "Hybrid copy-symlink workspace prepared successfully at {WorkspacePath} with {CopiedCount} copied files and {SymlinkedCount} symlinks ({TotalSize} bytes)",
                workspacePath,
                copiedFiles,
                symlinkedFiles,
                totalBytesProcessed);
            return workspaceInfo;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare hybrid copy-symlink workspace at {WorkspacePath}", workspacePath);
            CleanupWorkspaceOnFailure(workspacePath);
            throw;
        }
    }

    /// <summary>
    /// Copies or symlinks CAS content for the given hash and target path based on strategy logic.
    /// </summary>
    /// <param name="hash">CAS hash of the file.</param>
    /// <param name="targetPath">Target path for the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    protected override async Task CreateCasLinkAsync(string hash, string targetPath, CancellationToken cancellationToken)
    {
        if (ShouldCopyFile(targetPath))
        {
            var success = await FileOperations.CopyFromCasAsync(hash, targetPath, cancellationToken);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to copy from CAS for hash {hash} to {targetPath}");
            }
        }
        else
        {
            var success = await FileOperations.LinkFromCasAsync(hash, targetPath, useHardLink: false, cancellationToken);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to create symlink from CAS for hash {hash} to {targetPath}");
            }
        }
    }

    /// <inheritdoc/>
    protected override async Task ProcessLocalFileAsync(ManifestFile file, string targetPath, WorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);

        if (!ValidateSourceFile(sourcePath, file.RelativePath))
        {
            return;
        }

        FileOperationsService.EnsureDirectoryExists(targetPath);

        var isEssential = IsEssentialFile(file.RelativePath, file.Size);

        if (isEssential)
        {
            // Copy essential files
            await FileOperations.CopyFileAsync(sourcePath, targetPath, cancellationToken);

            // Verify file integrity if hash is provided
            if (!string.IsNullOrEmpty(file.Hash))
            {
                var hashValid = await FileOperations.VerifyFileHashAsync(targetPath, file.Hash, cancellationToken);
                if (!hashValid)
                {
                    throw new InvalidOperationException($"Hash verification failed for essential file: {file.RelativePath}");
                }
            }
        }
        else
        {
            // Create symlinks for non-essential files
            await FileOperations.CreateSymlinkAsync(targetPath, sourcePath, cancellationToken);
        }
    }

    private bool ShouldCopyFile(string targetPath)
    {
        // Use manifest size if available, otherwise fallback to file size
        long fileSize = 0;
        try
        {
            if (File.Exists(targetPath))
            {
                fileSize = new FileInfo(targetPath).Length;
            }
        }
        catch
        {
            fileSize = 0;
            Logger.LogWarning("Failed to get file size for {TargetPath}, defaulting to 0", targetPath);
        }

        // Use the static IsEssentialFile logic for consistency
        return WorkspaceStrategyBase<HybridCopySymlinkStrategy>.IsEssentialFile(targetPath, fileSize);
    }
}
