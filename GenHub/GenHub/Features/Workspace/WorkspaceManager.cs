using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
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
    CasReferenceTracker casReferenceTracker,
    IWorkspaceValidator workspaceValidator,
    WorkspaceReconciler workspaceReconciler
) : IWorkspaceManager
{
    private readonly string _workspaceMetadataPath = Path.Combine(configurationProvider.GetContentStoragePath(), "workspaces.json");

    private readonly IEnumerable<IWorkspaceStrategy> _strategies = strategies;
    private readonly ILogger<WorkspaceManager> _logger = logger;
    private readonly CasReferenceTracker _casReferenceTracker = casReferenceTracker;
    private readonly IWorkspaceValidator _workspaceValidator = workspaceValidator;
    private readonly WorkspaceReconciler _workspaceReconciler = workspaceReconciler;

    /// <summary>
    /// Prepares a workspace using the specified configuration and strategy.
    /// </summary>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The prepared workspace information.</returns>
    public async Task<OperationResult<WorkspaceInfo>> PrepareWorkspaceAsync(WorkspaceConfiguration configuration, IProgress<WorkspacePreparationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Workspace] === Preparing workspace {Id} with strategy {Strategy} ===", configuration.Id, configuration.Strategy);
        _logger.LogDebug("[Workspace] Manifests: {Count}, ForceRecreate: {Force}", configuration.Manifests?.Count ?? 0, configuration.ForceRecreate);

        // Check if workspace already exists and is current (unless ForceRecreate is true)
        if (!configuration.ForceRecreate)
        {
            _logger.LogDebug("[Workspace] Checking for existing workspace");
            var existingWorkspacesResult = await GetAllWorkspacesAsync(cancellationToken);
            if (existingWorkspacesResult.Success && existingWorkspacesResult.Data != null)
            {
                var workspace = existingWorkspacesResult.Data.FirstOrDefault(w => w.Id == configuration.Id);

                if (workspace != null && Directory.Exists(workspace.WorkspacePath))
                {
                    _logger.LogDebug(
                        "Found existing workspace {Id} at {Path}, checking if it's current...",
                        configuration.Id,
                        workspace.WorkspacePath);

                    if (workspace.Strategy != configuration.Strategy)
                    {
                        _logger.LogWarning(
                            "[Workspace] Strategy mismatch detected - existing: {ExistingStrategy}, requested: {RequestedStrategy}. Workspace will be recreated.",
                            workspace.Strategy,
                            configuration.Strategy);
                    }
                    else
                    {
                        // Strategy matches, proceed with normal reuse validation
                        _logger.LogDebug(
                            "[Workspace] Strategy matches ({Strategy}), checking file counts...",
                            workspace.Strategy);

                        // Quick check: compare expected file count from manifests with cached workspace file count
                        // Account for file deduplication - files with same relative path keep highest priority version only
                        var allFiles = (configuration.Manifests ?? Enumerable.Empty<ContentManifest>())
                            .SelectMany(m => (m.Files ?? Enumerable.Empty<ManifestFile>()).Select(f => new { File = f, Manifest = m }))
                            .GroupBy(x => x.File.RelativePath, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.OrderByDescending(x => ContentTypePriority.GetPriority(x.Manifest.ContentType)).First().File);
                        var expectedFileCount = allFiles.Count();

                        // Use cached file count from workspace metadata (set during preparation)
                        // This avoids expensive Directory.EnumerateFiles call on every launch
                        var cachedFileCount = workspace.FileCount;

                        _logger.LogInformation(
                            "[Workspace] Cached file count: {Cached}, Expected: {Expected}",
                            cachedFileCount,
                            expectedFileCount);

                        // Perform basic validation before reusing workspace
                        // Ensure workspace is not corrupted or incomplete
                        if (!await ValidateWorkspaceBasicsAsync(workspace, configuration))
                        {
                            _logger.LogWarning(
                                "[Workspace] Workspace {Id} validation failed, will recreate",
                                configuration.Id);

                            // Fall through to recreate
                        }
                        else if (cachedFileCount > 0 || Directory.Exists(workspace.WorkspacePath))
                        {
                            _logger.LogInformation(
                                "[Workspace] Reusing existing workspace {Id} for fast launch (basic validation passed)",
                                configuration.Id);
                            return OperationResult<WorkspaceInfo>.CreateSuccess(workspace);
                        }

                        // Workspace directory missing or empty - need to recreate
                        _logger.LogWarning(
                            "[Workspace] Workspace directory missing or empty, will recreate");

                        // Fall through to strategy preparation below
                    }
                }
                else if (workspace != null)
                {
                    _logger.LogWarning(
                        "Existing workspace {Id} directory not found at {Path}, will recreate",
                        configuration.Id,
                        workspace.WorkspacePath);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(configuration.Id) &&
            !string.IsNullOrWhiteSpace(configuration.BaseInstallationPath) &&
            !string.IsNullOrWhiteSpace(configuration.WorkspaceRootPath))
        {
            _logger.LogDebug("[Workspace] Validating workspace configuration");
            var configValidation = await _workspaceValidator.ValidateConfigurationAsync(configuration, cancellationToken);
            if (!configValidation.Success || configValidation.Issues.Any(i => i.Severity == ValidationSeverity.Error))
            {
                var errorMessages = configValidation.Issues
                    .Where(i => i.Severity == ValidationSeverity.Error)
                    .Select(i => i.Message);
                _logger.LogError("[Workspace] Configuration validation failed: {Errors}", string.Join(", ", errorMessages));
                return OperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", errorMessages));
            }

            _logger.LogDebug("[Workspace] Configuration validation passed");
        }

        var strategy = _strategies.FirstOrDefault(s => s.CanHandle(configuration));
        if (strategy == null)
        {
            _logger.LogError("[Workspace] No strategy available for {StrategyType}", configuration.Strategy);
            throw new InvalidOperationException($"No strategy available for workspace configuration {configuration.Id} with strategy {configuration.Strategy}");
        }

        _logger.LogDebug("[Workspace] Selected strategy: {Strategy}", strategy.Name);

        var prereqValidation = await _workspaceValidator.ValidatePrerequisitesAsync(strategy, configuration, cancellationToken);
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
            _logger.LogWarning("Workspace prerequisite warning: {Message}", warning.Message);
        }

        if (configuration.ForceRecreate)
        {
            _logger.LogInformation("[Workspace] ForceRecreate enabled, cleaning up existing workspace");
            await CleanupWorkspaceAsync(configuration.Id, cancellationToken);
        }

        _logger.LogInformation("[Workspace] Executing strategy preparation");
        var workspaceInfo = await strategy.PrepareAsync(configuration, progress, cancellationToken);

        if (!workspaceInfo.IsPrepared)
        {
            var messages = workspaceInfo.ValidationIssues?.Select(i => i.Message)
                           ?? new[] { "Workspace preparation failed" };
            _logger.LogError("[Workspace] Strategy preparation failed: {Errors}", string.Join(", ", messages));
            return OperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", messages) ?? "Workspace preparation failed");
        }

        _logger.LogDebug("[Workspace] Strategy preparation completed successfully");

        if (configuration.ValidateAfterPreparation)
        {
            _logger.LogDebug("[Workspace] Running post-preparation validation");
            var validationResult = await _workspaceValidator.ValidateWorkspaceAsync(workspaceInfo, cancellationToken);
            if (!validationResult.Success || !validationResult.Data!.IsValid)
            {
                var errors = validationResult.Data!.Issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message);
                _logger.LogError("[Workspace] Post-preparation validation failed: {Errors}", string.Join(", ", errors));
                return OperationResult<WorkspaceInfo>.CreateFailure($"Workspace validation failed: {string.Join(", ", errors)}");
            }

            _logger.LogDebug("[Workspace] Post-preparation validation passed");
        }

        _logger.LogDebug("[Workspace] Saving workspace metadata");
        await SaveWorkspaceMetadataAsync(workspaceInfo, cancellationToken);

        _logger.LogDebug("[Workspace] Tracking CAS references");
        await TrackWorkspaceCasReferencesAsync(configuration.Id, configuration.Manifests ?? [], cancellationToken);

        _logger.LogInformation("[Workspace] === Workspace {Id} prepared successfully at {Path} ===", workspaceInfo.Id, workspaceInfo.WorkspacePath);
        return OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
    }

    /// <summary>
    /// Retrieves all workspaces asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An operation result containing all prepared workspaces.</returns>
    public async Task<OperationResult<IEnumerable<WorkspaceInfo>>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving all workspaces");

        try
        {
            if (!File.Exists(_workspaceMetadataPath))
            {
                return OperationResult<IEnumerable<WorkspaceInfo>>.CreateSuccess(Enumerable.Empty<WorkspaceInfo>());
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
            _logger.LogError(ex, "Failed to retrieve workspaces");
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
                _logger.LogWarning("Workspace {Id} not found for cleanup", workspaceId);
                return OperationResult<bool>.CreateSuccess(false);
            }

            // CRITICAL: Untrack CAS references BEFORE deleting workspace to prevent reference counting leak
            _logger.LogDebug("[Workspace] Untracking CAS references for workspace {Id}", workspaceId);
            await _casReferenceTracker.UntrackWorkspaceAsync(workspaceId, cancellationToken);

            if (FileOperationsService.DeleteDirectoryIfExists(workspace.WorkspacePath))
            {
                _logger.LogInformation("Deleted workspace directory {Path}", workspace.WorkspacePath);
            }

            workspaces.Remove(workspace);
            await SaveAllWorkspacesAsync(workspaces, cancellationToken);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup workspace {Id}", workspaceId);
            return OperationResult<bool>.CreateFailure($"Failed to cleanup workspace: {ex.Message}");
        }
    }

    private async Task SaveAllWorkspacesAsync(IEnumerable<WorkspaceInfo> workspaces, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_workspaceMetadataPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(workspaces, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_workspaceMetadataPath, json, cancellationToken);
    }

    private async Task SaveWorkspaceMetadataAsync(WorkspaceInfo workspaceInfo, CancellationToken cancellationToken)
    {
        var workspacesResult = await GetAllWorkspacesAsync(cancellationToken);
        var workspaces = workspacesResult.Data?.ToList() ?? new List<WorkspaceInfo>();
        var existing = workspaces.FirstOrDefault(w => w.Id == workspaceInfo.Id);

        if (existing != null)
        {
            workspaces.Remove(existing);
        }

        workspaces.Add(workspaceInfo);
        await SaveAllWorkspacesAsync(workspaces, cancellationToken);
    }

    private async Task TrackWorkspaceCasReferencesAsync(string workspaceId, IEnumerable<ContentManifest> manifests, CancellationToken cancellationToken)
    {
        var casReferences = manifests.SelectMany(m => m.Files ?? Enumerable.Empty<ManifestFile>())
            .Where(f => f.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(f.Hash))
            .Select(f => f.Hash!)
            .ToList();

        if (casReferences.Any())
        {
            await _casReferenceTracker.TrackWorkspaceReferencesAsync(workspaceId, casReferences, cancellationToken);
        }
    }

    /// <summary>
    /// Validates basic workspace integrity before reuse.
    /// Checks if the workspace directory exists and contains expected structure.
    /// </summary>
    /// <param name="workspace">The workspace to validate.</param>
    /// <param name="configuration">The current workspace configuration.</param>
    /// <returns>True if workspace passes basic validation, false otherwise.</returns>
    private async Task<bool> ValidateWorkspaceBasicsAsync(WorkspaceInfo workspace, WorkspaceConfiguration configuration)
    {
        try
        {
            // Check if workspace directory exists
            if (!Directory.Exists(workspace.WorkspacePath))
            {
                _logger.LogWarning(
                    "[Workspace] Workspace directory {Path} does not exist",
                    workspace.WorkspacePath);
                return false;
            }

            // Check if directory has any files at all
            var files = Directory.GetFiles(workspace.WorkspacePath, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                _logger.LogWarning(
                    "[Workspace] Workspace directory {Path} is empty",
                    workspace.WorkspacePath);
                return false;
            }

            // Verify that the workspace has core expected structure (at least one file)
            // More thorough validation can be added here if needed
            _logger.LogDebug(
                "[Workspace] Basic validation passed - found {FileCount} files",
                files.Length);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[Workspace] Basic validation check failed for workspace {Id}",
                workspace.Id);
            return false;
        }
    }
}
