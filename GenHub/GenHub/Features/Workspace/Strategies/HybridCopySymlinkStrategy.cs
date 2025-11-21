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
    public override bool RequiresAdminRights => Environment.OSVersion.Platform == PlatformID.Win32NT;

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
        if (configuration?.Manifests == null || configuration.Manifests.Count == 0)
            return 0;

        long totalUsage = 0;
        foreach (var manifest in configuration.Manifests)
        {
            foreach (var file in manifest.Files)
            {
                if (IsEssentialFile(file.RelativePath, file.Size))
                {
                    totalUsage += file.Size;
                }
                else
                {
                    totalUsage += LinkOverheadBytes;
                }
            }
        }

        return totalUsage;
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
            var estimatedTotalBytes = EstimateDiskUsage(configuration);
            var copiedFiles = 0;
            var symlinkedFiles = 0;
            Logger.LogDebug("Processing {TotalFiles} files with estimated size {EstimatedSize} bytes", totalFiles, estimatedTotalBytes);

            // Pre-classify files for reporting
            var essentialCount = allFiles.Count(f => IsEssentialFile(f.RelativePath, f.Size));
            var nonEssentialCount = totalFiles - essentialCount;
            Logger.LogDebug(
                "Classified {EssentialCount} essential files (will copy) and {NonEssentialCount} non-essential files (will symlink)",
                essentialCount,
                nonEssentialCount);
            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            // Process each manifest and its files to maintain manifest context for source path resolution
            foreach (var manifest in configuration.Manifests)
            {
                foreach (var file in manifest.Files ?? Enumerable.Empty<ManifestFile>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destinationPath = Path.Combine(workspacePath, file.RelativePath);
                    var isEssential = IsEssentialFile(file.RelativePath, file.Size);

                    try
                    {
                        if (file.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(file.Hash))
                        {
                            if (isEssential)
                            {
                                await FileOperations.CopyFromCasAsync(file.Hash, destinationPath, cancellationToken);
                                copiedFiles++;
                                totalBytesProcessed += file.Size;
                            }
                            else
                            {
                                await FileOperations.LinkFromCasAsync(file.Hash, destinationPath, useHardLink: false, cancellationToken);
                                symlinkedFiles++;
                                totalBytesProcessed += LinkOverheadBytes;
                            }
                        }
                        else
                        {
                            // Resolve source path supporting multi-source installations
                            var sourcePath = ResolveSourcePath(file, manifest, configuration);
                            if (!ValidateSourceFile(sourcePath, file.RelativePath))
                            {
                                continue;
                            }

                            if (isEssential)
                            {
                                await FileOperations.CopyFileAsync(sourcePath, destinationPath, cancellationToken);
                                copiedFiles++;
                                totalBytesProcessed += file.Size;
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
                                await FileOperations.CreateSymlinkAsync(destinationPath, sourcePath, allowFallback: false, cancellationToken);
                                symlinkedFiles++;
                                totalBytesProcessed += LinkOverheadBytes;
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
            }

            UpdateWorkspaceInfo(workspaceInfo, processedFiles, totalBytesProcessed, configuration);
            workspaceInfo.IsPrepared = true;
            Logger.LogInformation(
                "Hybrid copy-symlink workspace prepared successfully at {WorkspacePath} with {CopiedCount} copied files and {SymlinkedCount} symlinks ({TotalSize} bytes)",
                workspacePath,
                copiedFiles,
                symlinkedFiles,
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
        var success = await FileOperations.CopyFromCasAsync(hash, targetPath, cancellationToken);
        if (!success)
        {
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
            await FileOperations.CreateSymlinkAsync(targetPath, sourcePath, allowFallback: false, cancellationToken);
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