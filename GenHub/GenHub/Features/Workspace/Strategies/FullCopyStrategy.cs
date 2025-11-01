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

            Logger.LogDebug("Processing {TotalFiles} files in parallel", totalFiles);
            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            int degreeOfParallelism;
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(workspacePath) ?? "C:\\");
                degreeOfParallelism = driveInfo.DriveType == DriveType.Fixed
                    ? Math.Min(Environment.ProcessorCount, 4) // HDD - limited parallelism
                    : Environment.ProcessorCount * 2;         // SSD - higher parallelism safe
                Logger.LogDebug(
                    "[Workspace] Using {DegreeOfParallelism} degree of parallelism for {DriveType} drive",
                    degreeOfParallelism,
                    driveInfo.DriveType);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[Workspace] Failed to detect drive type, using default parallelism");
                degreeOfParallelism = Environment.ProcessorCount * 2;
            }

            // Group files by destination path to handle conflicts
            var filesByDestination = configuration.Manifests
                .SelectMany(m => (m.Files ?? Enumerable.Empty<ManifestFile>()).Select(f => new { Manifest = m, File = f }))
                .GroupBy(item => item.File.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await Parallel.ForEachAsync(
                filesByDestination,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = degreeOfParallelism,
                    CancellationToken = cancellationToken,
                },
                async (fileGroup, ct) =>
                {
                    // For each destination path, process files in priority order (lowest to highest)
                    // Priority: GameInstallation (0) < GameClient (1) < Mod (2)
                    // This ensures higher priority content overwrites lower priority
                    var orderedFiles = fileGroup
                        .OrderBy(item => item.Manifest.ContentType)
                        .ToList();

                    // Process all versions of this file in priority order
                    // The last one (highest priority) will be the final version
                    foreach (var item in orderedFiles)
                    {
                        var destinationPath = Path.Combine(workspacePath, item.File.RelativePath);

                        try
                        {
                            // Handle different source types
                            if (item.File.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(item.File.Hash))
                            {
                                // Use CAS content
                                await CreateCasLinkAsync(item.File.Hash, destinationPath, ct);
                            }
                            else
                            {
                                // Resolve source path supporting multi-source installations
                                var sourcePath = ResolveSourcePath(item.File, item.Manifest, configuration);

                                if (!ValidateSourceFile(sourcePath, item.File.RelativePath))
                                {
                                    continue;
                                }

                                await FileOperations.CopyFileAsync(sourcePath, destinationPath, ct);

                                // Verify file integrity if hash is provided
                                if (!string.IsNullOrEmpty(item.File.Hash))
                                {
                                    var hashValid = await FileOperations.VerifyFileHashAsync(destinationPath, item.File.Hash, ct);
                                    if (!hashValid)
                                    {
                                        Logger.LogWarning("Hash verification failed for file: {RelativePath}", item.File.RelativePath);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(
                                ex,
                                "Failed to copy file {RelativePath} to {DestinationPath}",
                                item.File.RelativePath,
                                destinationPath);
                            throw new InvalidOperationException($"Failed to copy file {item.File.RelativePath}: {ex.Message}", ex);
                        }
                    }

                    // Only count the file group once for progress reporting
                    Interlocked.Add(ref totalBytesProcessed, orderedFiles.First().File.Size);
                    var current = Interlocked.Increment(ref processedFiles);
                    if (current % 50 == 0 || current == totalFiles)
                    {
                        ReportProgress(progress, current, totalFiles, "Copying files", orderedFiles.First().File.RelativePath);
                    }
                });

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
