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
/// Workspace strategy that creates complete copies of all game files.
/// Provides maximum compatibility and complete isolation at the cost of disk space.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="FullCopyStrategy"/> class.
/// </remarks>
public sealed class FullCopyStrategy(
    IFileOperationsService fileOperations,
    ILogger<FullCopyStrategy> logger)
    : WorkspaceStrategyBase<FullCopyStrategy>(fileOperations, logger)
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

    /// <summary>
    /// Estimates the total disk usage required for the workspace based on the manifest files.
    /// </summary>
    /// <param name="configuration">The workspace configuration containing the manifest.</param>
    /// <returns>The estimated disk usage in bytes, or <see cref="long.MaxValue"/> if overflow occurs.</returns>
    public override long EstimateDiskUsage(WorkspaceConfiguration configuration)
    {
        if (configuration?.Manifests == null || configuration.Manifests.Count == 0)
            return 0;

        long totalSize = 0;
        foreach (var manifest in configuration.Manifests)
        {
            foreach (var file in manifest.Files)
            {
                // Prevent negative sizes and overflow
                long safeSize = Math.Max(0, file.Size);
                if (long.MaxValue - totalSize < safeSize)
                    return long.MaxValue; // Indicate overflow
                totalSize += safeSize;
            }
        }

        return totalSize;
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
            // Allow cancellation to propagate for tests
            cancellationToken.ThrowIfCancellationRequested();

            // Clean existing workspace if force recreate is requested
            if (Directory.Exists(workspacePath) && configuration.ForceRecreate)
            {
                Logger.LogDebug("Removing existing workspace directory: {WorkspacePath}", workspacePath);
                Directory.Delete(workspacePath, true);
            }

            // Create workspace directory
            Directory.CreateDirectory(workspacePath);

            var allFiles = configuration.Manifests.SelectMany(m => m.Files ?? Enumerable.Empty<ManifestFile>()).ToList();
            var totalFiles = allFiles.Count;
            var processedFiles = 0;
            long totalBytesProcessed = 0;

            Logger.LogDebug("Processing {TotalFiles} files", totalFiles);
            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            // Process each manifest and its files to maintain manifest context for source path resolution
            foreach (var manifest in configuration.Manifests)
            {
                foreach (var file in manifest.Files ?? Enumerable.Empty<ManifestFile>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var destinationPath = Path.Combine(workspacePath, file.RelativePath);

                    try
                    {
                        // Handle different source types
                        if (file.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(file.Hash))
                        {
                            // Use CAS content
                            await CreateCasLinkAsync(file.Hash, destinationPath, cancellationToken);
                        }
                        else
                        {
                            // Resolve source path supporting multi-source installations
                            var sourcePath = ResolveSourcePath(file, manifest, configuration);

                            if (!ValidateSourceFile(sourcePath, file.RelativePath))
                            {
                                continue;
                            }

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
                        }

                        totalBytesProcessed += file.Size;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(
                            ex,
                            "Failed to copy file {RelativePath} to {DestinationPath}",
                            file.RelativePath,
                            destinationPath);
                        throw new InvalidOperationException($"Failed to copy file {file.RelativePath}: {ex.Message}", ex);
                    }

                    processedFiles++;
                    ReportProgress(progress, processedFiles, totalFiles, "Copying", file.RelativePath);
                }
            }

            UpdateWorkspaceInfo(workspaceInfo, processedFiles, totalBytesProcessed, configuration);
            workspaceInfo.IsPrepared = true;

            Logger.LogInformation(
                "Full copy workspace prepared successfully at {WorkspacePath} with {FileCount} files ({TotalSize} bytes)",
                workspacePath,
                processedFiles,
                totalBytesProcessed);

            return workspaceInfo;
        }
        catch (OperationCanceledException)
        {
            // Let cancellation propagate for tests
            CleanupWorkspaceOnFailure(workspacePath);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare full copy workspace at {WorkspacePath}", workspacePath);
            CleanupWorkspaceOnFailure(workspacePath);
            workspaceInfo.IsPrepared = false;
            workspaceInfo.ValidationIssues.Add(new() { Message = ex.Message, Severity = Core.Models.Validation.ValidationSeverity.Error });
            return workspaceInfo;
        }
    }

    /// <summary>
    /// Copies a file from the CAS (Content Addressable Storage) to the specified target path.
    /// </summary>
    /// <param name="hash">The hash of the file in CAS.</param>
    /// <param name="targetPath">The destination path for the copied file.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous copy operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the copy operation fails.</exception>
    protected override async Task CreateCasLinkAsync(string hash, string targetPath, CancellationToken cancellationToken)
    {
        var success = await FileOperations.CopyFromCasAsync(hash, targetPath, cancellationToken);
        if (!success)
        {
            Logger.LogError("Failed to copy from CAS for hash {Hash} to {TargetPath}", hash, targetPath);
            throw new InvalidOperationException($"Failed to copy from CAS for hash {hash} to {targetPath}");
        }
    }

    /// <inheritdoc/>
    protected override async Task ProcessLocalFileAsync(ManifestFile file, ContentManifest manifest, string targetPath, WorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        var sourcePath = ResolveSourcePath(file, manifest, configuration);

        if (!ValidateSourceFile(sourcePath, file.RelativePath))
        {
            return;
        }

        FileOperationsService.EnsureDirectoryExists(Path.GetDirectoryName(targetPath)!);

        await FileOperations.CopyFileAsync(sourcePath, targetPath, cancellationToken);

        // Verify file integrity if hash is provided
        if (!string.IsNullOrEmpty(file.Hash))
        {
            var hashValid = await FileOperations.VerifyFileHashAsync(targetPath, file.Hash, cancellationToken);
            if (!hashValid)
            {
                Logger.LogWarning("Hash verification failed for file: {RelativePath}", file.RelativePath);
            }
        }
    }

    /// <inheritdoc/>
    protected override async Task ProcessGameInstallationFileAsync(ManifestFile file, string targetPath, WorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        // For game installation files, treat them the same as local files
        // We need to find the manifest that contains this file
        var manifest = configuration.Manifests.FirstOrDefault(m => m.Files.Contains(file));
        if (manifest == null)
        {
            throw new InvalidOperationException($"Could not find manifest containing file {file.RelativePath}");
        }

        await ProcessLocalFileAsync(file, manifest, targetPath, configuration, cancellationToken);
    }
}