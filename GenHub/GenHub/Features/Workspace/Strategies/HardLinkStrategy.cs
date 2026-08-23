using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
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
    public override string Description => "Creates hard links or symbolic links to game files. Space-efficient zero-copy workspace.";

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
        // Deduplicate files for accurate estimation - only include workspace-targeted files
        var allFiles = configuration.GetWorkspaceUniqueFiles().ToList();

        // HardLink strategy enforces zero-copy (hard links or symlinks)
        return Math.Max(LinkOverheadBytes, allFiles.Count * LinkOverheadBytes);
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
                try
                {
                    Directory.Delete(workspacePath, true);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not delete workspace directory {WorkspacePath}, overwriting files in-place", workspacePath);
                }
            }

            // Create workspace directory
            Directory.CreateDirectory(workspacePath);

            // Deduplicate files by RelativePath with priority ordering (GameClient > GameInstallation)
            // so lower-priority sources cannot overwrite higher-priority files like modded clients.
            // ONLY include files where InstallTarget is Workspace.
            var prioritizedFiles = configuration.Manifests
                .SelectMany((manifest, index) => (manifest.Files ?? Enumerable.Empty<ManifestFile>())
                    .Where(f => f.InstallTarget == ContentInstallTarget.Workspace)
                    .Select(file => new { File = file, Manifest = manifest, ManifestIndex = index }))
                .GroupBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => ContentTypePriority.GetPriority(x.Manifest.ContentType))
                    .ThenByDescending(x => x.ManifestIndex) // deterministic tie-breaker
                    .First())
                .ToList();

            var totalFiles = prioritizedFiles.Count;
            var processedFiles = 0;
            long totalBytesProcessed = 0;
            var hardLinkedFiles = 0;
            var copiedFiles = 0;

            // Check if source and destination are on the same volume
            var sourceRoot = Path.GetPathRoot(configuration.BaseInstallationPath);
            var destRoot = Path.GetPathRoot(workspacePath);
            var sameVolume = string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);

            Logger.LogDebug("Processing {TotalFiles} files (prioritized by content type)", totalFiles);
            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            foreach (var prioritized in prioritizedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var manifest = prioritized.Manifest;
                var file = prioritized.File;
                var destinationPath = Path.Combine(workspacePath, file.RelativePath);

                try
                {
                    if ((file.SourceType == Core.Models.Enums.ContentSourceType.ContentAddressable ||
                         (!string.IsNullOrEmpty(file.Hash) &&
                          file.SourceType != Core.Models.Enums.ContentSourceType.GameInstallation &&
                          file.SourceType != Core.Models.Enums.ContentSourceType.LocalFile)) &&
                        !string.IsNullOrEmpty(file.Hash))
                    {
                        var (hardLinked, bytes) = await ProcessCasFileAsync(file, manifest, destinationPath, sameVolume, cancellationToken);
                        if (hardLinked)
                        {
                            hardLinkedFiles++;
                        }
                        else
                        {
                            copiedFiles++;
                        }

                        totalBytesProcessed += bytes;
                    }
                    else
                    {
                        var processResult = await ProcessStandardFileAsync(file, manifest, destinationPath, configuration, sameVolume, cancellationToken);
                        if (!processResult.Skipped)
                        {
                            if (processResult.HardLinked)
                            {
                                hardLinkedFiles++;
                            }
                            else
                            {
                                copiedFiles++;
                            }

                            totalBytesProcessed += processResult.BytesProcessed;
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
    /// Attempts to create a hard link for the specified CAS file hash at the target path; falls back to symlinking if hard link creation fails.
    /// </summary>
    /// <param name="hash">The content-addressable storage (CAS) hash of the file to link.</param>
    /// <param name="targetPath">The destination path where the link should be created.</param>
    /// <param name="contentType">The content type for pool-specific CAS lookup.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task CreateCasLinkAsync(string hash, string targetPath, ContentType? contentType, CancellationToken cancellationToken)
    {
        var success = await FileOperations.LinkFromCasAsync(hash, targetPath, useHardLink: true, contentType: contentType, cancellationToken: cancellationToken);
        if (!success)
        {
            Logger.LogWarning("Hard link creation failed for hash {Hash}, attempting symlink fallback", hash);
            success = await FileOperations.LinkFromCasAsync(hash, targetPath, useHardLink: false, contentType: contentType, cancellationToken: cancellationToken);
            if (!success)
            {
                throw new UnauthorizedAccessException(
                    $"Failed to create hard link or symbolic link from CAS for hash {hash} to {targetPath}. {WorkspaceConstants.ZeroCopyElevationGuidance}");
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

        if (sameVolume)
        {
            try
            {
                await FileOperations.CreateHardLinkAsync(targetPath, sourcePath, cancellationToken);
            }
            catch (Exception hardLinkEx) when (hardLinkEx is not OperationCanceledException)
            {
                Logger.LogWarning(hardLinkEx, "Hard link creation failed for {RelativePath}, attempting symlink fallback", file.RelativePath);

                try
                {
                    await FileOperations.CreateSymlinkAsync(targetPath, sourcePath, allowFallback: false, cancellationToken);
                }
                catch (Exception symlinkEx) when (symlinkEx is not OperationCanceledException)
                {
                    Logger.LogError(
                        symlinkEx,
                        "Both hard link and symlink creation failed for {RelativePath}. Refusing to copy to prevent disk overhead.",
                        file.RelativePath);

                    throw WrapLinkException(file.RelativePath, symlinkEx);
                }
            }
        }
        else
        {
            // Different volumes: create symlink to maintain zero-copy invariant
            try
            {
                await FileOperations.CreateSymlinkAsync(targetPath, sourcePath, allowFallback: false, cancellationToken);
            }
            catch (Exception symlinkEx) when (symlinkEx is not OperationCanceledException)
            {
                Logger.LogError(
                    symlinkEx,
                    "Cross-volume symlink creation failed for {RelativePath}. Refusing to copy to prevent disk overhead.",
                    file.RelativePath);

                throw WrapLinkException(file.RelativePath, symlinkEx, isCrossVolume: true);
            }
        }
    }

    /// <inheritdoc/>
    protected override async Task ProcessGameInstallationFileAsync(ManifestFile file, string targetPath, WorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        // For game installation files, treat them the same as local files
        // We need to find the manifest that contains this file
        var manifest = configuration.Manifests.FirstOrDefault(m => m.Files.Contains(file)) ?? throw new InvalidOperationException($"Could not find manifest containing file {file.RelativePath}");
        await ProcessLocalFileAsync(file, manifest, targetPath, configuration, cancellationToken);
    }

    private static Exception WrapLinkException(string relativePath, Exception ex, bool isCrossVolume = false)
    {
        if (ex is OperationCanceledException)
        {
            return ex;
        }

        var message = isCrossVolume
            ? $"Failed to create symbolic link across volumes for '{relativePath}'. {WorkspaceConstants.ZeroCopyElevationGuidance}"
            : $"Failed to create hard link or symbolic link for '{relativePath}'. {WorkspaceConstants.ZeroCopyElevationGuidance}";

        return new UnauthorizedAccessException(message, ex);
    }

    private async Task<(bool HardLinked, long BytesProcessed)> ProcessCasFileAsync(
        ManifestFile file,
        ContentManifest manifest,
        string destinationPath,
        bool sameVolume,
        CancellationToken cancellationToken)
    {
        await CreateCasLinkAsync(file.Hash!, destinationPath, manifest.ContentType, cancellationToken);
        return (true, LinkOverheadBytes);
    }

    private async Task<(bool Skipped, bool HardLinked, long BytesProcessed)> ProcessStandardFileAsync(
        ManifestFile file,
        ContentManifest manifest,
        string destinationPath,
        WorkspaceConfiguration configuration,
        bool sameVolume,
        CancellationToken cancellationToken)
    {
        var sourcePath = ResolveSourcePath(file, manifest, configuration);
        Logger.LogDebug(
            "[HardLink] File: {RelativePath}, Manifest: {ManifestId} ({ContentType}), Resolved source: {SourcePath}",
            file.RelativePath,
            manifest.Id.Value,
            manifest.ContentType,
            sourcePath);

        if (!ValidateSourceFile(sourcePath, file.RelativePath))
        {
            return (true, false, 0);
        }

        bool hardLinked = false;
        long bytesProcessed = 0;

        if (sameVolume)
        {
            var result = await ProcessSameVolumeFileAsync(file, sourcePath, destinationPath, cancellationToken);
            if (result.Skipped)
            {
                return (true, false, 0);
            }

            hardLinked = result.HardLinked;
            bytesProcessed = result.BytesProcessed;
        }
        else
        {
            var result = await ProcessDifferentVolumeFileAsync(file, sourcePath, destinationPath, cancellationToken);
            if (result.Skipped)
            {
                return (true, false, 0);
            }

            hardLinked = result.HardLinked;
            bytesProcessed = result.BytesProcessed;
        }

        return (false, hardLinked, bytesProcessed);
    }

    private async Task<(bool Skipped, bool HardLinked, long BytesProcessed, bool VerifyHash)> ProcessSameVolumeFileAsync(
        ManifestFile file,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await FileOperations.CreateHardLinkAsync(destinationPath, sourcePath, cancellationToken);
            return (false, true, LinkOverheadBytes, false);
        }
        catch (IOException ioEx)
        {
            if (ioEx.Message.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
                ioEx.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning("Skipping missing file: {RelativePath} (source: {SourcePath})", file.RelativePath, sourcePath);
                return (true, false, 0, false);
            }

            Logger.LogWarning(ioEx, "Hard link creation failed for {RelativePath}, attempting symlink fallback", file.RelativePath);

            try
            {
                await FileOperations.CreateSymlinkAsync(destinationPath, sourcePath, allowFallback: false, cancellationToken);
                return (false, true, LinkOverheadBytes, false);
            }
            catch (Exception symlinkEx) when (symlinkEx is not OperationCanceledException)
            {
                Logger.LogError(
                    symlinkEx,
                    "Both hard link and symlink creation failed for {RelativePath}. Refusing to copy to prevent disk overhead.",
                    file.RelativePath);

                throw WrapLinkException(file.RelativePath, symlinkEx);
            }
        }
    }

    private async Task<(bool Skipped, bool HardLinked, long BytesProcessed, bool VerifyHash)> ProcessDifferentVolumeFileAsync(
        ManifestFile file,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            Logger.LogWarning("Skipping missing file: {RelativePath} (source: {SourcePath})", file.RelativePath, sourcePath);
            return (true, false, 0, false);
        }

        try
        {
            await FileOperations.CreateSymlinkAsync(destinationPath, sourcePath, allowFallback: false, cancellationToken);
            return (false, true, LinkOverheadBytes, false);
        }
        catch (Exception symlinkEx) when (symlinkEx is not OperationCanceledException)
        {
            Logger.LogError(
                symlinkEx,
                "Cross-volume symlink creation failed for {RelativePath}. Refusing to copy to prevent disk overhead.",
                file.RelativePath);

            throw WrapLinkException(file.RelativePath, symlinkEx, isCrossVolume: true);
        }
    }
}
