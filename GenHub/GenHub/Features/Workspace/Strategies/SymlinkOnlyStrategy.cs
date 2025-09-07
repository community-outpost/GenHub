using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace.Strategies;

/// <summary>
/// Workspace strategy that creates symbolic links to all game files.
/// Minimal disk usage, requires administrator privileges.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SymlinkOnlyStrategy"/> class.
/// </remarks>
public sealed class SymlinkOnlyStrategy(
    IFileOperationsService fileOperations,
    ILogger<SymlinkOnlyStrategy> logger)
    : WorkspaceStrategyBase<SymlinkOnlyStrategy>(fileOperations, logger)
{
    private const long LinkOverheadBytes = 1024L;

    /// <inheritdoc/>
    public override string Name => "Symlink Only";

    /// <inheritdoc/>
    public override string Description => "Creates symbolic links to all game files. Minimal disk usage but requires administrator privileges.";

    /// <inheritdoc/>
    public override bool RequiresAdminRights => OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public override bool RequiresSameVolume => false;

    /// <inheritdoc/>
    public override bool CanHandle(WorkspaceConfiguration configuration)
    {
        return configuration.Strategy == WorkspaceStrategy.SymlinkOnly;
    }

    /// <inheritdoc/>
    public override long EstimateDiskUsage(WorkspaceConfiguration configuration)
    {
        // Symbolic links use minimal space - approximate 1KB per link for metadata
        return configuration.Manifest.Files.Count * LinkOverheadBytes;
    }

    /// <inheritdoc/>
    public override async Task<WorkspaceInfo> PrepareAsync(
        WorkspaceConfiguration configuration,
        IProgress<WorkspacePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Logger.LogInformation("Preparing workspace using symlink-only strategy for {WorkspaceId}", configuration.Id);

        var workspaceInfo = CreateBaseWorkspaceInfo(configuration);
        var workspacePath = workspaceInfo.WorkspacePath;

        try
        {
            if (configuration.ForceRecreate)
            {
                try
                {
                    Logger.LogDebug("Removing existing workspace directory: {WorkspacePath}", workspacePath);
                    FileOperationsService.DeleteDirectoryIfExists(workspacePath);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to clean existing workspace at {WorkspacePath}", workspacePath);
                    throw;
                }
            }

            // Create workspace directory
            Directory.CreateDirectory(workspacePath);

            var totalFiles = configuration.Manifest.Files.Count;
            var processedFiles = 0;

            Logger.LogDebug("Processing {TotalFiles} files", totalFiles);

            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            // Process each file using the base class method that has proper fallback logic
            foreach (var file in configuration.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Use the base class method that handles CAS files with proper fallback
                    await ProcessManifestFileAsync(file, workspacePath, configuration, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex,
                        "Failed to process file {RelativePath}",
                        file.RelativePath);
                    throw new InvalidOperationException($"Failed to process file {file.RelativePath}: {ex.Message}", ex);
                }

                processedFiles++;
                ReportProgress(progress, processedFiles, totalFiles, "Creating symlink", file.RelativePath);
            }

            UpdateWorkspaceInfo(workspaceInfo, processedFiles, 0, configuration);

            Logger.LogInformation(
                "Symlink-only workspace prepared successfully at {WorkspacePath} with {FileCount} symlinks",
                workspacePath,
                processedFiles);

            return workspaceInfo;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare symlink-only workspace at {WorkspacePath}", workspacePath);

            CleanupWorkspaceOnFailure(workspacePath);

            throw;
        }
    }

    /// <inheritdoc/>
    protected override async Task CreateCasLinkAsync(string hash, string targetPath, CancellationToken cancellationToken)
    {
        Logger.LogDebug("Creating CAS symlink for hash {Hash} to {TargetPath}", hash, targetPath);
        FileOperationsService.EnsureDirectoryExists(targetPath);

        // Use the service method to create the link from CAS
        var success = await FileOperations.LinkFromCasAsync(hash, targetPath, useHardLink: false, cancellationToken);
        if (!success)
        {
            throw new InvalidOperationException($"Failed to create symlink from CAS hash {hash} to {targetPath}");
        }

        Logger.LogDebug("Successfully created CAS symlink for hash {Hash} to {TargetPath}", hash, targetPath);
    }

    /// <inheritdoc/>
    protected override async Task ProcessLocalFileAsync(ManifestFile file, string targetPath, WorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);

        if (!ValidateSourceFile(sourcePath, file.RelativePath))
        {
            return;
        }

        Logger.LogDebug("Creating symlink for local file {RelativePath} from {SourcePath} to {TargetPath}", file.RelativePath, sourcePath, targetPath);

        FileOperationsService.EnsureDirectoryExists(targetPath);

        try
        {
            await FileOperations.CreateSymlinkAsync(targetPath, sourcePath, cancellationToken);
            Logger.LogDebug("Successfully created symlink from {SourcePath} to {TargetPath}", sourcePath, targetPath);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create symlink from {SourcePath} to {TargetPath}", sourcePath, targetPath);
            throw new InvalidOperationException($"Failed to create symlink for {file.RelativePath}: {ex.Message}", ex);
        }
    }
}
