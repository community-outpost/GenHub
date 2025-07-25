using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace.Strategies;

/// <summary>
/// Workspace strategy that copies essential files and creates symbolic links for others.
/// Balanced disk usage and compatibility.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="HybridCopySymlinkStrategy"/> class.
/// </remarks>
/// <param name="fileOperations">The file operations service.</param>
/// <param name="logger">The logger instance.</param>
public sealed class HybridCopySymlinkStrategy(
    IFileOperationsService fileOperations,
    ILogger<HybridCopySymlinkStrategy> logger) : WorkspaceStrategyBase<HybridCopySymlinkStrategy>(fileOperations, logger)
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

                var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);
                var destinationPath = Path.Combine(workspacePath, file.RelativePath);
                var isEssential = IsEssentialFile(file.RelativePath, file.Size);

                if (!ValidateSourceFile(sourcePath, file.RelativePath))
                {
                    continue;
                }

                try
                {
                    FileOperationsService.EnsureDirectoryExists(destinationPath);

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
                                Logger.LogWarning("Hash verification failed for essential file: {RelativePath}", file.RelativePath);
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
                catch (Exception ex)
                {
                    var operation = isEssential ? "copy" : "create symlink for";
                    Logger.LogError(
                        ex,
                        "Failed to {Operation} file {RelativePath} from {SourcePath} to {DestinationPath}",
                        operation,
                        file.RelativePath,
                        sourcePath,
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
}
