using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace;

/// <summary>
/// Complete workspace management service with persistence and cleanup.
/// Manages workspace operations including preparation, retrieval, and cleanup.
/// </summary>
public class WorkspaceManager(
    IEnumerable<IWorkspaceStrategy> strategies,
    IConfigurationProviderService configurationProvider,
    ILogger<WorkspaceManager> logger,
    ICasReferenceTracker casReferenceTracker,
    IWorkspaceValidator workspaceValidator,
    WorkspaceReconciler reconciler
) : IWorkspaceManager
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // Stores workspace metadata in the application data directory
    private readonly string _workspaceMetadataPath = Path.Combine(configurationProvider.GetApplicationDataPath(), "workspaces.json");

    /// <summary>
    /// Prepares a workspace using the specified configuration and strategy.
    /// </summary>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="skipCleanup">If true, skip removal of files not in manifests.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The prepared workspace information.</returns>
    public async Task<OperationResult<WorkspaceInfo>> PrepareWorkspaceAsync(WorkspaceConfiguration configuration, IProgress<WorkspacePreparationProgress>? progress = null, bool skipCleanup = false, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Workspace] === Preparing workspace {Id} with strategy {Strategy} ===", configuration.Id, configuration.Strategy);
        logger.LogDebug("[Workspace] Manifests: {Count}, ForceRecreate: {Force}", configuration.Manifests?.Count ?? 0, configuration.ForceRecreate);

        // Check if workspace already exists and is current (unless ForceRecreate is true)
        if (!configuration.ForceRecreate)
        {
            logger.LogDebug("[Workspace] Checking for existing workspace");
            var existingWorkspacesResult = await GetAllWorkspacesAsync(cancellationToken);
            if (existingWorkspacesResult.Success && existingWorkspacesResult.Data != null)
            {
                var workspace = existingWorkspacesResult.Data.FirstOrDefault(w => w.Id == configuration.Id);

                if (workspace != null && Directory.Exists(workspace.WorkspacePath))
                {
                    logger.LogDebug(
                        "Found existing workspace {Id} at {Path}, checking if it's current...",
                        configuration.Id,
                        workspace.WorkspacePath);

                    if (workspace.Strategy != configuration.Strategy)
                    {
                        logger.LogWarning(
                            "[Workspace] Strategy mismatch detected - existing: {ExistingStrategy}, requested: {RequestedStrategy}. Workspace will be recreated.",
                            workspace.Strategy,
                            configuration.Strategy);
                        configuration.ForceRecreate = true;
                    }
                    else
                    {
                        // Strategy matches, proceed with normal reuse validation
                        logger.LogDebug(
                            "[Workspace] Strategy matches ({Strategy}), checking manifests and file counts...",
                            workspace.Strategy);

                        // Check if manifest IDs or versions have changed
                        var currentManifests = (configuration.Manifests ?? [])
                            .Select(m => new { m.Id, Version = m.Version ?? string.Empty })
                            .OrderBy(m => m.Id.Value, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        var currentManifestIds = currentManifests.Select(m => m.Id.Value).ToList();
                        var cachedManifestIds = (workspace.ManifestIds ?? [])
                            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        var manifestsChanged = !currentManifestIds.SequenceEqual(cachedManifestIds, StringComparer.OrdinalIgnoreCase);

                        // If IDs match, check versions (crucial for local content where ID is static)
                        if (!manifestsChanged)
                        {
                            var currentVersions = currentManifests
                                .GroupBy(m => m.Id.Value, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First().Version, StringComparer.OrdinalIgnoreCase);

                            var cachedVersions = (workspace.ManifestVersions ?? [])
                                .GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

                            foreach (var (id, version) in currentVersions)
                            {
                                if (!cachedVersions.TryGetValue(id, out var cachedVersion) || cachedVersion != version)
                                {
                                    manifestsChanged = true;
                                    logger.LogInformation(
                                        "[Workspace] Manifest version changed for {Id} - cached: '{Cached}', current: '{Current}'. Workspace will be recreated.",
                                        id,
                                        cachedVersion ?? "(none)",
                                        version);
                                    break;
                                }
                            }
                        }

                        if (manifestsChanged)
                        {
                            // Force recreation to ensure any orphaned files from the previous version are removed
                            configuration.ForceRecreate = true;
                            logger.LogInformation("[Workspace] Configuration change detected, workspace will be recreated.");
                        }
                        else
                        {
                            // Quick check to avoid expensive validation on every launch
                            if (!ValidateWorkspaceBasics(workspace))
                            {
                                logger.LogWarning(
                                    "[Workspace] Workspace {Id} validation failed, will recreate",
                                    configuration.Id);
                                configuration.ForceRecreate = true;
                            }
                            else if (!configuration.ForceRecreate && (workspace.FileCount > 0 || Directory.Exists(workspace.WorkspacePath)))
                            {
                                logger.LogInformation(
                                    "[Workspace] Reusing existing workspace {Id} for fast launch",
                                    configuration.Id);
                                return OperationResult<WorkspaceInfo>.CreateSuccess(workspace);
                            }
                            else
                            {
                                logger.LogWarning("[Workspace] Workspace directory missing or empty, will recreate");
                                configuration.ForceRecreate = true;
                            }
                        }
                    }
                }
                else if (workspace != null)
                {
                    logger.LogWarning(
                        "Existing workspace {Id} directory not found at {Path}, will recreate",
                        configuration.Id,
                        workspace.WorkspacePath);
                    configuration.ForceRecreate = true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(configuration.Id) &&
            !string.IsNullOrWhiteSpace(configuration.BaseInstallationPath) &&
            !string.IsNullOrWhiteSpace(configuration.WorkspaceRootPath))
        {
            logger.LogDebug("[Workspace] Validating workspace configuration");
            var configValidation = await workspaceValidator.ValidateConfigurationAsync(configuration, cancellationToken);
            if (!configValidation.Success || configValidation.Issues.Any(i => i.Severity == ValidationSeverity.Error))
            {
                var errorMessages = configValidation.Issues
                    .Where(i => i.Severity == ValidationSeverity.Error)
                    .Select(i => i.Message);
                logger.LogError("[Workspace] Configuration validation failed: {Errors}", string.Join(", ", errorMessages));
                return OperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", errorMessages));
            }

            logger.LogDebug("[Workspace] Configuration validation passed");
        }

        var strategy = strategies.FirstOrDefault(s => s.CanHandle(configuration));
        if (strategy == null)
        {
            logger.LogError("[Workspace] No strategy available for {StrategyType}", configuration.Strategy);
            throw new InvalidOperationException($"No strategy available for workspace configuration {configuration.Id} with strategy {configuration.Strategy}");
        }

        logger.LogDebug("[Workspace] Selected strategy: {Strategy}", strategy.Name);

        var prereqValidation = await workspaceValidator.ValidatePrerequisitesAsync(strategy, configuration, cancellationToken);
        if (!prereqValidation.Success || prereqValidation.Issues.Any(i => i.Severity == ValidationSeverity.Error))
        {
            var errorMessages = prereqValidation.Issues
                .Where(i => i.Severity == ValidationSeverity.Error)
                .Select(i => i.Message);
            return OperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", errorMessages));
        }

        var warnings = prereqValidation.Issues.Where(i => i.Severity == ValidationSeverity.Warning);
        foreach (var warning in warnings)
        {
            logger.LogWarning("Workspace prerequisite warning: {Message}", warning.Message);
        }

        if (configuration.ForceRecreate)
        {
            logger.LogInformation("[Workspace] ForceRecreate enabled, cleaning up existing workspace");
            await CleanupWorkspaceAsync(configuration.Id, cancellationToken);
        }

        // Propagate skipCleanup to configuration for strategies to use
        configuration.SkipCleanup = skipCleanup;

        logger.LogInformation("[Workspace] Executing strategy preparation (skipCleanup: {SkipCleanup})", skipCleanup);
        var workspaceInfo = await strategy.PrepareAsync(configuration, progress, cancellationToken);

        if (workspaceInfo == null || !workspaceInfo.IsPrepared)
        {
            var messages = workspaceInfo?.ValidationIssues?.Select(i => i.Message).ToList();
            if (messages == null || messages.Count == 0)
            {
                messages = ["Workspace preparation failed and returned no information"];
            }

            var errorMessage = string.Join(", ", messages);
            logger.LogError("[Workspace] Strategy preparation failed: {Errors}", errorMessage);
            return OperationResult<WorkspaceInfo>.CreateFailure(errorMessage);
        }

        logger.LogDebug("[Workspace] Strategy preparation completed successfully");

        if (configuration.ValidateAfterPreparation)
        {
            logger.LogDebug("[Workspace] Running post-preparation validation");
            var validationResult = await workspaceValidator.ValidateWorkspaceAsync(workspaceInfo, cancellationToken);
            if (!validationResult.Success || !validationResult.Data!.IsValid)
            {
                var errors = validationResult.Data!.Issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message);
                logger.LogError("[Workspace] Post-preparation validation failed: {Errors}", string.Join(", ", errors));
                return OperationResult<WorkspaceInfo>.CreateFailure($"Workspace validation failed: {string.Join(", ", errors)}");
            }

            logger.LogDebug("[Workspace] Post-preparation validation passed");
        }

        // Store manifest IDs and versions for future reuse comparison
        workspaceInfo.ManifestIds = [.. (configuration.Manifests ?? []).Select(m => m.Id.Value)];
        var manifestVersionsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in configuration.Manifests ?? [])
        {
            if (!string.IsNullOrEmpty(m.Id.Value) && !manifestVersionsDict.ContainsKey(m.Id.Value))
            {
                manifestVersionsDict[m.Id.Value] = m.Version ?? string.Empty;
            }
        }

        workspaceInfo.ManifestVersions = manifestVersionsDict;

        // Track CAS references BEFORE persisting workspace metadata
        logger.LogDebug("[Workspace] Tracking CAS references");
        var trackResult = await TrackWorkspaceCasReferencesAsync(configuration.Id, configuration.Manifests ?? [], cancellationToken);
        if (!trackResult.Success)
        {
            logger.LogError("[Workspace] Failed to track CAS references for workspace {Id}: {Error}", configuration.Id, trackResult.FirstError);
            return OperationResult<WorkspaceInfo>.CreateFailure($"Failed to track CAS references: {trackResult.FirstError}");
        }

        logger.LogDebug("[Workspace] Saving workspace metadata");
        await SaveWorkspaceMetadataAsync(workspaceInfo, cancellationToken);

        logger.LogInformation("[Workspace] === Workspace {Id} prepared successfully at {Path} ===", workspaceInfo.Id, workspaceInfo.WorkspacePath);
        return OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
    }

    /// <summary>
    /// Retrieves all workspaces asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An operation result containing all prepared workspaces.</returns>
    public async Task<OperationResult<IEnumerable<WorkspaceInfo>>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Retrieving all workspaces");

        try
        {
            if (!File.Exists(_workspaceMetadataPath))
            {
                return OperationResult<IEnumerable<WorkspaceInfo>>.CreateSuccess([]);
            }

            var json = await File.ReadAllTextAsync(_workspaceMetadataPath, cancellationToken);
            var workspaces = JsonSerializer.Deserialize<List<WorkspaceInfo>>(json) ?? [];

            // Filter out workspaces that no longer exist
            var validWorkspaces = workspaces.Where(w => Directory.Exists(w.WorkspacePath)).ToList();

            if (validWorkspaces.Count != workspaces.Count)
            {
                await SaveAllWorkspacesAsync(validWorkspaces, cancellationToken);
            }

            return OperationResult<IEnumerable<WorkspaceInfo>>.CreateSuccess(validWorkspaces.AsEnumerable());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve workspaces");
            return OperationResult<IEnumerable<WorkspaceInfo>>.CreateFailure($"Failed to retrieve workspaces: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up the specified workspace asynchronously.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An operation result indicating whether the workspace was cleaned up successfully.</returns>
    public async Task<OperationResult<bool>> CleanupWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var workspacesResult = await GetAllWorkspacesAsync(cancellationToken);
            if (!workspacesResult.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to get workspaces for cleanup: {workspacesResult.FirstError}");
            }

            var workspaces = workspacesResult.Data!.ToList();
            var workspace = workspaces.FirstOrDefault(w => w.Id == workspaceId);

            if (workspace == null)
            {
                logger.LogWarning("Workspace {Id} not found for cleanup", workspaceId);
                return OperationResult<bool>.CreateSuccess(false);
            }

            // CRITICAL: Untrack CAS references BEFORE deleting workspace to prevent reference counting leak.
            // If we delete the directory but leave .refs, GC will think they are still used.
            logger.LogDebug("[Workspace] Untracking CAS references for workspace {Id}", workspaceId);
            var untrackResult = await casReferenceTracker.UntrackWorkspaceAsync(workspaceId, cancellationToken);
            if (!untrackResult.Success)
            {
                logger.LogError("[Workspace] Failed to untrack CAS references for workspace {Id}: {Error}. Aborting cleanup to prevent orphan reference leaks.", workspaceId, untrackResult.FirstError);
                return OperationResult<bool>.CreateFailure($"Failed to untrack CAS references: {untrackResult.FirstError}");
            }

            if (FileOperationsService.DeleteDirectoryIfExists(workspace.WorkspacePath))
            {
                logger.LogInformation("Deleted workspace directory {Path}", workspace.WorkspacePath);
            }

            workspaces.Remove(workspace);
            await SaveAllWorkspacesAsync(workspaces, cancellationToken);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cleanup workspace {Id}", workspaceId);
            return OperationResult<bool>.CreateFailure($"Failed to cleanup workspace: {ex.Message}");
        }
    }

    /// <summary>
    /// Analyzes what cleanup operations would be needed when switching to a new workspace configuration.
    /// </summary>
    /// <param name="currentWorkspaceId">The ID of the current workspace (null if no workspace exists).</param>
    /// <param name="newConfiguration">The new workspace configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result containing cleanup confirmation data, or null if no cleanup needed.</returns>
    public async Task<OperationResult<WorkspaceCleanupConfirmation?>> AnalyzeCleanupAsync(
        string? currentWorkspaceId,
        WorkspaceConfiguration newConfiguration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // If no current workspace, no cleanup needed
            if (string.IsNullOrEmpty(currentWorkspaceId))
            {
                return OperationResult<WorkspaceCleanupConfirmation?>.CreateSuccess(null);
            }

            // Get current workspace info
            var workspacesResult = await GetAllWorkspacesAsync(cancellationToken);
            if (!workspacesResult.Success || workspacesResult.Data == null)
            {
                return OperationResult<WorkspaceCleanupConfirmation?>.CreateSuccess(null);
            }

            var currentWorkspace = workspacesResult.Data.FirstOrDefault(w => w.Id == currentWorkspaceId);
            if (currentWorkspace == null || !Directory.Exists(currentWorkspace.WorkspacePath))
            {
                return OperationResult<WorkspaceCleanupConfirmation?>.CreateSuccess(null);
            }

            // Analyze deltas using the reconciler
            var deltas = await reconciler.AnalyzeWorkspaceDeltaAsync(currentWorkspace, newConfiguration);

            // Filter to only removal operations
            var removalDeltas = deltas.Where(d => d.Operation == WorkspaceDeltaOperation.Remove).ToList();

            if (removalDeltas.Count == 0)
            {
                return OperationResult<WorkspaceCleanupConfirmation?>.CreateSuccess(null);
            }

            // Calculate total size of files to be removed
            long totalSize = 0;
            foreach (var delta in removalDeltas)
            {
                if (File.Exists(delta.WorkspacePath))
                {
                    try
                    {
                        var fileInfo = new FileInfo(delta.WorkspacePath);
                        totalSize += fileInfo.Length;
                    }
                    catch
                    {
                        // Ignore file access errors
                    }
                }
            }

            // Identify affected manifests (manifests that have files being removed)
            var affectedManifests = currentWorkspace.ManifestIds?
                .Except(newConfiguration.Manifests?.Select(m => m.Id.Value) ?? [])
                .ToList() ?? [];

            var confirmation = new WorkspaceCleanupConfirmation
            {
                FilesToRemove = removalDeltas.Count,
                TotalSizeBytes = totalSize,
                AffectedManifests = affectedManifests,
                RemovalDeltas = removalDeltas,
            };

            logger.LogInformation(
                "[Workspace] Cleanup analysis: {FileCount} files ({Size:N0} bytes) would be removed from {ManifestCount} manifests",
                confirmation.FilesToRemove,
                confirmation.TotalSizeBytes,
                confirmation.AffectedManifests.Count);

            return OperationResult<WorkspaceCleanupConfirmation?>.CreateSuccess(confirmation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Workspace] Failed to analyze cleanup for workspace {WorkspaceId}", currentWorkspaceId);
            return OperationResult<WorkspaceCleanupConfirmation?>.CreateFailure($"Failed to analyze cleanup: {ex.Message}");
        }
    }

    private async Task SaveAllWorkspacesAsync(IEnumerable<WorkspaceInfo> workspaces, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_workspaceMetadataPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(workspaces, _jsonOptions);
        await File.WriteAllTextAsync(_workspaceMetadataPath, json, cancellationToken);
    }

    private async Task SaveWorkspaceMetadataAsync(WorkspaceInfo workspaceInfo, CancellationToken cancellationToken)
    {
        var workspacesResult = await GetAllWorkspacesAsync(cancellationToken);
        var workspaces = workspacesResult.Data?.ToList() ?? [];
        var existing = workspaces.FirstOrDefault(w => w.Id == workspaceInfo.Id);

        if (existing != null)
        {
            workspaces.Remove(existing);
        }

        workspaces.Add(workspaceInfo);
        await SaveAllWorkspacesAsync(workspaces, cancellationToken);
    }

    private async Task<OperationResult<bool>> TrackWorkspaceCasReferencesAsync(string workspaceId, IEnumerable<ContentManifest> manifests, CancellationToken cancellationToken)
    {
        // Only track CAS files that are actually installed into the workspace
        var casReferences = manifests.SelectMany(m => m.Files ?? [])
            .Where(f => f.SourceType == ContentSourceType.ContentAddressable
                     && !string.IsNullOrEmpty(f.Hash)
                     && !string.IsNullOrEmpty(f.RelativePath)) // Only track files with relative paths (workspace-targeted)
            .Select(f => f.Hash!)
            .Distinct()
            .ToList();

        if (casReferences.Count > 0)
        {
            var result = await casReferenceTracker.TrackWorkspaceReferencesAsync(workspaceId, casReferences, cancellationToken);
            return result.Success
                ? OperationResult<bool>.CreateSuccess(true)
                : OperationResult<bool>.CreateFailure(result.FirstError ?? "Unknown error tracking references");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <summary>
    /// Validates basic workspace integrity before reuse.
    /// Checks if the workspace directory exists and contains expected structure.
    /// </summary>
    /// <param name="workspace">The workspace to validate.</param>
    /// <returns>True if workspace passes basic validation, false otherwise.</returns>
    private bool ValidateWorkspaceBasics(WorkspaceInfo workspace)
    {
        try
        {
            // Check if workspace directory exists
            if (!Directory.Exists(workspace.WorkspacePath))
            {
                logger.LogWarning(
                    "[Workspace] Workspace directory {Path} does not exist",
                    workspace.WorkspacePath);
                return false;
            }

            // Check if directory has any files at all
            var files = Directory.GetFiles(workspace.WorkspacePath, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                logger.LogWarning(
                    "[Workspace] Workspace directory {Path} is empty",
                    workspace.WorkspacePath);
                return false;
            }

            // Verify that the workspace has core expected structure (at least one file)
            // More thorough validation can be added here if needed
            logger.LogDebug(
                "[Workspace] Basic validation passed - found {FileCount} files",
                files.Length);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[Workspace] Basic validation check failed for workspace {Id}",
                workspace.Id);
            return false;
        }
    }
}
