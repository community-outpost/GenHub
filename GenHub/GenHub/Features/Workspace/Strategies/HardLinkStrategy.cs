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
        var allFiles = configuration.Manifests.SelectMany(m => m.Files ?? Enumerable.Empty<ManifestFile>()).ToList();

        // Check if source and destination are on the same volume
        var sourceRoot = Path.GetPathRoot(configuration.BaseInstallationPath);
        var destRoot = Path.GetPathRoot(configuration.WorkspaceRootPath);

        if (string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Same volume: hard links use minimal space
            // Even empty workspaces need some directory overhead
            return Math.Max(LinkOverheadBytes, allFiles.Count * LinkOverheadBytes);
        }
        else
        {
            // Different volumes: will fall back to copying
            return allFiles.Sum(f => f.Size);
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
                        if (file.SourceType == Core.Models.Enums.ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(file.Hash))
                        {
                            // Use CAS content
                            await CreateCasLinkAsync(file.Hash, destinationPath, cancellationToken);
                            if (sameVolume)
                            {
                                hardLinkedFiles++;
                                totalBytesProcessed += LinkOverheadBytes;
                            }
                            else
                            {
                                copiedFiles++;
                                totalBytesProcessed += file.Size;
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

                            var verifyHash = !sameVolume; // For different volumes, always copy, so verify
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
                                    verifyHash = true; // Since copied, verify
                                }
                            }
                            else
                            {
                                // Different volumes, must copy
                                await FileOperations.CopyFileAsync(sourcePath, destinationPath, cancellationToken);
                                copiedFiles++;
                                totalBytesProcessed += file.Size;
                                verifyHash = true; // Copied, verify
                            }

                            // Verify file integrity if hash is provided and file was copied
                            if (verifyHash && !string.IsNullOrEmpty(file.Hash))
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
            }

            UpdateWorkspaceInfo(workspaceInfo, processedFiles, totalBytesProcessed, configuration);
            workspaceInfo.IsPrepared = true;

            Logger.LogInformation(
                "Hard link workspace prepared successfully at {WorkspacePath} with {HardLinked} hard links and {Copied} copies ({TotalSize} bytes)",
                workspacePath,
                hardLinkedFiles,
                copiedFiles,
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
            Logger.LogError(ex, "Failed to prepare hard link workspace at {WorkspacePath}", workspacePath);
            CleanupWorkspaceOnFailure(workspacePath);
            workspaceInfo.IsPrepared = false;
            workspaceInfo.ValidationIssues.Add(new() { Message = ex.Message, Severity = Core.Models.Validation.ValidationSeverity.Error });
            return workspaceInfo;
        }
    }

    /// <summary>
    /// Attempts to create a hard link for the specified CAS file hash at the target path; falls back to copying if hard link creation fails.
    /// </summary>
    /// <param name="hash">The content-addressable storage (CAS) hash of the file to link or copy.</param>
    /// <param name="targetPath">The destination path where the hard link or copy should be created.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task CreateCasLinkAsync(string hash, string targetPath, CancellationToken cancellationToken)
    {
        var success = await FileOperations.LinkFromCasAsync(hash, targetPath, useHardLink: true, cancellationToken);
        if (!success)
        {
            Logger.LogWarning("Hard link creation failed for hash {Hash}, attempting copy fallback", hash);
            success = await FileOperations.CopyFromCasAsync(hash, targetPath, cancellationToken);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to create hard link or copy from CAS for hash {hash} to {targetPath}");
            }
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

        // Check if source and destination are on the same volume
        var sourceRoot = Path.GetPathRoot(sourcePath);
        var destRoot = Path.GetPathRoot(targetPath);
        var sameVolume = string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);

        var verifyHash = !sameVolume;
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
                verifyHash = true;
            }
        }
        else
        {
            // Different volumes, must copy
            await FileOperations.CopyFileAsync(sourcePath, targetPath, cancellationToken);
            verifyHash = true;
        }

        // Verify file integrity if hash is provided and file was copied
        if (verifyHash && !string.IsNullOrEmpty(file.Hash))
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
