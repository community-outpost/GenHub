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
/// Workspace strategy that creates hard links to game files where possible, falling back to copy.
/// Space-efficient, requires same volume for optimal results.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="HardLinkStrategy"/> class.
/// </remarks>
public sealed class HardLinkStrategy(IFileOperationsService fileOperations, ILogger<HardLinkStrategy> logger) : WorkspaceStrategyBase<HardLinkStrategy>(fileOperations, logger)
{
    private const long LinkOverheadBytes = 1024L;

    /// <inheritdoc/>
    public override string Name => "Hard Link";

    /// <inheritdoc/>
    public override string Description => "Creates hard links where possible, copies otherwise. Space-efficient with good performance, works best on same volume.";

    /// <inheritdoc/>
    public override bool RequiresAdminRights => false;

    /// <inheritdoc/>
    public override bool RequiresSameVolume => true;

    /// <inheritdoc/>
    public override bool CanHandle(WorkspaceConfiguration configuration)
    {
        return configuration.Strategy == WorkspaceStrategy.HardLink;
    }

    /// <inheritdoc/>
    public override long EstimateDiskUsage(WorkspaceConfiguration configuration)
    {
        // Check if source and destination are on the same volume
        var sourceRoot = Path.GetPathRoot(configuration.BaseInstallationPath);
        var destRoot = Path.GetPathRoot(configuration.WorkspaceRootPath);

        if (string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Same volume: hard links use minimal space (just directory entries)
            return configuration.Manifest.Files.Count * LinkOverheadBytes;
        }
        else
        {
            // Different volumes: will fall back to copying
            return configuration.Manifest.Files.Sum(f => f.Size);
        }
    }

    /// <inheritdoc/>
    public override async Task<WorkspaceInfo> PrepareAsync(
        WorkspaceConfiguration configuration,
        IProgress<WorkspacePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Logger.LogInformation("Preparing workspace using hard link strategy for {WorkspaceId}", configuration.Id);

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

            var hardLinkedFiles = 0;
            var copiedFiles = 0;

            // Check if source and destination are on the same volume
            var sourceRoot = Path.GetPathRoot(configuration.BaseInstallationPath);
            var destRoot = Path.GetPathRoot(workspacePath);
            var sameVolume = string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);

            if (!sameVolume)
            {
                Logger.LogWarning(
                    "Source ({SourceVolume}) and destination ({DestVolume}) are on different volumes. Hard links will fall back to copies.",
                    sourceRoot,
                    destRoot);
            }

            Logger.LogDebug("Processing {TotalFiles} files with estimated size {EstimatedSize} bytes", totalFiles, estimatedTotalBytes);

            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            // Process each file
            foreach (var file in configuration.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var destinationPath = Path.Combine(workspacePath, file.RelativePath);

                try
                {
                    FileOperationsService.EnsureDirectoryExists(destinationPath);

                    // Handle different source types
                    if (file.SourceType == Core.Models.Enums.ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(file.Hash))
                    {
                        // Use CAS content
                        await CreateCasLinkAsync(file.Hash, destinationPath, cancellationToken);
                        hardLinkedFiles++;
                        totalBytesProcessed += LinkOverheadBytes;
                    }
                    else
                    {
                        // Use regular file from base installation
                        var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);

                        if (!ValidateSourceFile(sourcePath, file.RelativePath))
                        {
                            continue;
                        }

                        if (sameVolume)
                        {
                            try
                            {
                                await FileOperations.CreateHardLinkAsync(destinationPath, sourcePath, cancellationToken);
                                hardLinkedFiles++;
                                totalBytesProcessed += LinkOverheadBytes; // Minimal overhead for hard links
                            }
                            catch (Exception hardLinkEx)
                            {
                                Logger.LogDebug(hardLinkEx, "Hard link creation failed for {RelativePath}, falling back to copy", file.RelativePath);

                                // Fall back to copy
                                await FileOperations.CopyFileAsync(sourcePath, destinationPath, cancellationToken);
                                copiedFiles++;
                                totalBytesProcessed += file.Size;
                            }
                        }
                        else
                        {
                            // Different volumes, must copy
                            await FileOperations.CopyFileAsync(sourcePath, destinationPath, cancellationToken);
                            copiedFiles++;
                            totalBytesProcessed += file.Size;
                        }

                        // Verify file integrity if hash is provided (only for copied files)
                        if (!string.IsNullOrEmpty(file.Hash) && (copiedFiles > 0 || !sameVolume))
                        {
                            var hashValid = await FileOperations.VerifyFileHashAsync(destinationPath, file.Hash, cancellationToken);
                            if (!hashValid)
                            {
                                Logger.LogWarning("Hash verification failed for file: {RelativePath}", file.RelativePath);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex,
                        "Failed to process file {RelativePath} to {DestinationPath}",
                        file.RelativePath,
                        destinationPath);
                    throw new InvalidOperationException($"Failed to process file {file.RelativePath}: {ex.Message}", ex);
                }

                processedFiles++;
                var operation = sameVolume ? "Hard linking" : "Copying";
                ReportProgress(progress, processedFiles, totalFiles, operation, file.RelativePath);
            }

            UpdateWorkspaceInfo(workspaceInfo, processedFiles, totalBytesProcessed, configuration);

            Logger.LogInformation(
                "Hard link workspace prepared successfully at {WorkspacePath} with {HardLinked} hard links and {Copied} copies ({TotalSize} bytes)",
                workspacePath,
                hardLinkedFiles,
                copiedFiles,
                totalBytesProcessed);

            return workspaceInfo;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare hard link workspace at {WorkspacePath}", workspacePath);

            CleanupWorkspaceOnFailure(workspacePath);

            throw;
        }
    }

    /// <summary>
    /// Attempts to create a hard link for the specified CAS file hash at the target path; falls back to copying if hard link creation fails.
    /// </summary>
    /// <param name="hash">The content-addressable storage (CAS) hash of the file to link or copy.</param>
    /// <param name="targetPath">The destination path where the hard link or copy should be created.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when both hard link creation and copy fallback fail.</exception>
    protected override async Task CreateCasLinkAsync(string hash, string targetPath, CancellationToken cancellationToken)
    {
        var success = await FileOperations.LinkFromCasAsync(hash, targetPath, useHardLink: true, cancellationToken);
        if (success)
        {
            return;
        }

        Logger.LogWarning("Hard link creation failed for hash {Hash}, attempting copy fallback", hash);
        success = await FileOperations.CopyFromCasAsync(hash, targetPath, cancellationToken);
        if (!success)
        {
            throw new InvalidOperationException($"Failed to create hard link or copy from CAS for hash {hash} to {targetPath}");
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

        // Check if source and destination are on the same volume
        var sourceRoot = Path.GetPathRoot(sourcePath);
        var destRoot = Path.GetPathRoot(targetPath);
        var sameVolume = string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);

        if (sameVolume)
        {
            try
            {
                await FileOperations.CreateHardLinkAsync(targetPath, sourcePath, cancellationToken);
            }
            catch (Exception hardLinkEx)
            {
                Logger.LogDebug(hardLinkEx, "Hard link creation failed for {RelativePath}, falling back to copy", file.RelativePath);

                // Fall back to copy
                await FileOperations.CopyFileAsync(sourcePath, targetPath, cancellationToken);
            }
        }
        else
        {
            // Different volumes, must copy
            await FileOperations.CopyFileAsync(sourcePath, targetPath, cancellationToken);
        }

        // Verify file integrity if hash is provided (only for copied files)
        if (!string.IsNullOrEmpty(file.Hash))
        {
            var hashValid = await FileOperations.VerifyFileHashAsync(targetPath, file.Hash, cancellationToken);
            if (!hashValid)
            {
                Logger.LogWarning("Hash verification failed for file: {RelativePath}", file.RelativePath);
            }
        }
    }
}
