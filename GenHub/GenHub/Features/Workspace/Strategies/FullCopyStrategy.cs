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
/// Workspace strategy that creates complete copies of all game files.
/// Provides maximum compatibility and complete isolation at the cost of disk space.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="FullCopyStrategy"/> class.
/// </remarks>
/// <param name="fileOperations">The file operations service.</param>
/// <param name="logger">The logger instance.</param>
public sealed class FullCopyStrategy(IFileOperationsService fileOperations, ILogger<FullCopyStrategy> logger) : WorkspaceStrategyBase<FullCopyStrategy>(fileOperations, logger)
{
    /// <inheritdoc/>
    public override string Name => "Full Copy";

    /// <inheritdoc/>
    public override string Description => "Creates complete copies of all game files. Maximum compatibility and isolation, but uses the most disk space.";

    /// <inheritdoc/>
    public override bool RequiresAdminRights => false;

    /// <inheritdoc/>
    public override bool RequiresSameVolume => false;

    /// <inheritdoc/>
    public override bool CanHandle(WorkspaceConfiguration configuration)
    {
        return configuration.Strategy == WorkspaceStrategy.FullCopy;
    }

    /// <inheritdoc/>
    public override long EstimateDiskUsage(WorkspaceConfiguration configuration)
    {
        return configuration.Manifest.Files.Sum(f => f.Size);
    }

    /// <inheritdoc/>
    public override async Task<WorkspaceInfo> PrepareAsync(
        WorkspaceConfiguration configuration,
        IProgress<WorkspacePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Logger.LogInformation("Preparing workspace using full copy strategy for {WorkspaceId}", configuration.Id);

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

            Logger.LogDebug("Processing {TotalFiles} files with estimated size {EstimatedSize} bytes", totalFiles, estimatedTotalBytes);

            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            // Process each file
            foreach (var file in configuration.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);
                var destinationPath = Path.Combine(workspacePath, file.RelativePath);

                if (!ValidateSourceFile(sourcePath, file.RelativePath))
                {
                    continue;
                }

                try
                {
                    FileOperationsService.EnsureDirectoryExists(destinationPath);

                    await FileOperations.CopyFileAsync(sourcePath, destinationPath, cancellationToken);

                    // Verify file integrity if hash is provided
                    if (!string.IsNullOrEmpty(file.Hash))
                    {
                        var hashValid = await FileOperations.VerifyFileHashAsync(destinationPath, file.Hash, cancellationToken);
                        if (!hashValid)
                        {
                            Logger.LogWarning("Hash verification failed for file: {RelativePath}", file.RelativePath);
                        }
                    }

                    totalBytesProcessed += file.Size;
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex,
                        "Failed to copy file {RelativePath} from {SourcePath} to {DestinationPath}",
                        file.RelativePath,
                        sourcePath,
                        destinationPath);
                    throw new InvalidOperationException($"Failed to copy file {file.RelativePath}: {ex.Message}", ex);
                }

                processedFiles++;
                ReportProgress(progress, processedFiles, totalFiles, "Copying", file.RelativePath);
            }

            UpdateWorkspaceInfo(workspaceInfo, processedFiles, totalBytesProcessed, configuration);

            Logger.LogInformation(
                "Full copy workspace prepared successfully at {WorkspacePath} with {FileCount} files ({TotalSize} bytes)",
                workspacePath,
                processedFiles,
                totalBytesProcessed);

            return workspaceInfo;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare full copy workspace at {WorkspacePath}", workspacePath);

            CleanupWorkspaceOnFailure(workspacePath);
            throw;
        }
    }
}
